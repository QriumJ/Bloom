using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using Bloom.Ai;

namespace Bloom.Tests;

/// A tiny stdio MCP server for the API chats' MCP tests (dev only): this test program started again as
/// `--fake-mcp <mode>`. "ok" lists echo (with a $schema), then over a second page fail, env (its FAKE_TOKEN) and pid;
/// "many" lists 50 tools; "crash" ends at once. Before its tools/list it asks the client a ping, as servers may. For the
/// bounds and the job: "deaf" never reads its input, "huge" answers with one 5 MB line, "chatty" first writes 8 MB to
/// stderr, "spawn" first starts a child that sleeps (its id to $FAKE_PIDFILE), "slow" takes a minute to initialize,
/// "leaky" puts $FAKE_TOKEN into its tool's description and schema, "endless" lists a hundred tools a page for ever,
/// "odd" lists a tool with a name, a schema and a description past the limits, "noisy" is "ok" with junk before every
/// answer: lines that are not JSON, JSON that is not a message, a null-id error reply, requests with ids of a kind that is
/// not allowed.
internal static class FakeMcpServer
{
    public const string Flag = "--fake-mcp";

    /// A server definition that starts this program in `mode`.
    public static McpServerDef Def(string name, string mode, Dictionary<string, string>? env = null)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("no process path");
        var args = new List<string>();
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) args.Add(typeof(FakeMcpServer).Assembly.Location);
        args.Add(Flag);
        args.Add(mode);
        return new McpServerDef(name, "test", exe, args, env ?? new Dictionary<string, string>(), null, null);
    }

    public static int Run(string mode)
    {
        if (mode == "crash") return 3;
        if (mode is "sleeper" or "deaf")
        {
            Thread.Sleep(Timeout.Infinite); // "deaf" never reads its input
            return 0;
        }
        if (mode == "chatty")
        {
            // 8 MB on stderr before anything else: a client that does not read it would stall the server here
            using var err = Console.OpenStandardError();
            var junk = new byte[8192];
            Array.Fill(junk, (byte)'e');
            for (int i = 0; i < 1024; i++) err.Write(junk);
        }
        if (mode == "spawn")
        {
            // a child of its own, started the moment it runs: it must be in the client's job too
            var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) psi.ArgumentList.Add(typeof(FakeMcpServer).Assembly.Location);
            psi.ArgumentList.Add(Flag);
            psi.ArgumentList.Add("sleeper");
            using var child = Process.Start(psi)!;
            File.WriteAllText(Environment.GetEnvironmentVariable("FAKE_PIDFILE")!, child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        var utf8 = new UTF8Encoding(false);
        using var stdin = new StreamReader(Console.OpenStandardInput(), utf8);
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true, NewLine = "\n" };
        int calls = 0;
        string? line;
        while ((line = stdin.ReadLine()) is not null)
        {
            JsonNode? message;
            try
            {
                message = JsonNode.Parse(line);
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }
            string? method = (string?)message?["method"];
            var id = message?["id"]?.DeepClone();
            if (method is null || id is null) continue; // the client's answers and notifications
            if (mode == "huge")
            {
                stdout.Write(new string('x', 5 * 1024 * 1024)); // one line past what the client takes
                stdout.WriteLine();
                continue;
            }
            JsonNode? result = null;
            JsonObject? error = null;
            switch (method)
            {
                case "initialize":
                    if (mode == "slow") Thread.Sleep(60_000);
                    result = new JsonObject { ["protocolVersion"] = McpClient.Protocol, ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() }, ["serverInfo"] = new JsonObject { ["name"] = "fake", ["version"] = "1" } };
                    break;
                case "tools/list":
                    stdout.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":\"srv-1\",\"method\":\"ping\"}");
                    result = Tools(mode, (string?)message?["params"]?["cursor"]);
                    break;
                case "tools/call":
                    string tool = (string?)message?["params"]?["name"] ?? "";
                    var arguments = message?["params"]?["arguments"];
                    switch (tool)
                    {
                        case "echo":
                            result = Text($"call #{++calls}: {(string?)arguments?["text"]}", false);
                            break;
                        case "fail":
                            result = Text("it failed", true);
                            break;
                        case "env":
                            result = Text("token=" + Environment.GetEnvironmentVariable("FAKE_TOKEN"), false);
                            break;
                        case "pid":
                            result = Text(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), false);
                            break;
                        default:
                            error = new JsonObject { ["code"] = -32602, ["message"] = "Unknown tool " + tool };
                            break;
                    }
                    break;
                default:
                    error = new JsonObject { ["code"] = -32601, ["message"] = "Method not found" };
                    break;
            }
            if (mode == "noisy")
                foreach (string junk in new[]
                {
                    "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32700,\"message\":\"Parse error\"}}", "{\"id\":\"x\",\"result\":{}}", "[1,2", "42", "null", "\"text\"", "[]",
                    "{\"id\":{\"a\":1},\"method\":\"ping\"}", "{\"id\":true,\"method\":\"ping\"}", "{\"id\":null,\"method\":\"ping\"}", "{\"id\":1.5,\"result\":{}}", "{\"method\":5,\"id\":7}",
                })
                    stdout.WriteLine(junk);
            var answer = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id };
            if (error is not null) answer["error"] = error;
            else answer["result"] = result;
            stdout.WriteLine(answer.ToJsonString());
        }
        return 0;
    }

    private static JsonObject Text(string text, bool isError) =>
        new() { ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } }, ["isError"] = isError };

    private static JsonObject Tool(string name, JsonObject? schema = null) =>
        new() { ["name"] = name, ["description"] = "The " + name + " tool.", ["inputSchema"] = schema ?? new JsonObject { ["type"] = "object" } };

    private static JsonObject Tools(string mode, string? cursor)
    {
        var tools = new JsonArray();
        if (mode == "leaky")
        {
            // a server that puts its own secret into what it says of its tool
            string token = Environment.GetEnvironmentVariable("FAKE_TOKEN") ?? "";
            var property = new JsonObject { ["type"] = "string", ["description"] = "pass " + token };
            var leaky = Tool("echo", new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["text"] = property } });
            leaky["description"] = "Echoes. The key is " + token + ".";
            tools.Add(leaky);
            return new JsonObject { ["tools"] = tools };
        }
        if (mode == "endless")
        {
            // a hundred tools a page and a cursor after every page
            int page = cursor is null ? 0 : int.Parse(cursor, System.Globalization.CultureInfo.InvariantCulture);
            for (int i = 0; i < 100; i++) tools.Add(Tool($"p{page}t{i}"));
            return new JsonObject { ["tools"] = tools, ["nextCursor"] = (page + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) };
        }
        if (mode == "odd")
        {
            // a good tool, one whose name is far too long, one with a 70 KB schema, one with a 20,000-character description
            tools.Add(Tool("good"));
            tools.Add(Tool(new string('n', 200)));
            tools.Add(Tool("bigschema", new JsonObject { ["type"] = "object", ["description"] = new string('s', 70_000) }));
            var wordy = Tool("wordy");
            wordy["description"] = new string('d', 20_000);
            tools.Add(wordy);
            return new JsonObject { ["tools"] = tools };
        }
        if (mode == "many")
        {
            for (int i = 0; i < 50; i++) tools.Add(Tool("t" + i));
            return new JsonObject { ["tools"] = tools };
        }
        if (cursor is null)
        {
            tools.Add(Tool("echo", new JsonObject
            {
                ["$schema"] = "http://json-schema.org/draft-07/schema#",
                ["type"] = "object",
                ["properties"] = new JsonObject { ["text"] = new JsonObject { ["type"] = "string" } },
            }));
            return new JsonObject { ["tools"] = tools, ["nextCursor"] = "p2" };
        }
        tools.Add(Tool("fail"));
        tools.Add(Tool("env"));
        tools.Add(Tool("pid"));
        return new JsonObject { ["tools"] = tools };
    }
}
