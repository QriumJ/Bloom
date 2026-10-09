using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

    public List<string>? Done { get; set; } // what the AI did on the PC with the user's yes ("✓ 옮기기: …", "✕ …")
    [System.Text.Json.Serialization.JsonIgnore] public bool Streaming;
    [System.Text.Json.Serialization.JsonIgnore] public string? Status;
    [System.Text.Json.Serialization.JsonIgnore] public readonly List<ToolAsk> Asks = new(); // waiting for the user
}

internal sealed class ChatThread
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = Tr("새 대화", "New chat");
    public string Model { get; set; } = "sonnet"; // sonnet | opus | haiku | chatgpt:<Codex slug> (AiModels)
    public string? Effort { get; set; } // a level of AiModels.Efforts(Model); null = the settings' level
    public string? SessionId { get; set; }
    public string? SessionProvider { get; set; } // whose session SessionId is (null: claude, older files)
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

    public readonly List<ChatThread> Threads = new();

    /// UI thread; the thread whose messages changed (streaming text, status, end of turn).
    public event Action<ChatThread>? Updated;

    /// UI thread; a CLI session started or ended (the chat window's live marks).
    public event Action? SessionsChanged;

    /// Every open CLI session ends (a switch that changes how sessions start); the conversations stay.
    public void EndAll()
    {
        foreach (var t in Threads.Where(IsLive).ToList()) EndSession(t);
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
        if (!sessions.Remove(t.Id, out var s)) return;
        s.Dispose();
        if (t.Busy) Finish(t, t.Messages[^1], false, Tr("세션 종료 · 답변 중지됨", "Session ended · answer stopped"));
        SessionsChanged?.Invoke();
    }

    public Chats(Dispatcher ui)
    {
        this.ui = ui;
        dir = Paths.In("chats");
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
    /// other side (Claude ↔ ChatGPT) keeps its own level; the same side keeps the thread's, fitted to the model.
    public static void SetModel(ChatThread? t, string model)
    {
        bool sideChanged = AiModels.IsChatGpt(AiModels.Normalize(t?.Model ?? AiModels.DefaultModel())) != AiModels.IsChatGpt(model);
        var s = Settings.Current;
        s.AiProvider = AiModels.Provider(model);
        if (AiModels.GptSlug(model) is { } slug) s.GptModel = slug;
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
        if (AiModels.IsChatGpt(AiModels.Normalize(t?.Model ?? AiModels.DefaultModel()))) Settings.Current.GptEffort = level;
        else Settings.Current.AiEffort = level;
        Settings.Save();
    }

    public void Send(ChatThread t, string text, string? page = null, string? pageTitle = null, string? image = null, IReadOnlyList<string>? files = null)
    {
        if (t.Busy) return;
        if (!Threads.Contains(t)) Threads.Insert(0, t);
        if (t.Messages.Count == 0) t.Title = TitleOf(text, pageTitle ?? (files is { Count: > 0 } ? Path.GetFileName(files[0]) : null));
        var attached = files is { Count: > 0 } ? CopyIn(t, files) : null;
        t.Messages.Add(new ChatMessage { Role = "user", Text = text, At = Now, Page = pageTitle, Image = image, Files = attached });
        var answer = new ChatMessage { Role = "assistant", At = Now, Streaming = true, Status = Tr("생각하는 중…", "Thinking…") };
        t.Messages.Add(answer);
        Touch(t);
        Updated?.Invoke(t);

        var s = SessionFor(t, out string? error, out bool fresh);
        string prompt = ClaudeSession.LocalTime() + "\n" + (page is null ? text : $"<page title=\"{Esc(pageTitle ?? "")}\">\n{page}\n</page>\n\n{text}");
        if (attached is { Count: > 0 })
            prompt += AiModels.Provider(t.Model) == "chatgpt" ? Inline(attached) : "\n\n<attachments>\n" + string.Join("\n", attached.Select(f => "- " + FilesRel(t) + "/in/" + Path.GetFileName(f))) + "\n</attachments>";
        if (fresh) prompt = Earlier(t) + prompt;
        var images = new List<string>();
        if (image is not null) images.Add(image);
        if (attached is not null) images.AddRange(attached.Where(f => ImageType(f) is not null));
        turnStarted[t.Id] = DateTime.UtcNow.AddSeconds(-1);
        if (Settings.Current.AiTitles) leads[t.Id] = new ChatTitles.Lead();
        else leads.Remove(t.Id);
        if (s is null || !s.Send(prompt, images))
        {
            Finish(t, answer, false, error ?? Tr("메시지를 보내지 못했습니다. 다시 보내세요.", "Could not send the message. Send it again."));
            return;
        }
        idle.Start();
    }

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

    /// Copies of the attached files in the thread's in\ folder (the originals are never touched); same names get (2), (3).
    private static List<string> CopyIn(ChatThread t, IReadOnlyList<string> files)
    {
        string into = Path.Combine(FilesDir(t), "in");
        Directory.CreateDirectory(into);
        var list = new List<string>();
        foreach (string f in files.Take(MaxFiles))
        {
            try
            {
                if (!File.Exists(f) || new FileInfo(f).Length > MaxFileBytes) continue;
                string name = Path.GetFileNameWithoutExtension(f), ext = Path.GetExtension(f), dest = Path.Combine(into, name + ext);
                for (int n = 2; File.Exists(dest); n++) dest = Path.Combine(into, $"{name} ({n}){ext}");
                File.Copy(f, dest);
                list.Add(dest);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error("attach " + Path.GetFileName(f), e);
            }
        }
        return list;
    }

    /// ChatGPT reads no files itself: text files go into the message (each up to 200 KB), pictures as images, and the
    /// rest is named so it can say it could not open them.
    private static string Inline(List<string> attached)
    {
        var sb = new StringBuilder();
        foreach (string f in attached)
        {
            string name = Path.GetFileName(f);
            if (ImageType(f) is not null) continue;
            if (TextOf(f) is { } text) sb.Append($"\n\n<attachment name=\"{Esc(name)}\">\n{text}\n</attachment>");
            else sb.Append(Tr($"\n\n<attachment name=\"{Esc(name)}\">(이 형식은 여기서 열 수 없어요 — 내용을 받지 못했다고 알려 주세요)</attachment>", $"\n\n<attachment name=\"{Esc(name)}\">(This format can't be opened here — tell the user its contents didn't come through)</attachment>"));
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

    [System.Text.RegularExpressions.GeneratedRegex(@"```file:([^\r\n`]+)\r?\n(.*?)\r?\n```", System.Text.RegularExpressions.RegexOptions.Singleline)]
    private static partial System.Text.RegularExpressions.Regex FencedFile();

    /// ```file:name blocks in a ChatGPT answer become files in out\ (the block stays visible as a short note).
    private static void SaveFencedFiles(ChatThread t, ChatMessage m)
    {
        if (!m.Text.Contains("```file:", StringComparison.Ordinal)) return;
        string into = Path.Combine(FilesDir(t), "out");
        m.Text = FencedFile().Replace(m.Text, match =>
        {
            string name = string.Concat(match.Groups[1].Value.Trim().Split(Path.GetInvalidFileNameChars()));
            if (name.Length == 0 || name.StartsWith('.')) return match.Value;
            try
            {
                Directory.CreateDirectory(into);
                File.WriteAllText(Path.Combine(into, name), match.Groups[2].Value, new UTF8Encoding(false));
                return Tr($"**{name}** — 파일 저장 완료", $"**{name}** — file saved");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error("fenced file", e);
                return match.Value;
            }
        });
    }

    /// The files the AI wrote into the thread's out\ folder during the turn that just ended (null: none).
    private List<string>? MadeSince(ChatThread t)
    {
        if (!turnStarted.Remove(t.Id, out var since)) return null;
        string from = Path.Combine(FilesDir(t), "out");
        if (!Directory.Exists(from)) return null;
        var made = new DirectoryInfo(from).EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => f.LastWriteTimeUtc >= since)
            .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(f => f.FullName)
            .Take(20)
            .ToList();
        return made.Count > 0 ? made : null;
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

    private IChatSession? SessionFor(ChatThread t, out string? error, out bool fresh)
    {
        error = null;
        fresh = false;
        string effort = EffortOf(t), provider = AiModels.Provider(t.Model);
        if (sessions.TryGetValue(t.Id, out var s) && s.Alive && s.Model == t.Model && s.Effort == effort) return s;
        if (s is not null)
        {
            s.Dispose();
            sessions.Remove(t.Id);
        }
        s = Demo.On ? new DemoSession(t.Model, effort) : provider == "chatgpt" ? new CodexSession(t.Model, effort, FilesRel(t), t.Id) : new ClaudeSession(t.Model, effort, FilesRel(t), "Bloom · " + (t.Messages.Count > 0 ? (t.Title.Length > 40 ? t.Title[..40] + "…" : t.Title) : Tr("새 대화", "New chat")));
        string? resume = (t.SessionProvider ?? "claude") == provider ? t.SessionId : null;
        fresh = resume is null && t.Messages.Count > 2;
        ChatMessage? Current() => t.Messages.Count > 0 && t.Messages[^1].Role == "assistant" && t.Messages[^1].Streaming ? t.Messages[^1] : null;
        bool needBreak = false;
        s.SessionStarted += id => ui.BeginInvoke(() =>
        {
            t.SessionId = id;
            t.SessionProvider = provider;
            Save(t);
        });
        s.TextDelta += d => ui.BeginInvoke(() =>
        {
            if (Current() is not { } m) return;
            d = Shown(t, d);
            if (d.Length == 0) return;
            if (needBreak && m.Text.Length > 0 && !m.Text.EndsWith('\n')) m.Text += "\n\n";
            needBreak = false;
            m.Text += d;
            m.Status = null;
            Updated?.Invoke(t);
        });
        s.AssistantText += whole => ui.BeginInvoke(() =>
        {
            if (Current() is not { } m) return;
            whole = Shown(t, whole);
            if (whole.Length == 0) return;
            if (m.Text.Length > 0 && !m.Text.EndsWith('\n')) m.Text += "\n\n";
            m.Text += whole;
            Updated?.Invoke(t);
        });
        s.Status += st => ui.BeginInvoke(() =>
        {
            if (Current() is not { } m) return;
            if (st is not null) needBreak = true;
            m.Status = st;
            Updated?.Invoke(t);
        });
        s.TurnEnded += (ok, err) => ui.BeginInvoke(() =>
        {
            if (Current() is { } m) Finish(t, m, ok, err);
        });
        s.ToolAsked += raw => ui.BeginInvoke(() => Asked(t, Current(), s, raw));
        s.ToolAskWithdrawn += id => ui.BeginInvoke(() =>
        {
            if (Current() is { } m && m.Asks.RemoveAll(a => a.RequestId == id) > 0) Updated?.Invoke(t);
        });
        s.Exited += _ => ui.BeginInvoke(() =>
        {
            if (sessions.TryGetValue(t.Id, out var cur) && ReferenceEquals(cur, s) && sessions.Remove(t.Id)) SessionsChanged?.Invoke();
        });
        if (!s.Start(resume, out error)) return null;
        sessions[t.Id] = s;
        SessionsChanged?.Invoke();
        return s;
    }

    private void Finish(ChatThread t, ChatMessage m, bool ok, string? error)
    {
        m.Streaming = false;
        m.Status = null;
        m.Asks.Clear();
        if (leads.Remove(t.Id, out var lead)) m.Text += lead.Flush();
        if (AiModels.Provider(t.Model) == "chatgpt") SaveFencedFiles(t, m);
        m.Files = MadeSince(t);
        if (!ok)
        {
            m.Error = true;
            string e = error is null ? AiErrors.Failed : AiErrors.Friendly(error, AiModels.IsChatGpt(t.Model), details: true);
            m.Text = m.Text.Length > 0 ? m.Text + "\n\n" + e : e;
        }
        Touch(t);
        Updated?.Invoke(t);
    }

    public void Stop(ChatThread t)
    {
        if (sessions.TryGetValue(t.Id, out var s)) s.Interrupt();
    }

    public void Delete(ChatThread t)
    {
        if (sessions.Remove(t.Id, out var s))
        {
            s.Dispose();
            SessionsChanged?.Invoke();
        }
        Threads.Remove(t);
        leads.Remove(t.Id);
        turnStarted.Remove(t.Id);
        try
        {
            File.Delete(Path.Combine(dir, t.Id + ".json"));
            if (Directory.Exists(FilesDir(t))) Directory.Delete(FilesDir(t), true); // its attachments and the files it made
        }
        catch
        {
            /* already gone */
        }
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
        Json.Save(Path.Combine(dir, t.Id + ".json"), t);
    }

    private void ReapIdle()
    {
        int before = sessions.Count, idleMinutes = Settings.Current.AiIdleMinutes;
        foreach (var (id, s) in sessions.ToList())
            if (idleMinutes > 0 && !s.Busy && DateTime.UtcNow - s.LastUsed > TimeSpan.FromMinutes(idleMinutes))
            {
                s.Dispose();
                sessions.Remove(id);
            }
        if (sessions.Count != before) SessionsChanged?.Invoke();
        if (sessions.Count == 0) idle.Stop();
    }

    public void Shutdown()
    {
        foreach (var s in sessions.Values) s.Dispose();
        sessions.Clear();
    }
}
