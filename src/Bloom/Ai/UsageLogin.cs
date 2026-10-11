using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// Signs an added account in (설정 > AI 계정): the CLI's own login, run into the account's folder in a terminal window of
/// its own — `claude auth login --claudeai` with CLAUDE_CONFIG_DIR, `codex login` with CODEX_HOME. The browser sign-in
/// happens on Anthropic's / OpenAI's pages, and a code the browser may show goes into that terminal, never into Bloom:
/// Bloom only waits for the CLI to finish and checks that its sign-in file is there (AiAccounts.SignedIn).
internal static class UsageLogin
{
    /// The longest a sign-in may take before Bloom stops waiting (the terminal stays the user's to close).
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(10);

    /// The account whose sign-in runs (one at a time).
    public static string? Running { get; private set; }

    /// In the account's folder while its login terminal is open: "<process id> <start time>". The terminal can outlive
    /// the wait (Claude Code stays open, the 10 minutes run out) and Bloom itself (a restart), and the CLI may still
    /// write into the folder: until that window closes the account is neither taken out nor signed in again.
    private const string Marker = ".bloom-login";

    /// A sign-in runs for this account, or its terminal is still open.
    public static bool Open(AiAccount account) => Running == account.Id || Alive(account.Home);

    /// A login terminal is open on this folder: Bloom leaves its auth.json alone (CodexAuth) — the CLI writes there, and
    /// Run takes a newer file for a finished sign-in. Any thread.
    public static bool SigningIn(string home) => Alive(home);

    /// A sign-in started or ended.
    public static event Action? Changed;

    /// Why a sign-in for `kind` can't start now (another one runs, the CLI is missing); null = it can. Asked before an
    /// account is added, so a failed start leaves no empty account behind.
    public static string? Blocked(string kind)
    {
        if (Running is not null) return Tr("다른 계정 로그인 진행 중", "Another sign-in is running");
        return CliFor(kind) is null ? AiErrors.Missing(kind == "chatgpt") : null;
    }

    private static string? CliFor(string kind) => kind == "chatgpt" ? CodexSession.FindCli(Settings.Current.CodexPath) : ClaudeSession.FindCli(Settings.Current.ClaudePath);

    /// → null when signed in, else what went wrong (shown under the account).
    public static async Task<string?> Run(AiAccount account)
    {
        if (account.Home is null) return Tr("기본 계정은 Claude Code · Codex에서 직접 로그인하세요", "Sign the default account in from Claude Code or Codex itself");
        if (Alive(account.Home)) return Tr("이 계정의 로그인 터미널이 아직 열려 있습니다. 먼저 닫으세요.", "This account's sign-in terminal is still open. Close it first.");
        if (Blocked(account.Kind) is { } why) return why;
        bool claude = account.Kind == "claude";
        string cli = CliFor(account.Kind)!;
        // a sign-in counts when the CLI writes its file anew: "다시 로그인" starts with the old, spent one still there
        string file = AiAccounts.SignInFile(account)!;
        var before = File.GetLastWriteTimeUtc(file); // 1601 when there is none yet
        bool Fresh() => File.GetLastWriteTimeUtc(file) > before;
        // a window of its own (no redirection): the CLI shows its link there and reads a pasted code itself
        var psi = new ProcessStartInfo(cli) { UseShellExecute = false, CreateNoWindow = false, WorkingDirectory = OneShot.WorkDir() };
        foreach (string a in claude ? new[] { "auth", "login", "--claudeai" } : new[] { "login" }) psi.ArgumentList.Add(a);
        Cli.CleanEnvironment(psi);
        psi.Environment[claude ? "CLAUDE_CONFIG_DIR" : "CODEX_HOME"] = account.Home;
        Running = account.Id;
        Changed?.Invoke();
        Process? p = null;
        try
        {
            p = Process.Start(psi);
            if (p is null) return AiErrors.StartFailed;
            Mark(account.Home, p);
            var started = DateTime.Now;
            // Claude Code may stay open after its login; the sign-in file is the answer either way
            while (!p.HasExited && !Fresh() && DateTime.Now - started < Longest)
                await Task.Delay(1500);
            if (!Fresh()) await Task.Delay(1000); // written just as the CLI ended
            if (!Fresh()) return Tr("로그인 안 됨 · 다시 시도하세요", "Not signed in · Try again");
            AiUsage.SignedIn(account.Id);
            return null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            Log.Error("ai login", e);
            return AiErrors.StartFailed;
        }
        finally
        {
            Running = null;
            if (p is { HasExited: false }) _ = Linger(account.Home, p);
            else
            {
                Unmark(account.Home);
                p?.Dispose();
            }
            Changed?.Invoke();
        }
    }

    /// The terminal outlived the wait: the account stays locked (Marker) until it closes.
    private static async Task Linger(string home, Process p)
    {
        try
        {
            await p.WaitForExitAsync();
        }
        finally
        {
            Unmark(home);
            p.Dispose();
            Changed?.Invoke();
        }
    }

    private static void Mark(string home, Process p)
    {
        try
        {
            File.WriteAllText(Path.Combine(home, Marker), $"{p.Id} {Stamp(p)}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            /* no lock across a restart: the wait still holds it */
        }
    }

    private static void Unmark(string home)
    {
        try
        {
            File.Delete(Path.Combine(home, Marker));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            /* a stale marker is cleared by Alive */
        }
    }

    /// The marked login process is still running (the same process: its start time matches, a reused id doesn't).
    private static bool Alive(string? home)
    {
        if (home is null || !File.Exists(Path.Combine(home, Marker))) return false;
        try
        {
            if (File.ReadAllText(Path.Combine(home, Marker)).Split(' ') is [var id, var stamp] && int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
            {
                using var p = Process.GetProcessById(pid);
                if (!p.HasExited && Stamp(p) == stamp) return true;
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException)
        {
            /* gone, or not readable: stale */
        }
        Unmark(home);
        return false;
    }

    private static string Stamp(Process p) => p.StartTime.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture);
}
