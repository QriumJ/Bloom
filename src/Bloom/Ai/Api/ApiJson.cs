using System.Text.Json;

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
}
