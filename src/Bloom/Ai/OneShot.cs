using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// One question, one answer, no conversation kept: a fresh CLI call with its own system prompt and no tools
/// (translation, AI 빠른 동작, icon drawing). Claude Code (`claude -p`) or Codex (`codex exec`), each with the
/// user's own login. Every call counts against the user's plan.
internal static class OneShot
{
    /// → (answer, null) or (null, error); (null, null) when cancelled. `name` names the Codex instructions file.
    public static async Task<(string? text, string? error)> Ask(string system, string text, string provider, string model, string? effort, string name, CancellationToken ct)
    {
        text = text.Trim();
        if (text.Length == 0) return ("", null);
        if (Demo.On) return await DemoAnswers.Ask(name, text, ct); // recording mode: nothing is asked
        if (text.Length > 12000) text = text[..12000];
        return provider == "chatgpt" ? await Codex(system, text, model, effort, name, ct) : await Claude(system, text, model, effort, ct);
    }

    private static async Task<(string? text, string? error)> Claude(string system, string text, string model, string? effort, CancellationToken ct)
    {
        string? cli = ClaudeSession.FindCli(Settings.Current.ClaudePath);
        if (cli is null) return (null, Tr("Claude Code CLI를 찾지 못했어요", "Claude Code CLI not found"));
        var psi = Psi(cli);
        bool fast = effort is null or "fast";
        foreach (string a in new[]
        {
            "-p", "--model", model, "--output-format", "text", "--system-prompt", system,
            "--tools", "", "--strict-mcp-config", "--disable-slash-commands", "--name", "Bloom",
            "--settings", fast ? "{\"disableAllHooks\":true,\"alwaysThinkingEnabled\":false}" : "{\"disableAllHooks\":true}",
        }) psi.ArgumentList.Add(a);
        if (!fast)
        {
            psi.ArgumentList.Add("--effort");
            psi.ArgumentList.Add(effort!);
        }
        var (code, output, error) = await RunCli(psi, text, ct);
        if (ct.IsCancellationRequested) return (null, null);
        if (code != 0 || output.Trim().Length == 0) return (null, Tail(error, output, Tr("Claude가 답하지 않았어요", "Claude didn't answer")));
        return (output.Trim(), null);
    }

    private static async Task<(string? text, string? error)> Codex(string system, string text, string model, string? effort, string name, CancellationToken ct)
    {
        string? exe = CodexSession.FindCli(Settings.Current.CodexPath);
        if (exe is null) return (null, Tr("Codex CLI를 찾지 못했어요", "Codex CLI not found"));
        string cwd = WorkDir();
        string instructions = Path.Combine(cwd, name + ".md");
        await File.WriteAllTextAsync(instructions, system, ct);
        var psi = Psi(exe);
        var args = new List<string> { "exec", "--json", "--skip-git-repo-check", "--ignore-user-config", "--sandbox", "read-only", "--cd", cwd, "--color", "never" };
        if (AiModels.GptSlug(model) is { } slug)
        {
            args.Add("--model");
            args.Add(slug);
        }
        foreach (string c in new[]
        {
            $"model_reasoning_effort=\"{effort ?? AiModels.Fit(model, "minimal")}\"",
            $"model_instructions_file='{instructions}'",
            "include_environment_context=false",
            "include_permissions_instructions=false",
        })
        {
            args.Add("-c");
            args.Add(c);
        }
        args.Add("-");
        foreach (string a in args) psi.ArgumentList.Add(a);
        var (code, output, error) = await RunCli(psi, text, ct);
        if (ct.IsCancellationRequested) return (null, null);
        string? answer = null, failure = null;
        foreach (string line in output.Split('\n'))
        {
            if (!line.TrimStart().StartsWith('{')) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                string type = r.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                if (type == "item.completed" && r.TryGetProperty("item", out var it) && it.TryGetProperty("type", out var itype) && itype.GetString() == "agent_message"
                    && it.TryGetProperty("text", out var tx))
                    answer = tx.GetString();
                else if (type is "turn.failed" or "error")
                    failure = r.ToString();
            }
            catch (JsonException)
            {
                /* a log line */
            }
        }
        if (answer is { Length: > 0 }) return (answer.Trim(), null);
        return (null, failure is not null ? Tr("ChatGPT 실패: ", "ChatGPT failed: ") + Short(failure)
            : Tail(error, "", code == 0 ? Tr("ChatGPT가 답하지 않았어요", "ChatGPT didn't answer") : Tr("Codex가 오류로 끝났어요", "Codex ended with an error")));
    }

    /// The CLIs' working folder (Local\ai): no project files around, nothing to read by accident.
    public static string WorkDir()
    {
        string keep = Paths.InLocal(@"ai\.keep");
        if (!File.Exists(keep)) File.WriteAllText(keep, "");
        return Path.GetDirectoryName(keep)!;
    }

    public static ProcessStartInfo Psi(string exe)
    {
        var utf8 = new UTF8Encoding(false);
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
            StandardInputEncoding = utf8,
            WorkingDirectory = WorkDir(),
        };
        Cli.CleanEnvironment(psi);
        return psi;
    }

    /// Runs the CLI with `input` on stdin; 90 s at most.
    public static async Task<(int code, string output, string error)> RunCli(ProcessStartInfo psi, string input, CancellationToken ct)
    {
        Process? p = null;
        try
        {
            p = Process.Start(psi);
            if (p is null) return (-1, "", Tr("시작하지 못했어요", "Couldn't start it"));
            var outTask = p.StandardOutput.ReadToEndAsync(ct);
            var errTask = p.StandardError.ReadToEndAsync(ct);
            await p.StandardInput.WriteAsync(input);
            p.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                await p.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    p.Kill(true);
                }
                catch
                {
                    /* gone */
                }
                return (-1, "", ct.IsCancellationRequested ? "" : Tr("90초 안에 답이 없었어요", "No answer within 90 s"));
            }
            return (p.ExitCode, await outTask, await errTask);
        }
        catch (Exception e)
        {
            Log.Error("one-shot", e);
            return (-1, "", e.Message);
        }
        finally
        {
            p?.Dispose();
        }
    }

    public static string Short(string s) => s.Length > 160 ? s[..160] + "…" : s;

    private static string Tail(string error, string output, string fallback)
    {
        string e = (error.Trim().Length > 0 ? error : output).Trim();
        if (e.Length == 0) return fallback;
        string last = e.Split('\n').Last(l => l.Trim().Length > 0).Trim();
        return Short(last);
    }
}
