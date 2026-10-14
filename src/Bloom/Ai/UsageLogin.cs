using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// Signs an added account in (설정 > AI 계정): the CLI's own login, run into the account's folder in a terminal window of
/// its own — `claude auth login --claudeai` with CLAUDE_CONFIG_DIR, `codex login` with CODEX_HOME. The browser sign-in
/// happens on Anthropic's / OpenAI's pages, and a code the browser may show goes into that terminal, never into Bloom:
/// Bloom only waits for the CLI to finish and checks that its sign-in file is there (AiAccounts.SignedIn). LoginMarker
/// keeps track of the terminal until it closes, even past the wait or a Bloom restart.
internal static class UsageLogin
{
    /// The longest a sign-in may take before Bloom stops waiting (the terminal stays the user's to close).
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(10);

    /// The account whose sign-in runs (one at a time).
    public static string? Running { get; private set; }

    /// A sign-in runs for this account, or its terminal is still open.
    public static bool Open(AiAccount account) => Running == account.Id || LoginMarker.Alive(account.Home);

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
        if (LoginMarker.Alive(account.Home)) return Tr("이 계정의 로그인 터미널이 아직 열려 있습니다. 먼저 닫으세요.", "This account's sign-in terminal is still open. Close it first.");
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
        AiAccounts.UseHome(psi, account.Kind, account.Home);
        Running = account.Id;
        Changed?.Invoke();
        Process? p = null;
        try
        {
            p = ChildProcess.Spawn(psi);
            if (p is null) return AiErrors.StartFailed;
            LoginMarker.Mark(account.Home, p);
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
                LoginMarker.Unmark(account.Home);
                p?.Dispose();
            }
            Changed?.Invoke();
        }
    }

    /// The terminal outlived the wait: the account stays locked (LoginMarker) until it closes.
    private static async Task Linger(string home, Process p)
    {
        try
        {
            await p.WaitForExitAsync();
        }
        finally
        {
            LoginMarker.Unmark(home);
            p.Dispose();
            Changed?.Invoke();
        }
    }
}
