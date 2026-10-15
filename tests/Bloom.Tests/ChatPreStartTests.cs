using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Ai;
using Bloom.App;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.Tests;

/// Chats.PreStart: a conversation's Claude process starts before its first message and the message uses it.
internal static class ChatPreStartTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "bloom-prestart-test-" + Guid.NewGuid().ToString("N"));
        public readonly List<FakeSession> Sessions = new();
        public readonly Chats Chats;
        public readonly ChatThread Thread = Make("sonnet");
        public bool FailStart, HoldStarts, SlowClose;

        public Fixture()
        {
            Chats = new Chats(Dispatcher.CurrentDispatcher, (thread, effort) =>
            {
                var session = new FakeSession(thread.Model, effort, FailStart)
                {
                    StartGate = HoldStarts ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null,
                    ClosedGate = SlowClose ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null,
                };
                Sessions.Add(session);
                return session;
            }, Path.Combine(root, "chats"))
            {
                PreStartDelay = TimeSpan.Zero, // a test asks for the debounce itself
            };
        }

        public static ChatThread Make(string model) => new() { Id = Guid.NewGuid().ToString("N"), Model = model };

        public string FileOf(ChatThread t) => Path.Combine(root, "chats", t.Id + ".json");

        public void Dispose()
        {
            Chats.Shutdown();
            Pump(() => false, 20);
            Directory.Delete(root, true);
        }
    }

    private sealed class FakeSession(string model, string effort, bool fail) : IChatSession
    {
        public string Model => model;
        public string Effort => effort;
        public string? SessionId => null;
        public bool Busy { get; private set; }
        public bool Alive { get; private set; }
        public DateTime LastUsed => DateTime.UtcNow;
        public int Starts, Disposals;
        public string? Resume, Sent;
        public event Action<string>? SessionStarted;
        public event Action<string>? TextDelta { add { } remove { } }
        public event Action<string?>? Status { add { } remove { } }
        public event Action<string>? AssistantText { add { } remove { } }
        public event Action<bool, string?>? TurnEnded { add { } remove { } }
        public event Action<string>? Exited { add { } remove { } }
        public event Action<ToolAsk>? ToolAsked { add { } remove { } }
        public event Action<string>? ToolAskWithdrawn { add { } remove { } }

        /// Set: the start completes when the test says so (a process that takes a while to come up).
        public TaskCompletionSource? StartGate;
        /// Set: Dispose only starts the ending; the process is gone when the test says so.
        public TaskCompletionSource? ClosedGate;
        public Task Closed => ClosedGate?.Task ?? Task.CompletedTask;

        /// The process dies by itself (the Exited callback has not run yet).
        public void Kill() => Alive = false;

        public bool Start(string? resume, out string? error)
        {
            Starts++;
            Resume = resume;
            error = fail ? "start failed" : null;
            return Alive = !fail;
        }

        public async Task<SessionStart> StartAsync(string? resume, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (StartGate is null)
            {
                bool ok = Start(resume, out string? error);
                return new SessionStart(ok, error);
            }
            Starts++;
            Resume = resume;
            await StartGate.Task.ConfigureAwait(false);
            Alive = true; // the process comes up whether or not it was ended meanwhile, like one already launched
            return new SessionStart(true);
        }

        public bool Send(string text, IReadOnlyList<string> images)
        {
            Sent = text;
            return Busy = true;
        }

        public void Answer(ToolAsk ask, bool allow) { }
        public void Interrupt() { }
        public void Dispose() { Disposals++; Alive = false; }
        public void Init(string id) => SessionStarted?.Invoke(id);
    }

    /// Runs the dispatcher until `done` (or `ms` pass).
    private static bool Pump(Func<bool> done, int ms)
    {
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(2) };
        timer.Tick += (_, _) => { if (done() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        return done();
    }

    private static void Prepared(Fixture f, ChatThread t)
    {
        Task task = f.Chats.WhenPrepared(t);
        Check.True(Pump(() => task.IsCompleted, 5000), "the send must finish preparing");
        task.GetAwaiter().GetResult();
    }

    public static void PreStartThenSendUsesTheSameSession()
    {
        using var f = new Fixture();
        f.Chats.PreStart(f.Thread);
        Check.Eq(f.Sessions.Count, 1, "one process is started ahead");
        Check.Eq(f.Sessions[0].Starts, 1, "it is started");
        Check.True(f.Sessions[0].Sent is null, "nothing is sent to it");
        Check.True(f.Chats.IsWarm(f.Thread) && !f.Chats.IsLive(f.Thread), "it is warm, not a live session");
        Check.Eq(f.Thread.Messages.Count, 0, "the conversation gets no message");
        Check.True(f.Chats.Threads.Count == 0 && !File.Exists(f.FileOf(f.Thread)), "no empty chat is listed or saved");
        f.Chats.PreStart(f.Thread);
        Check.Eq(f.Sessions.Count, 1, "a second request starts nothing more");

        f.Chats.Send(f.Thread, "hello");
        Prepared(f, f.Thread);
        Check.Eq(f.Sessions.Count, 1, "the message reuses the process");
        Check.Eq(f.Sessions[0].Starts, 1, "and does not start it again");
        Check.True(f.Sessions[0].Sent?.Contains("hello") == true, "the message goes to it");
        Check.True(!f.Chats.IsWarm(f.Thread) && f.Chats.IsLive(f.Thread), "it is an ordinary live session now");
        Check.Eq(f.Sessions[0].Disposals, 0, "it stays");
    }

    public static void SessionIdIsKeptOnlyOnceATurnHappens()
    {
        using var f = new Fixture();
        f.Chats.PreStart(f.Thread);
        f.Sessions[0].Init("early-id"); // Claude Code reports its id before any message
        Pump(() => false, 150);
        Check.Eq(f.Thread.SessionId, null, "no session id is saved while nothing has been sent");
        Check.True(!File.Exists(f.FileOf(f.Thread)), "nothing is saved");
        f.Chats.Send(f.Thread, "hello");
        Prepared(f, f.Thread);
        Check.Eq(f.Thread.SessionId, "early-id", "the id is kept with the first turn");
    }

    public static void PreStartResumesAndOwesTheEarlierConversation()
    {
        using var f = new Fixture();
        f.Thread.SessionId = "saved-id";
        f.Thread.SessionProvider = "claude";
        f.Thread.SessionAccount = AiAccounts.Chat("claude").Id;
        f.Chats.PreStart(f.Thread);
        Check.Eq(f.Sessions[0].Resume, "saved-id", "it resumes the conversation's session as the send would");
        f.Chats.Send(f.Thread, "more");
        Prepared(f, f.Thread);
        Check.Eq(f.Sessions.Count, 1, "the send uses it");
        Check.True(f.Sessions[0].Sent?.Contains("<earlier_conversation>") == false, "a resumed session is not told the conversation again");

        using var g = new Fixture();
        g.Thread.Messages.Add(new ChatMessage { Role = "user", Text = "first question" });
        g.Thread.Messages.Add(new ChatMessage { Role = "assistant", Text = "first answer" });
        g.Chats.PreStart(g.Thread);
        Check.Eq(g.Sessions[0].Resume, null, "no session to resume");
        g.Chats.Send(g.Thread, "second");
        Prepared(g, g.Thread);
        Check.Eq(g.Sessions.Count, 1, "the send uses it");
        Check.True(g.Sessions[0].Sent?.Contains("<earlier_conversation>") == true && g.Sessions[0].Sent!.Contains("first question"), "a session with nothing to resume is given the conversation with the message");
    }

    public static void ModelChangeReplacesTheWarmSession()
    {
        using var f = new Fixture();
        f.Chats.PreStart(f.Thread);
        f.Thread.Model = "opus";
        f.Chats.PreStart(f.Thread);
        Check.Eq(f.Sessions.Count, 2, "a process for the new model");
        Check.Eq(f.Sessions[0].Disposals, 1, "the old one ends");
        Check.Eq(f.Sessions[1].Model, "opus", "the new one has the new model");
        Check.Eq(f.Chats.WarmCount, 1, "only one stays warm");

        f.Thread.Effort = "high";
        f.Chats.Send(f.Thread, "hello"); // the level changed after the last request: SessionFor replaces it
        Prepared(f, f.Thread);
        Check.Eq(f.Sessions.Count, 3, "a process for the new level");
        Check.Eq(f.Sessions[1].Disposals, 1, "the stale one ends");
        Check.Eq(f.Sessions[2].Effort, "high", "the send uses the new level");
        Check.True(f.Sessions[2].Sent is not null && f.Sessions[1].Sent is null, "only the new one gets the message");
        Check.Eq(f.Chats.WarmCount, 0, "nothing stays warm");
    }

    public static void OnlyOneWarmSessionExists()
    {
        using var f = new Fixture();
        var a = f.Thread;
        var b = Fixture.Make("sonnet");
        var c = Fixture.Make("haiku");
        f.Chats.PreStart(a);
        f.Chats.PreStart(b);
        Check.Eq(f.Chats.WarmCount, 1, "one across all chats");
        Check.Eq(f.Sessions.Count, 2, "two were started");
        Check.Eq(f.Sessions[0].Disposals, 1, "the older ended");
        Check.True(!f.Chats.IsWarm(a) && f.Chats.IsWarm(b), "the newest stays");
        f.Chats.PreStart(c);
        Check.True(f.Chats.WarmCount == 1 && f.Chats.IsWarm(c) && f.Sessions[1].Disposals == 1, "and again");
    }

    public static void OnlyTheNextSendsProviderIsPreStarted()
    {
        using var f = new Fixture();
        var gpt = Fixture.Make("chatgpt:gpt-5");
        f.Chats.PreStart(f.Thread);
        Check.True(f.Chats.IsWarm(f.Thread), "a Claude model is pre-started");
        f.Chats.PreStart(gpt);
        Check.Eq(f.Sessions.Count, 2, "a ChatGPT model starts its own app-server");
        Check.True(f.Sessions[1].Model == "chatgpt:gpt-5" && f.Chats.IsWarm(gpt), "for ChatGPT");
        Check.True(f.Chats.WarmCount == 1 && f.Sessions[0].Disposals == 1 && !f.Chats.IsWarm(f.Thread), "and the Claude one ends: never both");

        f.Chats.PreStart(f.Thread);
        Check.True(f.Chats.WarmCount == 1 && f.Chats.IsWarm(f.Thread) && f.Sessions[1].Disposals == 1, "back on Claude, the ChatGPT one ends");
        f.Thread.Model = "api:test:model"; // the chat's model changes to another provider
        f.Chats.PreStart(f.Thread);
        Check.True(f.Chats.WarmCount == 0 && f.Sessions[2].Disposals == 1 && f.Sessions.Count == 3, "an API model ends it and starts nothing");
    }

    public static void ChatGptPreStartFollowsTheSameRules()
    {
        using var f = new Fixture();
        var gpt = Fixture.Make("chatgpt:gpt-5");
        var other = Fixture.Make("chatgpt:gpt-5");
        f.Chats.PreStart(gpt);
        Check.True(f.Sessions.Count == 1 && f.Sessions[0].Starts == 1 && f.Sessions[0].Sent is null, "started ahead, nothing sent");
        f.Chats.PreStart(other);
        Check.True(f.Chats.WarmCount == 1 && f.Chats.IsWarm(other) && f.Sessions[0].Disposals == 1, "one warm process across chats");
        f.Chats.Send(other, "hello");
        Prepared(f, other);
        Check.True(f.Sessions.Count == 2 && f.Sessions[1].Sent?.Contains("hello") == true && f.Chats.IsLive(other), "the message takes it over");

        // a ChatGPT session to resume is resumed ahead, under the account it was saved in
        using var g = new Fixture();
        var saved = Fixture.Make("chatgpt:gpt-5");
        saved.SessionId = "exec-thread";
        saved.SessionProvider = "chatgpt";
        saved.SessionAccount = AiAccounts.Chat("chatgpt").Id;
        g.Chats.PreStart(saved);
        Check.Eq(g.Sessions[0].Resume, "exec-thread", "the saved thread is resumed");
        g.Chats.WarmIdle = TimeSpan.Zero;
        g.Chats.ReapWarm();
        Check.True(!g.Chats.IsWarm(saved) && g.Sessions[0].Disposals == 1, "an idle one ends like Claude's");

        // with the chats on `codex exec` (a process per turn) there is nothing to start ahead
        using var h = new Fixture();
        h.Chats.CodexAppServer = false;
        h.Chats.PreStart(Fixture.Make("chatgpt:gpt-5"));
        Check.Eq(h.Sessions.Count, 0, "nothing starts");
    }

    public static void CodexLaunchInputsAreInTheFingerprint()
    {
        var prior = (Settings.Current.CodexPath, Settings.Current.ClaudePath);
        try
        {
            using var f = new Fixture();
            var gpt = Fixture.Make("chatgpt:gpt-5");
            f.Chats.PreStart(gpt);
            Settings.Current.CodexPath = @"C:\elsewhere\codex.exe"; // another CLI after warming
            f.Chats.Send(gpt, "hello");
            Prepared(f, gpt);
            Check.True(f.Sessions.Count == 2 && f.Sessions[0].Sent is null && f.Sessions[0].Disposals == 1, "the stale app-server is dropped");
            Check.True(f.Sessions[1].Sent?.Contains("hello") == true, "the message goes to a new one");

            using var g = new Fixture();
            var other = Fixture.Make("chatgpt:gpt-5");
            g.Chats.PreStart(other);
            Settings.Current.ClaudePath = @"C:\elsewhere\claude.exe"; // Claude's CLI is not Codex's input
            g.Chats.PreStart(other);
            Check.True(g.Sessions.Count == 1 && g.Chats.IsWarm(other), "it stays");
            other.Effort = "high";
            g.Chats.PreStart(other);
            Check.True(g.Sessions.Count == 2 && g.Sessions[0].Disposals == 1 && g.Sessions[1].Effort == "high", "a new level replaces it");
        }
        finally
        {
            (Settings.Current.CodexPath, Settings.Current.ClaudePath) = prior;
        }
    }

    public static void UnusedWarmSessionEndsAfterTheIdleTime()
    {
        using var f = new Fixture();
        f.Chats.WarmIdle = TimeSpan.FromMinutes(5);
        f.Chats.PreStart(f.Thread);
        f.Chats.ReapWarm();
        Check.True(f.Chats.IsWarm(f.Thread) && f.Sessions[0].Disposals == 0, "a recent one stays");
        f.Chats.WarmIdle = TimeSpan.Zero;
        f.Chats.ReapWarm();
        Check.True(!f.Chats.IsWarm(f.Thread) && f.Sessions[0].Disposals == 1, "an idle one ends");
        Check.True(!f.Chats.IsLive(f.Thread), "and is gone");
        f.Chats.Send(f.Thread, "hello");
        Prepared(f, f.Thread);
        Check.Eq(f.Sessions.Count, 2, "the next message starts a new one as before");
        Check.Eq(f.Sessions[1].Starts, 1, "started once");
    }

    public static void ClosingTheChatEndsItsWarmSession()
    {
        using var f = new Fixture();
        f.Chats.PreStart(f.Thread);
        f.Chats.CancelPreStart(f.Thread);
        Check.True(!f.Chats.IsWarm(f.Thread) && f.Sessions[0].Disposals == 1, "the unsent process ends with its chat");

        f.Chats.Send(f.Thread, "hello");
        Prepared(f, f.Thread);
        f.Chats.CancelPreStart(f.Thread);
        Check.Eq(f.Sessions[^1].Disposals, 0, "a session already in use is left alone");
    }

    public static void RapidRequestsStartOneProcess()
    {
        using var f = new Fixture();
        f.Chats.PreStartDelay = TimeSpan.FromMilliseconds(60);
        var other = Fixture.Make("sonnet");
        for (int i = 0; i < 20; i++) f.Chats.PreStart(f.Thread); // typing
        f.Chats.PreStart(other); // flipping to another chat before the wait is over
        Check.Eq(f.Sessions.Count, 0, "nothing starts while requests keep coming");
        Check.True(Pump(() => f.Sessions.Count > 0, 3000), "the last request starts its process");
        Pump(() => false, 150);
        Check.Eq(f.Sessions.Count, 1, "one process");
        Check.True(f.Chats.IsWarm(other) && !f.Chats.IsWarm(f.Thread), "for the chat last asked for");
        for (int i = 0; i < 20; i++) f.Chats.PreStart(other);
        Pump(() => false, 150);
        Check.Eq(f.Sessions.Count, 1, "typing in a chat that has one starts no more");

        f.Chats.PreStartDelay = TimeSpan.Zero;
        for (int i = 0; i < 20; i++) f.Chats.PreStart(f.Thread);
        Check.Eq(f.Sessions.Count, 2, "twenty requests for one chat make one process");
    }

    public static void FailedPreStartShowsNothing()
    {
        using var f = new Fixture { FailStart = true };
        f.Chats.PreStart(f.Thread);
        Check.Eq(f.Sessions.Count, 1, "it tried");
        Check.True(!f.Chats.IsWarm(f.Thread) && f.Sessions[0].Disposals == 1 && !f.Chats.IsLive(f.Thread), "the failed process is dropped");
        Check.Eq(f.Thread.Messages.Count, 0, "no message, no error shown");

        f.FailStart = false;
        f.Chats.Send(f.Thread, "hello");
        Prepared(f, f.Thread);
        Check.Eq(f.Sessions.Count, 2, "the send starts its own");
        Check.True(f.Sessions[1].Sent is not null && !f.Thread.Messages[^1].Error, "and works");

        using var g = new Fixture { FailStart = true };
        g.Chats.PreStart(g.Thread);
        g.Chats.Send(g.Thread, "hello");
        Prepared(g, g.Thread);
        Check.True(g.Thread.Messages[^1].Error, "the send reports the failure as it always did");
    }

    public static void OtherProvidersAreNotPreStarted()
    {
        using var f = new Fixture();
        var api = Fixture.Make("api:test:model");
        f.Chats.PreStart(api);
        Check.Eq(f.Sessions.Count, 0, "an API chat has no process");
        f.Chats.PreStartOn = false;
        f.Chats.PreStart(f.Thread);
        Check.Eq(f.Sessions.Count, 0, "switched off");
    }

    public static void EndSessionAndEndAllDropWarmSessions()
    {
        using var f = new Fixture();
        var other = Fixture.Make("sonnet");
        f.Chats.PreStart(f.Thread);
        f.Chats.EndSession(f.Thread);
        Check.True(!f.Chats.IsWarm(f.Thread) && f.Sessions[0].Disposals == 1, "ending the session ends the warm one");
        f.Chats.PreStart(other);
        f.Chats.EndAll();
        Check.True(f.Chats.WarmCount == 0 && f.Sessions[1].Disposals == 1, "a setting that changes how sessions start ends it");
    }

    /// Processes that exist: not disposed, or disposed and still ending.
    private static int Running(Fixture f) => f.Sessions.Count(s => s.Alive || !s.Closed.IsCompleted);

    public static void EndingDuringTheWaitStartsNothing()
    {
        using var f = new Fixture();
        f.Chats.PreStartDelay = TimeSpan.FromMilliseconds(60);
        f.Chats.PreStart(f.Thread);
        f.Chats.EndSession(f.Thread);
        Pump(() => false, 250);
        Check.Eq(f.Sessions.Count, 0, "ending the session during the wait cancels the start");

        f.Chats.PreStart(f.Thread);
        f.Chats.EndAll();
        Pump(() => false, 250);
        Check.Eq(f.Sessions.Count, 0, "so does ending every session");

        var other = Fixture.Make("sonnet");
        f.Chats.PreStart(f.Thread);
        f.Chats.EndSession(other);
        Check.True(Pump(() => f.Sessions.Count == 1, 3000), "ending another conversation leaves the wait alone");
    }

    public static void EndingDuringAStartLeavesNoProcess()
    {
        using var f = new Fixture { HoldStarts = true };
        f.Chats.PreStart(f.Thread);
        Check.True(f.Chats.IsWarm(f.Thread) && f.Sessions[0].Starts == 1 && !f.Sessions[0].Alive, "the process is on its way up");
        f.Chats.EndSession(f.Thread);
        Check.True(!f.Chats.IsWarm(f.Thread) && f.Sessions[0].Disposals == 1, "ending the session drops it");
        f.Sessions[0].StartGate!.SetResult(); // it comes up anyway
        Check.True(Pump(() => f.Sessions[0].Disposals == 2, 3000), "a start that lands after the end ends the process again");
        Check.True(!f.Sessions[0].Alive && !f.Chats.IsWarm(f.Thread) && !f.Chats.IsLive(f.Thread), "nothing is left");

        var other = Fixture.Make("sonnet");
        f.Chats.PreStart(other);
        f.Chats.EndAll();
        f.Sessions[1].StartGate!.SetResult();
        Check.True(Pump(() => f.Sessions[1].Disposals == 2, 3000) && !f.Sessions[1].Alive, "the same for ending every session");
    }

    public static void SendDuringAStartWaitsForIt()
    {
        using var f = new Fixture { HoldStarts = true };
        f.Chats.PreStart(f.Thread);
        f.Chats.Send(f.Thread, "hello");
        Pump(() => false, 150);
        Check.True(f.Sessions[0].Sent is null && f.Sessions.Count == 1, "the message waits for the process on its way up");
        f.Sessions[0].StartGate!.SetResult();
        Prepared(f, f.Thread);
        Check.True(f.Sessions.Count == 1 && f.Sessions[0].Sent?.Contains("hello") == true && f.Sessions[0].Disposals == 0, "and then goes to it");
    }

    public static void DeadWarmSessionIsNotTakenAtSend()
    {
        using var f = new Fixture();
        f.Chats.PreStart(f.Thread);
        f.Sessions[0].Kill(); // Claude exited; the Exited callback has not drained
        Check.True(f.Chats.IsWarm(f.Thread), "still listed as warm");
        f.Chats.Send(f.Thread, "hello");
        Prepared(f, f.Thread);
        Check.Eq(f.Sessions.Count, 2, "a fresh process starts");
        Check.True(f.Sessions[0].Sent is null && f.Sessions[0].Disposals == 1, "the dead one is dropped, not sent to");
        Check.True(f.Sessions[1].Starts == 1 && f.Sessions[1].Sent?.Contains("hello") == true, "the message goes to the new one");
        Check.True(!f.Thread.Messages[^1].Error, "no error");

        using var g = new Fixture();
        g.Chats.PreStart(g.Thread);
        g.Sessions[0].Kill();
        g.Chats.PreStart(g.Thread);
        Check.True(g.Sessions.Count == 2 && g.Chats.IsWarm(g.Thread), "a request replaces a dead warm process");
    }

    public static void SlowEndingCountsAgainstTheCap()
    {
        using var f = new Fixture { SlowClose = true };
        var a = f.Thread;
        var b = Fixture.Make("sonnet");
        var c = Fixture.Make("haiku");
        f.Chats.PreStart(a);
        f.Chats.PreStart(b); // a ends (slowly): b waits
        Check.Eq(f.Sessions.Count, 1, "b is not started while a is still ending");
        Check.True(Running(f) <= 1 && f.Chats.WarmCount == 0, "one process at most");
        f.Chats.PreStart(c); // flipping on: c replaces b's wait
        Check.Eq(f.Sessions.Count, 1, "still waiting");
        Pump(() => false, 100);
        Check.Eq(f.Sessions.Count, 1, "the wait doesn't time out into a second process");
        f.Sessions[0].ClosedGate!.SetResult();
        Check.True(Pump(() => f.Sessions.Count == 2, 3000), "the last request starts once a is gone");
        Check.True(f.Chats.IsWarm(c) && !f.Chats.IsWarm(b) && Running(f) == 1, "for the chat last asked for, alone");

        f.Chats.CancelPreStart(c);
        f.Chats.PreStart(a); // c is ending: a waits, then the chat is closed before it starts
        f.Chats.CancelPreStart(a);
        f.Sessions[1].ClosedGate!.SetResult();
        Pump(() => false, 150);
        Check.True(f.Sessions.Count == 2 && Running(f) == 0, "a start that was waiting is cancelled with its chat");
    }

    public static void LaunchInputsChangedSinceWarmingMeanACleanStart()
    {
        var prior = Settings.Current.McpOn;
        try
        {
            using var f = new Fixture();
            f.Chats.PreStart(f.Thread);
            Settings.Current.McpOn = [.. prior, "test|server"]; // a server switched on after warming
            f.Chats.Send(f.Thread, "hello");
            Prepared(f, f.Thread);
            Check.Eq(f.Sessions.Count, 2, "the send starts its own");
            Check.True(f.Sessions[0].Sent is null && f.Sessions[0].Disposals == 1, "the stale process is dropped, not used");
            Check.True(f.Sessions[1].Sent?.Contains("hello") == true && f.Sessions[1].Starts == 1, "the message goes to the new one");

            using var g = new Fixture();
            Settings.Current.McpOn = prior;
            g.Chats.PreStart(g.Thread);
            Settings.Current.McpOn = [.. prior, "test|server"];
            g.Chats.PreStart(g.Thread); // typing: the stale process is replaced
            Check.True(g.Sessions.Count == 2 && g.Sessions[0].Disposals == 1 && g.Chats.IsWarm(g.Thread), "a request replaces it");
            g.Chats.PreStart(g.Thread);
            Check.Eq(g.Sessions.Count, 2, "and the new one is kept as it is");
        }
        finally
        {
            Settings.Current.McpOn = prior;
        }

        var permissions = Settings.Current.Permissions;
        try
        {
            using var h = new Fixture();
            h.Chats.PreStart(h.Thread);
            Settings.Current.Permissions = new(permissions) { ["test-action"] = "allow" };
            h.Chats.Send(h.Thread, "hello");
            Prepared(h, h.Thread);
            Check.True(h.Sessions.Count == 2 && h.Sessions[0].Sent is null, "a permission change counts too");
        }
        finally
        {
            Settings.Current.Permissions = permissions;
        }
    }

    public static void LeavingTheQuickChatEndsItsStart()
    {
        using var f = new Fixture();
        f.Chats.PreStartDelay = TimeSpan.FromMilliseconds(60);
        f.Chats.PreStart(f.Thread);
        Launcher.LeaveChat(f.Chats, f.Thread, Mode.Chat);
        Check.True(Pump(() => f.Sessions.Count == 1, 3000), "staying in the chat leaves the start alone");
        Pump(() => false, 100);

        Launcher.LeaveChat(f.Chats, f.Thread, Mode.Root);
        Check.True(!f.Chats.IsWarm(f.Thread) && f.Sessions[0].Disposals == 1, "Esc to the home ends the unsent process");

        var other = Fixture.Make("sonnet");
        f.Chats.PreStart(other);
        Launcher.LeaveChat(f.Chats, other, Mode.Root);
        Pump(() => false, 250);
        Check.Eq(f.Sessions.Count, 1, "and a start still in its wait never happens");
        Launcher.LeaveChat(f.Chats, null, Mode.Root); // no chat was open
    }
}
