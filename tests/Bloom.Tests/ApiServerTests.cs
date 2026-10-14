using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Ai;
using Bloom.Core;

namespace Bloom.Tests;

/// Each wire end to end against a provider on this PC (FakeApiServer) speaking the providers' recorded stream formats:
/// the request that goes out, the answer that comes back, a stream cut off, a redirect, and the model list's refresh.
internal static class ApiServerTests
{
    private const string Key = "sk-fake-server-key";

    private static ApiRequest Request(FakeApiServer server, ApiWire wire, string id = "fake") =>
        new(new ApiProvider(id, "Fake", server.BaseUrl, wire, ""), Key, "model-x", wire, "Be brief.",
            new[] { new ApiMessage(ApiMessage.User, "hi", Array.Empty<string>()) }, null, null);

    private static ApiReply Stream(ApiRequest request, out string streamed)
    {
        var pieces = new System.Text.StringBuilder();
        var reply = ApiClient.StreamAsync(request, d => { lock (pieces) pieces.Append(d); }, CancellationToken.None).GetAwaiter().GetResult();
        streamed = pieces.ToString();
        return reply;
    }

    public static void ChatCompletionsEndToEnd()
    {
        using var data = new SettingsTransferTests.DataFolder(); // the log goes to a scratch folder
        using var server = new FakeApiServer(_ => FakeApiServer.Sse(
            ": OPENROUTER PROCESSING",
            "data: {\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"\"},\"finish_reason\":null}]}",
            "data: {\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hello\"},\"finish_reason\":null}]}",
            "data: {\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\" there\"},\"finish_reason\":\"stop\"}]}",
            "data: [DONE]"));
        var reply = Stream(Request(server, ApiWire.ChatCompletions), out string streamed);
        Check.True(reply.Ok && reply.Text == "Hello there" && streamed == "Hello there", "the answer, streamed");
        Check.True(server.Requests.TryDequeue(out var sent), "one request");
        Check.Eq(sent!.Path, "/v1/chat/completions", "its endpoint");
        Check.Eq(sent.Headers["Authorization"], "Bearer " + Key, "the key as a Bearer token");
        Check.Eq((string?)JsonNode.Parse(sent.Body)!["messages"]![1]!["content"], "hi", "the question in the body");
    }

    public static void MessagesEndToEnd()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var server = new FakeApiServer(_ => FakeApiServer.Sse(
            "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"id\":\"m1\",\"role\":\"assistant\",\"content\":[]}}",
            "event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}",
            "event: ping\ndata: {\"type\": \"ping\"}",
            "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"안녕\"}}",
            "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"하세요\"}}",
            "event: content_block_stop\ndata: {\"type\":\"content_block_stop\",\"index\":0}",
            "event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":5}}",
            "event: message_stop\ndata: {\"type\":\"message_stop\"}"));
        var reply = Stream(Request(server, ApiWire.Messages, ApiProviders.AnthropicId), out _);
        Check.True(reply.Ok && reply.Text == "안녕하세요", "the answer");
        server.Requests.TryDequeue(out var sent);
        Check.Eq(sent!.Path, "/v1/messages", "its endpoint");
        Check.Eq(sent.Headers["x-api-key"], Key, "the key in x-api-key");
        Check.True(!sent.Headers.ContainsKey("Authorization"), "Anthropic gets no Bearer copy");
        Check.True(sent.Headers.ContainsKey("anthropic-version"), "the API version");
    }

    public static void ResponsesEndToEnd()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var server = new FakeApiServer(_ => FakeApiServer.Sse(
            "event: response.created\ndata: {\"type\":\"response.created\",\"response\":{\"id\":\"r1\",\"status\":\"in_progress\"}}",
            "event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"item_id\":\"i1\",\"output_index\":0,\"content_index\":0,\"delta\":\"Hi\"}",
            "event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"item_id\":\"i1\",\"output_index\":0,\"content_index\":0,\"delta\":\"!\"}",
            "event: response.output_text.done\ndata: {\"type\":\"response.output_text.done\",\"text\":\"Hi!\"}",
            "event: response.completed\ndata: {\"type\":\"response.completed\",\"response\":{\"id\":\"r1\",\"status\":\"completed\"}}"));
        var reply = Stream(Request(server, ApiWire.Responses), out _);
        Check.True(reply.Ok && reply.Text == "Hi!", "the answer, the done event not counted again");
        server.Requests.TryDequeue(out var sent);
        Check.Eq(sent!.Path, "/v1/responses", "its endpoint");
        Check.Eq((bool?)JsonNode.Parse(sent.Body)!["store"], false, "nothing stored");
    }

    public static void CutOffStreamsKeepTheirText()
    {
        using var data = new SettingsTransferTests.DataFolder();
        // the connection closes mid-answer: no finish reason, no [DONE]
        using (var server = new FakeApiServer(_ => FakeApiServer.Sse("data: {\"choices\":[{\"delta\":{\"content\":\"Half an\"}}]}")))
        {
            var reply = Stream(Request(server, ApiWire.ChatCompletions), out _);
            Check.True(!reply.Ok && reply.Text == "Half an" && reply.Error == AiErrors.ApiCutOff("Fake"), "an early end: the text kept, said cut off");
        }
        using (var server = new FakeApiServer(_ => FakeApiServer.Sse(
            "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"Long\"}}",
            "event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"max_tokens\"}}",
            "event: message_stop\ndata: {\"type\":\"message_stop\"}")))
        {
            var reply = Stream(Request(server, ApiWire.Messages), out _);
            Check.True(!reply.Ok && reply.Text == "Long" && reply.Error == AiErrors.ApiCutOff("Fake"), "the length ceiling: cut off");
        }
        using (var server = new FakeApiServer(_ => FakeApiServer.Sse(
            "event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"Part\"}",
            "event: response.incomplete\ndata: {\"type\":\"response.incomplete\",\"response\":{\"status\":\"incomplete\",\"incomplete_details\":{\"reason\":\"max_output_tokens\"}}}")))
        {
            var reply = Stream(Request(server, ApiWire.Responses), out _);
            Check.True(!reply.Ok && reply.Text == "Part" && reply.Error == AiErrors.ApiCutOff("Fake"), "response.incomplete: cut off");
        }
    }

    public static void RedirectsAreNotFollowed()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var server = new FakeApiServer(r => r.Path.StartsWith("/elsewhere", StringComparison.Ordinal)
            ? FakeApiServer.Sse("data: [DONE]")
            : FakeApiServer.Status(307, "Temporary Redirect", "Location: /elsewhere/chat/completions\r\n"));
        var reply = Stream(Request(server, ApiWire.ChatCompletions), out _);
        Check.True(!reply.Ok && reply.Error == AiErrors.ApiStatus("Fake", 307, ""), "a redirect is a refusal");
        Check.Eq(server.Requests.Count, 1, "the key went nowhere else");
        var (models, error) = ApiClient.ListModelsAsync(new ApiProvider("fake", "Fake", server.BaseUrl, ApiWire.ChatCompletions, ""), Key, CancellationToken.None).GetAwaiter().GetResult();
        Check.True(models is null && error == AiErrors.ApiStatus("Fake", 307, ""), "… for the model list too");
        Check.Eq(server.Requests.Count, 2, "still only the provider's own address");
    }

    public static void ProviderMessagesShowInTheError()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var server = new FakeApiServer(_ => FakeApiServer.Status(404, "Not Found", "",
            "[{\"error\":{\"code\":404,\"message\":\"This model models/gemini-2.5-flash is no longer available to new users. Key " + Key + "\",\"status\":\"NOT_FOUND\"}}]"));
        var reply = Stream(Request(server, ApiWire.ChatCompletions), out _);
        Check.True(reply.Error!.Contains("no longer available to new users", StringComparison.Ordinal), "the provider's reason under the line");
        Check.True(!reply.Error.Contains(Key, StringComparison.Ordinal), "the key taken out of it");
    }

    public static void RefreshOvertakenByAKeyChangePublishesNothing()
    {
        using var data = new SettingsTransferTests.DataFolder();
        string id = "fake-" + Guid.NewGuid().ToString("N")[..6];
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string Models(string model) => FakeApiServer.Status(200, "OK", "", "{\"data\":[{\"id\":\"" + model + "\"}]}");
        using var server = new FakeApiServer(async r =>
        {
            if (r.Headers["Authorization"] == "Bearer old-key")
            {
                asked.TrySetResult();
                await held.Task.ConfigureAwait(false);
                return Models("old-model");
            }
            return Models("new-model");
        });
        var provider = new ApiProvider(id, "Fake", server.BaseUrl, ApiWire.ChatCompletions, "");
        try
        {
            ApiKeys.Set(id, "old-key");
            var first = ApiCatalog.RefreshAsync(provider);
            Check.True(asked.Task.Wait(TimeSpan.FromSeconds(10)), "the first look is under way");
            ApiKeys.Set(id, "new-key");
            var second = ApiCatalog.RefreshAsync(provider); // a new key: a fresh look, the old one overtaken
            var (current, error) = second.GetAwaiter().GetResult();
            Check.True(current && error is null, "the new key's look reports");
            held.SetResult();
            Check.Eq(first.GetAwaiter().GetResult().Current, false, "the old key's look is overtaken");
            Check.Eq(string.Join(",", ApiCatalog.Models(id).Select(m => m.Id)), "new-model", "only the new key's list is kept");

            held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ApiKeys.Set(id, "old-key");
            var late = ApiCatalog.RefreshAsync(provider);
            Check.True(asked.Task.Wait(TimeSpan.FromSeconds(10)), "a look under way");
            ApiKeys.Remove(id);
            ApiCatalog.Forget(id);
            held.SetResult();
            Check.Eq(late.GetAwaiter().GetResult().Current, false, "a removed key's look is overtaken");
            Check.Eq(ApiCatalog.Models(id).Count, 0, "and publishes nothing");
            ApiCatalog.Saving().GetAwaiter().GetResult();
            Check.True(!System.IO.File.ReadAllText(Paths.In("api-models.json")).Contains(id, StringComparison.Ordinal), "the file has no list for it either");
        }
        finally
        {
            held.TrySetResult();
            ApiKeys.Remove(id);
            ApiCatalog.Forget(id);
            ApiCatalog.Saving().GetAwaiter().GetResult();
        }
    }
}
