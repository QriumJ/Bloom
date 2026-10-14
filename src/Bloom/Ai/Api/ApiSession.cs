using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// A conversation with an API model (ApiProviders): Bloom keeps the conversation itself and sends all of it each turn,
/// streaming the answer back as TextDelta. A session started again (another model, after the idle close) begins from
/// the saved chat (HistoryOf). Nothing runs between turns; Interrupt cancels the HTTP stream. No tools yet: a turn is one
/// model call (RunTurn), the place a tool loop goes. Attached files come inline and files it writes come back as
/// ```file: blocks, as with ChatGPT.
internal sealed class ApiSession : IChatSession
{
    private readonly object gate = new();
    private readonly List<ApiMessage> history = new();
    private IReadOnlyList<ApiPastTurn>? past; // the saved chat's turns, built into `history` by the first turn
    private readonly string conversation, unavailable;
    private CancellationTokenSource? turn;

    /// `past`: Past(the chat's messages); `unavailable`: the note for a file that can't be read (Chats.UnavailableNote).
    public ApiSession(string model, string effort, string conversation, IReadOnlyList<ApiPastTurn> past, string unavailable)
    {
        Model = model;
        Effort = effort;
        this.conversation = conversation;
        this.past = past;
        this.unavailable = unavailable;
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

    // never raised: whole answers come as deltas, no tools ask, no process ends
    public event Action<string?>? Status { add { } remove { } }
    public event Action<string>? AssistantText { add { } remove { } }
    public event Action<string>? Exited { add { } remove { } }
    public event Action<ToolAsk>? ToolAsked { add { } remove { } }
    public event Action<string>? ToolAskWithdrawn { add { } remove { } }

    /// Nothing to start: the session is marked (so a later Claude or ChatGPT session knows it hasn't seen these turns).
    public bool Start(string? resume, out string? error)
    {
        error = null;
        lock (gate) Alive = true;
        SessionId = "api-" + conversation;
        SessionStarted?.Invoke(SessionId);
        return true;
    }

    public void Answer(ToolAsk ask, bool allow) { } // no tools ask yet

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
        string system = Instructions(Settings.Current.AiTitles);
        _ = Task.Run(() => RunTurn(system, asked, stop));
        return true;
    }

    /// One turn, on a worker thread: the history built from the saved chat (the first turn: its files are read again),
    /// the call, its answer into the conversation (also a partial one, so the next turn knows what was said).
    private async Task RunTurn(string system, ApiMessage asked, CancellationTokenSource stop)
    {
        ApiReply reply;
        try
        {
            List<ApiMessage> sent;
            IReadOnlyList<ApiPastTurn>? earlier;
            lock (gate) (earlier, past) = (past, null);
            var built = earlier is null ? null : HistoryOf(earlier, unavailable);
            lock (gate)
            {
                if (built is not null) history.InsertRange(0, built);
                sent = [.. history, asked];
            }
            var (request, error) = await ApiClient.PrepareAsync(Model, Effort, system, sent, conversation).ConfigureAwait(false);
            reply = request is null ? new ApiReply(false, "", error) : await ApiClient.StreamAsync(request, d => TextDelta?.Invoke(d), stop.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Error("api turn", e);
            reply = new ApiReply(false, "", AiErrors.Failed);
        }
        lock (gate)
        {
            if (reply.Text.Length > 0)
            {
                history.Add(asked);
                history.Add(new ApiMessage(ApiMessage.Assistant, reply.Text, Array.Empty<string>()));
            }
            Busy = false;
            LastUsed = DateTime.UtcNow;
            if (ReferenceEquals(turn, stop)) turn = null;
            stop.Dispose();
        }
        TurnEnded?.Invoke(reply.Ok, reply.Ok ? null : reply.Error ?? AiErrors.Failed);
    }

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

    /// The instructions: the chats' own (no web search here), with files inline as with ChatGPT.
    private static string Instructions(bool titles) => ClaudeSession.SystemPrompt(null,
        "You are the assistant in a chat inside Bloom, a Windows launcher the user opens with the Win key. " + CodexSession.InlineFiles,
        titles: titles, web: false);

    /// The saved chat's earlier turns, on the UI thread (strings only): each question with the answer it got (a failed
    /// or empty answer drops the pair), the question as it was sent (ChatMessage.Sent: the time, a page's contents;
    /// older chats: its text); the turn being sent (the last two messages) is left out.
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
            if (a.Error || a.Text.Length == 0 || asked.Length == 0) continue;
            list.Add(new ApiPastTurn(asked, q.Image, q.Files?.ToArray() ?? [], a.Text));
        }
        return list;
    }

    /// Those turns as an API takes them, on a worker thread: text files go into the question again as when it was
    /// sent (Chats.Inline), pictures that are still there along with it.
    public static List<ApiMessage> HistoryOf(IEnumerable<ApiPastTurn> past, string unavailable)
    {
        var list = new List<ApiMessage>();
        foreach (var t in past)
        {
            var files = t.Files.ToList();
            string[] images = [.. (t.Image is { } image ? [image] : Array.Empty<string>()).Concat(files.Where(f => Chats.ImageType(f) is not null)).Where(File.Exists)];
            string text = files.Count > 0 ? t.Asked + Chats.Inline(files, unavailable) : t.Asked;
            list.Add(new ApiMessage(ApiMessage.User, text, images));
            list.Add(new ApiMessage(ApiMessage.Assistant, t.Answer, Array.Empty<string>()));
        }
        return list;
    }
}
