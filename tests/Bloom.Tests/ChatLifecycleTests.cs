using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Ai;

namespace Bloom.Tests;

internal static class ChatLifecycleTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "bloom-chat-test-" + Guid.NewGuid().ToString("N"));
        public readonly List<FakeSession> Sessions = new();
        public readonly Chats Chats;
        public readonly ChatThread Thread = new() { Id = Guid.NewGuid().ToString("N"), Model = "sonnet" };
        public bool FailStart;
        public string Saved => Path.Combine(root, "chats", Thread.Id + ".json");

        public Fixture()
        {
            Chats = new Chats(Dispatcher.CurrentDispatcher, (thread, effort) =>
            {
                var session = new FakeSession(thread.Model, effort) { FailStart = FailStart };
                Sessions.Add(session);
                return session;
            }, Path.Combine(root, "chats"));
        }

        public void Dispose()
        {
            Chats.Shutdown();
            Drain();
            var cleanup = Chats.WhenDeleted(Thread);
            if (!cleanup.IsCompleted)
            {
                var frame = new DispatcherFrame();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(2) };
                timer.Tick += (_, _) => { if (cleanup.IsCompleted || DateTime.UtcNow >= deadline) frame.Continue = false; };
                timer.Start();
                Dispatcher.PushFrame(frame);
                timer.Stop();
                Check.True(cleanup.IsCompleted, "deleted chat files must finish cleanup before fixture teardown");
            }
            cleanup.GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    private sealed class FakeSession(string model, string effort) : IChatSession
    {
        public string Model => model;
        public string Effort => effort;
        public string? SessionId => null;
        public bool Busy { get; private set; }
        public bool Alive { get; private set; }
        public DateTime LastUsed => DateTime.UtcNow;
        public bool FailStart;
        public int Disposals;
        public bool? Answered;
        public event Action<string>? SessionStarted;
        public event Action<string>? TextDelta;
        public event Action<string?>? Status;
        public event Action<string>? AssistantText;
        public event Action<bool, string?>? TurnEnded;
        public event Action<string>? Exited;
        public event Action<ToolAsk>? ToolAsked;
        public event Action<string>? ToolAskWithdrawn;

        public bool Start(string? resume, out string? error)
        {
            error = FailStart ? "test start failed" : null;
            return Alive = !FailStart;
        }

        public bool Send(string text, IReadOnlyList<string> images) => Busy = true;
        public void Answer(ToolAsk ask, bool allow) => Answered = allow;
        public void Interrupt() { }
        public void Dispose() { Disposals++; Alive = false; }

        public void CompleteAnswer()
        {
            SessionStarted?.Invoke("current-session");
            TextDelta?.Invoke("current answer");
            TurnEnded?.Invoke(true, null);
        }

        public void EmitLateEvents()
        {
            SessionStarted?.Invoke("old-session");
            TextDelta?.Invoke("old delta");
            AssistantText?.Invoke("old message");
            Status?.Invoke("old status");
            ToolAsked?.Invoke(new ToolAsk("old-ask", "test-tool", "{}", ""));
            ToolAskWithdrawn?.Invoke("old-ask");
            TurnEnded?.Invoke(true, null);
            Exited?.Invoke("");
        }
    }

    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Wait(Task task)
    {
        if (task.IsCompleted) { task.GetAwaiter().GetResult(); return; }
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(2) };
        timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        Check.True(task.IsCompleted, "chat worker must finish while the dispatcher keeps pumping");
        task.GetAwaiter().GetResult();
    }

    public static void DeletedChatIgnoresQueuedSessionEvents()
    {
        using var f = new Fixture();
        f.Chats.Send(f.Thread, "hello");
        var old = f.Sessions[0];
        old.EmitLateEvents(); // already queued before removal, executed afterward
        f.Chats.Delete(f.Thread);
        Drain();
        Wait(f.Chats.WhenDeleted(f.Thread));
        Check.Eq(f.Chats.Threads.Count, 0, "late completion must not restore the deleted chat");
        Check.True(!File.Exists(f.Saved), "late session start/completion must not restore its JSON");
        Check.Eq(old.Answered, false, "a removed session's approval is denied");
    }

    public static void ReplacedSessionCannotFinishNewAnswer()
    {
        using var f = new Fixture();
        f.Chats.Send(f.Thread, "first");
        var old = f.Sessions[0];
        f.Chats.EndSession(f.Thread);
        f.Chats.Send(f.Thread, "second");
        Wait(f.Chats.WhenPrepared(f.Thread));
        var current = f.Thread.Messages[^1];
        string? status = current.Status;
        old.EmitLateEvents();
        Drain();
        Check.True(current.Streaming, "old completion must not finish the new answer");
        Check.Eq(current.Text, "", "old text must not enter the new answer");
        Check.Eq(current.Status, status, "old status must not affect the new answer");
        Check.Eq(current.Asks.Count, 0, "old approvals must not enter the new answer");
        Check.Eq(f.Thread.SessionId, null, "old session id must not replace resume state");
        Check.True(f.Chats.IsLive(f.Thread), "old exit must not remove the replacement session");
        Check.Eq(old.Answered, false, "old approval is denied on its original session");
        Check.Eq(f.Sessions[1].Answered, null, "new session never receives the old answer");
        f.Sessions[1].CompleteAnswer();
        Drain();
        Wait(f.Chats.WhenFinished(f.Thread));
        Check.True(!current.Streaming, "current completion still finishes its answer");
        Check.Eq(current.Text, "current answer", "current session still streams normally");
        Check.Eq(f.Thread.SessionId, "current-session", "current resume id is saved");
    }

    public static void FailedStartDisposesTheSession()
    {
        using var f = new Fixture { FailStart = true };
        f.Chats.Send(f.Thread, "hello");
        Check.Eq(f.Sessions[0].Disposals, 1, "failed startup releases the session");
        Check.True(!f.Thread.Busy && f.Thread.Messages[^1].Error, "failed send remains an ordinary failed answer");
        Check.True(!f.Chats.IsLive(f.Thread), "failed session is never registered");
    }

    public static void PreviousRunCleanupLeavesCurrentApprovalPending()
    {
        var withdrawn = new List<string>();
        using var old = new PendingToolAsks(withdrawn.Add);
        var prior = old.Add("prior");
        old.Dispose(); // terminal event, before the old process output stream closes
        using var current = new PendingToolAsks(withdrawn.Add);
        var next = current.Add("next");
        old.Dispose(); // old output reader finally exits
        Check.True(prior.IsCompletedSuccessfully && !prior.Result, "old pending approval is denied");
        Check.True(!next.IsCompleted, "old cleanup cannot answer the next run's approval");
        current.Answer("next", true);
        Check.True(next.IsCompletedSuccessfully && next.Result, "current approval can still be allowed");
        Check.Eq(withdrawn.Count, 1, "each unanswered request is withdrawn once");
        Check.True(!old.Add("late").Result, "a completed run cannot accept new requests");
    }

    public static void ApprovalTimeoutAndAnswerCompleteOnlyOnce()
    {
        int withdrawals = 0;
        using var asks = new PendingToolAsks(_ => Interlocked.Increment(ref withdrawals), TimeSpan.FromMilliseconds(20));
        var accepted = asks.Add("accepted");
        asks.Answer("accepted", true);
        var expired = asks.Add("expired");
        Check.True(expired.Wait(TimeSpan.FromSeconds(3)), "unanswered approval must expire");
        Check.True(SpinWait.SpinUntil(() => Volatile.Read(ref withdrawals) == 1, TimeSpan.FromSeconds(3)), "timeout removes its approval card");
        asks.Answer("expired", true);
        asks.Dispose();
        Check.True(accepted.Result, "answered approval keeps its decision after cleanup");
        Check.True(!expired.Result, "late allow cannot reverse timeout denial");
        Check.Eq(withdrawals, 1, "answered requests do not fire withdrawal notifications");
    }
}
