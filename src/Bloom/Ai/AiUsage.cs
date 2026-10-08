using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Core;

namespace Bloom.Ai;

internal sealed record UsageLimit(string Title, double Percent, DateTime? Resets);

internal sealed record UsageAccount(string Name, string? Plan, List<UsageLimit> Limits, DateTime Fetched);

/// The plan usage on the home (AI 사용량), asked of the user's own CLIs: Claude Code (ClaudeUsage) and Codex
/// (CodexUsage). Each CLI runs only while it is asked, with no window (OneShot.Psi), and is closed after it; neither
/// login is read here. Every 5 or 10 minutes (설정 > 홈 화면) while the block or the limit notices are on, and on the
/// block's ↻.
internal static class AiUsage
{
    private static UsageAccount? claude, chatgpt;
    private static DispatcherTimer? timer;
    private static int busy;
    private static bool tried;
    private static bool? available;
    private static bool madeUp; // SetForQa: the numbers stay as set

    /// A look started or ended (the numbers may have changed).
    public static event Action? Updated;

    public static bool Busy => busy == 1;

    public static DateTime? Fetched { get; private set; }

    /// Claude Code or Codex is installed: the block shows only then. Looked up once, again on each refresh
    /// (the lookups scan folders, and the home asks on every draw).
    public static bool Available => available ??= Find() is not (null, null);

    /// What is known so far, Claude first; a limit whose reset time has passed starts again from 0.
    public static List<UsageAccount> Accounts
    {
        get
        {
            var now = DateTime.Now;
            var list = new List<UsageAccount>(2);
            foreach (var a in new[] { claude, chatgpt })
                if (a is not null)
                    list.Add(a with { Limits = a.Limits.ConvertAll(l => l.Resets is { } r && r <= now ? l with { Percent = 0, Resets = null } : l) });
            return list;
        }
    }

    /// The timer (Host, once): the first look a little after start, then every Settings.UsageMinutes.
    public static void Start(Dispatcher ui)
    {
        timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(20) };
        timer.Tick += (_, _) =>
        {
            Schedule();
            if (Wanted) _ = Refresh();
        };
        timer.Start();
    }

    /// Someone needs the numbers: the home block, or the limit notices (App\Notices).
    private static bool Wanted
    {
        get
        {
            var s = Settings.Current;
            return Home.IsOn("usage") || (s.Notices && (s.NoticeLimit || s.NoticeRecover));
        }
    }

    /// The interval picked in the settings, counted from now.
    public static void Schedule()
    {
        if (timer is not null) timer.Interval = TimeSpan.FromMinutes(Settings.Current.UsageMinutes == 5 ? 5 : 10);
    }

    /// The home shows the block before the first timed look: ask now (QA runs have no timer and never ask).
    public static void FirstLook()
    {
        if (!tried && timer is not null) _ = Refresh();
    }

    /// Both CLIs at once; a side that fails keeps its last numbers.
    public static async Task Refresh()
    {
        if (madeUp)
        {
            Fetched = DateTime.Now; // the ↻ still answers, with the same numbers
            Updated?.Invoke();
            return;
        }
        if (Interlocked.Exchange(ref busy, 1) == 1) return;
        tried = true;
        Updated?.Invoke();
        try
        {
            var (claudeCli, codexCli) = Find();
            available = claudeCli is not null || codexCli is not null; // installed or removed since the last look
            var c = claudeCli is null ? Task.FromResult<UsageAccount?>(null) : Task.Run(() => ClaudeUsage.Ask(claudeCli));
            var g = codexCli is null ? Task.FromResult<UsageAccount?>(null) : Task.Run(() => CodexUsage.Ask(codexCli));
            var (a, b) = (await c, await g);
            claude = a ?? claude;
            chatgpt = b ?? chatgpt;
            if (a is not null || b is not null) Fetched = DateTime.Now;
        }
        catch (Exception e)
        {
            Log.Error("ai usage", e);
        }
        finally
        {
            busy = 0;
            Updated?.Invoke();
        }
    }

    /// QA pictures and the recording mode: made-up numbers instead of the PC's own; nothing is asked from then on.
    public static void SetForQa(UsageAccount? claudeAccount, UsageAccount? chatgptAccount)
    {
        madeUp = true;
        claude = claudeAccount;
        chatgpt = chatgptAccount;
        available = true;
        tried = true;
        Fetched = DateTime.Now;
    }

    private static (string? claude, string? codex) Find() => (ClaudeSession.FindCli(Settings.Current.ClaudePath), CodexSession.FindCli(Settings.Current.CodexPath));
}
