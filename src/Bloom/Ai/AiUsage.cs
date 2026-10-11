using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Core;

namespace Bloom.Ai;

internal sealed record UsageLimit(string Title, double Percent, DateTime? Resets);

internal sealed record UsageAccount(string Name, string? Plan, List<UsageLimit> Limits, DateTime Fetched)
{
    public string Id { get; init; } = ""; // the AiAccount it belongs to
    public string? Who { get; init; } // the account's e-mail, shown when one kind has more than one card
}

/// The plan usage on the home (AI 사용량), one card per account (Core\AiAccounts): Claude through Claude Code's own
/// `/usage` (ClaudeUsage), ChatGPT through Codex's app-server (CodexUsage) or, if picked, one HTTPS request with the
/// account's Codex sign-in (ChatGptLight). Each CLI runs only while it is asked, with no window, and is closed after
/// it. The default accounts are asked every 5 or 10 minutes (설정 > 홈 화면), added ones every ExtraMinutes; the block's
/// ↻ asks all of them. Claude accounts go one after another, ChatGPT ones beside them.
internal static class AiUsage
{
    /// Added accounts are asked this rarely: each Claude look costs ~470 MB for 4 s, and an account not in use moves little.
    public const int ExtraMinutes = 30;

    private static readonly Dictionary<string, UsageAccount> known = new();
    private static readonly Dictionary<string, DateTime> asked = new();
    private static readonly HashSet<string> failed = new();
    private static List<UsageAccount>? madeUp; // SetForQa: the numbers stay as set
    private static DispatcherTimer? timer;
    private static int busy;
    private static bool tried, again, againAll;
    private static bool? available;

    /// A look started or ended, or one account answered (the numbers may have changed).
    public static event Action? Updated;

    public static bool Busy => busy == 1;

    public static DateTime? Fetched { get; private set; }

    /// Claude Code or Codex is installed: the block shows only then. Looked up once, again on each refresh
    /// (the lookups scan folders, and the home asks on every draw).
    public static bool Available => available ??= Find() is not (null, null);

    /// The last look at this account gave nothing (signed out, or the CLI failed); its card keeps the old numbers.
    public static bool Failed(string id) => failed.Contains(id);

    /// What is known so far, in the accounts' order; a limit whose reset time has passed starts again from 0.
    public static List<UsageAccount> Accounts
    {
        get
        {
            var now = DateTime.Now;
            var source = madeUp ?? AiAccounts.All.Where(a => !a.Hidden).Select(a => known.GetValueOrDefault(a.Id)).OfType<UsageAccount>();
            return source.Select(a => a with { Limits = a.Limits.ConvertAll(l => l.Resets is { } r && r <= now ? l with { Percent = 0, Resets = null } : l) }).ToList();
        }
    }

    /// The timer (Host, once): the first look a little after start, then every Settings.UsageMinutes.
    public static void Start(Dispatcher ui)
    {
        timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(20) };
        timer.Tick += (_, _) =>
        {
            Schedule();
            if (Wanted) _ = Refresh(all: false);
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

    /// `all` false (the timer): added accounts only when ExtraMinutes have passed since their last look. Asked while a
    /// look runs (a sign-in just ended, the ChatGPT way changed), one more pass follows it — over every account when
    /// any of those asks wanted all.
    public static async Task Refresh(bool all = true)
    {
        if (madeUp is not null)
        {
            Fetched = DateTime.Now; // the ↻ still answers, with the same numbers
            Updated?.Invoke();
            return;
        }
        if (Interlocked.Exchange(ref busy, 1) == 1)
        {
            again = true;
            againAll |= all;
            return;
        }
        tried = true;
        Updated?.Invoke();
        try
        {
            do
            {
                again = false;
                try
                {
                    await Pass(all);
                }
                catch (Exception e)
                {
                    Log.Error("ai usage", e); // this pass ends; one asked for while it ran still follows
                }
                all = againAll;
                againAll = false;
            }
            while (again);
        }
        finally
        {
            busy = 0;
            Updated?.Invoke();
        }
    }

    /// One look at the due accounts. The bookkeeping (asked, failed, known) happens on the UI thread only: here before
    /// the asks start and in Done after each; the asks themselves run on pool threads, Claude and ChatGPT side by side.
    private static async Task Pass(bool all)
    {
        var (claudeCli, codexCli) = Find();
        available = claudeCli is not null || codexCli is not null; // installed or removed since the last look
        var now = DateTime.Now;
        var due = AiAccounts.All.Where(a => !a.Hidden && (all || a.IsDefault || now - asked.GetValueOrDefault(a.Id) >= TimeSpan.FromMinutes(ExtraMinutes))).ToList();
        foreach (var a in due) asked[a.Id] = now;
        Func<AiAccount, Task<UsageAccount?>>? claude = claudeCli is null ? null : a => ClaudeUsage.Ask(claudeCli, a);
        Func<AiAccount, Task<UsageAccount?>>? chatgpt = Settings.Current.ChatGptUsage == "light" ? a => LightOrCli(a, codexCli)
            : codexCli is null ? null : a => CodexAuth.Unsaved(a.Home) ? Task.FromResult<UsageAccount?>(null) : CodexUsage.Ask(codexCli, a);
        var c = Task.Run(() => Each(due.Where(a => a.Kind == "claude").ToList(), claude));
        var g = Task.Run(() => Each(due.Where(a => a.Kind == "chatgpt").ToList(), chatgpt));
        if (await c | await g) Fetched = DateTime.Now;
    }

    /// The light way first; when it gives nothing (a token Bloom may not refresh ran out, the endpoint changed) Codex's
    /// own app-server, which refreshes its sign-in itself — not while tokens the light way refreshed still wait to be
    /// saved (CodexAuth.Unsaved), since Codex would send the spent refresh token.
    private static async Task<UsageAccount?> LightOrCli(AiAccount a, string? codexCli) =>
        await ChatGptLight.Ask(a) ?? (codexCli is null || CodexAuth.Unsaved(a.Home) ? null : await CodexUsage.Ask(codexCli, a));

    /// One account after another (a second CLI never runs beside the first); true when any answered. No CLI for the
    /// kind (`ask` null): nothing is asked and no account is called signed out.
    private static async Task<bool> Each(List<AiAccount> accounts, Func<AiAccount, Task<UsageAccount?>>? ask)
    {
        if (ask is null) return false;
        bool any = false;
        foreach (var a in accounts)
        {
            UsageAccount? r = null;
            if (AiAccounts.SignedIn(a))
            {
                try
                {
                    r = await ask(a);
                }
                catch (Exception e)
                {
                    Log.Error("ai usage " + a.Kind, e); // one account's failure leaves the others asked
                }
            }
            System.Windows.Application.Current?.Dispatcher.Invoke(() => Done(a, r));
            any |= r is not null;
        }
        return any;
    }

    /// An account's answer, on the UI thread: its card, or its sign-in marked as needed.
    private static void Done(AiAccount a, UsageAccount? r)
    {
        if (r is null)
        {
            failed.Add(a.Id);
            Updated?.Invoke();
            return;
        }
        failed.Remove(a.Id);
        known[a.Id] = r with { Id = a.Id };
        if (r.Who is { Length: > 0 } who && who != a.Email)
        {
            a.Email = who;
            Settings.Save();
        }
        Updated?.Invoke();
    }

    /// A sign-in just finished: the account is due at once (its last failed look no longer counts); the other added ones
    /// wait for their own time.
    public static void SignedIn(string id)
    {
        asked.Remove(id);
        failed.Remove(id);
        _ = Refresh(all: false);
    }

    /// An account left the list: its card goes too.
    public static void Forget(string id)
    {
        known.Remove(id);
        asked.Remove(id);
        failed.Remove(id);
        Updated?.Invoke();
    }

    /// QA pictures and the recording mode: made-up numbers instead of the PC's own; nothing is asked from then on.
    public static void SetForQa(params UsageAccount[] accounts)
    {
        madeUp = accounts.Select((a, i) => a.Id.Length > 0 ? a : a with { Id = "qa" + i }).ToList();
        available = true;
        tried = true;
        Fetched = DateTime.Now;
    }

    private static (string? claude, string? codex) Find() => (ClaudeSession.FindCli(Settings.Current.ClaudePath), CodexSession.FindCli(Settings.Current.CodexPath));
}
