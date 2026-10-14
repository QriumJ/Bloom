using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Ai;
using Bloom.App.Mcp;
using Bloom.Core;

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
        public readonly List<(string Id, bool Allow)> Answers = new();
        public void Answer(ToolAsk ask, bool allow) => Answers.Add((ask.RequestId, allow));
        public void Ask(ToolAsk ask) => ToolAsked?.Invoke(ask);
        public void Interrupt() { }
        public void Dispose() => Alive = false;
        public void Complete(string text) { TextDelta?.Invoke(text); TurnEnded?.Invoke(true, null); }
        public void Fail(string text) { TextDelta?.Invoke(text); TurnEnded?.Invoke(false, "failed"); }
        public void Late() { TextDelta?.Invoke("late delta"); TurnEnded?.Invoke(true, null); }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "bloom-completion-test-" + Guid.NewGuid().ToString("N"));
        public readonly ChatThread Thread = new() { Id = Guid.NewGuid().ToString("N"), Model = "sonnet", Effort = "high" };
        public readonly List<Session> Sessions = new();
        public readonly Chats Chats;
        public string Out => Path.Combine(Root, "files", Thread.Id, "out");

        public Fixture() : this((request, token) => Task.FromResult(ChatOutput.Process(request, token)))
        {
        }

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
    private static void PumpUntil(Func<bool> done, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (done()) return;
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(2) };
        timer.Tick += (_, _) => { if (done() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        Check.True(done(), "completion must finish without blocking the dispatcher (line " + line + ")");
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

    public static void AContentSearchAsksLikeAReadOutsideAndFollowsItsPermission()
    {
        var prior = new Dictionary<string, string>(Settings.Current.Permissions);
        var priorTools = Chats.Tools;
        Chats.Tools = new BloomTools(null!, Dispatcher.CurrentDispatcher);
        try
        {
            Settings.Current.Permissions.Clear();
            using var f = new Fixture();
            f.Chats.Send(f.Thread, "find it");
            var s = f.Sessions[0];
            s.Ask(new ToolAsk("a1", "mcp__bloom__pc", """{"do":"find_files","q":"annual report"}""", ""));
            PumpUntil(() => s.Answers.Count == 1);
            Check.True(s.Answers[0] == ("a1", true) && f.Thread.Messages[^1].Asks.Count == 0, "a plain name search runs without a card");
            s.Ask(new ToolAsk("a2", "mcp__bloom__pc", """{"do":"find_files","q":"regex:content:password"}""", ""));
            PumpUntil(() => f.Thread.Messages[^1].Asks.Count == 1);
            var ask = f.Thread.Messages[^1].Asks[0];
            Check.True(s.Answers.Count == 1 && ask.Key == ToolPermissions.ReadOutside && ask.Detail.Contains("content:password", StringComparison.Ordinal) && ToolPermissions.IsFileOrCommand(ask.Key),
                "a content search waits on the card of a read outside the folder, under that permission's key");
            Settings.Current.Permissions[ToolPermissions.ReadOutside] = ToolPermissions.Off;
            s.Ask(new ToolAsk("a3", "mcp__bloom__pc", """{"do":"find_files","q":"content:password"}""", ""));
            PumpUntil(() => s.Answers.Count == 2);
            Check.True(s.Answers[1] == ("a3", false) && f.Thread.Messages[^1].Asks.Count == 1, "읽기 사용 안 함: refused without a card");
            Settings.Current.Permissions[ToolPermissions.ReadOutside] = ToolPermissions.Allow;
            s.Ask(new ToolAsk("a4", "mcp__bloom__pc", """{"do":"find_files","q":"content:password"}""", ""));
            PumpUntil(() => s.Answers.Count == 3);
            Check.True(s.Answers[2] == ("a4", true), "항상 허용: allowed");
        }
        finally
        {
            Settings.Current.Permissions = prior;
            Chats.Tools = priorTools;
        }
    }

    /// A message sent and the session's reply (a finished answer, or a failed one). The fixture's session never has the
    /// thread's model, so each message gets a session of its own.
    private static void Say(Fixture f, string text, string reply, bool ok)
    {
        int before = f.Sessions.Count;
        f.Chats.Send(f.Thread, text);
        PumpUntil(() => f.Sessions.Count > before && f.Sessions[^1].Sends > 0);
        if (ok) f.Sessions[^1].Complete(reply);
        else f.Sessions[^1].Fail(reply);
    }

    /// The launcher's quick chat and the chat window share Chats: a new API chat under the default permissions (none set)
    /// whose model answers with a file block as small models write it (the `out\` folder in the name) reaches the save
    /// card, and a yes saves the file under its plain name.
    public static void QuickChatFileBlockReachesTheSaveCardUnderDefaultPermissions()
    {
        Bloom.Core.Lang.Force(true);
        var prior = new Dictionary<string, string>(Settings.Current.Permissions);
        try
        {
            Settings.Current.Permissions.Clear();
            using var f = new Fixture();
            f.Thread.Model = "api:gemini:gemini-3.5-flash-lite";
            Say(f, "hello.txt 파일을 만들고 안에 BLOOM TEST라고 써줘", "콰당!\n```file:out\\hello.txt\nBLOOM TEST\n```", ok: true);
            PumpUntil(() => f.Thread.Messages[^1].Asks.Count == 1);
            var ask = f.Thread.Messages[^1].Asks[0];
            Check.True(ask.Key == ToolPermissions.Write && ask.Detail.Contains("hello.txt · a new file", StringComparison.Ordinal) && ask.Detail.Contains("BLOOM TEST", StringComparison.Ordinal), "the card names the file and shows its content");
            f.Chats.Answer(f.Thread, ask, true);
            PumpUntil(() => !f.Thread.Busy);
            Check.Eq(File.ReadAllText(Path.Combine(f.Out, "hello.txt")), "BLOOM TEST", "saved under its plain name");
        }
        finally
        {
            Settings.Current.Permissions.Clear();
            foreach (var (key, value) in prior) Settings.Current.Permissions[key] = value;
        }
    }

    /// An API answer that is only a title line (taken out), with no tool call, is an error rather than an empty
    /// successful reply.
    public static void AnApiAnswerThatIsOnlyATitleLineIsAnError()
    {
        using var f = new Fixture();
        f.Thread.Model = "api:gemini:gemini-flash-lite-latest";
        Say(f, "hi", "<chat-title field=\"Etc\">인사</chat-title>", ok: true);
        PumpUntil(() => !f.Thread.Busy);
        var m = f.Thread.Messages[^1];
        Check.True(m.Error && m.Text == AiErrors.ApiNoAnswer("Gemini") && !m.Text.Contains("chat-title", StringComparison.Ordinal), "no empty answer saved as a success: the no-answer error, without the line");
    }

    public static void FencedFilesFollowTheWritePermissionAndNeverSaveAFailedAnswer()
    {
        Bloom.Core.Lang.Force(true);
        var prior = new Dictionary<string, string>(Settings.Current.Permissions);
        const string answer = "Here:\n```file:report.txt\nthe report\n```\nDone.";
        try
        {
            // 사용 안 함: not saved, the block stays in the text
            Settings.Current.Permissions.Clear();
            Settings.Current.Permissions[ToolPermissions.Write] = ToolPermissions.Off;
            using (var f = new Fixture())
            {
                f.Thread.Model = "api:fake:model"; // an inline model, whose session the fixture supplies
                Say(f, "write", answer, ok: true);
                PumpUntil(() => !f.Thread.Busy);
                Check.True(!File.Exists(Path.Combine(f.Out, "report.txt")) && f.Thread.Messages[^1].Text.Contains("```file:report.txt", StringComparison.Ordinal), "Off saves nothing");
            }
            // 매번 묻기: a card first; no keeps it unsaved, yes saves (a file there is replaced only on a yes, said so)
            Settings.Current.Permissions.Clear();
            using (var f = new Fixture())
            {
                f.Thread.Model = "api:fake:model"; // an inline model, whose session the fixture supplies
                Say(f, "write", answer, ok: true);
                PumpUntil(() => f.Thread.Messages[^1].Asks.Count == 1);
                var ask = f.Thread.Messages[^1].Asks[0];
                Check.True(ask.Key == ToolPermissions.Write && ask.Detail.Contains("report.txt · a new file", StringComparison.Ordinal) && ask.Detail.Contains("the report", StringComparison.Ordinal) && f.Thread.Busy
                    && !File.Exists(Path.Combine(f.Out, "report.txt")), "a card with the file and its content; nothing saved yet");
                f.Chats.Answer(f.Thread, ask, false);
                PumpUntil(() => !f.Thread.Busy);
                Check.True(!File.Exists(Path.Combine(f.Out, "report.txt")) && f.Sessions.All(x => x.Answers.Count == 0), "declined: not saved, and no session was asked");
                Say(f, "again", answer, ok: true);
                PumpUntil(() => f.Thread.Messages[^1].Asks.Count == 1);
                f.Chats.Answer(f.Thread, f.Thread.Messages[^1].Asks[0], true);
                PumpUntil(() => !f.Thread.Busy);
                Check.Eq(File.ReadAllText(Path.Combine(f.Out, "report.txt")), "the report", "allowed: saved");
                Check.True(f.Thread.Messages[^1].Text.Contains("report.txt", StringComparison.Ordinal) && !f.Thread.Messages[^1].Text.Contains("```file:", StringComparison.Ordinal), "and the block replaced by the saved line");
                Say(f, "a third time", answer, ok: true);
                PumpUntil(() => f.Thread.Messages[^1].Asks.Count == 1);
                Check.True(f.Thread.Messages[^1].Asks[0].Detail.Contains("report.txt · replaces the file that is there", StringComparison.Ordinal), "overwriting is asked, and says so");
                f.Chats.Stop(f.Thread);
                PumpUntil(() => !f.Thread.Busy);
                Check.True(f.Thread.Messages[^1].Asks.Count == 0 && File.ReadAllText(Path.Combine(f.Out, "report.txt")) == "the report", "a stop ends the question and saves nothing");
                // files too big for the card to show whole are not asked about, not saved, and the answer says so
                Say(f, "big", "Here:\n```file:big.txt\n" + new string('x', ToolText.MaxPayload + 1) + "\n```\nDone.", ok: true);
                PumpUntil(() => !f.Thread.Busy);
                Check.True(f.Thread.Messages[^1].Asks.Count == 0 && !File.Exists(Path.Combine(f.Out, "big.txt")) && f.Thread.Messages[^1].Text.Contains("```file:big.txt", StringComparison.Ordinal)
                    && f.Thread.Messages[^1].Done?.Any(d => d.StartsWith('✕')) == true, "too big for the card: declined, not saved, no card");
            }
            // 항상 허용 saves; a failed answer never does, whatever the permission
            Settings.Current.Permissions[ToolPermissions.Write] = ToolPermissions.Allow;
            using (var f = new Fixture())
            {
                f.Thread.Model = "api:fake:model"; // an inline model, whose session the fixture supplies
                Say(f, "write", answer, ok: false);
                PumpUntil(() => !f.Thread.Busy);
                Check.True(!File.Exists(Path.Combine(f.Out, "report.txt")), "a failed answer saves no file");
                Say(f, "again", answer, ok: true);
                PumpUntil(() => !f.Thread.Busy);
                Check.Eq(File.ReadAllText(Path.Combine(f.Out, "report.txt")), "the report", "Allow saves without a card");
            }
        }
        finally
        {
            Settings.Current.Permissions = prior;
        }
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
