using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Ai;
using static Bloom.Tests.McpSafetyTests;

namespace Bloom.Tests;

/// What MCP servers can do to Bloom with what they send or when they stop: ids of a kind that is not allowed and other
/// junk, secrets hidden in escaped JSON, a server that stops reading, a turn whose MCP lifetime ends (권한), a connection
/// closed under a request, a protocol version not named, and the addresses that count as "here".
internal static class McpHardeningTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
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

    // ── 1: ids and junk ──

    public static void AnIdIsCheckedForItsKindBeforeItIsRead()
    {
        foreach (string bad in new[] { """{"id":null,"error":{}}""", """{"id":"7","result":{}}""", """{"id":true,"result":{}}""", """{"id":{"a":1},"result":{}}""", """{"id":1.5,"result":{}}""", "42", "[1]", "null", "\"x\"" })
            Check.True(!McpRpc.IsAnswer(Parse(bad), out _), "not an answer of ours, and no exception: " + bad);
        Check.True(McpRpc.IsAnswer(Parse("""{"id":7,"result":{}}"""), out long id) && id == 7, "a numeric id");
        foreach (string request in new[] { """{"id":"a","method":"ping"}""", """{"id":3,"method":"ping"}""" })
            Check.True(McpRpc.IsRequest(Parse(request)), "a request of the server's: " + request);
        foreach (string not in new[] { """{"id":null,"method":"ping"}""", """{"id":{"a":1},"method":"ping"}""", """{"id":true,"method":"x"}""", """{"method":"notifications/x"}""", "[]", "7" })
            Check.True(!McpRpc.IsRequest(Parse(not)), "not one: " + not);
        string? message = MessageOf(() => McpRpc.Outcome(Parse("""{"id":1,"error":{"code":-1,"data":"\u0074oken-abcdef12"}}""")));
        Check.True(message is not null && !message.Contains("\\u0074", StringComparison.Ordinal) && !message.Contains("token-abcdef12", StringComparison.Ordinal), "an error without a message never shows its raw JSON: " + message);
    }

    public static void ANoisyStdioServerIsAnsweredAroundAndNeverBringsBloomDown()
    {
        using var cancel = new CancellationTokenSource(Wait);
        using var client = McpClient.StartAsync(FakeMcpServer.Def("noisy", "noisy"), cancel.Token).GetAwaiter().GetResult();
        Check.Eq(client.ToolsAsync(cancel.Token).GetAwaiter().GetResult().Count, 4, "its tools, with junk and null-id errors between its lines");
        var arguments = JsonSerializer.SerializeToElement(new { text = "hi" });
        Check.Eq(client.CallAsync("echo", arguments, [], cancel.Token).GetAwaiter().GetResult(), ("call #1: hi", false), "and its calls");
    }

    public static void AWebServersNullIdAndForeignIdMessagesFailOrPassWithoutAnException()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var plain = new FakeApiServer(r => FakeApiServer.Status(200, "OK", "", """{"jsonrpc":"2.0","id":null,"error":{"code":-32700,"message":"Parse error"}}"""));
        var connection = new McpHttpConnection(Web(plain.BaseUrl + "/mcp"));
        using var cancel = new CancellationTokenSource(Wait);
        Check.True(MessageOf(() => connection.RequestAsync("tools/list", null, cancel.Token).GetAwaiter().GetResult()) is not null, "a null-id error reply is no answer: the call fails with an error");
        using var streamed = new FakeApiServer(r =>
        {
            var body = JsonNode.Parse(r.Body.Length > 0 ? r.Body : "{}")!;
            return (string?)body["method"] switch
            {
                "initialize" => Initialized(body["id"], "s1"),
                "tools/list" => FakeApiServer.Sse(
                    """data: {"jsonrpc":"2.0","id":null,"error":{"code":-1,"message":"x"}}""",
                    """data: {"id":{"a":1},"method":"ping"}""",
                    "data: " + Answer(body["id"], ToolList())),
                _ => FakeApiServer.Status(202, "Accepted"),
            };
        });
        using var client = McpClient.StartAsync(Web(streamed.BaseUrl + "/mcp"), cancel.Token).GetAwaiter().GetResult();
        Check.Eq(client.ToolsAsync(cancel.Token).GetAwaiter().GetResult().Count, 1, "the real answer is found among the junk on an event stream");
    }

    // ── 3: secrets in escaped JSON ──

    public static void ASecretInAnEscapedFormDoesNotSurviveInADecodedValue()
    {
        string[] secrets = ["token-abcdef12"];
        // \u0074 is "t": the raw JSON never holds the secret as text
        string structured = McpClient.Text(Parse("""{"structuredContent":{"\u0074oken-abcdef12":"v \u0074oken-abcdef12 w","n":[1,"\u0074oken-abcdef12"]}}"""), secrets);
        Check.True(!structured.Contains("token-abcdef12", StringComparison.Ordinal) && !structured.Contains("\\u0074", StringComparison.Ordinal) && structured.Contains("***", StringComparison.Ordinal), "structured content: " + structured);
        string unknown = McpClient.Text(Parse("""{"content":[{"type":"custom","data":"x \u0074oken-abcdef12 y","k\u0065y":"\u0074oken-abcdef12"},{"type":"text","text":"a \u0074oken-abcdef12"}]}"""), secrets);
        Check.True(!unknown.Contains("token-abcdef12", StringComparison.Ordinal) && unknown.Contains("x *** y", StringComparison.Ordinal) && unknown.Contains("a ***", StringComparison.Ordinal), "an unknown part and a text part: " + unknown);
        Check.True(McpClient.Text(Parse("""{"structuredContent":{"caf\u00e9":"na\u00efve"}}"""), secrets).Contains("café", StringComparison.Ordinal), "other text is written as it is");
    }

    // ── 5: the outgoing queue ──

    public static void AServerThatStopsReadingIsClosedOnceItsQueueIsFull()
    {
        var connection = McpStdioConnection.Start(FakeMcpServer.Def("deaf", "deaf"));
        try
        {
            using var cancel = new CancellationTokenSource(Wait);
            var clock = Stopwatch.StartNew();
            // eight messages of 5 MB: more than the queue's bytes (ApiLimits.MaxQueuedBytes), none ever read
            var calls = Enumerable.Range(0, 8).Select(_ => connection.RequestAsync("tools/call", new JsonObject { ["blob"] = new string('x', 5 * 1024 * 1024) }, cancel.Token)).ToList();
            Check.True(Task.WhenAll(calls.Select(c => c.ContinueWith(_ => { }, TaskScheduler.Default))).Wait(Wait), "every request ended, none left waiting on the full queue");
            Check.True(calls.All(c => c.IsFaulted && c.Exception!.InnerException is McpException), "each failed with an error");
            Check.True(calls.Any(c => c.Exception!.InnerException!.Message.Contains("stopped reading", StringComparison.Ordinal)), "and one says why");
            Check.True(clock.Elapsed < TimeSpan.FromSeconds(25), "without waiting out the cancellation token: " + clock.Elapsed);
        }
        finally
        {
            connection.Dispose();
        }
    }

    // ── 6: a turn's MCP lifetime ──

    public static void ATurnWhoseMcpLifetimeHasEndedStartsNothing()
    {
        int started = 0;
        using var mcp = new ApiMcp((_, _) =>
        {
            Interlocked.Increment(ref started);
            throw new McpException("not here");
        });
        using var life = new CancellationTokenSource();
        life.Cancel();
        var notes = mcp.PrepareAsync([FakeMcpServer.Def("a", "ok")], ApiToolbox.Reserved, CancellationToken.None, life.Token).GetAwaiter().GetResult();
        Check.True(started == 0 && notes.Count == 0 && mcp.Tools.Count == 0, "no server started, no note, no tools");
    }

    public static void TheLifetimeEndingDuringAStartCancelsItAndTheToolsAreRefused()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var mcp = new ApiMcp();
        using var life = new CancellationTokenSource();
        var preparing = Task.Run(() => mcp.PrepareAsync([FakeMcpServer.Def("slow", "slow")], ApiToolbox.Reserved, CancellationToken.None, life.Token));
        Thread.Sleep(1500);
        var clock = Stopwatch.StartNew();
        life.Cancel();
        Check.True(preparing.Wait(Wait) && clock.Elapsed < ApiMcp.StartLimit && preparing.Result.Count == 0 && mcp.Tools.Count == 0, "the start ended at once with no tools");

        using var running = new ApiMcp();
        using var turn = new CancellationTokenSource();
        running.PrepareAsync([FakeMcpServer.Def("fake", "ok")], ApiToolbox.Reserved, CancellationToken.None, turn.Token).GetAwaiter().GetResult();
        Check.True(running.Tools.Count > 0 && running.Has("fake__echo"), "tools offered while the lifetime lasts");
        turn.Cancel();
        Check.True(running.Tools.Count == 0 && !running.Has("fake__echo") && running.Of("fake__echo") is null, "none once it has ended, before anything closes the server");
        Check.True(Call(running, "fake__echo").Error, "and a call is refused");
    }

    public static void CloseMcpAllEndsATurnsMcpLifetimeBeforeItReturns()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var f = new ApiToolTests.Folder();
        using var ended = new ManualResetEventSlim();
        var session = new ApiSession("api:fake:x", "", "conv", [], "", f.Path);
        session.TurnEnded += (_, _) => ended.Set();
        try
        {
            session.Start(null, out _);
            Check.True(session.Send("hello", []), "a turn began");
            var life = session.McpLifetime;
            Check.True(!life.IsCancellationRequested, "its MCP lifetime lasts");
            ApiSession.CloseMcpAll();
            Check.True(life.IsCancellationRequested, "and ended before CloseMcpAll returned, on this thread");
            Check.True(ended.Wait(Wait), "the turn itself ended");
        }
        finally
        {
            session.Dispose();
        }
    }

    // ── 7: a connection closed under a request ──

    public static void DisposingAWebConnectionCancelsARequestInFlightAndRefusesLaterOnes()
    {
        using var server = new FakeApiServer(_ =>
        {
            Thread.Sleep(3000);
            return FakeApiServer.Status(200, "OK");
        });
        var connection = new McpHttpConnection(Web(server.BaseUrl + "/mcp"));
        var call = Task.Run(() => connection.RequestAsync("tools/list", null, CancellationToken.None));
        Thread.Sleep(500);
        var clock = Stopwatch.StartNew();
        connection.Dispose();
        Check.True(((Task)call).ContinueWith(_ => { }, TaskScheduler.Default).Wait(TimeSpan.FromSeconds(2)) && clock.Elapsed < TimeSpan.FromSeconds(2), "the request ended with Dispose, not when the server answered");
        Check.True(call.IsFaulted && call.Exception!.InnerException is McpException, "with a clear error");
        Check.True(MessageOf(() => connection.RequestAsync("tools/list", null, CancellationToken.None).GetAwaiter().GetResult())?.Contains("closed", StringComparison.Ordinal) == true, "a request after Dispose is refused");
        Check.True(MessageOf(() => connection.NotifyAsync("notifications/x", null, CancellationToken.None).GetAwaiter().GetResult()) is not null, "and so is a notification");
    }

    // ── 14: where plain http may go ──

    public static void PlainHttpIsForLoopbackAddressesAndLocalhostOnly()
    {
        foreach (string here in new[] { "127.0.0.1", "127.0.0.2", "[::1]", "::1", "localhost", "LOCALHOST" })
            Check.True(McpHttpConnection.IsHere(here), "here: " + here);
        foreach (string away in new[] { "loopback", "0.0.0.0", "10.0.0.1", "localhost.example.com", "127.0.0.1.example.com", "example.com", "[fe80::1]" })
            Check.True(!McpHttpConnection.IsHere(away), "not here: " + away);
        Check.True(MessageOf(() => new McpHttpConnection(Web("http://loopback/mcp")).Dispose()) is not null, "the name loopback needs https: "); foreach (string tricky in new[] { "http://user@loopback/mcp", "http://loopback:8080/mcp", "http://127.0.0.1@example.com/mcp" })
            Check.True(MessageOf(() => new McpHttpConnection(Web(tricky)).Dispose()) is not null, "refused: " + tricky);
        Check.True(MessageOf(() => new McpHttpConnection(Web("http://127.0.0.2:9/mcp")).Dispose()) is null, "127.0.0.0/8 is here");
        // the client for those connects only to a loopback address, however the name resolves
        using var message = new HttpRequestMessage(HttpMethod.Get, "http://10.255.255.1/");
        var clock = Stopwatch.StartNew();
        bool refused = false;
        try
        {
            ApiClient.Loopback.SendAsync(message).GetAwaiter().GetResult();
        }
        catch (HttpRequestException)
        {
            refused = true;
        }
        Check.True(refused && clock.Elapsed < TimeSpan.FromSeconds(5), "an address that is not loopback is never connected to: " + clock.Elapsed);
    }

    // ── 15: the protocol version ──

    public static void TheServerMustNameAKnownProtocolVersionAsAString()
    {
        foreach (string result in new[] { "{}", """{"protocolVersion":20250618}""", """{"protocolVersion":null}""", """{"protocolVersion":""}""", """{"protocolVersion":"2024-01-01"}""" })
        {
            using var server = new FakeApiServer(r => FakeApiServer.Status(200, "OK", "Mcp-Session-Id: s1\r\n", Answer(JsonNode.Parse(r.Body)!["id"], JsonNode.Parse(result)!)));
            using var cancel = new CancellationTokenSource(Wait);
            string? message = MessageOf(() => McpClient.StartAsync(Web(server.BaseUrl + "/mcp"), cancel.Token).GetAwaiter().GetResult().Dispose());
            Check.True(message is not null && message.Contains("protocol version", StringComparison.Ordinal), "refused: " + result);
        }
    }
}
