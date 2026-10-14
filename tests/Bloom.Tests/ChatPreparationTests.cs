using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Ai;

namespace Bloom.Tests;

internal static class ChatPreparationTests
{
    private sealed class Session : IChatSession
    {
        public string Model => "sonnet";
        public string Effort => "high";
        public string? SessionId => null;
        public bool Alive { get; private set; } = true;
        public bool Busy => false;
        public DateTime LastUsed => DateTime.UtcNow;
        public int Sends, Disposals;
        public TaskCompletionSource<SessionStart>? StartGate;
        public TaskCompletionSource<bool>? SendGate;
#pragma warning disable CS0067
        public event Action<string>? SessionStarted;
        public event Action<string>? TextDelta;
        public event Action<string?>? Status;
        public event Action<string>? AssistantText;
        public event Action<bool, string?>? TurnEnded;
        public event Action<string>? Exited;
        public event Action<ToolAsk>? ToolAsked;
        public event Action<string>? ToolAskWithdrawn;
#pragma warning restore CS0067
        public bool Start(string? resume, out string? error) { error = null; return true; }
        public Task<SessionStart> StartAsync(string? resume, CancellationToken cancellation) => StartGate?.Task ?? Task.FromResult(new SessionStart(true));
        public bool Send(string text, IReadOnlyList<string> images) { Sends++; return true; }
        public Task<bool> SendAsync(string text, IReadOnlyList<string> images, CancellationToken cancellation)
        { Sends++; return SendGate?.Task ?? Task.FromResult(true); }
        public void Answer(ToolAsk ask, bool allow) { }
        public void Interrupt() { }
        public void Dispose() { Disposals++; Alive = false; }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "bloom-prepare-test-" + Guid.NewGuid().ToString("N"));
        public readonly List<Session> Sessions = new();
        public readonly ChatThread Thread = new() { Id = Guid.NewGuid().ToString("N"), Model = "sonnet", Effort = "high" };
        public readonly Chats Chats;
        public Action<Session>? Configure;
        public Fixture(Func<string, IReadOnlyList<string>, Func<List<string>, string>, CancellationToken, Task<PreparedAttachments>>? copy = null)
        {
            Chats = new Chats(Dispatcher.CurrentDispatcher, (_, _) =>
            {
                var s = new Session(); Configure?.Invoke(s); Sessions.Add(s); return s;
            }, Path.Combine(Root, "chats"), copy);
        }
        public void Dispose()
        {
            Chats.Shutdown();
            PumpUntil(() => Chats.WhenDeleted(Thread).IsCompleted);
            Directory.Delete(Root, true);
        }
    }

    private static TaskCompletionSource<T> Gate<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void PumpUntil(Func<bool> done)
    {
        var frame = new DispatcherFrame();
        var timeout = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(2) };
        timer.Tick += (_, _) => { if (done() || DateTime.UtcNow >= timeout) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        Check.True(done(), "preparation must finish without blocking the dispatcher");
    }

    public static void BlockedStartAllowsUiAndStopCannotSendLater()
    {
        using var f = new Fixture();
        var start = Gate<SessionStart>();
        f.Configure = s => s.StartGate = start;
        f.Chats.Send(f.Thread, "first");
        Task pending = f.Chats.WhenPrepared(f.Thread);
        bool input = false;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => input = true));
        PumpUntil(() => input);
        Check.True(!pending.IsCompleted && f.Thread.Busy, "startup remains blocked while UI input runs");
        f.Chats.Stop(f.Thread);
        Check.True(!f.Thread.Busy && f.Sessions[0].Disposals == 1, "Stop immediately owns and disposes the starting session");
        f.Configure = null;
        f.Chats.Send(f.Thread, "replacement");
        var answer = f.Thread.Messages[^1];
        start.SetResult(new SessionStart(true));
        PumpUntil(() => pending.IsCompleted);
        pending.GetAwaiter().GetResult();
        Check.Eq(f.Sessions[0].Sends, 0, "late startup never sends the cancelled prompt");
        Check.True(answer.Streaming && f.Chats.IsLive(f.Thread), "late startup cannot finish the replacement");
    }

    public static void SentMessageKeepsThePageForALaterSession()
    {
        using var f = new Fixture();
        f.Chats.Send(f.Thread, "sum it up", "the page's own words", "Page");
        PumpUntil(() => f.Chats.WhenPrepared(f.Thread).IsCompleted);
        var user = f.Thread.Messages[0];
        Check.True(user.Sent is { } sent && sent.Contains("the page's own words", StringComparison.Ordinal) && sent.EndsWith("sum it up", StringComparison.Ordinal), "the message as sent, page and all");
        string json = System.Text.Json.JsonSerializer.Serialize(user, Bloom.Core.Json.Options);
        Check.True(System.Text.Json.JsonSerializer.Deserialize<ChatMessage>(json, Bloom.Core.Json.Options)?.Sent == user.Sent, "kept in the chat's file");
    }

    public static void DeletedAttachmentPreparationRollsBackOnlyItsBatch()
    {
        var entered = Gate<bool>(); var release = Gate<bool>();
        PreparedAttachments? batch = null;
        using var f = new Fixture(async (folder, files, inline, _) =>
        {
            entered.TrySetResult(true);
            await release.Task.ConfigureAwait(false);
            var copied = await PreparedAttachments.Copy(folder, files, inline, CancellationToken.None).ConfigureAwait(false);
            batch ??= copied;
            return copied; // simulates an I/O implementation completing after cancellation
        });
        string source = Path.Combine(f.Root, "source.txt"); File.WriteAllText(source, "owned contents");
        f.Chats.Send(f.Thread, "attachment", files: new[] { source });
        Task pending = f.Chats.WhenPrepared(f.Thread);
        PumpUntil(() => entered.Task.IsCompleted);
        f.Chats.Delete(f.Thread);
        Task deletion = f.Chats.WhenDeleted(f.Thread);
        Check.True(!deletion.IsCompleted, "folder cleanup waits for the open preparation");
        var replacement = new ChatThread { Id = f.Thread.Id, Model = "sonnet", Effort = "high" };
        f.Chats.Send(replacement, "replacement", files: new[] { source });
        Task replacementPrepared = f.Chats.WhenPrepared(replacement);
        var answer = replacement.Messages[^1];
        release.SetResult(true);
        PumpUntil(() => pending.IsCompleted);
        pending.GetAwaiter().GetResult();
        PumpUntil(() => deletion.IsCompleted && replacementPrepared.IsCompleted);
        Check.True(batch is not null && !File.Exists(batch.Files[0]), "old preparation owns and rolls back its copy");
        Check.True(File.Exists(source), "original file remains untouched");
        Check.True(replacement.Messages[0].Files is { Count: 1 } current && File.Exists(current[0]), "same-id replacement waits for deletion and keeps its new attachment");
        Check.True(answer.Streaming && f.Sessions.Count == 1, "late copy neither starts a session nor finishes the new answer");
    }

    public static void ShutdownDuringSendIgnoresLateFailure()
    {
        using var f = new Fixture(); var send = Gate<bool>();
        f.Configure = s => s.SendGate = send;
        f.Chats.Send(f.Thread, "first");
        Task pending = f.Chats.WhenPrepared(f.Thread);
        var answer = f.Thread.Messages[^1];
        f.Chats.Shutdown();
        string stopped = answer.Text;
        send.SetException(new IOException("delayed write failure"));
        PumpUntil(() => pending.IsCompleted);
        pending.GetAwaiter().GetResult();
        Check.True(answer.Error && answer.Text == stopped && !f.Chats.IsLive(f.Thread), "shutdown saves the stopped answer and suppresses late failure/session resurrection");
        Check.Eq(f.Sessions[0].Disposals, 1, "shutdown releases the pending session once");
    }

    public static void ReentrantDeleteBeforePreparationDoesNotDeadlockReplacement()
    {
        using var f = new Fixture();
        string source = Path.Combine(f.Root, "source.txt"); File.WriteAllText(source, "attachment");
        var replacement = new ChatThread { Id = f.Thread.Id, Model = "sonnet", Effort = "high" };
        bool deleted = false;
        f.Chats.Updated += thread =>
        {
            if (deleted || !ReferenceEquals(thread, f.Thread)) return;
            deleted = true;
            f.Chats.Delete(thread);
            f.Chats.Send(replacement, "replacement", files: new[] { source });
        };
        f.Chats.Send(f.Thread, "cancel before copy", files: new[] { source });
        Task pending = f.Chats.WhenPrepared(replacement);
        PumpUntil(() => pending.IsCompleted && f.Chats.WhenDeleted(f.Thread).IsCompleted);
        Check.Eq(f.Sessions.Count, 1, "only the replacement starts a provider session");
        Check.True(replacement.Messages[0].Files is { Count: 1 } files && File.Exists(files[0]), "replacement files are created after explicit deletion finishes");
    }

    public static void AttachmentBatchesPreserveNamesAndOwnCleanup()
    {
        string root = Path.Combine(Path.GetTempPath(), "bloom-batch-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "report.txt"); File.WriteAllText(source, "report");
            using var old = PreparedAttachments.Copy(Path.Combine(root, "thread"), new[] { source }, _ => "inline", CancellationToken.None).GetAwaiter().GetResult();
            using var next = PreparedAttachments.Copy(Path.Combine(root, "thread"), new[] { source }, _ => "inline", CancellationToken.None).GetAwaiter().GetResult();
            next.Accept(); old.Dispose();
            Check.True(old.Files[0] != next.Files[0] && !File.Exists(old.Files[0]), "batches never share rollback paths");
            Check.True(File.Exists(next.Files[0]) && File.Exists(source), "accepted and original files survive old cleanup");
            Check.Eq(Path.GetFileName(next.Files[0]), "report.txt", "original filename stays visible");
            Check.Eq(next.Inline, "inline", "prepared prompt text travels with its batch");
        }
        finally { Directory.Delete(root, true); }
    }

    public static void DeleteSessionNotificationCanSafelySendSameId()
    {
        using var f = new Fixture();
        f.Chats.Send(f.Thread, "first");
        string source = Path.Combine(f.Root, "source.txt"); File.WriteAllText(source, "attachment");
        var replacement = new ChatThread { Id = f.Thread.Id, Model = "sonnet", Effort = "high" };
        bool sent = false;
        f.Chats.SessionsChanged += () =>
        {
            if (sent) return;
            sent = true;
            f.Chats.Send(replacement, "replacement", files: new[] { source });
        };
        f.Chats.Delete(f.Thread);
        Task pending = f.Chats.WhenPrepared(replacement);
        PumpUntil(() => pending.IsCompleted && f.Chats.WhenDeleted(f.Thread).IsCompleted);
        Check.True(replacement.Messages[0].Files is { Count: 1 } files && File.Exists(files[0]), "notification starts new attachments after the old folder deletion");
        f.Chats.Flush();
        Check.True(File.Exists(Path.Combine(f.Root, "chats", f.Thread.Id + ".json")), "old delete cannot erase the replacement JSON");
        Check.True(f.Chats.Threads.Contains(replacement), "old delete cannot remove the replacement thread");
    }

    public static void StopThenDeleteWaitsForRetiredAttachmentCopy()
    {
        var entered = Gate<bool>(); var release = Gate<bool>();
        using var f = new Fixture(async (folder, files, inline, _) =>
        {
            entered.SetResult(true);
            await release.Task.ConfigureAwait(false);
            return await PreparedAttachments.Copy(folder, files, inline, CancellationToken.None).ConfigureAwait(false);
        });
        string source = Path.Combine(f.Root, "source.txt"); File.WriteAllText(source, "attachment");
        f.Chats.Send(f.Thread, "first", files: new[] { source });
        Task retired = f.Chats.WhenPrepared(f.Thread);
        PumpUntil(() => entered.Task.IsCompleted);
        f.Chats.Stop(f.Thread);
        f.Chats.Delete(f.Thread);
        Task deletion = f.Chats.WhenDeleted(f.Thread);
        Check.True(!deletion.IsCompleted, "Delete includes a still-running preparation retired by Stop");
        release.SetResult(true);
        PumpUntil(() => retired.IsCompleted && deletion.IsCompleted);
        Check.True(!Directory.Exists(Path.Combine(f.Root, "files", f.Thread.Id)), "retired copy cannot recreate the explicitly deleted thread folder");
        Check.Eq(f.Sessions.Count, 0, "retired copy never starts a provider");
    }
}
