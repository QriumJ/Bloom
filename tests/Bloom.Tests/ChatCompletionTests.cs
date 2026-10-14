using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Ai;

namespace Bloom.Tests;

internal static class ChatCompletionTests
{
    private sealed class Session : IChatSession
    {
        public string Model => "sonnet";
        public string Effort => "high";
        public string? SessionId => null;
        public bool Alive { get; private set; } = true;
        public bool Busy => false;
        public DateTime LastUsed => DateTime.UtcNow;
        public int Sends;
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
        public bool Send(string text, IReadOnlyList<string> images) { Sends++; return true; }
        public void Answer(ToolAsk ask, bool allow) { }
        public void Interrupt() { }
        public void Dispose() => Alive = false;
        public void Complete(string text) { TextDelta?.Invoke(text); TurnEnded?.Invoke(true, null); }
        public void Late() { TextDelta?.Invoke("late delta"); TurnEnded?.Invoke(true, null); }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "bloom-completion-test-" + Guid.NewGuid().ToString("N"));
        public readonly ChatThread Thread = new() { Id = Guid.NewGuid().ToString("N"), Model = "sonnet", Effort = "high" };
        public readonly List<Session> Sessions = new();
        public readonly Chats Chats;
        public Fixture(Func<ChatOutputRequest, CancellationToken, Task<ChatOutputResult>> process)
        {
            Chats = new Chats(Dispatcher.CurrentDispatcher, (_, _) => { var session = new Session(); Sessions.Add(session); return session; },
                Path.Combine(Root, "chats"), collectOutput: process);
        }
        public void Dispose()
        {
            Chats.Shutdown();
            PumpUntil(() => Chats.WhenDeleted(Thread).IsCompleted && Chats.WhenFinished(Thread).IsCompleted);
            Directory.Delete(Root, true);
        }
    }

    private static TaskCompletionSource<T> Gate<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void PumpUntil(Func<bool> done)
    {
        if (done()) return;
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(2) };
        timer.Tick += (_, _) => { if (done() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        Check.True(done(), "completion must finish without blocking the dispatcher");
    }

    public static void BlockedOutputAllowsInputAndStopOrdersNextProvider()
    {
        var entered = Gate<bool>(); var release = Gate<bool>();
        using var f = new Fixture(async (request, _) =>
        {
            entered.TrySetResult(true);
            await release.Task.ConfigureAwait(false);
            return new ChatOutputResult(request.Text + " processed", null);
        });
        f.Chats.Send(f.Thread, "first");
        f.Sessions[0].Complete("answer");
        PumpUntil(() => entered.Task.IsCompleted);
        bool input = false;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => input = true));
        PumpUntil(() => input);
        Check.True(f.Thread.Busy, "normal completion stays Busy while files are processed");
        f.Sessions[0].Late();
        f.Chats.Stop(f.Thread);
        string stopped = f.Thread.Messages[^1].Text;
        f.Chats.Send(f.Thread, "next");
        Task next = f.Chats.WhenPrepared(f.Thread);
        Check.Eq(f.Sessions.Count, 1, "next provider waits for previous output work");
        release.SetResult(true);
        PumpUntil(() => next.IsCompleted);
        Check.Eq(f.Sessions.Count, 2, "next provider starts after the old output retires");
        Check.Eq(f.Thread.Messages[1].Text, stopped, "cancelled output cannot replace the stopped answer");
        Check.True(f.Thread.Messages[^1].Streaming && f.Thread.Messages[^1].Text.Length == 0, "late events cannot alter the next answer");
    }

    public static void DeleteWaitsForOutputAndNeverRestoresHistory()
    {
        var entered = Gate<bool>(); var release = Gate<bool>();
        using var f = new Fixture(async (request, _) =>
        {
            entered.SetResult(true);
            await release.Task.ConfigureAwait(false);
            Directory.CreateDirectory(request.Directory);
            string file = Path.Combine(request.Directory, "late.txt"); File.WriteAllText(file, "late output");
            return new ChatOutputResult("late result", new[] { file });
        });
        f.Chats.Send(f.Thread, "first"); f.Sessions[0].Complete("answer");
        PumpUntil(() => entered.Task.IsCompleted);
        f.Chats.Delete(f.Thread);
        Task deletion = f.Chats.WhenDeleted(f.Thread);
        Check.True(!deletion.IsCompleted, "explicit deletion waits for the outstanding writer");
        release.SetResult(true);
        PumpUntil(() => deletion.IsCompleted);
        f.Chats.Flush();
        Check.True(!Directory.Exists(Path.Combine(f.Root, "files", f.Thread.Id)), "output cannot recreate the deleted folder");
        Check.True(!File.Exists(Path.Combine(f.Root, "chats", f.Thread.Id + ".json")), "output cannot restore deleted JSON");
        Check.Eq(f.Chats.Threads.Count, 0, "output cannot restore the deleted conversation");
    }

    public static void OutputFailureFinishesAnswerAndSessionCanContinue()
    {
        using var f = new Fixture((_, _) => Task.FromException<ChatOutputResult>(new IOException("output unavailable")));
        f.Chats.Send(f.Thread, "first"); f.Sessions[0].Complete("answer");
        PumpUntil(() => !f.Thread.Busy);
        Check.True(f.Thread.Messages[^1].Error && f.Thread.Messages[^1].Text.Contains("output unavailable", StringComparison.Ordinal), "output failure finishes as a visible failed answer");
        f.Chats.Send(f.Thread, "next");
        PumpUntil(() => f.Chats.WhenPrepared(f.Thread).IsCompleted);
        Check.True(f.Thread.Busy && f.Sessions[0].Sends == 2, "a file failure does not wedge the session");
    }

    public static void ShutdownIgnoresLateOutputAndPersistsStoppedSnapshot()
    {
        var entered = Gate<bool>(); var release = Gate<bool>();
        using var f = new Fixture(async (request, _) =>
        {
            entered.SetResult(true);
            await release.Task.ConfigureAwait(false);
            return new ChatOutputResult("obsolete output", null);
        });
        f.Chats.Send(f.Thread, "first"); f.Sessions[0].Complete("partial");
        PumpUntil(() => entered.Task.IsCompleted);
        f.Chats.Shutdown();
        string path = Path.Combine(f.Root, "chats", f.Thread.Id + ".json");
        string saved = File.ReadAllText(path);
        release.SetResult(true);
        PumpUntil(() => f.Chats.WhenFinished(f.Thread).IsCompleted);
        Check.Eq(File.ReadAllText(path), saved, "late output cannot write after shutdown closes persistence");
        Check.True(f.Thread.Messages[^1].Error && !f.Thread.Busy, "shutdown snapshot records the partial stopped answer");
    }

    public static void GeneratedFilesKeepPathsTextAndFirstTwentyOrdering()
    {
        string root = Path.Combine(Path.GetTempPath(), "bloom-output-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (int n = 25; n >= 1; n--) File.WriteAllText(Path.Combine(root, $"{n:00}.txt"), "existing");
            var result = ChatOutput.Process(new ChatOutputRequest("before\n```file:report.txt\ncontents\n```\nafter", root, DateTime.UtcNow.AddMinutes(-1), true, "**{0}** saved"), CancellationToken.None);
            Check.Eq(File.ReadAllText(Path.Combine(root, "report.txt")), "contents", "generated file keeps its established name and contents");
            Check.Eq(result.Text, "before\n**report.txt** saved\nafter", "saved block is replaced in the final answer");
            Check.Eq(result.Files!.Length, 20, "output attachment cap stays twenty");
            Check.True(result.Files.Select(Path.GetFileName).SequenceEqual(Enumerable.Range(1, 20).Select(n => $"{n:00}.txt")), "first filenames retain ordinal sorted order");
        }
        finally { Directory.Delete(root, true); }
    }
}
