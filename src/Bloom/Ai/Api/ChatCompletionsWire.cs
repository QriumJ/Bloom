using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bloom.Ai;

/// OpenAI's Chat Completions, which OpenAI, OpenRouter, Gemini, DeepSeek, xAI and OpenCode Go all take: a Bearer key,
/// the instructions as the first (system) message, pictures as data-URL image parts, `choices[0].delta.content` deltas
/// until `[DONE]`; tools as functions, a call's arguments streamed in `delta.tool_calls` pieces, results as "tool"
/// messages.
internal sealed class ChatCompletionsWire : IApiWire
{
    public static readonly ChatCompletionsWire Instance = new();

    public string Path => "chat/completions";

    public void Authorize(HttpRequestMessage request, ApiProvider provider, string key) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

    public string Body(ApiRequest request)
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = request.System } };
        foreach (var m in request.Messages)
        {
            if (m.Role == ApiMessage.Tool)
            {
                // a failed call says so in its text: this wire has no error flag
                messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = m.CallId, ["content"] = m.IsError ? "Error: " + m.Text : m.Text });
                continue;
            }
            if (m.Role == ApiMessage.Assistant)
            {
                // a turn that only calls tools has no content at all (the field is optional with tool_calls; an empty
                // one is refused by some providers)
                var turn = new JsonObject { ["role"] = "assistant" };
                if (m.Text.Length > 0 || m.Calls.Count == 0) turn["content"] = m.Text;
                // DeepSeek's thinking mode: with tools, every earlier assistant turn carries its reasoning_content (the
                // model's own, else empty — one from another model or before Bloom kept it); without tools it is ignored
                if (request.Provider.ReplaysReasoning)
                {
                    string? own = m.From == request.Source ? m.Reasoning : null;
                    if (own is not null || request.Tools.Count > 0) turn["reasoning_content"] = own ?? "";
                }
                if (m.Calls.Count > 0)
                {
                    var calls = new JsonArray();
                    foreach (var c in m.Calls)
                    {
                        var call = new JsonObject { ["id"] = c.Id, ["type"] = "function", ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = ApiJson.SendableArguments(c.Arguments) } };
                        // Gemini's thought signature goes back on the call it came with, as it came (its Gemini 3 models
                        // refuse the next request without it)
                        if (c.Extra is { } extra) call["extra_content"] = JsonNode.Parse(extra);
                        calls.Add(call);
                    }
                    turn["tool_calls"] = calls;
                }
                messages.Add(turn);
                continue;
            }
            var images = ApiImage.Load(m.Images);
            if (images.Count == 0)
            {
                messages.Add(new JsonObject { ["role"] = m.Role, ["content"] = m.Text });
                continue;
            }
            var parts = new JsonArray();
            foreach (var image in images) parts.Add(new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = image.DataUrl } });
            parts.Add(new JsonObject { ["type"] = "text", ["text"] = m.Text });
            messages.Add(new JsonObject { ["role"] = m.Role, ["content"] = parts });
        }
        // the token counts come in a last chunk only when asked for (OpenAI, Gemini, DeepSeek, xAI take this)
        var body = new JsonObject { ["model"] = request.Model, ["stream"] = true, ["stream_options"] = new JsonObject { ["include_usage"] = true }, ["messages"] = messages };
        if (request.Tools.Count > 0)
        {
            var tools = new JsonArray();
            foreach (var t in request.Tools)
                tools.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = JsonNode.Parse(t.Parameters) } });
            body["tools"] = tools;
        }
        if (request.Effort is { } effort)
        {
            if (request.Provider.NestedEffort) body["reasoning"] = new JsonObject { ["effort"] = effort };
            else body["reasoning_effort"] = effort;
        }
        return body.ToJsonString();
    }

    public ApiChunk Read(SseEvent e)
    {
        if (e.Data == "[DONE]") return ApiChunk.Finished;
        try
        {
            using var doc = JsonDocument.Parse(e.Data);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out _) && ApiJson.ErrorOf(root) is { } error) return new ApiChunk(Error: error);
            // the counts: in the last chunk (its choices empty), or beside the finish reason (Gemini)
            var tokens = root.TryGetProperty("usage", out var usage) ? ApiTokens.Of(usage, "prompt_tokens", "completion_tokens") : null;
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                var delta = choice.TryGetProperty("delta", out var d) ? d : default;
                // the last chunk names why the answer ended ("length": the ceiling cut it); a provider that sends no
                // [DONE] after it (Gemini) has still ended whole
                var end = ApiJson.Str(choice, "finish_reason") switch { "" => ApiEnd.None, "length" => ApiEnd.Cut, _ => ApiEnd.Complete };
                string reasoning = ApiJson.Str(delta, "reasoning_content");
                return new ApiChunk(ApiJson.Str(delta, "content"), End: end, Calls: CallPieces(delta), Reasoning: reasoning.Length > 0 ? reasoning : null, Tokens: tokens);
            }
            if (tokens is not null) return new ApiChunk(Tokens: tokens);
        }
        catch (JsonException)
        {
            /* a line that isn't an event of ours */
        }
        return ApiChunk.None;
    }

    /// `delta.tool_calls`: per call its index, the id and name in its first piece, then the arguments a piece at a time
    /// (Gemini: each call whole in one piece, the index the same or left out; ApiCallBuilder tells them apart), and
    /// Gemini's `extra_content` (the thought signature) as it came.
    private static ApiCallPiece[]? CallPieces(JsonElement delta)
    {
        if (delta.ValueKind != JsonValueKind.Object || !delta.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array) return null;
        var pieces = new List<ApiCallPiece>();
        foreach (var c in calls.EnumerateArray())
        {
            int index = c.TryGetProperty("index", out var i) && i.TryGetInt32(out int n) ? n : pieces.Count;
            var f = c.TryGetProperty("function", out var fn) ? fn : default;
            string? extra = c.TryGetProperty("extra_content", out var x) && x.ValueKind == JsonValueKind.Object ? x.GetRawText() : null;
            pieces.Add(new ApiCallPiece(index, ApiJson.Str(c, "id") is { Length: > 0 } id ? id : null, ApiJson.Str(f, "name") is { Length: > 0 } name ? name : null,
                ApiJson.Str(f, "arguments"), Extra: extra));
        }
        return pieces.ToArray();
    }
}
