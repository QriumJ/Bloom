using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bloom.Ai;

/// An API chat's run_command, once the user allowed it: Windows PowerShell in the chat's folder, no profile, no window,
/// no input, and an environment without Bloom's secrets (Environment); its output and errors as one text (cut at
/// MaxOutput). It runs in a job of its own (CommandJob): stopped after Timeout or when the turn is stopped, and at its
/// end what it left running ends too.
internal static class ApiCommand
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);
    public const int MaxOutput = 30_000;

    /// Variable names holding one of these (any case) look like credentials and stay out of a command's environment.
    private static readonly string[] SecretWords = { "KEY", "TOKEN", "SECRET", "PASSWORD", "PASSWD", "API", "AUTH", "COOKIE", "CREDENTIAL", "PRIVATE" };

    /// … and so do the AI providers' own variables, whatever they are called.
    private static readonly string[] SecretPrefixes = { "ANTHROPIC_", "OPENAI_", "GEMINI_", "GOOGLE_", "XAI_", "DEEPSEEK_", "OPENROUTER_", "OPENCODE_", "CLAUDE_", "CODEX_" };

    /// A value shorter than this is too common to take out of a tool's result ("1", "true").
    private const int MinRedacted = 8;

    public static bool Secret(string name) =>
        SecretWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)) || SecretPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// Bloom's environment split for a command: what it gets, and the values of what it doesn't (taken out of tool
    /// results too, ApiToolbox.Secrets).
    public static (Dictionary<string, string> Kept, List<string> Hidden) Environment()
    {
        var kept = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hidden = new List<string>();
        foreach (DictionaryEntry e in System.Environment.GetEnvironmentVariables())
        {
            if (e.Key is not string name || e.Value is not string value) continue;
            if (!Secret(name)) kept[name] = value;
            else if (value.Length >= MinRedacted) hidden.Add(value);
        }
        return (kept, hidden);
    }

    /// → (what it printed and its exit code, failed). Cancelling `stop` kills it and throws OperationCanceledException.
    public static async Task<(string Text, bool Error)> RunAsync(string command, string folder, CancellationToken stop, TimeSpan? timeout = null)
    {
        // UTF-8 out, so Korean file names and text come through; -EncodedCommand takes the command with no quoting to break
        string script = "[Console]::OutputEncoding = [Text.Encoding]::UTF8; $ProgressPreference = 'SilentlyContinue'; " + command;
        string powershell = Path.Combine(System.Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"); // not whatever the current folder holds
        string line = string.Join(" ", new[] { powershell, "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }.Select(McpLaunch.Quote));
        // an explicit environment: Bloom's own without what looks like a credential (the provider keys a user may keep there)
        var kept = Environment().Kept;
        using var job = CommandJob.Create();
        if (job is null) return ("The command could not be started in a job of its own, so it was not run.", true);
        // created suspended and put into the job before it runs (ChildProcess): nothing it starts can be outside the job
        ChildProcess p;
        try
        {
            p = ChildProcess.Start(line, folder, kept, job);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return ("PowerShell could not start: " + e.Message, true);
        }
        using var _ = p;
        p.Input.Dispose();
        var output = new StringBuilder();
        // read in pieces, not lines: a command printing one endless line holds no more than MaxOutput of it
        var readers = Task.WhenAll(Task.Run(() => Collect(p.Output, output)), Task.Run(() => Collect(p.Error, output)));
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(timeout ?? Timeout);
        try
        {
            // the process itself ending (its output closing too may wait for something it left running in the background)
            await p.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            p.Kill();
            job.Kill(); // the command and all it started, detached ones too
            readers.Wait(OutputWait);
            stop.ThrowIfCancellationRequested();
            return (Shown(output) + $"\n(stopped: still running after {(int)(timeout ?? Timeout).TotalSeconds} s)", true);
        }
        // what it left running ends with it (an app is the pc tool's to open), and with them their hold on the output
        job.Kill();
        readers.Wait(OutputWait); // the last of the output
        int code = p.ExitCode;
        return (Shown(output) + $"\n(exit code {code})", code != 0);
    }

    /// How long the readers get to finish after the command and its job ended (the pipes are closed by then).
    private static readonly TimeSpan OutputWait = TimeSpan.FromSeconds(5);

    /// A pipe's text, in 4 KB pieces, kept up to MaxOutput characters and read on (so the command never blocks) to its end.
    private static void Collect(Stream stream, StringBuilder output)
    {
        var decoder = new UTF8Encoding(false).GetDecoder();
        var bytes = new byte[4096];
        var chars = new char[bytes.Length + 1];
        try
        {
            int n;
            while ((n = stream.Read(bytes, 0, bytes.Length)) > 0)
            {
                int count = decoder.GetChars(bytes, 0, n, chars, 0);
                lock (output)
                    if (output.Length <= MaxOutput) output.Append(chars, 0, count);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            /* the pipe ended */
        }
        finally
        {
            stream.Dispose();
        }
    }

    private static string Shown(StringBuilder output)
    {
        lock (output)
        {
            string text = output.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
            return text.Length > MaxOutput ? text[..MaxOutput] + "\n… (output cut)" : text.Length == 0 ? "(no output)" : text;
        }
    }
}
