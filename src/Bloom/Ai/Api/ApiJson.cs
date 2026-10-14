using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bloom.Ai;

/// Reading the providers' JSON: a string field, and the message of an error however it is wrapped.
internal static class ApiJson
{
    public static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// `{"error":{"message":…}}`, `{"error":"…"}`, `{"message":…}`, or Gemini's `[{"error":…}]`; else the text itself,
    /// cut short.
    public static string ErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0) root = root[0];
            if (ErrorOf(root) is { Length: > 0 } m) return m;
        }
        catch (JsonException)
        {
            /* not JSON: the text itself */
        }
        string text = body.Trim();
        return text.Length > MaxShown ? text[..MaxShown] + "…" : text;
    }

    /// The message of an error object inside a stream event or a body; null when it holds none.
    public static string? ErrorOf(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.String) return error.GetString();
            if (Str(error, "message") is { Length: > 0 } m) return m;
            if (error.ValueKind == JsonValueKind.Object) return error.GetRawText();
        }
        return Str(root, "message") is { Length: > 0 } message ? message : null;
    }

    private const int MaxShown = 300;

    /// A tool call's arguments as a JSON object for a wire that sends them as one (Messages' input): what the model
    /// wrote, or {} when that was no object (a cut-off call; its result already says the arguments were bad).
    public static JsonObject Arguments(string json) => ArgumentsObject(json) ?? new JsonObject();

    /// The arguments as text for a wire that sends them so: what the model wrote when it is a JSON object, else "{}"
    /// (a provider that reads them back refuses the whole request over a broken one: Gemini's 400 "invalid argument").
    public static string SendableArguments(string json) => ArgumentsObject(json) is null ? "{}" : json;

    private static JsonObject? ArgumentsObject(string json)
    {
        try
        {
            return JsonNode.Parse(json.Length > 0 ? json : "{}") as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
