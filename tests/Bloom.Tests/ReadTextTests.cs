using Bloom.Ui;

namespace Bloom.Tests;

/// Selecting and copying text in a conversation (Ui\Controls\ReadText.cs, Ui\Markdown.cs).
internal static class ReadTextTests
{
    public static void CopiesPlainText()
    {
        var answer = Markdown.Render("**굵은 글씨** 한글 문장이에요.\n\n- 첫째 항목\n- 둘째 항목\n\n| 이름 | 값 |\n|---|---|\n| 사과 | 3 |", _ => { });
        answer.SelectAll();
        string copied = answer.SelectedText;
        Check.True(!copied.Contains('\u2060'), "no Korean joiners in what is copied");
        foreach (string part in new[] { "굵은 글씨 한글 문장이에요.", "첫째 항목", "둘째 항목", "사과", "3" })
            Check.True(copied.Contains(part, System.StringComparison.Ordinal), "copied text has: " + part);
        Check.True(!copied.Contains("**", System.StringComparison.Ordinal), "no Markdown marks");

        // a code block turns selectable when the mouse comes over it
        var withCode = Markdown.Render("```text\nline one\nline two\n```", _ => { });
        var holder = (System.Windows.Controls.Border)((System.Windows.Controls.Grid)((SmoothBorder)((System.Windows.Documents.BlockUIContainer)withCode.Document.Blocks.FirstBlock).Child).Child).Children[0];
        Check.True(holder.Child is System.Windows.Controls.TextBlock, "code starts as a plain TextBlock");
        holder.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.UIElement.MouseEnterEvent });
        Check.True(holder.Child is ReadText, "and becomes selectable under the mouse");

        var question = ReadText.Plain("이 런처 단축키\n정리해줘", 14, Theme.Regular, fit: true);
        question.SelectAll();
        Check.Eq(question.SelectedText.Replace("\r\n", "\n", System.StringComparison.Ordinal), "이 런처 단축키\n정리해줘", "a question copies as typed");
    }
}
