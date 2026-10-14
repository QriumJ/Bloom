using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Core;

namespace Bloom.Ai;

internal sealed class ChatMessage
{
    public string Role { get; set; } = "user"; // user | assistant
    public string Text { get; set; } = "";
    public long At { get; set; }
    public bool Error { get; set; }
    public string? Page { get; set; } // title of attached text (clipboard, screen text)
    public string? Image { get; set; } // a screen picture sent with it (Bloom Lens), a PNG under Local\lens
    public List<string>? Files { get; set; } // user: the attached files (copies in the thread's in\); assistant: files it made (out\)
    public string? Sent { get; set; } // user: the message as any model got it (time, a page's contents; Files are added by each side); null = Text (older files)

    public List<string>? Done { get; set; } // what the AI did on the PC with the user's yes ("✓ 옮기기: …", "✕ …")
    public List<ApiStep>? Steps { get; set; } // assistant, API chats: the model calls of an answer that used tools, sent again when the chat resumes
    [System.Text.Json.Serialization.JsonIgnore] public bool Streaming;
    [System.Text.Json.Serialization.JsonIgnore] public string? Status;
    [System.Text.Json.Serialization.JsonIgnore] public readonly List<ToolAsk> Asks = new(); // waiting for the user
}

internal sealed class ChatThread
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = Tr("새 대화", "New chat");
    public string Model { get; set; } = "sonnet"; // sonnet | opus | haiku | chatgpt:<Codex slug> | api:<provider>:<model> (AiModels)
    public string? Effort { get; set; } // a level of AiModels.Efforts(Model); null = the settings' level
    public string? SessionId { get; set; }
    public string? SessionProvider { get; set; } // whose session SessionId is (null: claude, older files)
    public string? SessionAccount { get; set; } // the AiAccount it lives in (null: the default one, older files)
    public long Created { get; set; }
    public long Updated { get; set; }
    public string? Group { get; set; } // ChatGroup.Id; null = not in a group
    public List<ChatMessage> Messages { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore] public bool Busy => Messages.Count > 0 && Messages[^1].Streaming;
    [System.Text.Json.Serialization.JsonIgnore] public readonly HashSet<string> AlwaysAllowed = new(); // 이 대화에서 항상 허용
}

/// A folder of conversations in the AI chat window (chat-groups.json, in the order they were made).
internal sealed class ChatGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Folded { get; set; }
}

/// Conversations (one JSON file each under chats\) and their CLI sessions. A session left unused for
/// Settings.AiIdleMinutes (10 by default, 0 = never) is closed to give its memory back; the next message resumes it by id. A new
/// model or level restarts the session (resumed when the provider stays); a switch between Claude
/// and ChatGPT starts a new one that gets the conversation so far with the next message.
internal sealed partial class Chats
{
    private readonly Dispatcher ui;
    private readonly Dictionary<string, IChatSession> sessions = new();
    private readonly DispatcherTimer idle;
    private readonly string dir;
    private readonly ChatStore store;
    private bool shutdown;
    private readonly Func<ChatThread, string, IChatSession>? sessionFactory;
    private readonly string filesRoot;
    private readonly Func<string, IReadOnlyList<string>, Func<List<string>, string>, CancellationToken, Task<PreparedAttachments>> prepareAttachments;
    private sealed class Preparing(ChatMessage answer)
    {
        public readonly ChatMessage Answer = answer;
        public readonly CancellationTokenSource Cancellation = new();
        public readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Sending;
    }
    private readonly Dictionary<string, Preparing> preparing = new();
    private readonly Dictionary<string, Task> deletingFiles = new();
    private readonly Dictionary<string, List<Task>> retiredPreparations = new();
    private sealed class Finishing(ChatMessage answer, bool immediate)
    {
        public readonly ChatMessage Answer = answer;
        public readonly bool Immediate = immediate;
        public readonly CancellationTokenSource Cancellation = new();
        public readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly Dictionary<string, Finishing> finishing = new();
    private readonly Dictionary<string, Task> outputWork = new();
    private readonly Func<ChatOutputRequest, CancellationToken, Task<ChatOutputResult>> collectOutput;

    public readonly List<ChatThread> Threads = new();

    /// UI thread; the thread whose messages changed (streaming text, status, end of turn).
    public event Action<ChatThread>? Updated;

    /// UI thread; a CLI session started or ended (the chat window's live marks).
    public event Action? SessionsChanged;

    /// Every open CLI session ends (a switch that changes how sessions start); the conversations stay.
    public void EndAll()
    {
        foreach (var t in Threads.Where(t => IsLive(t) || preparing.ContainsKey(t.Id) || finishing.ContainsKey(t.Id)).ToList()) EndSession(t);
    }

    /// The conversation has a running CLI session (it answers at once; 세션 종료 gives its memory back).
    public bool IsLive(ChatThread t) => sessions.TryGetValue(t.Id, out var s) && s.Alive || liveForQa.Contains(t.Id);

    private readonly HashSet<string> liveForQa = new();

    /// QA pictures: the conversation shows as having an open session (nothing runs).
    public void SetLiveForQa(ChatThread t, bool live)
    {
        if (live) liveForQa.Add(t.Id);
        else liveForQa.Remove(t.Id);
    }

    /// 세션 종료: the conversation's CLI process ends now (an answer being written stops); the conversation stays and its
    /// next message resumes the session by id.
    public void EndSession(ChatThread t)
    {
        bool collectFiles = !preparing.TryGetValue(t.Id, out var preparation) || preparation.Sending;
        bool completing = CancelFinish(t);
        if (completing) collectFiles = false;
        bool pending = CancelPreparation(t);
        bool removed = sessions.Remove(t.Id, out var s);
        if (!pending && !removed && !completing) return;
        s?.Dispose();
        if (t.Busy) Finish(t, t.Messages[^1], false, Tr("세션 종료 · 답변 중지됨", "Session ended · answer stopped"), collectFiles, immediate: true);
        SessionsChanged?.Invoke();
    }

    public Chats(Dispatcher ui, Func<ChatThread, string, IChatSession>? sessionFactory = null, string? directory = null,
        Func<string, IReadOnlyList<string>, Func<List<string>, string>, CancellationToken, Task<PreparedAttachments>>? prepareAttachments = null,
        Func<ChatOutputRequest, CancellationToken, Task<ChatOutputResult>>? collectOutput = null)
    {
        this.ui = ui;
        store = new ChatStore(ui);
        this.collectOutput = collectOutput ?? ((request, token) => Task.FromResult(ChatOutput.Process(request, token)));
        this.sessionFactory = sessionFactory;
        this.prepareAttachments = prepareAttachments ?? PreparedAttachments.Copy;
        dir = directory ?? Paths.In("chats");
        filesRoot = directory is null ? Path.Combine(Paths.Local, "ai", "files") : Path.Combine(Path.GetDirectoryName(directory)!, "files");
        Directory.CreateDirectory(dir);
        foreach (string f in Directory.GetFiles(dir, "*.json"))
        {
            var t = Json.Load<ChatThread>(f);
            if (t.Id.Length > 0) Threads.Add(t);
        }
        Threads.Sort((a, b) => b.Updated.CompareTo(a.Updated));
        LoadGroups();
        idle = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMinutes(1) };
        idle.Tick += (_, _) => ReapIdle();
    }

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public ChatThread? Latest => Threads.Count > 0 ? Threads[0] : null;

    public ChatThread New()
    {
        string model = AiModels.DefaultModel();
        var t = new ChatThread { Id = Guid.NewGuid().ToString("N")[..12], Model = model, Effort = AiModels.DefaultEffort(model), Created = Now, Updated = Now };
        return t; // saved (and listed) once it has a message
    }

    /// The thread's level, one its model takes.
    public static string EffortOf(ChatThread t) => t.Effort is { } e ? AiModels.Fit(t.Model, e) : AiModels.DefaultEffort(t.Model);

    /// The conversation goes on with this model from its next message; it also becomes the default for new ones. The
    /// other side (Claude ↔ ChatGPT ↔ API) keeps its own level; the same side keeps the thread's, fitted to the model.
    public static void SetModel(ChatThread? t, string model)
    {
        bool sideChanged = AiModels.Provider(AiModels.Normalize(t?.Model ?? AiModels.DefaultModel())) != AiModels.Provider(model);
        var s = Settings.Current;
        s.AiProvider = AiModels.Provider(model);
        if (AiModels.GptSlug(model) is { } slug) s.GptModel = slug;
        else if (AiModels.IsApi(model)) s.ApiModel = model;
        else if (!AiModels.IsChatGpt(model)) s.AiModel = model;
        if (t is not null)
        {
            t.Model = model;
            t.Effort = sideChanged || t.Effort is null ? AiModels.DefaultEffort(model) : AiModels.Fit(model, t.Effort);
        }
        Settings.Save();
    }

    /// A thinking level for the conversation, also the default on its side.
    public static void SetEffort(ChatThread? t, string level)
    {
        if (t is not null) t.Effort = level;
        string model = AiModels.Normalize(t?.Model ?? AiModels.DefaultModel());
        if (AiModels.IsChatGpt(model)) Settings.Current.GptEffort = level;
        else if (AiModels.IsApi(model)) Settings.Current.ApiEffort = level;
        else Settings.Current.AiEffort = level;
        Settings.Save();
    }

    public void Send(ChatThread t, string text, string? page = null, string? pageTitle = null, string? image = null, IReadOnlyList<string>? files = null)
    {
        if (shutdown || t.Busy) return;
        Task deletion = Task.WhenAll(WhenDeleted(t), WhenFinished(t));
        if (!Threads.Contains(t)) Threads.Insert(0, t);
        if (t.Messages.Count == 0) t.Title = TitleOf(text, pageTitle ?? (files is { Count: > 0 } ? Path.GetFileName(files[0]) : null));
        string prompt = ClaudeSession.LocalTime() + "\n" + (page is null ? text : $"<page title=\"{Esc(pageTitle ?? "")}\">\n{page}\n</page>\n\n{text}");
        // kept with the message so an API session started later sends it again as it went now (ApiSession.Past)
        var user = new ChatMessage { Role = "user", Text = text, At = Now, Page = pageTitle, Image = image, Sent = prompt };
        t.Messages.Add(user);
        var answer = new ChatMessage { Role = "assistant", At = Now, Streaming = true, Status = Tr("생각하는 중…", "Thinking…") };
        t.Messages.Add(answer);
        var pending = new Preparing(answer);
        preparing[t.Id] = pending;
        Touch(t);
        Updated?.Invoke(t);

        string[] sources = files?.Take(MaxFiles).ToArray() ?? Array.Empty<string>();
        string folder = ThreadFiles(t);
        bool inline = AiModels.Inline(t.Model);
        _ = PrepareAndSend(t, user, pending, prompt, image, sources, folder, inline, UnavailableNote(), deletion);
    }

    /// What an inlined file that can't be read becomes ("{0}" = its name), for the model to tell the user.
    internal static string UnavailableNote()
    {
        string nameSlot = "{0}";
        return Tr($"\n\n<attachment name=\"{nameSlot}\">(이 형식은 여기서 열 수 없어요 — 내용을 읽지 못했다고 알려 주세요)</attachment>", $"\n\n<attachment name=\"{nameSlot}\">(This format can't be opened here — tell the user its contents didn't come through)</attachment>");
    }

    internal Task WhenPrepared(ChatThread t) => preparing.TryGetValue(t.Id, out var pending) ? pending.Completion.Task : Task.CompletedTask;
    internal Task WhenDeleted(ChatThread t) => deletingFiles.TryGetValue(t.Id, out var pending) ? pending : Task.CompletedTask;
    internal Task WhenFinished(ChatThread t) => outputWork.TryGetValue(t.Id, out var pending) ? pending : Task.CompletedTask;

    private async Task PrepareAndSend(ChatThread t, ChatMessage user, Preparing pending, string prompt,
        string? image, string[] sources, string folder, bool inline, string unavailable, Task deletion)
    {
        PreparedAttachments? attached = null;
        IChatSession? session = null;
        bool Current() => preparing.TryGetValue(t.Id, out var current) && ReferenceEquals(current, pending);
        try
        {
            var token = pending.Cancellation.Token;
            await deletion.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (sources.Length > 0)
                attached = await Task.Run(() => prepareAttachments(folder, sources, inline ? list => Inline(list, unavailable) : _ => "", token), token).ConfigureAwait(false);
            var images = new List<string>();
            if (image is not null) images.Add(image);
            if (attached is not null) images.AddRange(attached.Files.Where(f => ImageType(f) is not null));
            await OnUi(() => Current() && AiModels.IsChatGpt(t.Model) ? GptCatalog.RefreshAsync(token) : Task.FromResult(true)).ConfigureAwait(false);
            var started = await OnUi(() =>
            {
                if (!Current()) return Task.FromResult(new SessionStart(false));
                if (attached is { Files.Count: > 0 })
                {
                    user.Files = attached.Files;
                    attached.Accept();
                    prompt += inline ? attached.Inline : "\n\n<attachments>\n" + string.Join("\n", attached.Files.Select(f => "- " + FilesRel(t) + "/" + Path.GetRelativePath(folder, f).Replace('\\', '/'))) + "\n</attachments>";
                    Save(t);
                    Updated?.Invoke(t);
                }
                if (!Current()) return Task.FromResult(new SessionStart(false));
                session = SessionFor(t, out string? resume, out bool fresh, out bool start);
                if (!Current()) return Task.FromResult(new SessionStart(false));
                if (fresh) prompt = Earlier(t) + prompt;
                turnStarted[t.Id] = DateTime.UtcNow.AddSeconds(-1);
                if (Settings.Current.AiTitles) leads[t.Id] = new ChatTitles.Lead();
                else leads.Remove(t.Id);
                return start ? session.StartAsync(resume, token) : Task.FromResult(new SessionStart(true));
            }).ConfigureAwait(false);
            bool sent = false;
            if (started.Success)
                sent = await OnUi(() =>
                {
                    if (!Current() || session is null) return Task.FromResult(false);
                    pending.Sending = true;
                    return session.SendAsync(prompt, images, token);
                }).ConfigureAwait(false);
            await OnUi(() =>
            {
                if (!Current()) return Task.FromResult(false);
                preparing.Remove(t.Id);
                if (!started.Success && session is not null && sessions.TryGetValue(t.Id, out var current) && ReferenceEquals(current, session))
                {
                    sessions.Remove(t.Id);
                    session.Dispose();
                }
                if (!sent) Finish(t, pending.Answer, false, started.Error ?? Tr("메시지를 보내지 못했습니다. 다시 보내세요.", "Could not send the message. Send it again."), pending.Sending);
                else idle.Start();
                SessionsChanged?.Invoke();
                return Task.FromResult(true);
            }).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            try
            {
                await OnUi(() =>
            {
                if (!Current()) return Task.FromResult(false);
                preparing.Remove(t.Id);
                if (sessions.TryGetValue(t.Id, out var current) && ReferenceEquals(current, session))
                {
                    sessions.Remove(t.Id);
                    current.Dispose();
                }
                Finish(t, pending.Answer, false, e is OperationCanceledException ? AiErrors.Stopped : e.Message, pending.Sending);
                SessionsChanged?.Invoke();
                return Task.FromResult(true);
            }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ui.HasShutdownStarted) { }
        }
        finally
        {
            attached?.Dispose();
            pending.Cancellation.Dispose();
            pending.Completion.TrySetResult();
        }
    }

    private Task<T> OnUi<T>(Func<Task<T>> action) => ui.HasShutdownStarted
        ? Task.FromCanceled<T>(new CancellationToken(true))
        : ui.CheckAccess() ? action() : ui.InvokeAsync(action).Task.Unwrap();

    private bool CancelPreparation(ChatThread t)
    {
        if (!preparing.Remove(t.Id, out var pending)) return false;
        foreach (string id in retiredPreparations.Keys.ToArray())
        {
            retiredPreparations[id].RemoveAll(task => task.IsCompleted);
            if (retiredPreparations[id].Count == 0) retiredPreparations.Remove(id);
        }
        if (!retiredPreparations.TryGetValue(t.Id, out var retired)) retiredPreparations[t.Id] = retired = new();
        retired.Add(pending.Completion.Task);
        _ = pending.Cancellation.CancelAsync();
        return true;
    }

    private string ThreadFiles(ChatThread t) => Path.Combine(filesRoot, t.Id);

    private static string Esc(string s) => s.Replace("\"", "'");

    // ── files in a conversation ─────────────────────────────────────────────────────────────────
    /// Attached files: at most this many per message, each at most this big.
    public const int MaxFiles = 10;
    public const long MaxFileBytes = 50L * 1024 * 1024;

    private readonly Dictionary<string, DateTime> turnStarted = new();

    private readonly Dictionary<string, ChatTitles.Lead> leads = new(); // the start of each answer, for its title line

    /// The answer's text without its title line; a finished title line renames the conversation.
    private string Shown(ChatThread t, string piece)
    {
        if (!leads.TryGetValue(t.Id, out var lead)) return piece;
        string shown = lead.Take(piece, out string? field, out string? task);
        if (task is not null) t.Title = ChatTitles.Compose(t.Model, field ?? "", task);
        return shown;
    }

    /// The thread's folder as the sessions see it (relative to Local\ai) and on disk.
    public static string FilesRel(ChatThread t) => "files/" + t.Id;

    public static string FilesDir(ChatThread t) => Path.Combine(Paths.Local, "ai", "files", t.Id);

    /// The media type Claude takes for a picture, or null when the file is not one.
    public static string? ImageType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => null,
    };

    /// ChatGPT reads no files itself: text files go into the message (each up to 200 KB), pictures as images, and the
    /// rest is named so it can say it could not open them.
    internal static string Inline(List<string> attached, string unavailable)
    {
        var sb = new StringBuilder();
        foreach (string f in attached)
        {
            string name = Path.GetFileName(f);
            if (ImageType(f) is not null) continue;
            if (TextOf(f) is { } text) sb.Append($"\n\n<attachment name=\"{Esc(name)}\">\n{text}\n</attachment>");
            else sb.Append(unavailable.Replace("{0}", Esc(name), StringComparison.Ordinal));
        }
        return sb.ToString();
    }

    /// A file's text when it is text (UTF-8, no NUL, at most 200 KB), else null.
    private static string? TextOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 200 * 1024) return null;
            byte[] bytes = File.ReadAllBytes(path);
            if (Array.IndexOf(bytes, (byte)0) >= 0) return null;
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return null;
        }
    }

    /// The conversation so far, for a session that has not seen it (Claude ↔ ChatGPT).
    private static string Earlier(ChatThread t)
    {
        var sb = new StringBuilder();
        foreach (var m in t.Messages.Take(t.Messages.Count - 2).Where(m => !m.Error && m.Text.Length > 0).TakeLast(16))
            sb.Append(m.Role == "user" ? "User: " : "Assistant: ").Append(m.Text.Length > 2000 ? m.Text[..2000] + "…" : m.Text).Append("\n\n");
        return sb.Length == 0 ? "" : "<earlier_conversation>\n" + sb.ToString().TrimEnd() + "\n</earlier_conversation>\n\n";
    }

    private static string TitleOf(string text, string? page)
    {
        string first = text.Trim().Split('\n')[0].Trim();
        if (first.Length == 0 && page is not null) first = page;
        return first.Length > 42 ? first[..42] + "…" : first.Length == 0 ? Tr("새 대화", "New chat") : first;
    }

    private IChatSession SessionFor(ChatThread t, out string? resume, out bool fresh, out bool start)
    {
        resume = null;
        fresh = false;
        start = false;
        string effort = EffortOf(t), provider = AiModels.Provider(t.Model);
        var account = AiAccounts.Chat(provider);
        if (sessions.TryGetValue(t.Id, out var s) && s.Alive && s.Model == t.Model && s.Effort == effort) return s; // 설정's account change ends them (EndAll)
        if (s is not null)
        {
            s.Dispose();
            sessions.Remove(t.Id);
        }
        s = sessionFactory is not null ? sessionFactory(t, effort)
            : Demo.On ? new DemoSession(t.Model, effort)
            : provider == "api" ? new ApiSession(t.Model, effort, t.Id, ApiSession.Past(t.Messages), UnavailableNote(), ThreadFiles(t))
            : provider == "chatgpt" ? new CodexSession(t.Model, effort, FilesRel(t), t.Id) { AccountHome = account.Home }
            : new ClaudeSession(t.Model, effort, FilesRel(t), "Bloom · " + (t.Messages.Count > 0 ? (t.Title.Length > 40 ? t.Title[..40] + "…" : t.Title) : Tr("새 대화", "New chat"))) { AccountHome = account.Home };
        // a session lives in its account's folder: under another account it starts fresh, with the chat so far given to it;
        // an API session always starts from the saved chat itself (ApiSession.Past), so it is neither resumed nor told it
        bool api = provider == "api";
        resume = !api && (t.SessionProvider ?? "claude") == provider && (t.SessionAccount ?? provider) == account.Id ? t.SessionId : null;
        fresh = !api && resume is null && t.Messages.Count > 2;
        bool IsCurrent() => sessions.TryGetValue(t.Id, out var current) && ReferenceEquals(current, s);
        ChatMessage? Current() => IsCurrent() && !finishing.ContainsKey(t.Id) && t.Messages.Count > 0 && t.Messages[^1].Role == "assistant" && t.Messages[^1].Streaming ? t.Messages[^1] : null;
        bool needBreak = false;
        var events = new ChatEventPump(ui);
        s.SessionStarted += id => events.Post(() =>
        {
            if (!IsCurrent()) return;
            t.SessionId = id;
            t.SessionProvider = provider;
            t.SessionAccount = account.Id;
            Save(t);
        });
        void Append(string d)
        {
            if (Current() is not { } m) return;
            d = Shown(t, d);
            if (d.Length == 0) return;
            if (needBreak && m.Text.Length > 0 && !m.Text.EndsWith('\n')) m.Text += "\n\n";
            needBreak = false;
            m.Text += d;
            m.Status = null;
            Updated?.Invoke(t);
        }
        s.TextDelta += d => events.PostText(d, Append);
        s.AssistantText += whole => events.Post(() =>
        {
            if (Current() is not { } m) return;
            whole = Shown(t, whole);
            if (whole.Length == 0) return;
            if (m.Text.Length > 0 && !m.Text.EndsWith('\n')) m.Text += "\n\n";
            m.Text += whole;
            Updated?.Invoke(t);
        });
        s.Status += st => events.Post(() =>
        {
            if (Current() is not { } m) return;
            if (st is not null) needBreak = true;
            m.Status = st;
            Updated?.Invoke(t);
        });
        s.TurnEnded += (ok, err) => events.Post(() =>
        {
            if (Current() is { } m) Finish(t, m, ok, err);
        });
        s.ToolAsked += raw => events.Post(() => Asked(t, Current(), s, raw));
        s.ToolAskWithdrawn += id => events.Post(() =>
        {
            if (Current() is { } m && m.Asks.RemoveAll(a => a.RequestId == id) > 0) Updated?.Invoke(t);
        });
        // an API answer's tool calls, kept with it before it finishes (TurnEnded comes after) so the chat's file has them
        if (s is ApiSession apiSession)
            apiSession.StepsDone += steps => events.Post(() =>
            {
                if (Current() is { } m) m.Steps = steps;
            });
        s.Exited += _ => events.Post(() =>
        {
            if (sessions.TryGetValue(t.Id, out var cur) && ReferenceEquals(cur, s) && sessions.Remove(t.Id)) SessionsChanged?.Invoke();
        });
        start = true;
        sessions[t.Id] = s;
        SessionsChanged?.Invoke();
        return s;
    }

    private void Finish(ChatThread t, ChatMessage m, bool ok, string? error, bool collectFiles = true, bool immediate = false)
    {
        if (finishing.ContainsKey(t.Id)) return;
        m.Status = null;
        m.Asks.Clear();
        if (leads.Remove(t.Id, out var lead)) m.Text += lead.Flush();
        DateTime? since = turnStarted.Remove(t.Id, out var started) ? started : null;
        bool fenced = AiModels.Inline(t.Model) && m.Text.Contains("```file:", StringComparison.Ordinal);
        if (!collectFiles || (since is null && !fenced))
        {
            Complete(t, m, ok, error);
            return;
        }
        var pending = new Finishing(m, immediate);
        finishing[t.Id] = pending;
        foreach (string id in outputWork.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToArray()) outputWork.Remove(id);
        outputWork[t.Id] = pending.Completion.Task;
        if (immediate) Complete(t, m, ok, error);
        string nameSlot = "{0}";
        string saved = Tr($"**{nameSlot}** — 파일 저장 완료", $"**{nameSlot}** — file saved");
        var snapshot = new ChatOutputRequest(m.Text, Path.Combine(ThreadFiles(t), "out"), since, fenced, saved);
        _ = FinishOutput(t, pending, snapshot, ok, error);
        Updated?.Invoke(t);
    }

    private async Task FinishOutput(ChatThread t, Finishing pending, ChatOutputRequest snapshot, bool ok, string? error)
    {
        ChatOutputResult? result = null;
        try
        {
            try
            {
                result = await Task.Run(() => collectOutput(snapshot, pending.Cancellation.Token), pending.Cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (pending.Cancellation.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                Log.Error("chat output", e);
                ok = false;
                error = e.Message;
            }
            if (pending.Cancellation.IsCancellationRequested) return;
            await OnUi(() =>
            {
                if (shutdown || !finishing.TryGetValue(t.Id, out var current) || !ReferenceEquals(current, pending)) return Task.FromResult(false);
                finishing.Remove(t.Id);
                if (result is not null)
                {
                    pending.Answer.Text = result.Text;
                    pending.Answer.Files = result.Files?.ToList();
                }
                if (pending.Immediate)
                {
                    Save(t);
                    Updated?.Invoke(t);
                }
                else Complete(t, pending.Answer, ok, error);
                return Task.FromResult(true);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ui.HasShutdownStarted) { }
        finally
        {
            pending.Cancellation.Dispose();
            pending.Completion.TrySetResult();
        }
    }

    private bool CancelFinish(ChatThread t)
    {
        if (!finishing.Remove(t.Id, out var pending)) return false;
        _ = pending.Cancellation.CancelAsync();
        return true;
    }

    private void Complete(ChatThread t, ChatMessage m, bool ok, string? error)
    {
        m.Streaming = false;
        m.Status = null;
        m.Asks.Clear();
        if (!ok)
        {
            m.Error = true;
            // an API session's errors are already worded (ApiClient) and name their provider
            string e = error is null ? AiErrors.Failed : AiModels.IsApi(t.Model) ? error : AiErrors.Friendly(error, AiModels.IsChatGpt(t.Model), details: true);
            m.Text = m.Text.Length > 0 ? m.Text + "\n\n" + e : e;
        }
        Touch(t);
        Updated?.Invoke(t);
    }

    public void Stop(ChatThread t)
    {
        if (preparing.ContainsKey(t.Id) || finishing.ContainsKey(t.Id))
        {
            EndSession(t);
            return;
        }
        if (sessions.TryGetValue(t.Id, out var s)) s.Interrupt();
    }

    public void Delete(ChatThread t)
    {
        Task previousDelete = WhenDeleted(t);
        CancelPreparation(t);
        CancelFinish(t);
        Task output = WhenFinished(t);
        Task<bool> jsonDeleted = store.Delete(Path.Combine(dir, t.Id + ".json"));
        Task preparation = retiredPreparations.Remove(t.Id, out var retired) ? Task.WhenAll(retired) : Task.CompletedTask;
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string folder = ThreadFiles(t);
        foreach (string id in deletingFiles.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToList()) deletingFiles.Remove(id);
        deletingFiles[t.Id] = Task.Run(async () =>
        {
            await previousDelete.ConfigureAwait(false);
            await preparation.ConfigureAwait(false);
            await output.ConfigureAwait(false);
            await jsonDeleted.ConfigureAwait(false);
            await released.Task.ConfigureAwait(false);
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Error("delete chat files", e); }
        });
        bool removed = sessions.Remove(t.Id, out var s);
        Threads.Remove(t);
        leads.Remove(t.Id);
        turnStarted.Remove(t.Id);
        try { s?.Dispose(); }
        finally { released.TrySetResult(); }
        if (removed) SessionsChanged?.Invoke();
    }

    private void Touch(ChatThread t)
    {
        t.Updated = Now;
        Threads.Remove(t);
        Threads.Insert(0, t);
        Save(t);
        // keep the newest 100 conversations outside groups (one put in a group stays until it is deleted)
        while (Threads.Count(x => x.Group is null) > 100)
        {
            var old = Threads.Last(x => x.Group is null);
            if (old.Busy) break;
            Delete(old);
        }
    }

    private void Save(ChatThread t)
    {
        if (t.Messages.Count == 0) return;
        store.Save(Path.Combine(dir, t.Id + ".json"), t);
    }

    public Task<bool> FlushAsync() => store.FlushAsync();
    public void Flush() => store.FlushAsync().GetAwaiter().GetResult();

    private void ReapIdle()
    {
        int before = sessions.Count, idleMinutes = Settings.Current.AiIdleMinutes;
        foreach (var (id, s) in sessions.ToList())
            if (idleMinutes > 0 && !preparing.ContainsKey(id) && !s.Busy && DateTime.UtcNow - s.LastUsed > TimeSpan.FromMinutes(idleMinutes))
            {
                s.Dispose();
                sessions.Remove(id);
            }
        if (sessions.Count != before) SessionsChanged?.Invoke();
        if (sessions.Count == 0) idle.Stop();
    }

    public void Shutdown(bool save = true)
    {
        if (shutdown) return;
        shutdown = true;
        idle.Stop();
        foreach (var pending in preparing.Values) _ = pending.Cancellation.CancelAsync();
        preparing.Clear();
        foreach (var pending in finishing.Values) _ = pending.Cancellation.CancelAsync();
        finishing.Clear();
        foreach (var s in sessions.Values) s.Dispose();
        sessions.Clear();
        if (save)
        {
            foreach (var t in Threads.ToArray())
                if (t.Busy)
                {
                    if (leads.Remove(t.Id, out var lead)) t.Messages[^1].Text += lead.Flush();
                    Complete(t, t.Messages[^1], false, AiErrors.Stopped);
                }
        }
        store.CloseAsync().GetAwaiter().GetResult(); // explicit shutdown durability; the store worker never calls the UI
    }
}
