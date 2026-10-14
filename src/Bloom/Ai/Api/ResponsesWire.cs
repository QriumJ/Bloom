using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bloom.Ai;

/// OpenAI's Responses, for the OpenCode Go models served only there: a Bearer key, the instructions in `instructions`,
/// nothing stored on the provider (the whole conversation goes each turn, as on the other wires),
/// `response.output_text.delta` deltas until `response.completed` (or `response.incomplete`, cut short).
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
            bool user = m.Role == ApiMessage.User;
            var parts = new JsonArray { new JsonObject { ["type"] = user ? "input_text" : "output_text", ["text"] = m.Text } };
            foreach (var image in ApiImage.Load(m.Images)) parts.Add(new JsonObject { ["type"] = "input_image", ["image_url"] = image.DataUrl });
            input.Add(new JsonObject { ["role"] = m.Role, ["content"] = parts });
        }
        var body = new JsonObject { ["model"] = request.Model, ["stream"] = true, ["store"] = false, ["instructions"] = request.System, ["input"] = input };
        if (request.Effort is { } effort) body["reasoning"] = new JsonObject { ["effort"] = effort };
        return body.ToJsonString();
    }

    public ApiChunk Read(SseEvent e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.Data);
            var root = doc.RootElement;
            switch (ApiJson.Str(root, "type") is { Length: > 0 } type ? type : e.Name)
            {
                case "response.output_text.delta":
                    return new ApiChunk(ApiJson.Str(root, "delta"));
                case "response.completed":
                    return ApiChunk.Finished;
                case "response.incomplete": // the length ceiling or a filter ended it early: what came is kept, as cut off
                    return new ApiChunk(Done: true, End: ApiEnd.Cut);
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
}
