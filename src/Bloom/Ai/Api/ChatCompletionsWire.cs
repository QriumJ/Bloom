using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bloom.Ai;

/// OpenAI's Chat Completions, which OpenAI, OpenRouter, Gemini, DeepSeek, xAI and OpenCode Go all take: a Bearer key,
/// the instructions as the first (system) message, pictures as data-URL image parts, `choices[0].delta.content` deltas
/// until `[DONE]`.
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
        var body = new JsonObject { ["model"] = request.Model, ["stream"] = true, ["messages"] = messages };
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
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                string? text = choice.TryGetProperty("delta", out var delta) ? ApiJson.Str(delta, "content") : null;
                // the last chunk names why the answer ended ("length": the ceiling cut it); a provider that sends no
                // [DONE] after it (Gemini) has still ended whole
                var end = ApiJson.Str(choice, "finish_reason") switch { "" => ApiEnd.None, "length" => ApiEnd.Cut, _ => ApiEnd.Complete };
                return new ApiChunk(text, End: end);
            }
        }
        catch (JsonException)
        {
            /* a line that isn't an event of ours */
        }
        return ApiChunk.None;
    }
}
