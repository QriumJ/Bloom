using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Bloom.Ai;
using static Bloom.Tests.ApiToolTests;

namespace Bloom.Tests;

/// The bounds on what the API chats take in (ApiLimits): lines, events, answers, tool calls and discovery lists, a
/// command's output. A connection past one is closed with a clear error.
internal static class ApiLimitsTests
{
    private static BoundedLineReader Reader(string text, int max, long total = 0) => new(new MemoryStream(Encoding.UTF8.GetBytes(text)), max, total);

    private static bool TooLarge(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ResponseTooLargeException)
        {
            return true;
        }
    }

    public static void LinesAreReadWithinTheirBound()
    {
        var r = Reader("\uFEFFa\r\nb\n\nlast", 10);
        Check.Eq(string.Join("|", new[] { r.ReadLine(), r.ReadLine(), r.ReadLine(), r.ReadLine() }), "a|b||last", "lines, a CR before the break dropped, the last one without a break, the mark at the start dropped");
        Check.Eq(r.ReadLine(), null, "then the end");
        string wide = string.Concat(Enumerable.Repeat("é", 20_000)); // more than a read: multi-byte characters across chunks
        Check.Eq(Reader(wide + "\n", 100_000).ReadLine(), wide, "characters are not split across the reads");
        Check.True(TooLarge(() => Reader(new string('x', 11) + "\n", 10).ReadLine()), "a line past the bound");
        Check.True(TooLarge(() => Reader(new string('x', 5000), 1000).ReadLine()), "even with no line break in sight");
        Check.True(TooLarge(() =>
        {
            var many = Reader("aa\nbb\ncc\n", 10, 5);
            while (many.ReadLine() is not null) { }
        }), "a stream past its total");
        Check.Eq(Reader("one\ntwo", 10).ReadLineAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult(), "one", "and the same asynchronously");
        Check.True(TooLarge(() => new SseParser(100).Line("data: " + new string('x', 101))), "an event's data is bounded too");
    }

    private static ApiReply StreamOf(string sse)
    {
        using var server = new FakeApiServer(_ => sse);
        return ApiClient.StreamAsync(Request(server), _ => { }, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static ApiRequest Request(FakeApiServer server) =>
        new(new ApiProvider("fake", "Fake", server.BaseUrl, ApiWire.ChatCompletions, ""), "sk-limits", "model-x", ApiWire.ChatCompletions, "",
            new[] { new ApiMessage(ApiMessage.User, "hi", Array.Empty<string>()) }, null, null);

    private static string CallEvent(int index, string arguments) =>
        "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":" + index + ",\"id\":\"c" + index + "\",\"type\":\"function\",\"function\":{\"name\":\"read_file\",\"arguments\":\"" + arguments + "\"}}]}}]}";

    public static void AProviderStreamPastABoundEndsWithAClearError()
    {
        using var data = new SettingsTransferTests.DataFolder();
        var line = StreamOf(FakeApiServer.Sse("data: " + new string('x', ApiLimits.MaxLine + 10)));
        Check.True(!line.Ok && line.Error == AiErrors.ApiTooLarge("Fake"), "a line past the bound: " + line.Error);
        var calls = StreamOf(FakeApiServer.Sse(Enumerable.Range(0, ApiLimits.MaxCallsPerResponse + 8).Select(i => CallEvent(i, "{}")).ToArray()));
        Check.True(!calls.Ok && calls.Error == AiErrors.ApiTooLarge("Fake") && calls.Calls.Count == 0, "more tool calls than a response may hold");
        string piece = new('y', 100_000);
        var text = StreamOf(FakeApiServer.Sse(Enumerable.Range(0, ApiLimits.MaxAnswerText / 100_000 + 2).Select(_ => "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"" + piece + "\"}}]}").ToArray()));
        Check.True(!text.Ok && text.Error == AiErrors.ApiTooLarge("Fake") && text.Text.Length <= ApiLimits.MaxAnswerText, "an answer's text past its bound, what came kept");
        string args = new('a', 100_000);
        var arguments = StreamOf(FakeApiServer.Sse(Enumerable.Range(0, 4).Select(_ => "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"c0\",\"function\":{\"name\":\"read_file\",\"arguments\":\"" + args + "\"}}]}}]}").ToArray()));
        Check.True(!arguments.Ok && arguments.Error == AiErrors.ApiTooLarge("Fake"), "one call's arguments past their bound");
    }

    public static void TooManyToolCallsInOneAnswerStopIt()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var f = new Folder();
        string step = FakeApiServer.Sse(Enumerable.Range(0, 25).Select(i => CallEvent(i, "{\\\"path\\\":\\\"notes.txt\\\"}")).Append("data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"tool_calls\"}]}").Append("data: [DONE]").ToArray());
        using var server = new FakeApiServer(_ => step);
        var (reply, steps) = ApiTurn.RunAsync(Request(server), Box(f, _ => true), _ => { }, _ => { }, CancellationToken.None).GetAwaiter().GetResult();
        Check.True(!reply.Ok && reply.Error == AiErrors.ApiTooManyCalls(ApiLimits.MaxCallsPerTurn), "stopped past the calls a turn may make: " + reply.Error);
        Check.True(steps.Count * 25 <= ApiLimits.MaxCallsPerTurn && steps.All(s => s.Calls.Count == 25), "and the steps it kept are whole");
    }

    public static void ACommandsOutputIsReadInPiecesAndBounded()
    {
        using var f = new Folder();
        var (text, error) = ApiCommand.RunAsync("[Console]::Out.Write('x' * 5000000)", f.Path, CancellationToken.None).GetAwaiter().GetResult();
        Check.True(!error && text.Length < ApiCommand.MaxOutput + 100 && text.Contains("(output cut)", StringComparison.Ordinal), "five million characters on one line: cut at the bound, the command finished");
    }

    public static void AServersToolListIsBounded()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using (var endless = McpClient.StartAsync(FakeMcpServer.Def("endless", "endless"), cancel.Token).GetAwaiter().GetResult())
            Check.Eq(endless.ToolsAsync(cancel.Token).GetAwaiter().GetResult().Count, ApiLimits.MaxServerTools, "a server that never stops giving pages is read to the bound");
        using var odd = McpClient.StartAsync(FakeMcpServer.Def("odd", "odd"), cancel.Token).GetAwaiter().GetResult();
        var tools = odd.ToolsAsync(cancel.Token).GetAwaiter().GetResult();
        Check.Eq(string.Join(",", tools.Select(t => t.Name)), "good,wordy", "a name or schema past the bound leaves the tool out");
        Check.Eq(tools[1].Description.Length, ApiLimits.MaxToolDescription, "a long description is cut");
    }
}
