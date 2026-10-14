using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Ai;

namespace Bloom.Tests;

/// What keeps API chats' other MCP servers in bounds: a batch file's arguments, credentials in what a server says, a
/// server that stops reading or sends too much, what it starts, a web address and its headers and session, a changed
/// definition, and 권한 switched to 사용 안 함.
internal static class McpSafetyTests
{
    private static readonly string[] Reserved = ApiToolbox.Reserved;
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    internal static List<string> Prepare(ApiMcp mcp, params McpServerDef[] defs) => mcp.PrepareAsync(defs, Reserved, CancellationToken.None).GetAwaiter().GetResult();

    internal static (string Text, bool Error) Call(ApiMcp mcp, string function) =>
        mcp.CallAsync(function, JsonSerializer.SerializeToElement(new { }), CancellationToken.None).GetAwaiter().GetResult();

    internal static McpServerDef Web(string url, Dictionary<string, string>? headers = null) =>
        new("web", "test", null, [], new Dictionary<string, string>(), url, null) { Headers = headers ?? new Dictionary<string, string>() };

    private static bool Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (T)
        {
            return true;
        }
    }

    internal static bool Gone(int pid)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited) return true;
            }
            catch (ArgumentException)
            {
                return true;
            }
            Thread.Sleep(100);
        }
        return false;
    }

    private static string Temp()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bloom-mcp-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ── the command line ──

    public static void ABatchFilesArgumentsAreQuotedForCmdOrRefused()
    {
        Check.Eq(McpLaunch.BatchQuote("plain-1.2/x"), "plain-1.2/x", "letters, digits and the path characters stay bare");
        Check.Eq(McpLaunch.BatchQuote("a&b"), "\"a&b\"", "& is quoted, where cmd reads it as text");
        Check.Eq(McpLaunch.BatchQuote(""), "\"\"", "an empty argument");
        foreach (string bad in new[] { "x\" & echo pwned & \"", "%PATH%", "a\nb", "dir\\", "a\0b" })
            Check.True(Throws<McpException>(() => McpLaunch.BatchQuote(bad)), "refused rather than passed to cmd: " + bad.Replace("\n", "\\n", StringComparison.Ordinal));

        string dir = Temp();
        try
        {
            string batch = Path.Combine(dir, "args to file.cmd"), output = Path.Combine(dir, "out.txt");
            File.WriteAllText(batch, "@echo off\r\n>\"%~1\" echo [%2]\r\n");
            const string nasty = "x&echo pwned>canary.txt ^ | < ( ) ,;=";
            var (file, arguments) = McpLaunch.Resolve(batch, [output, nasty]);
            Check.True(file.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) && arguments.StartsWith("/d /v:off /s /c \"", StringComparison.Ordinal), "a batch file runs through cmd.exe, no AutoRun");
            var psi = new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir };
            using (var p = Process.Start(psi)!) Check.True(p.WaitForExit(20_000), "the batch file ended");
            Check.Eq(File.ReadAllText(output).Trim(), "[\"" + nasty + "\"]", "the argument arrived whole, in its quotes");
            Check.True(!File.Exists(Path.Combine(dir, "canary.txt")), "nothing extra ran");
            Check.True(Throws<McpException>(() => McpLaunch.Resolve(batch, [output, "a\" & echo pwned > canary.txt & \""])), "an embedded quote never reaches cmd");
            Check.True(Throws<McpException>(() => McpLaunch.Resolve(batch, [output, "%PATH%"])), "nor does a %VARIABLE%");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    public static void AProgramNeedsNoShellAndItsArgumentsAreQuotedForTheRuntime()
    {
        var (file, arguments) = McpLaunch.Resolve("cmd.exe", ["a&b", "two words", "q\"uote"]);
        Check.True(file.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) && arguments == "a&b \"two words\" \"q\\\"uote\"", "no wrapper; only what the C runtime needs quoted: " + arguments);
        Check.Eq(McpLaunch.Quote("C:\\dir\\ with space\\"), "\"C:\\dir\\ with space\\\\\"", "backslashes before the closing quote doubled");
        Check.Eq(McpLaunch.Quote("a\\\"b"), "\"a\\\\\\\"b\"", "a backslash before a quote, and the quote, escaped");
        Check.Eq(McpLaunch.Quote(""), "\"\"", "an empty argument");
    }

    // ── credentials ──

    public static void CredentialsAreRedactedWholeAndInPart()
    {
        string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:s3cretPass"));
        var def = new McpServerDef("web", "test", null, [], new Dictionary<string, string> { ["API_KEY"] = "key-123456789" }, "https://user:urlpass99@example.com/mcp?token=qs-token-1234", null)
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer tok-abcdef12", ["X-Basic"] = "Basic " + basic },
        };
        string text = $"a tok-abcdef12 b s3cretPass c alice d {basic} e key-123456789 f urlpass99 g qs-token-1234 h Bearer tok-abcdef12 i keep this";
        string clean = McpSecrets.Redact(text, McpSecrets.Of(def));
        foreach (string secret in new[] { "tok-abcdef12", "s3cretPass", "alice", basic, "key-123456789", "urlpass99", "qs-token-1234" })
            Check.True(!clean.Contains(secret, StringComparison.Ordinal), "taken out: " + secret);
        Check.True(clean.EndsWith("i keep this", StringComparison.Ordinal), "the rest stays");
    }

    public static void WhatAServerSaysOfItselfHasItsSecretsTakenOut()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var mcp = new ApiMcp();
        Prepare(mcp, FakeMcpServer.Def("leak", "leaky", new Dictionary<string, string> { ["FAKE_TOKEN"] = "Bearer leak-token-12345" }));
        var tool = mcp.Tools.Single();
        Check.True(!tool.Description.Contains("leak-token-12345", StringComparison.Ordinal) && tool.Description.Contains("***", StringComparison.Ordinal), "the description: " + tool.Description);
        Check.True(!tool.Parameters.Contains("leak-token-12345", StringComparison.Ordinal) && tool.Parameters.Contains("pass ***", StringComparison.Ordinal), "the schema: " + tool.Parameters);
    }

    // ── a changed definition, the approval key ──

    public static void AChangedDefinitionRestartsTheServerAndKeepsItsOwnSecrets()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var mcp = new ApiMcp();
        var first = FakeMcpServer.Def("fake", "ok", new Dictionary<string, string> { ["FAKE_TOKEN"] = "first-token-1111" });
        Prepare(mcp, first);
        int pid1 = int.Parse(Call(mcp, "fake__pid").Text, System.Globalization.CultureInfo.InvariantCulture);
        string key1 = mcp.Of("fake__echo")!.Value.Key;
        Prepare(mcp, first);
        Check.Eq(Call(mcp, "fake__pid").Text, pid1.ToString(System.Globalization.CultureInfo.InvariantCulture), "the same definition keeps its server");
        Check.Eq(mcp.Of("fake__echo")!.Value.Key, key1, "and its approval key");

        var second = FakeMcpServer.Def("fake", "ok", new Dictionary<string, string> { ["FAKE_TOKEN"] = "second-token-2222" }); // same source and name
        Prepare(mcp, second);
        int pid2 = int.Parse(Call(mcp, "fake__pid").Text, System.Globalization.CultureInfo.InvariantCulture);
        Check.True(pid2 != pid1 && Gone(pid1), "a changed definition ends the old server and starts a new one");
        Check.True(mcp.Of("fake__echo")!.Value.Key != key1, "another definition is another server for 항상 허용");
        Check.Eq(Call(mcp, "fake__env").Text, "token=***", "the new server's secret is taken out");
        Check.True(mcp.Secrets.Contains("second-token-2222") && !mcp.Secrets.Contains("first-token-1111"), "each connection's secrets go with it");
    }

    public static void ApprovalKeysAreTheServersIdentityNotTheFunctionName()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var mcp = new ApiMcp();
        var x = FakeMcpServer.Def("my.server", "ok");
        var y = FakeMcpServer.Def("my_server", "ok"); // the same function names after cleaning: one gets _2
        Prepare(mcp, x, y);
        string kx = mcp.Of("my_server__echo")!.Value.Key, ky = mcp.Of("my_server__echo_2")!.Value.Key;
        Prepare(mcp, y, x); // the order changes who gets the suffix
        Check.True(mcp.Of("my_server__echo")!.Value.Key == ky && mcp.Of("my_server__echo_2")!.Value.Key == kx, "each server keeps its key whatever its function is called");
        Check.True(kx != ky && kx.StartsWith("mcp:", StringComparison.Ordinal) && kx.EndsWith(":echo", StringComparison.Ordinal) && !kx.Contains("__", StringComparison.Ordinal), "the key: the server's identity, then the tool's own name: " + kx);
        Check.Eq(Core.ToolPermissions.ActionOf(kx), Core.ToolPermissions.Mcp, "it falls under 다른 MCP 도구");
        Check.True(x.Identity != y.Identity && x.Identity == FakeMcpServer.Def("my.server", "ok").Identity, "an identity: source, name and a hash of the configuration");
    }

    public static void AFailedServerIsTriedAgainWhenItsDefinitionChanges()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var mcp = new ApiMcp();
        McpServerDef Missing(string arg) => new("missing", "test", "bloom-no-such-mcp-server-cmd", [arg], new Dictionary<string, string>(), null, null);
        Check.Eq(Prepare(mcp, Missing("a")).Count, 1, "told once");
        Check.Eq(Prepare(mcp, Missing("a")).Count, 0, "not tried again");
        Check.Eq(Prepare(mcp, Missing("b")).Count, 1, "another definition is tried");
    }

    // ── 권한: 다른 MCP 도구 → 사용 안 함 ──

    public static void CloseCancelsAStartUnderWayWithoutAnError()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var mcp = new ApiMcp();
        var preparing = Task.Run(() => mcp.PrepareAsync([FakeMcpServer.Def("slow", "slow")], Reserved, CancellationToken.None));
        Thread.Sleep(1500); // the server is starting, its initialize not answered for a minute
        var clock = Stopwatch.StartNew();
        mcp.Close();
        Check.True(preparing.Wait(Wait), "the start ended with Close");
        Check.True(clock.Elapsed < ApiMcp.StartLimit && preparing.Result.Count == 0 && mcp.Tools.Count == 0, "at once, with no note and no tools");
    }

    public static void SwitchingMcpOffClosesEveryChatsServers()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var f = new ApiToolTests.Folder();
        var session = new ApiSession("api:fake:x", "", "conv", [], "", f.Path);
        try
        {
            var mcp = (ApiMcp)typeof(ApiSession).GetField("mcp", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;
            Prepare(mcp, FakeMcpServer.Def("fake", "ok"));
            int pid = int.Parse(Call(mcp, "fake__pid").Text, System.Globalization.CultureInfo.InvariantCulture);
            ApiSession.CloseMcpAll();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (mcp.Tools.Count > 0 && DateTime.UtcNow < deadline) Thread.Sleep(50);
            Check.True(mcp.Tools.Count == 0 && Gone(pid), "the tools are gone and the server ended");
        }
        finally
        {
            session.Dispose();
        }
    }

    // ── stdio ──

    public static void ARequestToAServerThatDoesNotReadCancelsAndDisposeDoesNotWait()
    {
        var connection = McpStdioConnection.Start(FakeMcpServer.Def("deaf", "deaf"));
        var blob = new JsonObject { ["blob"] = new string('x', 5 * 1024 * 1024) }; // more than a pipe holds: the write blocks
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var clock = Stopwatch.StartNew();
        bool cancelled = false;
        try
        {
            connection.RequestAsync("tools/call", blob, cancel.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        Check.True(cancelled && clock.Elapsed < TimeSpan.FromSeconds(5), "the request is cancelled while its write is stuck");
        clock.Restart();
        connection.Dispose();
        Check.True(clock.Elapsed < TimeSpan.FromSeconds(8), "Dispose does not wait for the writer");
        connection.Dispose(); // a second time does nothing
    }

    public static void AMessageTooLargeClosesTheServer()
    {
        var connection = McpStdioConnection.Start(FakeMcpServer.Def("huge", "huge"));
        try
        {
            using var cancel = new CancellationTokenSource(Wait);
            string? message = null;
            try
            {
                connection.RequestAsync("initialize", null, cancel.Token).GetAwaiter().GetResult();
            }
            catch (McpException e)
            {
                message = e.Message;
            }
            Check.True(message is not null && message.Contains("too large", StringComparison.Ordinal), "a clear error: " + message);
        }
        finally
        {
            connection.Dispose();
        }
    }

    public static void AServerThatWritesMuchToStderrIsNotStalled()
    {
        using var cancel = new CancellationTokenSource(Wait);
        using var client = McpClient.StartAsync(FakeMcpServer.Def("chatty", "chatty"), cancel.Token).GetAwaiter().GetResult();
        Check.True(client.ToolsAsync(cancel.Token).GetAwaiter().GetResult().Count == 4, "it initialized and listed its tools although 8 MB went to its stderr");
    }

    public static void WhatTheServerStartsFirstEndsWithIt()
    {
        string dir = Temp();
        try
        {
            string pidFile = Path.Combine(dir, "child.pid");
            using var cancel = new CancellationTokenSource(Wait);
            var client = McpClient.StartAsync(FakeMcpServer.Def("spawn", "spawn", new Dictionary<string, string> { ["FAKE_PIDFILE"] = pidFile }), cancel.Token).GetAwaiter().GetResult();
            int pid = int.Parse(File.ReadAllText(pidFile).Trim(), System.Globalization.CultureInfo.InvariantCulture);
            using (var child = Process.GetProcessById(pid)) Check.True(!child.HasExited, "the child the server started at its very start runs");
            client.Dispose();
            Check.True(Gone(pid), "it ended with the server: the job held it from the first instruction");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── a web address ──

    public static void AWebAddressMustBeHttpsOrOnThisPcAndItsHeadersPlain()
    {
        using var data = new SettingsTransferTests.DataFolder();
        bool Refused(McpServerDef def)
        {
            try
            {
                _ = new McpHttpConnection(def);
                return false;
            }
            catch (McpException e)
            {
                return e.Refused;
            }
        }
        Check.True(Refused(Web("http://example.com/mcp")), "http away from this PC is refused");
        foreach (string ok in new[] { "https://example.com/mcp", "http://127.0.0.1:9/mcp", "http://localhost/mcp", "http://[::1]/mcp" })
            Check.True(!Refused(Web(ok)), "allowed: " + ok);
        foreach (string name in new[] { "Host", "content-length", "Transfer-Encoding", "Connection", "Bad Name", "Mcp-Session-Id" })
            Check.True(Refused(Web("https://example.com/mcp", new Dictionary<string, string> { [name] = "x" })), "header refused: " + name);
        foreach (string value in new[] { "a\r\nX-Evil: 1", "a\nb", "caf\u00e9", "a\0b" })
            Check.True(Refused(Web("https://example.com/mcp", new Dictionary<string, string> { ["X-Token"] = value })), "value refused: " + value.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal));
        Check.True(!Refused(Web("https://example.com/mcp", new Dictionary<string, string> { ["Authorization"] = "Bearer abc.def-123", ["X-Api_Key"] = "k" })), "ordinary headers pass");

        using var mcp = new ApiMcp();
        var notes = Prepare(mcp, Web("http://example.com/mcp"));
        Check.True(notes.Count == 1 && notes[0].Contains("web", StringComparison.Ordinal) && mcp.Tools.Count == 0, "the server is skipped with a line in the chat");
    }

    internal static string Answer(JsonNode? id, JsonNode result) => new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result }.ToJsonString();

    internal static string Initialized(JsonNode? id, string session, string version = McpClient.Protocol) =>
        FakeApiServer.Status(200, "OK", "Mcp-Session-Id: " + session + "\r\n", Answer(id, new JsonObject { ["protocolVersion"] = version }));

    internal static JsonNode ToolList() => new JsonObject { ["tools"] = new JsonArray { new JsonObject { ["name"] = "who", ["inputSchema"] = new JsonObject { ["type"] = "object" } } } };

    internal static JsonNode Called() => new JsonObject { ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = "called" } } };

    public static void AnUnknownProtocolVersionIsRefused()
    {
        using var server = new FakeApiServer(r => Initialized(JsonNode.Parse(r.Body)!["id"], "s1", "1999-01-01"));
        using var cancel = new CancellationTokenSource(Wait);
        string? message = null;
        try
        {
            McpClient.StartAsync(Web(server.BaseUrl + "/mcp"), cancel.Token).GetAwaiter().GetResult().Dispose();
        }
        catch (McpException e)
        {
            message = e.Message;
        }
        Check.True(message is not null && message.Contains("protocol version", StringComparison.Ordinal), "an unknown version is refused: " + message);
    }

    public static void ALostSessionIsRenewedBeforeTheNextCall()
    {
        using var data = new SettingsTransferTests.DataFolder();
        string current = "";
        int made = 0;
        var log = new List<string>();
        using var server = new FakeApiServer(r =>
        {
            var body = JsonNode.Parse(r.Body.Length > 0 ? r.Body : "{}")!;
            string method = (string?)body["method"] ?? "", session = r.Headers.GetValueOrDefault("Mcp-Session-Id") ?? "";
            lock (log)
            {
                log.Add(method + "@" + session);
                if (r.Method == "POST" && method != "initialize" && session != current) return FakeApiServer.Status(404, "Not Found");
                return method switch
                {
                    "initialize" => Initialized(body["id"], current = "s" + ++made),
                    "tools/list" => FakeApiServer.Status(200, "OK", "", Answer(body["id"], ToolList())),
                    "tools/call" => FakeApiServer.Status(200, "OK", "", Answer(body["id"], Called())),
                    _ => FakeApiServer.Status(202, "Accepted"),
                };
            }
        });
        using var mcp = new ApiMcp();
        Prepare(mcp, Web(server.BaseUrl + "/mcp"));
        Check.Eq(Call(mcp, "web__who"), ("called", false), "a call in the session");
        lock (log) current = "dropped"; // the server forgets the session
        var lost = Call(mcp, "web__who");
        Check.True(lost.Error && lost.Text.Contains("ended the session", StringComparison.Ordinal), "the call that meets the 404 fails: " + lost.Text);
        Check.Eq(Call(mcp, "web__who"), ("called", false), "the next one initializes again, lists again, and goes through");
        lock (log)
        {
            Check.Eq(log.Count(l => l.StartsWith("initialize@", StringComparison.Ordinal)), 2, "initialized twice");
            Check.Eq(log.Count(l => l.StartsWith("tools/list@", StringComparison.Ordinal)), 2, "listed again");
            Check.Eq(log.Count(l => l == "tools/call@s1"), 2, "the failed call was not replayed in the old session");
            Check.Eq(log.Count(l => l == "tools/call@s2"), 1, "and the later call used the new one");
        }
    }

    public static void AServersOwnRequestsOnTheEventStreamAreAnswered()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var server = new FakeApiServer(r =>
        {
            var body = JsonNode.Parse(r.Body.Length > 0 ? r.Body : "{}")!;
            string method = (string?)body["method"] ?? "";
            if (body["method"] is null) return FakeApiServer.Status(202, "Accepted"); // our answers to its requests
            return method switch
            {
                "initialize" => Initialized(body["id"], "s1"),
                "tools/list" => FakeApiServer.Status(200, "OK", "", Answer(body["id"], ToolList())),
                "tools/call" => FakeApiServer.Sse(
                    "data: {\"jsonrpc\":\"2.0\",\"id\":\"srv-9\",\"method\":\"ping\"}",
                    "data: {\"jsonrpc\":\"2.0\",\"id\":\"srv-10\",\"method\":\"sampling/createMessage\"}",
                    "data: " + Answer(body["id"], Called())),
                _ => FakeApiServer.Status(202, "Accepted"),
            };
        });
        using var cancel = new CancellationTokenSource(Wait);
        using var client = McpClient.StartAsync(Web(server.BaseUrl + "/mcp"), cancel.Token).GetAwaiter().GetResult();
        Check.Eq(client.CallAsync("who", JsonSerializer.SerializeToElement(new { }), [], cancel.Token).GetAwaiter().GetResult(), ("called", false), "the call's answer after the server's requests");
        var bodies = server.Requests.Select(q => q.Body).ToList();
        Check.True(bodies.Any(b => b.Contains("\"srv-9\"", StringComparison.Ordinal) && b.Contains("\"result\"", StringComparison.Ordinal)), "the ping got a result");
        Check.True(bodies.Any(b => b.Contains("\"srv-10\"", StringComparison.Ordinal) && b.Contains("-32601", StringComparison.Ordinal)), "an unknown request got an error");
    }

    public static void AWebServersBodiesAreBounded()
    {
        using var big = new FakeApiServer(_ => FakeApiServer.Status(200, "OK", "", new string('x', ApiLimits.MaxBody + 1)));
        using var cancel = new CancellationTokenSource(Wait);
        var connection = new McpHttpConnection(Web(big.BaseUrl + "/mcp"));
        Check.True(MessageOf(() => connection.RequestAsync("tools/list", null, cancel.Token).GetAwaiter().GetResult())?.Contains("more than Bloom takes", StringComparison.Ordinal) == true, "a body past the limit");
        using var line = new FakeApiServer(_ => FakeApiServer.Sse("data: " + new string('x', ApiLimits.MaxLine + 1)));
        var streamed = new McpHttpConnection(Web(line.BaseUrl + "/mcp"));
        Check.True(MessageOf(() => streamed.RequestAsync("tools/list", null, cancel.Token).GetAwaiter().GetResult())?.Contains("more than Bloom takes", StringComparison.Ordinal) == true, "an event past the limit");
    }

    private static string? MessageOf(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (McpException e)
        {
            return e.Message;
        }
    }

    public static void DisposingAWebConnectionEndsItsSessionOnce()
    {
        using var server = new FakeApiServer(r => r.Method == "DELETE" ? FakeApiServer.Status(200, "OK") : Initialized(JsonNode.Parse(r.Body)!["id"], "s1"));
        var connection = new McpHttpConnection(Web(server.BaseUrl + "/mcp"));
        using var cancel = new CancellationTokenSource(Wait);
        connection.RequestAsync("initialize", null, cancel.Token).GetAwaiter().GetResult();
        var clock = Stopwatch.StartNew();
        connection.Dispose();
        connection.Dispose();
        Check.True(clock.Elapsed < TimeSpan.FromSeconds(1), "Dispose does not wait for the server");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (server.Requests.All(q => q.Method != "DELETE") && DateTime.UtcNow < deadline) Thread.Sleep(50);
        Thread.Sleep(300);
        Check.Eq(server.Requests.Count(q => q.Method == "DELETE"), 1, "one DELETE for the session, not two");
    }

    public static void CodexHeadersReachTheWebServer()
    {
        using var data = new SettingsTransferTests.DataFolder();
        const string token = "BLOOM_TEST_MCP_TOKEN", header = "BLOOM_TEST_MCP_HEADER";
        Environment.SetEnvironmentVariable(token, "envtok-12345678");
        Environment.SetEnvironmentVariable(header, "hdrval-9999");
        try
        {
            var defs = Bloom.Ai.McpCatalog.ParseToml($$"""
                [mcp_servers.docs]
                url = "https://example.com/mcp"
                bearer_token_env_var = "{{token}}"
                http_headers = { "X-Plain" = "plain-1" }
                env_http_headers = { "X-Env" = "{{header}}" }

                [mcp_servers.more]
                url = "https://example.com/more"
                [mcp_servers.more.http_headers]
                X-Section = "section-1"

                [mcp_servers.local]
                command = "npx"
                args = ["-y", "pkg"]
                [mcp_servers.local.env]
                A = "b"
                """);
            var docs = defs.Single(d => d.Name == "docs");
            Check.True(docs.Url == "https://example.com/mcp" && docs.Command is null && docs.Args.Count == 0 && docs.Env.Count == 0, "the definition itself as before");
            Check.Eq(string.Join(",", docs.Headers.OrderBy(h => h.Key, StringComparer.Ordinal).Select(h => h.Key + "=" + h.Value)),
                "Authorization=Bearer envtok-12345678,X-Env=hdrval-9999,X-Plain=plain-1", "the three kinds of header");
            Check.Eq(defs.Single(d => d.Name == "more").Headers["X-Section"], "section-1", "a header table of its own");
            var local = defs.Single(d => d.Name == "local");
            Check.True(local.Headers.Count == 0 && local.Command == "npx" && string.Join(" ", local.Args) == "-y pkg" && local.Env["A"] == "b", "a program server is read as before");

            using var server = new FakeApiServer(r =>
            {
                var body = JsonNode.Parse(r.Body.Length > 0 ? r.Body : "{}")!;
                return (string?)body["method"] switch
                {
                    "initialize" => Initialized(body["id"], "s1"),
                    "tools/list" => FakeApiServer.Status(200, "OK", "", Answer(body["id"], ToolList())),
                    _ => FakeApiServer.Status(202, "Accepted"),
                };
            });
            var def = Bloom.Ai.McpCatalog.ParseToml($"[mcp_servers.docs]\nurl = \"{server.BaseUrl}/mcp\"\nbearer_token_env_var = \"{token}\"\nenv_http_headers = {{ \"X-Env\" = \"{header}\" }}\n").Single();
            using var cancel = new CancellationTokenSource(Wait);
            McpClient.StartAsync(def, cancel.Token).GetAwaiter().GetResult().Dispose();
            Check.True(server.Requests.Where(q => q.Method == "POST").All(q => q.Headers.GetValueOrDefault("Authorization") == "Bearer envtok-12345678" && q.Headers.GetValueOrDefault("X-Env") == "hdrval-9999"),
                "the API MCP client sends them on every message");
        }
        finally
        {
            Environment.SetEnvironmentVariable(token, null);
            Environment.SetEnvironmentVariable(header, null);
        }
    }
}
