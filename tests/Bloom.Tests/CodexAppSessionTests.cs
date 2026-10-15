using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Ai;

namespace Bloom.Tests;

/// CodexAppSession against a stand-in `codex app-server`: the handshake, streaming, how turns end, resume ids, stopping,
/// and the fall back to `codex exec`.
internal static class CodexAppSessionTests
{
    /// The app-server's side of the pipe: answers Bloom's calls (Respond), records every message, sends notifications.
    private sealed class FakeAppServer
    {
        private readonly BlockingCollection<string> toBloom = new();
        private readonly ManualResetEventSlim exited = new(), treeGone = new();
        private readonly List<JsonObject> received = new();
        private int turns, kills;
        public bool InputClosed, Killed;
        /// Runs once, as the process is ended (what it does then: a refresh it had out lands).
        public Action? OnKill;
        /// Set: a process the app-server started outlives it, until KillsToEnd ends have been made (the first ones fail).
        public bool ChildStays;
        public int KillsToEnd = 1;
        public ProcessStartInfo? Launched;
        /// method, params → the result (null: no answer); an exception becomes a JSON-RPC error.
        public Func<string, JsonObject, JsonNode?> Respond;

        public FakeAppServer() => Respond = Default;

        public int Kills => Volatile.Read(ref kills);

        private JsonNode? Default(string method, JsonObject p) => method switch
        {
            "initialize" => new JsonObject { ["userAgent"] = "codex", ["codexHome"] = "C:\\h", ["platformFamily"] = "windows", ["platformOs"] = "windows" },
            "thread/start" => new JsonObject { ["thread"] = new JsonObject { ["id"] = "thr-new" } },
            "thread/resume" => new JsonObject { ["thread"] = new JsonObject { ["id"] = p["threadId"]!.GetValue<string>() } },
            "turn/start" => new JsonObject { ["turn"] = new JsonObject { ["id"] = "turn-" + Interlocked.Increment(ref turns), ["status"] = "inProgress", ["items"] = new JsonArray() } },
            "turn/interrupt" => new JsonObject(),
            _ => throw new InvalidOperationException("unknown variant " + method),
        };

        public AppServerPipe Pipe(ProcessStartInfo psi)
        {
            Launched = psi;
            return new AppServerPipe
            {
                Id = 4242,
                Output = new QueueReader(toBloom),
                Write = (line, _) =>
                {
                    Received(line);
                    return Task.CompletedTask;
                },
                CloseInput = () =>
                {
                    InputClosed = true;
                    End();
                },
                Kill = () =>
                {
                    Killed = true;
                    Interlocked.Exchange(ref OnKill, null)?.Invoke();
                    End();
                    if (Interlocked.Increment(ref kills) >= KillsToEnd) treeGone.Set();
                },
                WaitForExit = ms => (ChildStays ? treeGone : exited).Wait(ms),
                Errors = () => "",
            };
        }

        private void Received(string line)
        {
            var message = JsonNode.Parse(line)!.AsObject();
            lock (received) received.Add(message);
            if (message["id"] is not { } id || message["method"] is not { } method) return;
            JsonObject reply;
            try
            {
                var result = Respond(method.GetValue<string>(), message["params"]?.AsObject() ?? new JsonObject());
                if (result is null) return;
                reply = new JsonObject { ["id"] = id.DeepClone(), ["result"] = result };
            }
            catch (InvalidOperationException e)
            {
                reply = new JsonObject { ["id"] = id.DeepClone(), ["error"] = new JsonObject { ["code"] = -32600, ["message"] = e.Message } };
            }
            Push(reply);
        }

        public void Push(JsonObject message)
        {
            if (!toBloom.IsAddingCompleted) toBloom.Add(message.ToJsonString());
        }

        public void Notify(string method, JsonObject p) => Push(new JsonObject { ["method"] = method, ["params"] = p });

        public void End()
        {
            toBloom.CompleteAdding();
            exited.Set();
        }

        public List<JsonObject> Messages()
        {
            lock (received) return received.ToList();
        }

        public List<string> Methods() => Messages().Select(m => m["method"]?.GetValue<string>() ?? "(reply)").ToList();

        public JsonObject Call(string method) => Messages().Last(m => m["method"]?.GetValue<string>() == method)["params"]!.AsObject();
    }

    /// What Bloom reads: the lines the fake queues, null once it has ended.
    private sealed class QueueReader(BlockingCollection<string> lines) : TextReader
    {
        public override string? ReadLine()
        {
            try { return lines.Take(); }
            catch (InvalidOperationException) { return null; }
        }
    }

    /// The `codex exec` session the fallback hands over to.
    private sealed class StubSession : IChatSession
    {
        public string Model => "chatgpt:gpt-test";
        public string Effort => "low";
        public string? SessionId => null;
        public bool Busy { get; private set; }
        public bool Alive { get; private set; }
        public DateTime LastUsed => DateTime.UtcNow;
        public string? Resume, Sent;
        public int Disposals;
        public event Action<string>? SessionStarted { add { } remove { } }
        public event Action<string>? TextDelta { add { } remove { } }
        public event Action<string?>? Status { add { } remove { } }
        public event Action<string>? AssistantText;
        public event Action<bool, string?>? TurnEnded;
        public event Action<string>? Exited { add { } remove { } }
        public event Action<ToolAsk>? ToolAsked { add { } remove { } }
        public event Action<string>? ToolAskWithdrawn { add { } remove { } }

        public bool Start(string? resume, out string? error)
        {
            Resume = resume;
            error = null;
            return Alive = true;
        }

        public bool Send(string text, IReadOnlyList<string> images)
        {
            Sent = text;
            return Busy = true;
        }

        public void Answer(ToolAsk ask, bool allow) { }
        public void Interrupt() { }
        public void Dispose() { Disposals++; Alive = false; }

        public void Reply(string text)
        {
            AssistantText?.Invoke(text);
            Busy = false;
            TurnEnded?.Invoke(true, null);
        }
    }

    /// The events a session raised, in order.
    private sealed class Seen
    {
        private readonly List<string> events = new();
        public readonly List<(bool ok, string? error)> Ends = new();

        public Seen(IChatSession s)
        {
            s.SessionStarted += id => Add("started:" + id);
            s.TextDelta += d => Add("delta:" + d);
            s.Status += st => Add("status:" + (st ?? "-"));
            s.AssistantText += a => Add("text:" + a);
            s.TurnEnded += (ok, e) =>
            {
                lock (events) Ends.Add((ok, e));
                Add("end:" + ok);
            };
            s.Exited += _ => Add("exited");
        }

        private void Add(string e)
        {
            lock (events) events.Add(e);
        }

        public List<string> All()
        {
            lock (events) return events.ToList();
        }

        public string Text() => string.Concat(All().Where(e => e.StartsWith("delta:", StringComparison.Ordinal)).Select(e => e["delta:".Length..]));

        public int EndCount()
        {
            lock (events) return Ends.Count;
        }
    }

    /// A new stand-in for each launch: a process started again is a new one.
    private sealed class Launches
    {
        private readonly List<FakeAppServer> servers = new();

        public FakeAppServer this[int i]
        {
            get { lock (servers) return servers[i]; }
        }

        public int Count
        {
            get { lock (servers) return servers.Count; }
        }

        public AppServerPipe Launch(ProcessStartInfo psi)
        {
            var server = new FakeAppServer();
            lock (servers) servers.Add(server);
            return server.Pipe(psi);
        }
    }

    private static readonly string Work = Path.Combine(Path.GetTempPath(), "bloom-codex-app-test");
    private static readonly string Account = Path.Combine(Work, "account"); // the chat account's folder (its CODEX_HOME)

    private static CodexAppSession Session(FakeAppServer server, Func<IChatSession>? fallback = null, int grace = 150, string? account = null) =>
        Session(server.Pipe, fallback, grace, account);

    private static CodexAppSession Session(Func<ProcessStartInfo, AppServerPipe?> launch, Func<IChatSession>? fallback = null, int grace = 150, string? account = null) =>
        new("chatgpt:gpt-test", "low", "conv-1", fallback, launch)
        {
            CliPath = "codex.exe",
            WorkDir = Work,
            AccountHome = account ?? Signed(Account),
            InterruptGrace = TimeSpan.FromMilliseconds(grace),
            ExitGrace = 100,
            KillWait = 200,
        };

    /// A sign-in as Codex writes it, for the account `who`.
    private static string Tokens(string who) =>
        new JsonObject { ["tokens"] = new JsonObject { ["access_token"] = "a-" + who, ["refresh_token"] = "r-" + who, ["account_id"] = who } }.ToJsonString();

    /// The account folder, signed in (as `who` when it wasn't yet).
    private static string Signed(string account, string who = "acct-1")
    {
        Directory.CreateDirectory(account);
        string auth = Path.Combine(account, "auth.json");
        if (!File.Exists(auth)) File.WriteAllText(auth, Tokens(who));
        return account;
    }

    private static string NewAccount(string who) => Signed(Path.Combine(Work, "account-" + Guid.NewGuid().ToString("N")), who);

    /// Another sign-in in a new file put in the account's place (a sign-in written anew, CodexAuth's tokens moved in).
    private static void Replace(string account, string who)
    {
        string auth = Path.Combine(account, "auth.json"), temp = auth + ".new";
        File.WriteAllText(temp, Tokens(who));
        File.Move(temp, auth, true);
    }

    private static string? Text(string file)
    {
        try { return File.ReadAllText(file); }
        catch (IOException) { return null; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string link, string existing, IntPtr security);

    private static bool Until(Func<bool> done, int ms = 3000) => SpinWait.SpinUntil(done, ms);

    private static void Started(CodexAppSession s, string? resume = null)
    {
        var start = s.StartAsync(resume, CancellationToken.None);
        Check.True(start.Wait(5000), "the start must finish");
        Check.True(start.Result.Success, "the start succeeds: " + start.Result.Error);
    }

    public static void HandshakeStartsTheThreadAsCodexExecWould()
    {
        var server = new FakeAppServer();
        using var s = Session(server);
        Started(s);
        Check.Eq(string.Join(",", server.Launched!.ArgumentList), "app-server,--listen,stdio://", "app-server on stdio");
        Check.Eq(server.Launched.WorkingDirectory, Work, "in the chats' folder");
        Check.Eq(server.Launched.Environment["CODEX_HOME"], CodexAppHome.Folder(Account), "in Bloom's folder for the account, not the account's own");
        Check.Eq(string.Join(" ", server.Methods()), "initialize initialized thread/start", "the handshake, in order");
        var client = server.Call("initialize")["clientInfo"]!;
        Check.True(client["name"]!.GetValue<string>() == "bloom" && client["title"]!.GetValue<string>() == "Bloom" && client["version"]!.GetValue<string>().Length > 0, "Bloom introduces itself with its version");
        var p = server.Call("thread/start");
        Check.Eq(p["sandbox"]!.GetValue<string>(), "read-only", "read-only sandbox");
        Check.Eq(p["approvalPolicy"]!.GetValue<string>(), "never", "Codex asks nothing (Bloom's server asks)");
        Check.Eq(p["cwd"]!.GetValue<string>(), Work, "the chats' folder");
        Check.Eq(p["model"]!.GetValue<string>(), "gpt-test", "the model picked");
        Check.True(p["baseInstructions"]!.GetValue<string>().Contains("ChatGPT", StringComparison.Ordinal), "Bloom's own instructions");
        var config = p["config"]!;
        Check.Eq(config["model_reasoning_effort"]!.GetValue<string>(), "low", "the level picked");
        Check.Eq(config["web_search"]!.GetValue<string>(), "live", "live web search");
        Check.True(config["include_environment_context"]!.GetValue<bool>() == false && config["include_permissions_instructions"]!.GetValue<bool>() == false, "no environment or permission notes");
        Check.True(p["modelProvider"] is null && p["serviceTier"] is null && config["notify"] is null && config["features"] is null, "nothing of the account's file to switch off");
        Check.True(s.Alive && !s.Busy && s.SessionId is null && s.ProcessId == 4242, "running, no turn yet");
    }

    /// The account's config.toml, however it is set, is not what app-server reads: its folder is Bloom's, with Bloom's
    /// config.toml, the account's own sign-in (the same file) and its sessions.
    public static void AHostileUserConfigHasNoEffect()
    {
        string account = Path.Combine(Work, "hostile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(account, "sessions", "2026"));
        File.WriteAllText(Path.Combine(account, "config.toml"), """
            openai_base_url = "http://127.0.0.1:9/v1"
            chatgpt_base_url = "http://127.0.0.1:9/backend-api/"
            model_provider = "evil"
            sandbox_mode = "danger-full-access"
            approval_policy = "on-request"
            [model_providers.evil]
            name = "evil"
            base_url = "http://127.0.0.1:9/v1"
            [shell_environment_policy]
            inherit = "all"
            ignore_default_excludes = true
            set = { EVIL = "1" }
            [mcp_servers.evil]
            command = "evil.exe"
            """);
        File.WriteAllText(Path.Combine(account, "auth.json"), """{"tokens":{"refresh_token":"one"}}""");
        File.WriteAllText(Path.Combine(account, "sessions", "2026", "rollout-x-thr-old.jsonl"), "{}");
        var server = new FakeAppServer();
        using var s = Session(server, account: account);
        Started(s, "thr-old");
        string home = server.Launched!.Environment["CODEX_HOME"]!;
        Check.True(home != account && Path.GetDirectoryName(home) == account, "app-server runs in Bloom's folder inside the account's");
        Check.True(CodexAppHome.Ready(home), "a start there is known to have happened (the next isn't given the first one's time)");
        Check.Eq(File.ReadAllText(Path.Combine(home, "config.toml")), CodexAppHome.Config, "its config.toml is Bloom's");
        foreach (string bad in new[] { "127.0.0.1", "evil", "danger", "inherit", "on-request" })
            Check.True(!File.ReadAllText(Path.Combine(home, "config.toml")).Contains(bad, StringComparison.Ordinal), "nothing of the account's file: " + bad);
        Check.True(CodexAppHome.Config.Contains("sandbox_mode = \"read-only\"", StringComparison.Ordinal) && CodexAppHome.Config.Contains("cli_auth_credentials_store = \"file\"", StringComparison.Ordinal),
            "read-only before any thread; the sign-in stays in auth.json");
        var p = server.Call("thread/resume");
        Check.True(p["sandbox"]!.GetValue<string>() == "read-only" && p["config"]!["mcp_servers"]!["evil"] is null, "the thread too: read-only, no server of the file's");
        Check.True(server.Launched.Environment.Keys.All(k => !k.StartsWith("CODEX_", StringComparison.OrdinalIgnoreCase) || k == "CODEX_HOME"), "no other Codex setting comes from Bloom's environment");
        // the sign-in is the account's file itself: a refresh either side writes is the other's
        File.WriteAllText(Path.Combine(account, "auth.json"), """{"tokens":{"refresh_token":"two"}}""");
        Check.True(File.ReadAllText(Path.Combine(home, "auth.json")).Contains("two", StringComparison.Ordinal), "a refresh in the account's folder is seen in Bloom's");
        File.WriteAllText(Path.Combine(home, "auth.json"), """{"tokens":{"refresh_token":"three"}}""");
        Check.True(File.ReadAllText(Path.Combine(account, "auth.json")).Contains("three", StringComparison.Ordinal), "and one app-server writes lands in the account's");
        Check.True(s.Send("hi", []) && s.ProcessId == 4242, "a refresh is not a new sign-in: the process goes on");
        Check.True(File.Exists(Path.Combine(home, "sessions", "2026", "rollout-x-thr-old.jsonl")), "the account's sessions are its sessions (saved threads resume)");
        File.WriteAllText(Path.Combine(home, "sessions", "new.jsonl"), "{}");
        Check.True(File.Exists(Path.Combine(account, "sessions", "new.jsonl")), "and new ones are kept in the account's folder, as `codex exec` keeps them");
    }

    /// A re-login while a process waits unused: a new file in the account's place ends the process before Bloom's folder
    /// links it, and the next start runs on the new sign-in, resuming the thread. Another account signed in over the same
    /// file ends it too.
    public static void AReLoginWhileWarmEndsTheProcessBeforeTheLinkMoves()
    {
        string account = NewAccount("acct-1"), link = Path.Combine(CodexAppHome.Folder(account), "auth.json");
        var launches = new Launches();
        using var s = Session(launches.Launch, account: account);
        var seen = new Seen(s);
        Started(s, "thr-1");
        Check.True(Text(link)?.Contains("acct-1", StringComparison.Ordinal) == true, "Bloom's folder has the account's sign-in while it runs");
        string? atEnd = "(not ended)";
        launches[0].OnKill = () => atEnd = Text(link);
        Replace(account, "acct-2");
        Check.True(CodexAppHome.Changed(account).Wait(5000), "looked at");
        Check.True(launches[0].Killed, "the waiting process is ended");
        Check.True(atEnd?.Contains("acct-1", StringComparison.Ordinal) == true, "while Bloom's file is still the old sign-in: nothing is linked anew under it");
        Check.True(Until(() => seen.All().Contains("exited")) && !s.Alive, "the chat hears it ended");
        Check.True(Until(() => !File.Exists(link)), "with none running, no sign-in is kept in Bloom's folder");
        Check.True(File.ReadAllText(Path.Combine(account, "auth.json")).Contains("acct-2", StringComparison.Ordinal), "the new sign-in is untouched");

        using var t = Session(launches.Launch, account: account);
        Started(t, "thr-1");
        Check.Eq(launches[1].Call("thread/resume")["threadId"]!.GetValue<string>(), "thr-1", "the next start resumes the thread");
        Check.True(Text(link)?.Contains("acct-2", StringComparison.Ordinal) == true, "on the new sign-in");

        File.WriteAllText(Path.Combine(account, "auth.json"), Tokens("acct-3")); // another account, written into the same file
        Check.True(CodexAppHome.Changed(account).Wait(5000), "looked at");
        Check.True(launches[1].Killed && Until(() => !t.Alive), "a process on the other account's sign-in ends");
    }

    /// A re-login during an answer: the turn ends saying so, and the process is gone before the new file is linked — a
    /// refresh it had out lands in the old file, not over the new sign-in. The conversation goes on in a new process.
    public static void AReLoginMidTurnEndsTheTurnAndKeepsTheNewSignIn()
    {
        string account = NewAccount("acct-1"), auth = Path.Combine(account, "auth.json"), link = Path.Combine(CodexAppHome.Folder(account), "auth.json");
        var launches = new Launches();
        using var s = Session(launches.Launch, account: account);
        var seen = new Seen(s);
        Started(s);
        Check.True(s.Send("long", []), "sent");
        Check.True(Until(() => seen.All().Contains("started:thr-new")), "the answer is under way");
        // Codex 0.159.3 writes a refresh back after its request without looking again: the old account's tokens, through its file
        launches[0].OnKill = () => File.WriteAllText(link, Tokens("acct-1-late"));
        Replace(account, "acct-2");
        Check.True(CodexAppHome.Changed(account).Wait(5000), "looked at");
        Check.True(Until(() => seen.EndCount() == 1) && seen.Ends[0] == (false, AiErrors.SignInChanged), "the answer ends, saying why");
        Check.True(Until(() => seen.All().Contains("exited")), "and the session with it");
        Check.True(File.ReadAllText(auth).Contains("acct-2", StringComparison.Ordinal) && !File.ReadAllText(auth).Contains("late", StringComparison.Ordinal), "the new sign-in isn't written over");
        Check.True(Until(() => !File.Exists(link)), "nothing of the old one is kept in Bloom's folder");
        Check.Eq(s.SessionId, "thr-new", "the thread to go on with");

        using var t = Session(launches.Launch, account: account);
        Started(t, s.SessionId);
        Check.True(t.Send("again", []) && Until(() => launches[1].Methods().Contains("turn/start")), "the next message goes to a new process on the thread");
        Check.True(Text(link)?.Contains("acct-2", StringComparison.Ordinal) == true, "on the new sign-in");
    }

    /// A sign-out (the account's auth.json gone, as `codex logout` leaves it): the watch ends the process at once and the
    /// link goes, so the old tokens aren't kept in Bloom's folder; the next start doesn't go on app-server.
    public static void ASignOutEndsTheProcessAndTakesTheLinkAway()
    {
        string account = NewAccount("acct-1"), link = Path.Combine(CodexAppHome.Folder(account), "auth.json");
        var launches = new Launches();
        using var s = Session(launches.Launch, account: account);
        Started(s, "thr-1");
        Check.True(File.Exists(link), "linked while it runs");
        File.Delete(Path.Combine(account, "auth.json"));
        Check.True(Until(() => launches[0].Killed, 5000), "the watch ends the process");
        Check.True(Until(() => !File.Exists(link), 5000), "and Bloom's link goes with it");
        var exec = new StubSession();
        using var t = Session(launches.Launch, () => exec, account: account);
        var start = t.StartAsync("thr-1", CancellationToken.None);
        Check.True(start.Wait(5000) && start.Result.Success && t.OnFallback && exec.Resume == "thr-1", "signed out: `codex exec` takes the chat (and says so)");
        Check.True(launches.Count == 1 && !File.Exists(link), "no app-server starts, nothing is linked");
    }

    /// A sign-in that can't be told or linked sends nothing on app-server: a turn whose process's sign-in can't be checked
    /// goes to `codex exec` (the process is ended), and a start whose sign-in can't be linked starts none.
    public static void ASignInThatCantBeToldOrLinkedSendsNothingOnAppServer()
    {
        string account = NewAccount("acct-1");
        var launches = new Launches();
        var exec = new StubSession();
        using var s = Session(launches.Launch, () => exec, account: account);
        Started(s, "thr-1");
        using (new FileStream(Path.Combine(account, "auth.json"), FileMode.Open, FileAccess.Read, FileShare.None)) // held: whose it is can't be told
            Check.True(s.Send("hi", []), "the message still goes");
        Check.True(s.OnFallback && exec.Sent == "hi" && exec.Resume == "thr-1", "to `codex exec`, on the same thread");
        Check.True(!launches[0].Methods().Contains("turn/start") && launches[0].Killed && launches.Count == 1, "not on the app-server, which is ended; none other starts");

        string other = NewAccount("acct-2");
        Directory.CreateDirectory(Path.Combine(CodexAppHome.Folder(other), "auth.json")); // Bloom's file can't be made a link
        var exec2 = new StubSession();
        using var t = Session(launches.Launch, () => exec2, account: other);
        var start = t.StartAsync(null, CancellationToken.None);
        Check.True(start.Wait(5000) && start.Result.Success && t.OnFallback && exec2.Alive && launches.Count == 1, "the chat goes on with `codex exec`");
    }

    /// Bloom's start takes away the sign-in links a crash left in its Codex folders, the account signed out meanwhile or not
    /// (the account's own file stays); the one a running app-server is on stays.
    public static void ALinkLeftBehindIsSweptAtStart()
    {
        string signedOut = NewAccount("acct-1"), signedIn = NewAccount("acct-2"), running = NewAccount("acct-3");
        static string Left(string account)
        {
            string folder = CodexAppHome.Folder(account), link = Path.Combine(folder, "auth.json");
            Directory.CreateDirectory(folder);
            Check.True(CreateHardLinkW(link, Path.Combine(account, "auth.json"), IntPtr.Zero), "a link as a crash leaves it");
            return link;
        }
        string orphan = Left(signedOut), kept = Left(signedIn);
        File.Delete(Path.Combine(signedOut, "auth.json")); // signed out while Bloom was closed: only Bloom's name holds the tokens
        using var s = Session(new FakeAppServer(), account: running);
        Started(s);
        Check.True(CodexAppHome.Sweep([signedOut, signedIn, running]).Wait(5000), "swept");
        Check.True(!File.Exists(orphan) && !File.Exists(kept), "no link is left where nothing runs");
        Check.True(File.Exists(Path.Combine(signedIn, "auth.json")), "the account's own file stays");
        Check.True(File.Exists(Path.Combine(CodexAppHome.Folder(running), "auth.json")) && s.Alive, "the one a running app-server is on stays");
    }

    /// Through Chats: a pre-started process whose sign-in changes is dropped, and the conversation's next message starts a
    /// new one that resumes its thread.
    public static void AWarmChatWhoseSignInChangesResumesOnTheNextMessage()
    {
        string root = Path.Combine(Path.GetTempPath(), "bloom-codex-app-chat-" + Guid.NewGuid().ToString("N"));
        string account = NewAccount("acct-1");
        var launches = new Launches();
        var chats = new Chats(Dispatcher.CurrentDispatcher, (thread, effort) => Session(launches.Launch, account: account), Path.Combine(root, "chats")) { PreStartDelay = TimeSpan.Zero };
        try
        {
            var t = new ChatThread
            {
                Id = Guid.NewGuid().ToString("N"),
                Model = "chatgpt:gpt-test",
                SessionId = "thr-saved",
                SessionProvider = "chatgpt",
                SessionAccount = Bloom.Core.AiAccounts.Chat("chatgpt").Id,
            };
            chats.PreStart(t);
            Check.True(Pump(() => chats.IsWarm(t) && launches.Count == 1 && launches[0].Methods().Contains("thread/resume"), 5000), "pre-started on the saved thread");
            Replace(account, "acct-2");
            _ = CodexAppHome.Changed(account);
            Check.True(Pump(() => !chats.IsWarm(t) && launches[0].Killed, 5000), "the waiting process ends and is dropped");
            chats.Send(t, "hello");
            Check.True(Pump(() => launches.Count == 2 && launches[1].Methods().Contains("turn/start"), 5000), "the message starts a new process");
            Check.Eq(launches[1].Call("thread/resume")["threadId"]!.GetValue<string>(), "thr-saved", "resuming the conversation's thread");
        }
        finally
        {
            chats.Shutdown();
            Pump(() => false, 20);
            Directory.Delete(root, true);
        }
    }

    /// The tests' Codex folder is a throwaway one (Program): the catalog and the default account never read the user's.
    public static void TheUsersCodexFolderIsNotRead()
    {
        string real = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));
        foreach (string used in new[] { GptCatalog.Home, CodexAuth.DefaultHome() })
            Check.True(!string.Equals(Path.GetFullPath(used), real, StringComparison.OrdinalIgnoreCase) && Path.GetFullPath(used).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase),
                "Codex's default folder is the run's own: " + used);
    }

    public static void BloomsServersKeepTheirOwnNames()
    {
        static McpServerDef Def(string name, string source, string? command = "x.exe", string? url = null) => new(name, source, command, [], new Dictionary<string, string>(), url, null);
        var extra = new List<McpServerDef>
        {
            Def("bloom", "claude"), // Bloom's own, as another app lists it: Bloom's server is the one
            Def("files", "claude"),
            Def("files", "codex"), // the same name from another source
            Def("files_bloom", "claude"), // a name the one before is given
            Def("web", "claude", null, "http://web"),
        };
        var servers = CodexAppSession.Servers(("bloom.exe", "pipe-1", "hint"), ("relay.exe", "pipe-2"), extra, "conv-1");
        var bloom = servers["bloom"]!;
        Check.Eq(bloom["command"]!.GetValue<string>(), "bloom.exe", "Bloom's server");
        Check.Eq(bloom["args"]!.ToJsonString(), """["--mcp","pipe-1","conv-1"]""", "told which conversation asks");
        Check.Eq(bloom["default_tools_approval_mode"]!.GetValue<string>(), "approve", "approved by Codex: Bloom asks in the chat");
        Check.Eq(servers["files"]!["args"]!.ToJsonString(), """["--mcp-proxy","pipe-2","conv-1","claude","files"]""", "a switched-on server behind Bloom's relay");
        Check.Eq(servers["files_bloom"]!["args"]!.ToJsonString(), """["--mcp-proxy","pipe-2","conv-1","codex","files"]""", "the same name again gets a name of its own");
        Check.Eq(servers["files_bloom_bloom"]!["args"]!.ToJsonString(), """["--mcp-proxy","pipe-2","conv-1","claude","files_bloom"]""", "and a server named so isn't written over");
        Check.Eq(servers.Count, 4, "every server once");
        Check.True(servers["web"] is null, "a web-address server can't be relayed");

        // with PC work off, a relayed server may not take Bloom's name either way
        var none = CodexAppSession.Servers(null, ("relay.exe", "pipe-2"), [Def("bloom", "claude"), Def("files", "claude")], "conv-1");
        Check.True(none["bloom"] is null && none["files"] is not null, "Bloom's own name is never relayed");
    }

    public static void ResumeKeepsTheSavedThreadId()
    {
        var server = new FakeAppServer();
        using var s = Session(server);
        var seen = new Seen(s);
        Started(s, "exec-thread-1");
        var p = server.Call("thread/resume");
        Check.Eq(p["threadId"]!.GetValue<string>(), "exec-thread-1", "the id `codex exec` saved is resumed");
        Check.True(p["excludeTurns"]!.GetValue<bool>(), "without its history (the chat has it)");
        Check.Eq(s.SessionId, "exec-thread-1", "the conversation keeps its id");
        Check.True(s.Send("more", []), "sent");
        Check.True(Until(() => server.Methods().Contains("turn/start")), "a turn starts");
        Check.Eq(server.Call("turn/start")["threadId"]!.GetValue<string>(), "exec-thread-1", "on the resumed thread");
        Thread.Sleep(50);
        Check.True(!seen.All().Any(e => e.StartsWith("started:", StringComparison.Ordinal)), "no new id is reported");

        var fresh = new FakeAppServer();
        using var t = Session(fresh);
        var told = new Seen(t);
        Started(t);
        Check.True(t.SessionId is null && told.All().Count == 0, "a new thread is not reported before a message (nothing is on disk yet)");
        t.Send("hello", []);
        Check.True(Until(() => told.All().Contains("started:thr-new")), "it is reported with the first turn");
        Check.Eq(t.SessionId, "thr-new", "and kept");
    }

    public static void AnswerStreamsAsDeltas()
    {
        Directory.CreateDirectory(Work);
        string image = Path.Combine(Work, "shot.png");
        File.WriteAllBytes(image, [137, 80, 78, 71]);
        var server = new FakeAppServer();
        using var s = Session(server);
        var seen = new Seen(s);
        Started(s);
        Check.True(s.Send("hi", [image, Path.Combine(Work, "gone.png")]), "sent");
        Check.True(Until(() => server.Methods().Contains("turn/start")), "a turn starts");
        var input = server.Call("turn/start")["input"]!.AsArray();
        Check.Eq(input.Count, 2, "the picture that exists, then the text");
        Check.True(input[0]!["type"]!.GetValue<string>() == "localImage" && input[0]!["path"]!.GetValue<string>() == image, "the picture by path");
        Check.True(input[1]!["type"]!.GetValue<string>() == "text" && input[1]!["text"]!.GetValue<string>() == "hi", "the message");
        Check.True(s.Busy, "busy while the answer comes");
        Check.True(Until(() => seen.All().Contains("started:thr-new")), "the turn has its id");
        JsonObject Of(string item, string delta) => new() { ["threadId"] = "thr-new", ["turnId"] = "turn-1", ["itemId"] = item, ["delta"] = delta };
        JsonObject Item(string type, string id, string text = "") => new() { ["threadId"] = "thr-new", ["turnId"] = "turn-1", ["item"] = new JsonObject { ["type"] = type, ["id"] = id, ["text"] = text } };
        server.Notify("item/started", Item("userMessage", "u1"));
        server.Notify("item/started", Item("reasoning", "r1"));
        server.Notify("item/started", Item("agentMessage", "m1"));
        server.Notify("item/agentMessage/delta", Of("m1", "Hel"));
        server.Notify("item/agentMessage/delta", Of("m1", "lo \uE200cite\uE202turn0"));
        server.Notify("item/agentMessage/delta", Of("m1", "search0\uE201 world"));
        server.Notify("item/agentMessage/delta", new JsonObject { ["threadId"] = "other", ["turnId"] = "turn-1", ["itemId"] = "x", ["delta"] = "NOT OURS" });
        server.Notify("item/completed", Item("agentMessage", "m1", "Hello world"));
        server.Notify("item/started", Item("agentMessage", "m2"));
        server.Notify("item/agentMessage/delta", Of("m2", "Second"));
        server.Notify("item/completed", Item("agentMessage", "m3", "Whole one"));
        server.Notify("thread/tokenUsage/updated", new JsonObject { ["threadId"] = "thr-new", ["turnId"] = "turn-1", ["tokenUsage"] = new JsonObject { ["last"] = new JsonObject { ["inputTokens"] = 10, ["cachedInputTokens"] = 2, ["outputTokens"] = 3 } } });
        server.Notify("turn/completed", new JsonObject { ["threadId"] = "thr-new", ["turn"] = new JsonObject { ["id"] = "turn-1", ["status"] = "completed", ["items"] = new JsonArray() } });
        Check.True(Until(() => seen.EndCount() == 1), "the turn ends");
        Check.Eq(seen.Text(), "Hello world\n\nSecond", "the text as it streamed, citations out, a second message on a paragraph of its own");
        var all = seen.All();
        Check.True(all.Contains("status:" + Bloom.Core.Lang.Tr("생각하는 중…", "Thinking…")) && !all.Contains("status:"), "reasoning shows; the user's own message changes nothing");
        Check.True(all.Count(e => e == "text:Whole one") == 1 && !all.Contains("text:Hello world"), "only a message that came without deltas comes whole");
        Check.True(seen.Ends[0] == (true, null), "it ended well");
        Check.True(!s.Busy && s.Alive, "ready for the next one, the process kept");
        Check.True(s.Send("again", []), "the next message goes to the same process");
        Check.True(Until(() => server.Methods().Count(m => m == "turn/start") == 2), "a second turn on it");
    }

    public static void TurnEndsReportTheirStatus()
    {
        var server = new FakeAppServer();
        using var s = Session(server);
        var seen = new Seen(s);
        Started(s);
        void Turn(int n, JsonObject turn, JsonObject? error = null)
        {
            Check.True(s.Send("q" + n, []), "sent");
            Check.True(Until(() => server.Methods().Count(m => m == "turn/start") == n), "turn " + n);
            if (error is not null) server.Notify("error", new JsonObject { ["threadId"] = "thr-new", ["turnId"] = "turn-" + n, ["willRetry"] = false, ["error"] = error });
            turn["id"] = "turn-" + n;
            turn["items"] = new JsonArray();
            server.Notify("turn/completed", new JsonObject { ["threadId"] = "thr-new", ["turn"] = turn });
            Check.True(Until(() => seen.EndCount() == n), "turn " + n + " ends");
        }
        Turn(1, new JsonObject { ["status"] = "failed", ["error"] = new JsonObject { ["message"] = "Try again at 5 PM.", ["codexErrorInfo"] = "usageLimitExceeded" } });
        Check.True(!seen.Ends[0].ok && seen.Ends[0].error!.Contains("Try again at 5 PM.", StringComparison.Ordinal), "a failed turn says why");
        Check.Eq(AiErrors.Friendly(seen.Ends[0].error!, true), Bloom.Core.Lang.Tr("AI 사용 한도에 도달했습니다. 한도 초기화 후 다시 보내세요.", "AI usage limit reached. Send again after the limit resets."), "a usage limit reads as one");
        Turn(2, new JsonObject { ["status"] = "failed" }, new JsonObject { ["message"] = "stream disconnected", ["codexErrorInfo"] = "other" });
        Check.Eq(seen.Ends[1].error, "stream disconnected", "the error reported during the turn, when the end names none");
        Turn(3, new JsonObject { ["status"] = "interrupted" });
        Check.Eq(seen.Ends[2].error, AiErrors.Stopped, "an interrupted turn is a stopped answer");
        Turn(4, new JsonObject { ["status"] = "completed" });
        Check.True(seen.Ends[3] == (true, null), "a completed one is fine");

        Check.True(s.Send("q5", []), "sent");
        Check.True(Until(() => server.Methods().Count(m => m == "turn/start") == 5), "turn 5");
        server.End(); // the process dies mid-answer
        Check.True(Until(() => seen.EndCount() == 5), "the turn ends with it");
        Check.True(!seen.Ends[4].ok && seen.Ends[4].error == AiErrors.CodexNoAnswer, "with no answer");
        Check.True(Until(() => seen.All().Contains("exited")) && !s.Alive, "and the session is gone");
    }

    public static void StopInterruptsTheTurn()
    {
        var server = new FakeAppServer();
        using var s = Session(server);
        var seen = new Seen(s);
        Started(s);
        s.Send("long", []);
        Check.True(Until(() => seen.All().Contains("started:thr-new")), "the turn has its id");
        s.Interrupt();
        Check.True(Until(() => server.Methods().Contains("turn/interrupt")), "turn/interrupt goes out");
        var p = server.Call("turn/interrupt");
        Check.True(p["threadId"]!.GetValue<string>() == "thr-new" && p["turnId"]!.GetValue<string>() == "turn-1", "for this thread's turn");
        server.Notify("turn/completed", new JsonObject { ["threadId"] = "thr-new", ["turn"] = new JsonObject { ["id"] = "turn-1", ["status"] = "interrupted", ["items"] = new JsonArray() } });
        Check.True(Until(() => seen.EndCount() == 1) && seen.Ends[0].error == AiErrors.Stopped, "the answer stops");
        Thread.Sleep(300);
        Check.True(s.Alive && !server.Killed, "the process stays for the next message");

        // a turn that doesn't end when asked ends with its process
        var stuck = new FakeAppServer();
        using var t = Session(stuck);
        var gone = new Seen(t);
        Started(t);
        var answer = stuck.Respond;
        stuck.Respond = (method, q) => method == "turn/interrupt" ? null : answer(method, q);
        t.Send("long", []);
        Check.True(Until(() => gone.All().Contains("started:thr-new")), "the turn has its id");
        t.Interrupt();
        Check.True(Until(() => stuck.Killed, 3000), "the process is ended");
        Check.True(Until(() => gone.EndCount() == 1) && gone.Ends[0].error == AiErrors.Stopped && !t.Alive, "the answer stops, the session is gone");

        // a stop before the turn has its id goes out once it has one
        var slow = new FakeAppServer();
        using var u = Session(slow, grace: 5000);
        Started(u);
        var hold = new ManualResetEventSlim();
        var respond = slow.Respond;
        slow.Respond = (method, q) =>
        {
            if (method == "turn/start") hold.Wait(3000);
            return respond(method, q);
        };
        var send = Task.Run(() => u.Send("long", []));
        Check.True(Until(() => u.Busy), "sent");
        u.Interrupt();
        hold.Set();
        Check.True(send.Wait(3000) && Until(() => slow.Methods().Contains("turn/interrupt")), "the stop follows the turn's id");
    }

    public static void ServerRequestsAreRefused()
    {
        var server = new FakeAppServer();
        using var s = Session(server);
        Started(s);
        server.Push(new JsonObject { ["id"] = 99, ["method"] = "item/commandExecution/requestApproval", ["params"] = new JsonObject { ["threadId"] = "thr-new" } });
        Check.True(Until(() => server.Messages().Any(m => m["id"]?.GetValue<int>() == 99 && m["error"] is not null)), "Bloom answers with an error, so Codex never waits");
    }

    public static void FailedStartFallsBackToExec()
    {
        var exec = new StubSession();
        var s = new CodexAppSession("chatgpt:gpt-test", "low", "conv-1", () => exec, _ => throw new Win32Exception(2)) { CliPath = "codex.exe", WorkDir = Work, AccountHome = Signed(Account) };
        var seen = new Seen(s);
        var start = s.StartAsync("saved-1", CancellationToken.None);
        Check.True(start.Wait(5000) && start.Result.Success, "the chat still starts");
        Check.True(s.OnFallback && exec.Resume == "saved-1", "on `codex exec`, resuming the same id");
        Check.True(s.Alive && s.Send("hello", []) && exec.Sent == "hello", "messages go to it");
        exec.Reply("hi");
        Check.True(seen.All().Contains("text:hi") && seen.Ends.Count == 1 && seen.Ends[0].ok, "its answers come through");
        s.Dispose();
        Check.Eq(exec.Disposals, 1, "and it ends with the session");
        Check.True(s.Closed.Wait(1000), "closed");

        // the process starts but won't initialize
        var server = new FakeAppServer();
        server.Respond = (method, _) => throw new InvalidOperationException("initialize failed");
        var other = new StubSession();
        using var t = Session(server, () => other);
        var r = t.StartAsync(null, CancellationToken.None);
        Check.True(r.Wait(5000) && r.Result.Success && t.OnFallback && other.Alive, "falls back too");
        Check.True(server.Killed, "the app-server is ended");

        // no fallback: the start reports the failure
        var bad = new FakeAppServer();
        bad.Respond = (method, _) => throw new InvalidOperationException("no");
        using var u = Session(bad);
        var failed = u.StartAsync(null, CancellationToken.None);
        Check.True(failed.Wait(5000) && !failed.Result.Success && !u.Alive, "fails without one");
    }

    /// The process ends (its output closes) while it initializes or starts its thread: the start goes on with `codex exec`,
    /// and the chat is told nothing of the end — the session it holds is the one that now carries exec.
    public static void AnEndDuringTheStartFallsBackWithoutAWord()
    {
        foreach (string during in new[] { "initialize", "thread/start", "thread/resume" })
        {
            var server = new FakeAppServer();
            var answer = server.Respond;
            server.Respond = (method, q) =>
            {
                if (method != during) return answer(method, q);
                server.End(); // EOF, no answer
                return null;
            };
            var exec = new StubSession();
            using var s = Session(server, () => exec);
            var seen = new Seen(s);
            var start = s.StartAsync(during == "thread/resume" ? "saved-1" : null, CancellationToken.None);
            Check.True(start.Wait(5000) && start.Result.Success, "the chat still starts (" + during + ")");
            Check.True(s.OnFallback && exec.Alive && s.Alive, "on `codex exec` (" + during + ")");
            Check.Eq(exec.Resume, during == "thread/resume" ? "saved-1" : null, "resuming what it would have");
            Thread.Sleep(300); // the reader has seen the end by now
            Check.True(!seen.All().Contains("exited"), "no end is reported (" + during + ")");
            Check.True(s.Send("hello", []) && exec.Sent == "hello", "messages go to exec");
            exec.Reply("hi");
            Check.True(seen.All().Contains("text:hi") && seen.Ends.Count == 1 && seen.Ends[0].ok, "and its answer comes through (" + during + ")");
        }
    }

    /// The same through Chats: the conversation keeps the session and its answer shows (it used to be dropped as no longer current).
    public static void AChatWhoseServerEndsWhileStartingIsAnswered()
    {
        string root = Path.Combine(Path.GetTempPath(), "bloom-codex-app-chat-" + Guid.NewGuid().ToString("N"));
        var exec = new StubSession();
        var server = new FakeAppServer();
        server.Respond = (method, _) =>
        {
            server.End();
            return null;
        };
        var chats = new Chats(Dispatcher.CurrentDispatcher, (thread, effort) => Session(server, () => exec), Path.Combine(root, "chats")) { PreStartDelay = TimeSpan.Zero };
        try
        {
            var t = new ChatThread { Id = Guid.NewGuid().ToString("N"), Model = "chatgpt:gpt-test" };
            chats.Send(t, "hello");
            Check.True(Pump(() => exec.Sent is not null, 5000), "the message reaches `codex exec`");
            Pump(() => false, 300); // the end of the app-server's output is handled by now
            Check.True(chats.IsLive(t), "the conversation keeps its session");
            exec.Reply("hi there");
            Check.True(Pump(() => t.Messages.Count == 2 && !t.Messages[1].Streaming, 3000), "the answer finishes");
            Check.Eq(t.Messages[1].Text, "hi there", "and shows");
        }
        finally
        {
            chats.Shutdown();
            Pump(() => false, 20);
            Directory.Delete(root, true);
        }
    }

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

    /// Closed waits until the app-server and what it started are gone, an end that fails being made again.
    public static void ClosedWaitsForEveryProcess()
    {
        // the first two ends fail: one from Dispose and the reader's first come together, the reader's next a second later
        var server = new FakeAppServer { ChildStays = true, KillsToEnd = 3 };
        var s = new CodexAppSession("chatgpt:gpt-test", "low", "conv-1", null, server.Pipe) { CliPath = "codex.exe", WorkDir = Work, AccountHome = Signed(Account), ExitGrace = 100, KillWait = 1000 };
        Started(s);
        s.Dispose();
        Check.True(Until(() => server.Kills >= 1), "a process left running is ended");
        Check.True(!s.Closed.IsCompleted, "not closed while one is still running (the end failed)");
        Check.True(s.Closed.Wait(5000), "closed once an end has worked");
        Check.True(server.Kills >= 3, "ended again until it did");
    }

    public static void DisposeEndsTheProcess()
    {
        var server = new FakeAppServer();
        var s = Session(server);
        Started(s);
        Check.True(!s.Closed.IsCompleted, "the process runs");
        s.Dispose();
        Check.True(Until(() => server.InputClosed), "its input is closed (app-server ends then)");
        Check.True(s.Closed.Wait(3000), "Closed completes once it is gone");
        Check.True(!s.Alive && !s.Send("late", []), "nothing more goes to it");

        var never = Session(new FakeAppServer());
        never.Dispose();
        Check.True(never.Closed.IsCompleted, "a session that never started is closed at once");

        // ended while starting: the start is cancelled and leaves nothing running
        var held = new FakeAppServer();
        var gate = new ManualResetEventSlim();
        var respond = held.Respond;
        held.Respond = (method, q) =>
        {
            if (method == "initialize") gate.Wait(3000);
            return respond(method, q);
        };
        var exec = new StubSession();
        var w = Session(held, () => exec);
        using var cts = new CancellationTokenSource();
        var start = w.StartAsync(null, cts.Token);
        Check.True(Until(() => held.Launched is not null), "launching");
        cts.Cancel();
        w.Dispose();
        gate.Set();
        try { start.Wait(3000); }
        catch (AggregateException) { /* cancelled */ }
        Check.True(start.IsCompleted && (start.IsCanceled || !start.Result.Success), "the start doesn't succeed");
        Check.True(w.Closed.Wait(3000) && !exec.Alive, "the process is gone and no fallback started");
    }
}
