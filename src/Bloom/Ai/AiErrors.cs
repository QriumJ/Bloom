using System;
using System.Linq;
using Bloom.Core;

namespace Bloom.Ai;

/// The AI failures Bloom words itself, one text each (used where they happen), and Friendly: a CLI's own error text
/// turned into what happened and what to do next. The raw text goes to the log; when it matches nothing known, a chat
/// shows it under the message for the bug report.
internal static class AiErrors
{
    public static string Stopped => Tr("답변 중지됨", "Answer stopped");
    public static string ClaudeMissing => Tr("Claude Code를 찾지 못했습니다. 설치 후 로그인하세요.", "Claude Code not found. Install it and sign in.");
    public static string CodexMissing => Tr("Codex를 찾지 못했습니다. 설치 후 로그인하세요.", "Codex not found. Install it and sign in.");
    public static string Missing(bool chatgpt) => chatgpt ? CodexMissing : ClaudeMissing;
    public static string ClaudeStartFailed => Tr("Claude Code를 시작하지 못했습니다. 설치 상태를 확인하고 다시 보내세요.", "Could not start Claude Code. Check its installation and send again.");
    public static string StartFailed => Tr("AI를 시작하지 못했습니다. 설치 상태를 확인하고 다시 시도하세요.", "Could not start AI. Check its installation and try again.");
    public static string ClaudeNoAnswer => Tr("Claude 답변이 없습니다. 다시 보내세요.", "No answer from Claude. Send again.");
    public static string CodexNoAnswer => Tr("Codex 답변이 없습니다. 다시 보내세요.", "No answer from Codex. Send again.");
    public static string Failed => Tr("답변 실패 · 다시 보내세요", "Answer failed. Send the message again.");
    public static string TimedOut => Tr("90초 동안 응답 없음 · 다시 시도하세요", "No response for 90 s. Try again.");

    /// Bloom's own texts pass through Friendly unchanged.
    private static bool Own(string e) => new[] { Stopped, ClaudeMissing, CodexMissing, ClaudeStartFailed, StartFailed, ClaudeNoAnswer, CodexNoAnswer, Failed, TimedOut }.Contains(e);

    /// `details`: an unknown error keeps its last line under the message (a chat answer; toasts stay one line).
    public static string Friendly(string e, bool chatgpt, bool details = false)
    {
        if (Own(e)) return e;
        if (e.Length > 0) Log.Write("ai error: " + e);
        string l = e.ToLowerInvariant();
        if (l.Contains("login") || l.Contains("log in") || l.Contains("oauth") || l.Contains("authenticat") || l.Contains("401") || l.Contains("unauthorized"))
            return chatgpt ? Tr("ChatGPT 로그인이 필요합니다. 터미널에서 codex login을 실행하세요.", "Sign in to ChatGPT. Run codex login in a terminal.") : Tr("Claude 로그인이 필요합니다. 터미널에서 claude를 실행하고 로그인하세요.", "Sign in to Claude. Run claude in a terminal and sign in.");
        if (l.Contains("rate limit") || l.Contains("usage limit") || l.Contains("limit reached") || l.Contains("quota") || l.Contains("429"))
            return Tr("AI 사용 한도에 도달했습니다. 한도 초기화 후 다시 보내세요.", "AI usage limit reached. Send again after the limit resets.");
        if ((l.Contains("claude") || l.Contains("codex")) && l.Contains("not found"))
            return Missing(chatgpt);
        if (l.Contains("timeout") || l.Contains("timed out"))
            return Tr("AI 응답 시간이 초과되었습니다. 잠시 후 다시 시도하세요.", "AI response timed out. Try again shortly.");
        if (l.Contains("connect") || l.Contains("network") || l.Contains("fetch") || l.Contains("socket"))
            return Tr("AI에 연결하지 못했습니다. 인터넷 연결을 확인하고 다시 보내세요.", "Could not connect to AI. Check your connection and send again.");
        if (l.Contains("permission") || l.Contains("access denied"))
            return Tr("AI가 필요한 파일에 접근하지 못했습니다. 파일 권한을 확인하고 다시 보내세요.", "AI could not access a required file. Check file permissions and send again.");
        if (l.Contains("model") && (l.Contains("unsupported") || l.Contains("not available") || l.Contains("does not exist")))
            return Tr("선택한 AI 모델을 사용할 수 없습니다. 다른 모델을 선택하고 다시 보내세요.", "AI model unavailable. Choose another model and send again.");
        string generic = Tr("AI 답변을 받지 못했습니다. 다시 보내세요. 반복되면 로그인과 인터넷 연결을 확인하세요.", "Could not get an AI answer. Send again. If it persists, check sign-in and connection.");
        string? last = e.Split('\n').Select(x => x.Trim()).LastOrDefault(x => x.Length > 0);
        if (!details || last is null) return generic;
        string shown = last.Length > 200 ? last[..200] + "…" : last;
        return generic + "\n\n" + Tr($"오류 내용: {shown}", $"Details: {shown}");
    }
}
