using System;
using System.Windows.Threading;
using Bloom.App.Mcp;

namespace Bloom.Tests;

/// Bloom's MCP server: the JSON-RPC answers, and which calls of the one "pc" tool ask the user.
internal static class McpTests
{
    private static BloomTools Tools() => new(null!, Dispatcher.CurrentDispatcher); // nothing here runs an action

    public static void JsonRpc()
    {
        var server = new McpServer(Tools());
        string init = server.Answer("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""").Result!;
        Check.True(init.Contains("\"protocolVersion\":\"2025-06-18\"") && init.Contains("\"name\":\"bloom\""), "initialize answers with the client's version");
        Check.Eq(server.Answer("""{"jsonrpc":"2.0","method":"notifications/initialized"}""").Result, null, "a notification gets no reply");
        string list = server.Answer("""{"jsonrpc":"2.0","id":"a","method":"tools/list"}""").Result!;
        Check.True(list.Contains("\"name\":\"pc\"") && list.Contains("run_line") && list.Contains("\"id\":\"a\""), "one tool; a string id comes back as it was");
        Check.True(server.Answer("""{"jsonrpc":"2.0","id":2,"method":"nope"}""").Result!.Contains("-32601"), "an unknown method");
        string call = server.Answer("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"pc","arguments":{"do":"format_disk"}}}""").Result!;
        Check.True(call.Contains("\"isError\":true"), "an unknown action is an error and nothing runs");
        Check.Eq(server.Answer("not json").Result, null, "a broken line is ignored");
    }

    public static void Approvals()
    {
        var tools = Tools();
        Check.True(tools.Quiet("mcp__bloom__pc", """{"do":"find_files","q":"x"}"""), "a look-up runs without asking");
        Check.True(!tools.Quiet("mcp__bloom__pc", """{"do":"recycle","q":"C:\\a.txt"}"""), "recycle asks");
        Check.True(!tools.Quiet("mcp__bloom__pc", """{"do":"run_line","q":"10분 타이머"}"""), "a launcher line asks");
        Check.True(!tools.Quiet("Read", "{}"), "Claude's own tools ask");
        Check.Eq(tools.Says("mcp__bloom__pc", """{"do":"move","q":"C:\\a\\b.pdf","to":"D:\\문서"}"""), "옮기기: b.pdf → D:\\문서", "how a move reads");
        Check.Eq(tools.Says("mcp__bloom__pc", """{"do":"recycle","q":"C:\\a\\b.pdf"}"""), "휴지통으로: b.pdf (C:\\a)", "recycling names the file, then its folder");
        string deep = """{"do":"recycle","q":"C:\\Users\\me\\Downloads\\a very long folder name\\and another one\\report.pdf"}""";
        Check.True(tools.Says("mcp__bloom__pc", deep).StartsWith("휴지통으로: report.pdf (…", StringComparison.Ordinal), "a long path keeps the file's name whole");
        Check.Eq(tools.Says("Read", """{"file_path":"C:\\x.txt"}"""), "파일 읽기: C:\\x.txt", "Claude's Read reads by path");
        Check.Eq(tools.Says("mcp__bloom__pc", "{broken"), "mcp__bloom__pc", "a broken input: the tool's name");
        Check.Eq(tools.Key("mcp__bloom__pc", """{"do":"move","q":"C:\\a.pdf"}"""), "pc:move", "항상 허용 covers one action");
        Check.True(tools.Key("mcp__bloom__pc", """{"do":"recycle"}""") != tools.Key("mcp__bloom__pc", """{"do":"move"}"""), "moving allowed ≠ recycling allowed");
        Check.Eq(tools.Key("Read", "{}"), "Read", "another tool by name");
    }

    public static void Groups()
    {
        var tools = Tools();
        var off = Bloom.Core.Settings.Current.AiToolsOff;
        string all = System.Text.Json.JsonSerializer.Serialize(tools.Definition);
        Check.True(all.Contains("recycle") && all.Contains("clip_write") && all.Contains("Look up:"), "every group on: every action");
        off.AddRange(new[] { "files", "clip" });
        try
        {
            string some = System.Text.Json.JsonSerializer.Serialize(tools.Definition);
            Check.True(!some.Contains("recycle") && !some.Contains("clip_write") && some.Contains("run_line") && some.Contains("find_files"), "files and clipboard off: gone from the definition");
            Check.True(some.Length < all.Length - 100, "a shorter definition");
            Check.True(!tools.Hint.Contains("recycle") && tools.Hint.Contains("find_files") && tools.Hint.Contains("run_line"), "Codex's one-line hint names only the actions on");
            var call = tools.Call("mcp__bloom__pc", System.Text.Json.JsonDocument.Parse("""{"do":"recycle","q":"C:\\x"}""").RootElement).Result;
            Check.True(call.error && call.text.Contains("switched this off"), "a call to a group switched off is refused");
            off.AddRange(new[] { "look", "open", "bloom" });
            Check.True(!Bloom.App.Mcp.BloomTools.OfferedTo("claude") && !Bloom.App.Mcp.BloomTools.OfferedTo("chatgpt"), "every group off: no tool for either chat");
        }
        finally
        {
            off.Clear();
        }
    }

    /// A Codex run: the relay names the conversation first, and Bloom asks there before an action (not before a look-up).
    public static void CodexAsks()
    {
        var tools = Tools();
        var server = new McpServer(tools);
        var asked = new System.Collections.Generic.List<string>();
        tools.Approve = (conversation, tool, input) =>
        {
            asked.Add($"{conversation} {tool} {input}");
            return System.Threading.Tasks.Task.FromResult(false);
        };
        string recycle = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pc","arguments":{"do":"recycle","q":"C:\\nothing.txt"}}}""";
        string declined = server.Answer(recycle, "t1").Result!;
        Check.True(declined.Contains("declined") && declined.Contains("\"isError\":true"), "a no from the chat: refused, nothing runs");
        Check.True(asked.Count == 1 && asked[0].StartsWith("t1 pc {", StringComparison.Ordinal) && asked[0].Contains("recycle"), "asked in that conversation, with the call's input");
        server.Answer("""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"pc","arguments":{"do":"find_recent"}}}""", "t1").Wait();
        Check.Eq(asked.Count, 1, "a look-up isn't asked");
    }

    public static void FeatureSwitches()
    {
        var off = Bloom.Core.Settings.Current.FeaturesOff;
        Check.True(Bloom.Core.Features.Shows("cmd:color") && Bloom.Core.Features.Shows("cmd:settings"), "all on by default");
        Bloom.Core.Features.Set("color", false);
        Bloom.Core.Features.Set("system", false);
        try
        {
            Check.True(!Bloom.Core.Features.Shows("cmd:color") && !Bloom.Core.Features.Shows("sys:dark"), "the command and a pinned switch of an off feature hide");
            Check.True(Bloom.Core.Features.Shows("cmd:settings") && Bloom.Core.Features.Shows("app:x"), "the rest stays");
            Check.True(Bloom.Core.Features.Shows("timers") && Bloom.Core.Features.Shows("favorites"), "home blocks of features that are on stay");
        }
        finally
        {
            off.Clear();
        }
    }
}
