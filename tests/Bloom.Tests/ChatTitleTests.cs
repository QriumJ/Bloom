using Bloom.Ai;

namespace Bloom.Tests;

/// Titles the AI sets per task (Ai\ChatTitles.cs).
internal static class ChatTitleTests
{
    private static string Run(string[] pieces, out string? title, string model = "opus")
    {
        var lead = new ChatTitles.Lead();
        string shown = "";
        title = null;
        foreach (string p in pieces)
        {
            shown += lead.Take(p, out string? field, out string? task);
            if (task is not null) title = ChatTitles.Compose(model, field ?? "", task);
        }
        return shown + lead.Flush();
    }

    public static void TitleLineIsTakenOut()
    {
        string shown = Run(new[] { "<chat", "-title field=\"Code\">로그인 ", "수정</chat-title>\n\n", "네, 고칠게요." }, out string? title);
        Check.Eq(shown, "네, 고칠게요.", "the line never shows");
        Check.Eq(title, "[◆ Opus • Code ] 로그인 수정", "model mark + field + task");

        Check.Eq(Run(new[] { "<chat-title field=\"3D\">아바타 옷 맞추기</chat-title>본문" }, out title, "chatgpt:gpt-6.1-sol"), "본문", "one whole message (Codex)");
        Check.Eq(title, "[✹ Sol • 3D ] 아바타 옷 맞추기", "ChatGPT family from the slug");
        Run(new[] { "<chat-title field=\"Cooking\">파스타</chat-title>x" }, out title, "haiku");
        Check.Eq(title, "[∴ Haiku • Etc ] 파스타", "an unknown field counts as Etc");
    }

    public static void OtherAnswersPassThrough()
    {
        Check.Eq(Run(new[] { "<", "b>굵게</b> 답" }, out string? title), "<b>굵게</b> 답", "another tag is shown as it came");
        Check.True(title is null, "no title without the line");
        Check.Eq(Run(new[] { "그냥 답이에요." }, out _), "그냥 답이에요.", "plain text goes straight through");
        Check.Eq(Run(new[] { "<chat-ti" }, out _), "<chat-ti", "a cut-off start is given back at the end");
    }

    public static void Marks()
    {
        Check.Eq(ChatTitles.Mark("sonnet"), "◇ Sonnet", "sonnet");
        Check.Eq(ChatTitles.Mark("chatgpt:gpt-6-astra"), "✦ Astra", "astra");
        Check.Eq(ChatTitles.Mark("chatgpt:gpt-5.6-terra"), "◉ Terra", "terra");
        Check.Eq(ChatTitles.Mark("chatgpt:gpt-6-luna"), "☾ Luna", "luna");
    }
}
