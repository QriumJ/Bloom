using System;
using System.IO;
using System.Windows.Threading;
using Bloom.App.Mcp;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Tests;

/// What waits for the user among Bloom's own pc actions: a file search that can read into files is not a look-up, and
/// every action's card shows its arguments whole with the paths it works on resolved.
internal static class PcApprovalTests
{
    private static BloomTools Tools() => new(null!, Dispatcher.CurrentDispatcher); // nothing here runs an action

    public static void OnlyAPlainNameQueryRunsWithoutAsking()
    {
        foreach (string query in new[] { "", "report", "*.pdf", "a|b !tmp", "\"my files\" 2024" })
            Check.True(EverythingQuery.IsPlainName(query), "plain: " + query);
        foreach (string query in new[] { @"C:\Users\me\ report", "D:", "\"E:\\my files\" x", "c:\\a", "C: report", "c:", "ext:pdf dm:today", "size:>1mb", "audio:", "parent:x", "regex:^a", "file: x", "content:secret", "CONTENT:secret", "regex:content:pass", "ext:txt content:pass", "\"content:pass word\"", "<content:a|content:b>", "ansicontent:x", "utf8content:x", "utf16content:x", "file-signature:x", "sha256:abc", "frobnicate:1", "_mine:x", "C:content:x", @"C:\a:b", "a_c:\\x", "ab:", "f:report", "!content:x", "(content:x)" })
            Check.True(!EverythingQuery.IsPlainName(query), "a function or macro, so it may read contents: " + query);

        var tools = Tools();
        Check.True(tools.Quiet("mcp__bloom__pc", """{"do":"find_files","q":"annual report"}"""), "a plain name search is quiet");
        Check.True(!tools.Quiet("mcp__bloom__pc", """{"do":"find_files","q":"regex:content:password"}"""), "a content search is not");
        Check.True(tools.Quiet("pc", """{"do":"find_apps","q":"content:x"}"""), "another look-up has no file contents to read");
        Check.Eq(tools.Key("mcp__bloom__pc", """{"do":"find_files","q":"content:x"}"""), ToolPermissions.ReadOutside, "it falls under the read-outside permission");
        Check.Eq(tools.Key("pc", """{"do":"find_files","q":"annual report"}"""), "pc:find_files", "a plain name search under its own action");
        Check.Eq(tools.Key("pc", """{"do":"find_files","q":"audio:"}"""), ToolPermissions.ReadOutside, "a macro Everything can redefine is not trusted");
        Check.True(tools.Says("pc", """{"do":"find_files","q":"content:x"}""").Contains("내용", StringComparison.Ordinal), "and the card says it searches inside files");
    }

    public static void EveryPcActionCardShowsItsArgumentsWholeAndResolved()
    {
        var tools = Tools();
        string temp = Environment.GetEnvironmentVariable("TEMP")!;
        string source = Path.GetFullPath(Path.Combine(temp, "a b.pdf")), docs = Path.GetFullPath(Path.Combine(temp, "docs"));
        string move = tools.Detail("mcp__bloom__pc", """{"do":"move","q":"%TEMP%\\a b.pdf","to":"%TEMP%\\docs"}""");
        Check.True(move.Contains("do: move", StringComparison.Ordinal) && move.Contains("q: %TEMP%\\a b.pdf", StringComparison.Ordinal) && move.Contains("to: %TEMP%\\docs", StringComparison.Ordinal), "every argument as given: " + move);
        Check.True(move.Contains(source, StringComparison.Ordinal) && move.Contains(Path.Combine(docs, "a b.pdf"), StringComparison.Ordinal), "the source, and where it will land, resolved");
        string rename = tools.Detail("pc", """{"do":"rename","q":"%TEMP%\\a b.pdf","to":"c.pdf"}""");
        Check.True(rename.Contains(Path.Combine(Path.GetDirectoryName(source)!, "c.pdf"), StringComparison.Ordinal), "a rename's new path: " + rename);
        string note = tools.Detail("pc", "{\"do\":\"note\",\"q\":\"line one\\nline two\\u0007\",\"to\":\"title\"}");
        Check.True(note.Contains("q: line one\nline two␇", StringComparison.Ordinal) && note.Contains("to: title", StringComparison.Ordinal), "a note whole, hidden characters shown");
        Check.True(tools.Detail("pc", """{"do":"open","q":"https://example.com/x"}""").Contains("https://example.com/x", StringComparison.Ordinal), "an address to open");
        Check.True(tools.Detail("pc", """{"do":"run_line","q":"볼륨 30"}""").Contains("볼륨 30", StringComparison.Ordinal), "a launcher line");

        string write = tools.Detail("Write", """{"file_path":"C:\\a.txt","content":"x\ny"}""");
        Check.True(write.StartsWith("C:\\a.txt", StringComparison.Ordinal) && write.Contains("x\ny", StringComparison.Ordinal), "Claude's Write: the path and the content");
        string edit = tools.Detail("Edit", """{"file_path":"C:\\a.txt","old_string":"old","new_string":"new"}""");
        Check.True(edit.Contains("old", StringComparison.Ordinal) && edit.Contains("new", StringComparison.Ordinal), "Claude's Edit: the old and the new text");
        Check.True(tools.Detail("Mystery", """{"a":1}""").Contains("\"a\": 1", StringComparison.Ordinal), "an unknown tool's input");
        Check.Eq(tools.Detail("pc", "{broken"), "", "a broken input has no detail");

        string longPath = @"C:\" + string.Join(@"\", System.Linq.Enumerable.Repeat("a-long-folder-name", 8)) + @"\file.txt";
        Check.True(tools.Says("Write", "{\"file_path\":\"" + longPath.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"}").Length < 120 && tools.Detail("Write", "{\"file_path\":\"" + longPath.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"}").StartsWith(longPath, StringComparison.Ordinal),
            "the ✓ line is short, the card has the whole path");
    }
}
