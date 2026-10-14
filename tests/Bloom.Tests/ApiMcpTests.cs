using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Ai;
using Bloom.Core;

namespace Bloom.Tests;

/// API chats' other MCP servers: the client against FakeMcpServer (initialize, paged tools/list, tools/call, a server
/// that fails), a web-address server against FakeApiServer, the approval each call waits for, the function names and
/// the cap on how many are offered.
internal static class ApiMcpTests
{
    private static readonly string[] Reserved = ApiToolbox.Reserved;

    private static List<string> Prepare(ApiMcp mcp, params McpServerDef[] defs) =>
        mcp.PrepareAsync(defs, Reserved, CancellationToken.None).GetAwaiter().GetResult();

    private static (string Text, bool Error) Call(ApiMcp mcp, string function, object arguments) =>
        mcp.CallAsync(function, JsonSerializer.SerializeToElement(arguments), CancellationToken.None).GetAwaiter().GetResult();

    public static void StdioServerListsAndCalls()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var mcp = new ApiMcp();
        var notes = Prepare(mcp, FakeMcpServer.Def("fake", "ok"));
        Check.Eq(notes.Count, 0, "no note for a server that started");
        var names = mcp.Tools.Select(t => t.Name).ToList();
        Check.Eq(string.Join(",", names), "fake__echo,fake__fail,fake__env,fake__pid", "both pages of tools/list, named server__tool");
        var echo = JsonNode.Parse(mcp.Tools[0].Parameters)!.AsObject();
        Check.True(!echo.ContainsKey("$schema") && (string?)echo["type"] == "object" && echo["properties"]!["text"] is not null, "the schema as providers take it");
        Check.True(mcp.Tools[0].Description.Contains("MCP server fake", StringComparison.Ordinal), "the description names its server");
        Check.Eq(Call(mcp, "fake__echo", new { text = "hi" }), ("call #1: hi", false), "a call's text (the server answered a ping of its own first)");
        Check.Eq(Call(mcp, "fake__fail", new { }), ("it failed", true), "isError is the call's error");
        Check.True(Call(mcp, "fake__nope", new { }).Error, "a function no server has");
    }

    public static void FailingServersAreLeftOutOnce()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var mcp = new ApiMcp();
        var crash = FakeMcpServer.Def("crashy", "crash");
        var missing = new McpServerDef("missing", "test", "bloom-no-such-mcp-server-cmd", [], new Dictionary<string, string>(), null, null);
        var ok = FakeMcpServer.Def("fake", "ok");
        var clock = Stopwatch.StartNew();
        var notes = Prepare(mcp, crash, missing, ok);
        Check.True(clock.Elapsed < ApiMcp.StartLimit, "a failing server does not hold the turn to the limit");
        Check.Eq(notes.Count, 2, "one note per server that failed");
        Check.True(notes[0].Contains("crashy", StringComparison.Ordinal) && notes[1].Contains("missing", StringComparison.Ordinal), "each note names its server");
        Check.True(mcp.Tools.Count == 4 && mcp.Tools.All(t => t.Name.StartsWith("fake__", StringComparison.Ordinal)), "the server that started still offers its tools");
        Check.Eq(Prepare(mcp, crash, missing, ok).Count, 0, "the next turn says nothing again");
    }

    public static void ClosingEndsTheServer()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var mcp = new ApiMcp();
        Prepare(mcp, FakeMcpServer.Def("fake", "ok"));
        int pid = int.Parse(Call(mcp, "fake__pid", new { }).Text, System.Globalization.CultureInfo.InvariantCulture);
        using var server = Process.GetProcessById(pid);
        mcp.Close(); // what a stop and the session's end do
        Check.True(server.WaitForExit(5000), "the server's process ended with Close");
        Check.Eq(mcp.Tools.Count, 0, "nothing offered after it");
        Prepare(mcp, FakeMcpServer.Def("fake", "ok"));
        Check.Eq(Call(mcp, "fake__echo", new { text = "again" }).Text, "call #1: again", "the next turn starts it again");
        Prepare(mcp); // switched off in the settings: closed
        Check.Eq(mcp.Tools.Count, 0, "a server no longer switched on is not offered");
    }

    public static void ToolsAreCappedFairly()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var mcp = new ApiMcp();
        var notes = Prepare(mcp, FakeMcpServer.Def("one", "many"), FakeMcpServer.Def("two", "many"));
        Check.Eq(mcp.Tools.Count, ApiMcp.MaxTools, "at most MaxTools offered");
        Check.Eq(mcp.Tools.Count(t => t.Name.StartsWith("one__", StringComparison.Ordinal)), ApiMcp.MaxTools / 2, "each server gets its share");
        Check.True(notes.Count == 1 && notes[0].Contains("100", StringComparison.Ordinal) && notes[0].Contains("36", StringComparison.Ordinal), "the chat is told how many were left out");
        Check.Eq(Prepare(mcp, FakeMcpServer.Def("one", "many"), FakeMcpServer.Def("two", "many")).Count, 0, "told once");
    }

    public static void NamesFitEveryProvider()
    {
        var valid = new Regex("^[A-Za-z_][A-Za-z0-9_-]{0,63}$");
        var taken = new HashSet<string>(Reserved, StringComparer.OrdinalIgnoreCase);
        Check.Eq(McpNames.Function("github", "create_issue", taken), "github__create_issue", "server__tool");
        Check.Eq(McpNames.Function("my server", "do.it", taken), "my_server__do_it", "other characters become _");
        Check.Eq(McpNames.Function("1password", "read", taken), "m1password__read", "never a digit first (Gemini)");
        Check.Eq(McpNames.Function("my.server", "do it", taken), "my_server__do_it_2", "a name taken gets a number");
        string longServer = new('s', 40), longTool = new('t', 70);
        string a = McpNames.Function(longServer, longTool, taken), b = McpNames.Function(longServer, longTool + "x", taken);
        Check.True(a.Length <= McpNames.MaxLength && b.Length <= McpNames.MaxLength && a != b, "long names cut, still apart");
        Check.True(a.StartsWith(new string('s', 20) + "__", StringComparison.Ordinal), "the server's part shortened first");
        foreach (string n in taken.Where(n => n.Contains("__", StringComparison.Ordinal))) Check.True(valid.IsMatch(n), "a valid function name: " + n);
        Check.Eq(McpNames.Function("mcp", "bloom__pc", taken), "mcp__bloom__pc_2", "Bloom's own names stay its own");
    }

    public static void McpCallsAskFirst()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var f = new ApiToolTests.Folder();
        using var mcp = new ApiMcp();
        var server = FakeMcpServer.Def("fake", "ok", new Dictionary<string, string> { ["FAKE_TOKEN"] = "tok-fake-1234567" });
        Prepare(mcp, server);
        var asked = new List<ToolAsk>();
        bool allow = false;
        var box = new ApiToolbox(f.Path, null, new Dictionary<string, string>(), ask =>
        {
            asked.Add(ask);
            return Task.FromResult(allow);
        }, null, mcp);
        Check.True(box.Offers("fake__echo") && box.OffersMcp, "the MCP tools offered with Bloom's");
        var declined = ApiToolTests.Run(box, "fake__echo", new { text = "no" });
        Check.True(declined.Error && declined.Text.Contains("declined", StringComparison.Ordinal), "declined: not run");
        Check.True(asked.Count == 1 && asked[0].Key == McpNames.ApprovalKey(server.Identity, "echo") && asked[0].Says.Contains("fake · echo", StringComparison.Ordinal) && asked[0].Detail.Contains("\"no\"", StringComparison.Ordinal),
            "the card names the server and tool and shows the arguments; 항상 허용 remembers the one tool of that server (its identity and the tool's own name)");
        Check.True(ToolPermissions.IsMcp(asked[0].Key) && !ToolPermissions.IsFileOrCommand(asked[0].Key), "its card says MCP 도구 승인");
        allow = true;
        Check.Eq(ApiToolTests.Run(box, "fake__echo", new { text = "yes" }).Text, "call #1: yes", "allowed: run (the declined call never reached the server)");
        Check.Eq(ApiToolTests.Run(box, "fake__env", new { }).Text, "token=***", "the server's env values never come back");
        var off = new ApiToolbox(f.Path, null, new Dictionary<string, string> { [ToolPermissions.Mcp] = ToolPermissions.Off }, _ => Task.FromResult(true), null, mcp);
        Check.True(!off.OffersMcp && !off.Offers("fake__echo"), "다른 MCP 도구 사용 안 함: not offered");
        Check.True(ApiToolTests.Run(off, "fake__echo", new { text = "x" }).Error, "and refused if called anyway");
        Check.Eq(ToolPermissions.Of(new Dictionary<string, string> { [ToolPermissions.Mcp] = ToolPermissions.Allow }, "mcp:fake__echo"), ToolPermissions.Allow, "항상 허용 covers every MCP tool");
    }

    public static void McpToolInATurn()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var f = new ApiToolTests.Folder();
        using var mcp = new ApiMcp();
        Prepare(mcp, FakeMcpServer.Def("fake", "ok"));
        int n = -1;
        string[] steps =
        {
            FakeApiServer.Sse(
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_m\",\"type\":\"function\",\"function\":{\"name\":\"fake__echo\",\"arguments\":\"{\\\"text\\\":\\\"from the model\\\"}\"}}]}}]}",
                "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"tool_calls\"}]}",
                "data: [DONE]"),
            FakeApiServer.Sse("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Echoed.\"},\"finish_reason\":\"stop\"}]}", "data: [DONE]"),
        };
        using var server = new FakeApiServer(_ => steps[Math.Min(Interlocked.Increment(ref n), steps.Length - 1)]);
        var box = new ApiToolbox(f.Path, null, new Dictionary<string, string>(), _ => Task.FromResult(true), null, mcp);
        var request = new ApiRequest(new ApiProvider("fake", "Fake", server.BaseUrl, ApiWire.ChatCompletions, ""), "sk-mcp", "model-x", ApiWire.ChatCompletions, "",
            new[] { new ApiMessage(ApiMessage.User, "echo it", Array.Empty<string>()) }, null, null);
        var (reply, done) = ApiTurn.RunAsync(request, box, _ => { }, _ => { }, CancellationToken.None).GetAwaiter().GetResult();
        Check.True(reply.Ok && reply.Text == "Echoed.", "the answer after the MCP tool");
        Check.Eq(done[0].Calls[0].Result, "call #1: from the model", "the call routed to its server");
        server.Requests.TryDequeue(out var first);
        server.Requests.TryDequeue(out var second);
        Check.True(JsonNode.Parse(first!.Body)!["tools"]!.AsArray().Any(t => (string?)t!["function"]!["name"] == "fake__echo"), "offered as a function");
        Check.Eq((string?)JsonNode.Parse(second!.Body)!["messages"]![3]!["content"], "call #1: from the model", "its result sent back");
    }

    public static void HttpServerKeepsItsSession()
    {
        using var data = new SettingsTransferTests.DataFolder();
        const string token = "Bearer tok-http-123456";
        using var server = new FakeApiServer(r =>
        {
            var body = JsonNode.Parse(r.Body.Length > 0 ? r.Body : "{}")!;
            string method = (string?)body["method"] ?? "";
            string Answer(JsonNode result) => new JsonObject { ["jsonrpc"] = "2.0", ["id"] = body["id"]!.DeepClone(), ["result"] = result }.ToJsonString();
            return method switch
            {
                "initialize" => FakeApiServer.Status(200, "OK", "Mcp-Session-Id: sess-1\r\n", Answer(new JsonObject { ["protocolVersion"] = "2025-03-26" })),
                "notifications/initialized" => FakeApiServer.Status(202, "Accepted"),
                "tools/list" => FakeApiServer.Sse("data: " + Answer(new JsonObject { ["tools"] = new JsonArray { new JsonObject { ["name"] = "who", ["inputSchema"] = new JsonObject { ["type"] = "object" } } } })),
                "tools/call" => FakeApiServer.Status(200, "OK", "", Answer(new JsonObject { ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = "sent " + r.Headers.GetValueOrDefault("Authorization") } } })),
                _ => FakeApiServer.Status(404, "Not Found"),
            };
        });
        var raw = JsonDocument.Parse(JsonSerializer.Serialize(new { url = server.BaseUrl + "/mcp", headers = new Dictionary<string, string> { ["Authorization"] = token } })).RootElement.Clone();
        var def = new McpServerDef("web", "test", null, [], new Dictionary<string, string>(), server.BaseUrl + "/mcp", raw);
        using var f = new ApiToolTests.Folder();
        using var mcp = new ApiMcp();
        Check.Eq(Prepare(mcp, def).Count, 0, "the web-address server started");
        Check.True(mcp.Tools.Count == 1 && mcp.Tools[0].Name == "web__who", "its tools from an event-stream answer");
        var box = new ApiToolbox(f.Path, null, new Dictionary<string, string>(), _ => Task.FromResult(true), null, mcp);
        Check.Eq(ApiToolTests.Run(box, "web__who", new { }).Text, "sent ***", "the header's token kept out of the result");
        var requests = server.Requests.ToArray();
        Check.True(requests.Skip(1).All(q => q.Headers.GetValueOrDefault("Mcp-Session-Id") == "sess-1" && q.Headers.GetValueOrDefault("MCP-Protocol-Version") == "2025-03-26"),
            "every later message carries the session and the protocol version");
        Check.True(requests.All(q => q.Headers.GetValueOrDefault("Authorization") == token), "the configured header on every message");
    }
}
