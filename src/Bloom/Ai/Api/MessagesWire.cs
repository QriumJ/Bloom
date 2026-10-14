using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bloom.Ai;

/// Anthropic's Messages (also some OpenCode Go models): the key in x-api-key with the API version, the instructions in
/// `system`, pictures as base64 image blocks, `content_block_delta` text deltas until `message_stop`.
internal sealed class MessagesWire : IApiWire
{
    public static readonly MessagesWire Instance = new();

    /// The API version every request names (Anthropic's current one).
    private const string Version = "2023-06-01";

    /// Messages requires a ceiling on the answer's length; this one holds a long answer on every current model.
    public const int MaxTokens = 8192;

    public string Path => "messages";

    public void Authorize(HttpRequestMessage request, ApiProvider provider, string key)
    {
        request.Headers.Add("x-api-key", key);
        request.Headers.Add("anthropic-version", Version);
        // another provider serving this wire (OpenCode Go) takes its usual Bearer key too
        if (provider.Id != ApiProviders.AnthropicId) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }

    public string Body(ApiRequest request)
    {
        var messages = new JsonArray();
        foreach (var m in request.Messages)
        {
            var parts = new JsonArray();
            foreach (var image in ApiImage.Load(m.Images))
                parts.Add(new JsonObject { ["type"] = "image", ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = image.MediaType, ["data"] = image.Data } });
            parts.Add(new JsonObject { ["type"] = "text", ["text"] = m.Text });
            messages.Add(new JsonObject { ["role"] = m.Role, ["content"] = parts });
        }
        var body = new JsonObject { ["model"] = request.Model, ["max_tokens"] = MaxTokens, ["stream"] = true };
        if (request.System.Length > 0) body["system"] = request.System;
        body["messages"] = messages;
        return body.ToJsonString();
    }

    public ApiChunk Read(SseEvent e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.Data);
            var root = doc.RootElement;
            switch (e.Name ?? ApiJson.Str(root, "type"))
            {
                case "content_block_delta" when root.TryGetProperty("delta", out var delta) && ApiJson.Str(delta, "type") == "text_delta":
                    return new ApiChunk(ApiJson.Str(delta, "text"));
                // the stop reason comes before the end: "max_tokens" = MaxTokens cut the answer
                case "message_delta" when root.TryGetProperty("delta", out var stop) && ApiJson.Str(stop, "stop_reason") == "max_tokens":
                    return new ApiChunk(End: ApiEnd.Cut);
                case "message_stop":
                    return ApiChunk.Finished;
                case "error":
                    return new ApiChunk(Error: ApiJson.ErrorOf(root) ?? e.Data);
            }
        }
        catch (JsonException)
        {
            /* a line that isn't an event of ours */
        }
        return ApiChunk.None;
    }
}
