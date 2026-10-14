using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// A conversation with an API model (ApiProviders): Bloom keeps the conversation itself and sends all of it each turn,
/// streaming the answer back as TextDelta. A turn is ApiTurn's loop: the model may call tools (ApiToolbox: Bloom's pc
/// tool, files and commands in the chat's folder), each action asked in the chat first (ToolAsked → Chats → Answer), and
/// its steps are kept with the answer (StepsDone → ChatMessage.Steps). A session started again (another model, after
/// the idle close) begins from the saved chat, tool calls and all (Past, HistoryOf). Nothing runs between turns;
/// Interrupt cancels the HTTP stream, a running command and the questions waiting. Attached files come inline; files it
/// writes come back through write_file into out\ (or as ```file: blocks, as with ChatGPT, when it has no file tools).
internal sealed class ApiSession : IChatSession
{
    private readonly object gate = new();
    private readonly List<ApiMessage> history = new();
    private IReadOnlyList<ApiPastTurn>? past; // the saved chat's turns, built into `history` by the first turn
    private readonly string conversation, unavailable, folder;
    private CancellationTokenSource? turn;
    private PendingToolAsks? asks; // the running turn's questions to the user

    /// `past`: Past(the chat's messages); `unavailable`: the note for a file that can't be read (Chats.UnavailableNote);
    /// `folder`: the chat's own (its attachments' in\, the out\ its files go to; commands run there).
    public ApiSession(string model, string effort, string conversation, IReadOnlyList<ApiPastTurn> past, string unavailable, string folder)
    {
        Model = model;
        Effort = effort;
        this.conversation = conversation;
        this.past = past;
        this.unavailable = unavailable;
        this.folder = folder;
    }

    public string Model { get; }
    public string Effort { get; }
    public string? SessionId { get; private set; }
    public bool Busy { get; private set; }
    public bool Alive { get; private set; }
    public DateTime LastUsed { get; private set; } = DateTime.UtcNow;

    public event Action<string>? SessionStarted;
    public event Action<string>? TextDelta;
    public event Action<bool, string?>? TurnEnded;
    public event Action<string?>? Status;
    public event Action<ToolAsk>? ToolAsked;
    public event Action<string>? ToolAskWithdrawn;

    /// Raised before TurnEnded with the steps the answer took (results cut to ApiStep.MaxSaved), for the chat's file.
    public event Action<List<ApiStep>>? StepsDone;

    // never raised: whole answers come as deltas, no process ends
    public event Action<string>? AssistantText { add { } remove { } }
    public event Action<string>? Exited { add { } remove { } }

    /// Nothing to start: the session is marked (so a later Claude or ChatGPT session knows it hasn't seen these turns).
    public bool Start(string? resume, out string? error)
    {
        error = null;
        lock (gate) Alive = true;
        SessionId = "api-" + conversation;
        SessionStarted?.Invoke(SessionId);
        return true;
    }

    public void Answer(ToolAsk ask, bool allow)
    {
        lock (gate) asks?.Answer(ask.RequestId, allow);
    }

    /// A question for the user in the chat; its answer, or no when the turn ends or stops first.
    private Task<bool> Ask(PendingToolAsks pending, ToolAsk ask)
    {
        var done = pending.Add(ask.RequestId);
        if (!done.IsCompleted) ToolAsked?.Invoke(ask);
        return done;
    }

    public bool Send(string text, IReadOnlyList<string> images)
    {
        var asked = new ApiMessage(ApiMessage.User, text, images.ToArray());
        CancellationTokenSource stop;
        lock (gate)
        {
            if (!Alive || Busy) return false;
            Busy = true;
            LastUsed = DateTime.UtcNow;
            stop = turn = new CancellationTokenSource();
        }
        // the settings as they are now: 설정 > PC 작업's switches and 권한 apply from the next message, as in the CLI chats
        var s = Settings.Current;
        var pc = App.Mcp.BloomTools.OfferedTo("api") ? Chats.Tools as IPcTools : null;
        var rules = new Dictionary<string, string>(s.Permissions);
        _ = Task.Run(() => RunTurn(asked, stop, pc, rules, s.AiTitles));
        return true;
    }

    /// One turn, on a worker thread: the history built from the saved chat (the first turn: its files are read again),
    /// the answer's steps (ApiTurn), and they into the conversation (also a partial answer, so the next turn knows what
    /// was said).
    private async Task RunTurn(ApiMessage asked, CancellationTokenSource stop, IPcTools? pc, Dictionary<string, string> rules, bool titles)
    {
        ApiReply reply;
        var steps = new List<ApiStep>();
        using var pending = new PendingToolAsks(id => ToolAskWithdrawn?.Invoke(id));
        lock (gate) asks = pending;
        try
        {
            using var stopping = stop.Token.Register(pending.Dispose); // a stop answers every waiting question no
            List<ApiMessage> sent;
            IReadOnlyList<ApiPastTurn>? earlier;
            lock (gate) (earlier, past) = (past, null);
            var built = earlier is null ? null : HistoryOf(earlier, unavailable);
            lock (gate)
            {
                if (built is not null) history.InsertRange(0, built);
                sent = [.. history, asked];
            }
            var (request, error) = await ApiClient.PrepareAsync(Model, Effort, "", sent, conversation).ConfigureAwait(false);
            if (request is null) reply = new ApiReply(false, "", error);
            else
            {
                var tools = request.TakesTools ? new ApiToolbox(folder, pc, rules, ask => Ask(pending, ask), ApiToolbox.Secrets()) : null;
                request = request with { System = Instructions(titles, tools, pc is not null && tools is not null) };
                (reply, steps) = await ApiTurn.RunAsync(request, tools, d => TextDelta?.Invoke(d), st => Status?.Invoke(st), stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Log.Error("api turn", e);
            reply = new ApiReply(false, "", AiErrors.Failed);
        }
        lock (gate)
        {
            if (steps.Count > 0)
            {
                history.Add(asked);
                history.AddRange(steps.SelectMany(step => step.Messages()));
            }
            else if (reply.Text.Length > 0)
            {
                history.Add(asked);
                history.Add(new ApiMessage(ApiMessage.Assistant, reply.Text, Array.Empty<string>()));
            }
            asks = null;
            Busy = false;
            LastUsed = DateTime.UtcNow;
            if (ReferenceEquals(turn, stop)) turn = null;
            stop.Dispose();
        }
        if (steps.Any(step => step.Keeps)) StepsDone?.Invoke(steps.Select(Saved).ToList());
        TurnEnded?.Invoke(reply.Ok, reply.Ok ? null : reply.Error ?? AiErrors.Failed);
    }

    /// A step as the chat's file keeps it: long results cut.
    private static ApiStep Saved(ApiStep step) => step with
    {
        Calls = step.Calls.Select(c => c.Result.Length > ApiStep.MaxSaved ? c with { Result = c.Result[..ApiStep.MaxSaved] + "\n… (cut in the saved chat)" } : c).ToList(),
    };

    public void Interrupt()
    {
        lock (gate) turn?.Cancel();
    }

    public void Dispose()
    {
        lock (gate)
        {
            Alive = false;
            turn?.Cancel();
        }
    }

    private const string Intro = "You are the assistant in a chat inside Bloom, a Windows launcher the user opens with the Win key. ";

    /// The instructions: the chats' own (no web search here). Without write_file (no file tools, or 파일 쓰기 사용 안 함),
    /// files go inline as with ChatGPT; with it, the working folder and its out\ for the files the user asks for (Chats
    /// shows what lands there).
    private static string Instructions(bool titles, ApiToolbox? tools, bool pc)
    {
        string folder = tools is not null && tools.Offers(ApiToolbox.ReadFile)
            ? "You have file tools on the user's Windows PC; your working folder is this chat's own, relative paths start there, and the user's attached files have copies in in\\ there. "
                + (tools.Offers(ApiToolbox.RunCommand) ? "run_command runs Windows PowerShell. " : "")
                + "Write, edit or run a command only when the request needs it: each may wait for the user's OK, and if declined, don't retry. "
            : "";
        if (tools is null || !tools.Offers(ApiToolbox.WriteFile)) return ClaudeSession.SystemPrompt(null, Intro + CodexSession.InlineFiles + folder, pc: pc, titles: titles, web: false);
        string files = "Files the user attached come in the message: text files inside <attachment name=\"…\"> blocks, pictures as images. "
            + "When the user asks for a file (a document, code, a table, an SVG picture...), write it with write_file into out\\ (as out\\<a fitting name>) and say its name in one line; "
            + "it appears in the chat as a file the user can open or save. ";
        return ClaudeSession.SystemPrompt(null, Intro + files + folder, pc: pc, titles: titles, web: false);
    }

    /// The saved chat's earlier turns, on the UI thread (strings only): each question with the answer it got, the
    /// question as it was sent (ChatMessage.Sent: the time, a page's contents; older chats: its text); the turn being
    /// sent (the last two messages) is left out. A failed or stopped answer keeps the tool calls it finished, with their
    /// results (the model must know what already ran), but not its text; one with nothing finished drops the pair.
    public static List<ApiPastTurn> Past(IReadOnlyList<ChatMessage> messages)
    {
        var list = new List<ApiPastTurn>();
        int end = messages.Count - 2;
        for (int i = 0; i + 1 < end; i++)
        {
            ChatMessage q = messages[i], a = messages[i + 1];
            if (q.Role != ApiMessage.User || a.Role != ApiMessage.Assistant) continue;
            i++;
            string asked = q.Sent ?? q.Text;
            var steps = a.Steps?.Where(step => !a.Error || step.Calls.Count > 0).ToArray() ?? [];
            if (asked.Length == 0 || (a.Error || a.Text.Length == 0) && steps.Length == 0) continue;
            list.Add(new ApiPastTurn(asked, q.Image, q.Files?.ToArray() ?? [], a.Error ? "" : a.Text) { Steps = steps });
        }
        return list;
    }

    /// Those turns as an API takes them, on a worker thread: text files go into the question again as when it was
    /// sent (Chats.Inline), pictures that are still there along with it; an answer that called tools as its steps.
    public static List<ApiMessage> HistoryOf(IEnumerable<ApiPastTurn> past, string unavailable)
    {
        var list = new List<ApiMessage>();
        foreach (var t in past)
        {
            var files = t.Files.ToList();
            string[] images = [.. (t.Image is { } image ? [image] : Array.Empty<string>()).Concat(files.Where(f => Chats.ImageType(f) is not null)).Where(File.Exists)];
            string text = files.Count > 0 ? t.Asked + Chats.Inline(files, unavailable) : t.Asked;
            list.Add(new ApiMessage(ApiMessage.User, text, images));
            if (t.Steps.Count > 0) list.AddRange(t.Steps.SelectMany(step => step.Messages()));
            else list.Add(new ApiMessage(ApiMessage.Assistant, t.Answer, Array.Empty<string>()));
        }
        return list;
    }
}
