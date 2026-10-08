using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Bloom.Ai;

/// The recording mode's chat (Core.Demo): a canned answer comes in piece by piece like a real one. No CLI runs and
/// nothing leaves the PC; the answer is picked by a few words of the question, with a general one for the rest.
internal sealed class DemoSession : IChatSession
{
    private CancellationTokenSource? turn;

    public DemoSession(string model, string effort)
    {
        Model = model;
        Effort = effort;
    }

    public string Model { get; }
    public string Effort { get; }
    public string? SessionId { get; private set; }
    public bool Busy { get; private set; }
    public bool Alive { get; private set; }
    public DateTime LastUsed { get; private set; } = DateTime.Now;

    public event Action<string>? SessionStarted;
    public event Action<string>? TextDelta;
    public event Action<bool, string?>? TurnEnded;

    // never raised here: no tools, no statuses, no process
    public event Action<string?>? Status { add { } remove { } }
    public event Action<string>? AssistantText { add { } remove { } }
    public event Action<string>? Exited { add { } remove { } }
    public event Action<ToolAsk>? ToolAsked { add { } remove { } }
    public event Action<string>? ToolAskWithdrawn { add { } remove { } }

    public bool Start(string? resume, out string? error)
    {
        error = null;
        Alive = true;
        SessionId = resume ?? "demo-" + Guid.NewGuid().ToString("N")[..8];
        SessionStarted?.Invoke(SessionId);
        return true;
    }

    public bool Send(string text, IReadOnlyList<string> images)
    {
        if (!Alive || Busy) return false;
        Busy = true;
        LastUsed = DateTime.Now;
        var stop = turn = new CancellationTokenSource();
        string answer = DemoAnswers.Chat(text);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(900, stop.Token); // a moment of "생각하는 중…"
                var parts = StringInfo.GetTextElementEnumerator(answer);
                var chunk = new System.Text.StringBuilder();
                while (parts.MoveNext())
                {
                    chunk.Append(parts.GetTextElement());
                    if (chunk.Length < 3) continue;
                    TextDelta?.Invoke(chunk.ToString());
                    chunk.Clear();
                    await Task.Delay(40, stop.Token);
                }
                if (chunk.Length > 0) TextDelta?.Invoke(chunk.ToString());
            }
            catch (OperationCanceledException)
            {
                // 멈춤: the turn ends with what came so far
            }
            Busy = false;
            TurnEnded?.Invoke(true, null);
        });
        return true;
    }

    public void Answer(ToolAsk ask, bool allow)
    {
        // nothing is ever asked
    }

    public void Interrupt() => turn?.Cancel();

    public void Dispose()
    {
        Alive = false;
        turn?.Cancel();
    }
}

/// The recording mode's made-up answers: the chat's, and the one-shot asks' (translation, AI 빠른 동작).
internal static class DemoAnswers
{
    // the same sentence in Korean, English and Japanese: a translation card shows the other two
    private static readonly string[][] Sentences =
    {
        new[] { "회의가 금요일 오후 3시로 옮겨졌어요.\n고친 슬라이드를 가져와 주세요.", "The meeting has moved to Friday at 3 PM.\nPlease bring the updated slides.", "会議は金曜日の午後3時に変更になりました。\n修正したスライドを持ってきてください。" },
        new[] { "오늘 저녁 7시에 만나요. 늦으면 연락 주세요.", "See you at 7 tonight. Text me if you're running late.", "今夜7時に会いましょう。遅れるなら連絡してください。" },
        new[] { "이 기능은 다음 업데이트에서 추가될 예정입니다.", "This feature will be added in the next update.", "この機能は次のアップデートで追加される予定です。" },
    };

    /// OneShot.Ask in the recording mode: a known sentence in the asked language, else a line saying only the samples
    /// are translated; other asks get a general answer. A short wait, as a real ask takes one.
    public static async Task<(string? text, string? error)> Ask(string name, string text, CancellationToken ct)
    {
        try
        {
            await Task.Delay(800, ct);
        }
        catch (OperationCanceledException)
        {
            return (null, null);
        }
        if (!name.StartsWith("translate-", StringComparison.Ordinal))
            return (Tr("녹화 모드의 예시 답이에요. 실제 답은 Claude나 ChatGPT가 써요.", "A sample answer from the recording mode. Claude or ChatGPT writes the real one."), null);
        int lang = name["translate-".Length..] switch { "en" => 1, "ja" => 2, _ => 0 };
        string key = Key(text);
        foreach (var s in Sentences)
            if (Array.Exists(s, v => Key(v) == key)) return (s[lang], null);
        return (Tr("녹화 모드에서는 예시 문장만 번역해요.", "The recording mode translates its sample sentences only."), null);
    }

    private static string Key(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();

    /// The question without what Chats wraps around it (an earlier conversation or a page before it, attachments after).
    public static string Chat(string prompt)
    {
        string q = prompt;
        foreach (string end in new[] { "</earlier_conversation>", "</page>" })
            if (q.LastIndexOf(end, StringComparison.Ordinal) is int at and >= 0) q = q[(at + end.Length)..];
        if (q.IndexOf("\n\n<attachments>", StringComparison.Ordinal) is int files and >= 0) q = q[..files];
        q = q.ToLowerInvariant();
        bool Has(params string[] words) => Array.Exists(words, w => q.Contains(w, StringComparison.Ordinal));

        if (Has("발표", "talk", "presentation"))
            return Tr(
                "발표 전까지 할 일이에요.\n\n1. **슬라이드 초안** 목요일까지\n2. **리허설** 팀과 한 번\n3. **예상 질문** 다섯 개 정리\n\n하나씩 같이 볼까요?",
                "Here's what to do before the talk.\n\n1. **Draft the slides** by Thursday\n2. **Rehearse** once with the team\n3. **List five questions** people may ask\n\nWant to go through them one by one?");
        if (Has("메일", "mail"))
            return Tr(
                "이렇게 보내 보세요.\n\n> 안녕하세요, 지난 회의 자료 공유드려요. 금요일까지 의견 주시면 다음 주 초에 정리해서 다시 보내 드릴게요. 감사합니다.\n\n더 짧게 줄여 드릴까요?",
                "Here's a draft.\n\n> Hi all, sharing the notes from our last meeting. Send me your thoughts by Friday and I'll circulate a summary early next week. Thanks!\n\nWant it shorter?");
        if (Has("요약", "정리", "summar"))
            return Tr(
                "핵심만 추렸어요.\n\n- **목표**: 다음 분기 재구매율 45%\n- **방법**: 첫 구매 후 7일 안에 안내 메일\n- **할 일**: 담당자 정하기, 메일 문구 쓰기\n\n표로도 만들어 드릴까요?",
                "Here are the key points.\n\n- **Goal**: 45% repeat buyers next quarter\n- **How**: a follow-up email within 7 days of the first order\n- **Next**: pick an owner, write the email\n\nWant it as a table?");
        if (Has("여행", "주말", "trip", "weekend"))
            return Tr(
                "가볍게 다녀오기 좋은 일정이에요.\n\n1. **토요일 오전** 시장 구경과 브런치\n2. **오후** 바닷가 산책\n3. **일요일** 느지막이 카페 들렀다 귀가\n\n숙소도 같이 찾아볼까요?",
                "Here's an easy weekend plan.\n\n1. **Saturday morning** a market and brunch\n2. **Afternoon** a walk by the sea\n3. **Sunday** a slow café stop, then home\n\nShall we look for a place to stay too?");
        return Tr(
            "좋아요, 이렇게 해 볼게요.\n\n1. **할 일**을 먼저 나눠요\n2. 급한 것부터 **순서**를 정해요\n3. 끝난 건 바로 **체크**해요\n\n더 자세히 알려 드릴까요?",
            "Sure, here's a way to go about it.\n\n1. **Split** it into small tasks\n2. **Order** them by what's most urgent\n3. **Check** each one off as you go\n\nWant more detail?");
    }
}
