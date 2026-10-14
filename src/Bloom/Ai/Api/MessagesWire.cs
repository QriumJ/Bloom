using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bloom.Ai;

/// Anthropic's Messages (also some OpenCode Go models): the key in x-api-key with the API version, the instructions in
/// `system`, pictures as base64 image blocks, `content_block_delta` text deltas until `message_stop`; tools as
/// tool_use blocks, their results as tool_result blocks in the next user message.
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
        JsonArray? results = null; // the tool results after one assistant turn go together in one user message
        foreach (var m in request.Messages)
        {
            if (m.Role == ApiMessage.Tool)
            {
                if (results is null)
                {
                    results = new JsonArray();
                    messages.Add(new JsonObject { ["role"] = ApiMessage.User, ["content"] = results });
                }
                results.Add(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = m.CallId, ["content"] = m.Text, ["is_error"] = m.IsError });
                continue;
            }
            // a question right after tool results (an answer that stopped mid-way, sent again from the saved chat) joins
            // their user message, after them: two user messages in a row are one turn
            var parts = m.Role == ApiMessage.User && results is not null ? results : new JsonArray();
            if (!ReferenceEquals(parts, results)) messages.Add(new JsonObject { ["role"] = m.Role, ["content"] = parts });
            results = null;
            foreach (var image in ApiImage.Load(m.Images))
                parts.Add(new JsonObject { ["type"] = "image", ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = image.MediaType, ["data"] = image.Data } });
            // an empty text block is refused: a turn that only calls tools has none
            if (m.Text.Length > 0 || m.Calls.Count == 0) parts.Add(new JsonObject { ["type"] = "text", ["text"] = m.Text });
            foreach (var c in m.Calls) parts.Add(new JsonObject { ["type"] = "tool_use", ["id"] = c.Id, ["name"] = c.Name, ["input"] = ApiJson.Arguments(c.Arguments) });
        }
        var body = new JsonObject { ["model"] = request.Model, ["max_tokens"] = MaxTokens, ["stream"] = true };
        if (request.System.Length > 0) body["system"] = request.System;
        if (request.Tools.Count > 0)
        {
            var tools = new JsonArray();
            foreach (var t in request.Tools) tools.Add(new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["input_schema"] = JsonNode.Parse(t.Parameters) });
            body["tools"] = tools;
        }
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
                // a tool_use block: its id and name at its start, its input as JSON text a piece at a time
                case "content_block_start" when root.TryGetProperty("content_block", out var block) && ApiJson.Str(block, "type") == "tool_use":
                    return new ApiChunk(Calls: [new ApiCallPiece(Index(root), ApiJson.Str(block, "id"), ApiJson.Str(block, "name"))]);
                case "content_block_delta" when root.TryGetProperty("delta", out var json) && ApiJson.Str(json, "type") == "input_json_delta":
                    return new ApiChunk(Calls: [new ApiCallPiece(Index(root), Arguments: ApiJson.Str(json, "partial_json"))]);
                // the input's count at the start (cached parts counted apart), the output's at the end
                case "message_start" when root.TryGetProperty("message", out var message) && message.TryGetProperty("usage", out var start):
                    return new ApiChunk(Tokens: Tokens(start));
                // the stop reason comes before the end: "max_tokens" = MaxTokens cut the answer
                case "message_delta":
                    bool cut = root.TryGetProperty("delta", out var stop) && ApiJson.Str(stop, "stop_reason") == "max_tokens";
                    return new ApiChunk(End: cut ? ApiEnd.Cut : ApiEnd.None, Tokens: root.TryGetProperty("usage", out var end) ? Tokens(end) : null);
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

    private static ApiTokens? Tokens(JsonElement usage) => ApiTokens.Of(usage, "input_tokens", "output_tokens", "cache_creation_input_tokens", "cache_read_input_tokens");

    private static int Index(JsonElement root) => root.TryGetProperty("index", out var i) && i.TryGetInt32(out int n) ? n : 0;
}
