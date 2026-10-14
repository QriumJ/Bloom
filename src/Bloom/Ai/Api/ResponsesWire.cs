using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bloom.Ai;

/// OpenAI's Responses, for the OpenCode Go models served only there: a Bearer key, the instructions in `instructions`,
/// nothing stored on the provider (the whole conversation goes each turn, as on the other wires),
/// `response.output_text.delta` deltas until `response.completed` (or `response.incomplete`, cut short); tools as
/// functions, calls as function_call items, their results as function_call_output items; reasoning items come back
/// encrypted and go again with the calls they led to (ApiThought).
internal sealed class ResponsesWire : IApiWire
{
    public static readonly ResponsesWire Instance = new();

    public string Path => "responses";

    public void Authorize(HttpRequestMessage request, ApiProvider provider, string key) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

    public string Body(ApiRequest request)
    {
        var input = new JsonArray();
        foreach (var m in request.Messages)
        {
            if (m.Role == ApiMessage.Tool)
            {
                input.Add(new JsonObject { ["type"] = "function_call_output", ["call_id"] = m.CallId, ["output"] = m.IsError ? "Error: " + m.Text : m.Text });
                continue;
            }
            bool user = m.Role == ApiMessage.User;
            var items = new List<JsonObject>(); // the turn's text, then its calls
            if (m.Text.Length > 0 || m.Calls.Count == 0)
            {
                var parts = new JsonArray { new JsonObject { ["type"] = user ? "input_text" : "output_text", ["text"] = m.Text } };
                foreach (var image in ApiImage.Load(m.Images)) parts.Add(new JsonObject { ["type"] = "input_image", ["image_url"] = image.DataUrl });
                items.Add(new JsonObject { ["role"] = m.Role, ["content"] = parts });
            }
            foreach (var c in m.Calls) items.Add(new JsonObject { ["type"] = "function_call", ["call_id"] = c.Id, ["name"] = c.Name, ["arguments"] = ApiJson.SendableArguments(c.Arguments) });
            // the model's own reasoning items back where they stood, each just before the item it led to (one with
            // nothing after it is left out: the API refuses a reasoning item without its following one)
            IReadOnlyList<ApiThought> thoughts = m.From == request.Source ? m.Thoughts : [];
            for (int i = 0; i < items.Count; i++)
            {
                foreach (var t in thoughts)
                    if (t.At == i && JsonNode.Parse(t.Item) is JsonObject thought) input.Add(thought);
                input.Add(items[i]);
            }
        }
        // nothing is kept on the provider, so its reasoning comes back encrypted to be sent again (ApiThought)
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["stream"] = true,
            ["store"] = false,
            ["include"] = new JsonArray { "reasoning.encrypted_content" },
            ["instructions"] = request.System,
            ["input"] = input,
        };
        if (request.Tools.Count > 0)
        {
            var tools = new JsonArray();
            foreach (var t in request.Tools) tools.Add(new JsonObject { ["type"] = "function", ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = JsonNode.Parse(t.Parameters) });
            body["tools"] = tools;
        }
        if (request.Effort is { } effort) body["reasoning"] = new JsonObject { ["effort"] = effort };
        return body.ToJsonString();
    }

    public ApiChunk Read(SseEvent e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.Data);
            var root = doc.RootElement;
            string? kind = ApiJson.Str(root, "type") is { Length: > 0 } type ? type : e.Name;
            switch (kind)
            {
                case "response.output_text.delta":
                    return new ApiChunk(ApiJson.Str(root, "delta"));
                // a function_call item: its call id and name when it is added, its arguments a piece at a time, and whole
                // when it is done (which wins: a provider may send only that)
                case "response.output_item.added" or "response.output_item.done" when root.TryGetProperty("item", out var item) && ApiJson.Str(item, "type") == "function_call":
                    bool whole = kind == "response.output_item.done";
                    return new ApiChunk(Calls: [new ApiCallPiece(Index(root), ApiJson.Str(item, "call_id"), ApiJson.Str(item, "name"), whole ? ApiJson.Str(item, "arguments") : null, whole)]);
                // a reasoning item, whole when done: kept as it came, to go back with the conversation (ApiThought)
                case "response.output_item.done" when root.TryGetProperty("item", out var reasoning) && ApiJson.Str(reasoning, "type") == "reasoning":
                    return new ApiChunk(Thought: reasoning.GetRawText());
                case "response.function_call_arguments.delta":
                    return new ApiChunk(Calls: [new ApiCallPiece(Index(root), Arguments: ApiJson.Str(root, "delta"))]);
                case "response.completed":
                    return ApiChunk.Finished with { Tokens = Tokens(root) };
                case "response.incomplete": // the length ceiling or a filter ended it early: what came is kept, as cut off
                    return new ApiChunk(Done: true, End: ApiEnd.Cut, Tokens: Tokens(root));
                case "response.failed":
                    return new ApiChunk(Error: root.TryGetProperty("response", out var r) ? ApiJson.ErrorOf(r) ?? e.Data : e.Data);
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

    /// The whole response's counts, in the event that ends it.
    private static ApiTokens? Tokens(JsonElement root) =>
        root.TryGetProperty("response", out var r) && r.ValueKind == JsonValueKind.Object && r.TryGetProperty("usage", out var u) ? ApiTokens.Of(u, "input_tokens", "output_tokens") : null;

    private static int Index(JsonElement root) => root.TryGetProperty("output_index", out var i) && i.TryGetInt32(out int n) ? n : 0;
}
