using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bloom.Ai;

/// What an MCP server's configuration holds that must not reach a provider or a saved chat: its env values and header
/// values whole and in their parts (the token in `Bearer TOKEN`, a Basic credential's user and password), the address's
/// own credentials (user info, query values). Taken out of the server's tool descriptions, schemas and results.
internal static class McpSecrets
{
    /// A whole value shorter than this is too common to take out of text ("1", "true"); a credential's part (a token, a
    /// password) is taken out from MinPart.
    private const int MinValue = 8, MinPart = 4;

    private const string Redacted = "***";

    /// The authorization schemes whose value is `<scheme> <credential>`.
    private static readonly HashSet<string> Schemes = new(["Bearer", "Basic", "Token", "ApiKey", "Digest"], StringComparer.OrdinalIgnoreCase);

    /// Everything of `def` to take out, the longest first (one that holds another goes whole).
    public static List<string> Of(McpServerDef def)
    {
        var secrets = new HashSet<string>(StringComparer.Ordinal);
        foreach (string v in def.Env.Values.Concat(def.AllHeaders.Values)) Add(secrets, v);
        if (def.Url is not null && Uri.TryCreate(def.Url, UriKind.Absolute, out var url))
        {
            foreach (string part in Uri.UnescapeDataString(url.UserInfo).Split(':')) AddPart(secrets, part);
            foreach (string pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                if (pair.IndexOf('=') is > 0 and var eq) AddPart(secrets, Uri.UnescapeDataString(pair[(eq + 1)..]));
        }
        return secrets.OrderByDescending(s => s.Length).ToList();
    }

    /// A header or env value: itself, and each word of it (`Bearer TOKEN` → TOKEN); a Basic credential also as the
    /// user and password it encodes.
    private static void Add(HashSet<string> secrets, string value)
    {
        value = value.Trim();
        if (value.Length >= MinValue) secrets.Add(value);
        string[] words = value.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || !Schemes.Contains(words[0])) return; // any other value with spaces is not a credential's parts
        foreach (string word in words.Skip(1)) AddPart(secrets, word);
        if (words[0].Equals("Basic", StringComparison.OrdinalIgnoreCase) && Decode(words[1]) is { } plain)
        {
            AddPart(secrets, plain);
            foreach (string part in plain.Split(':', 2)) AddPart(secrets, part);
        }
    }

    private static void AddPart(HashSet<string> secrets, string part)
    {
        if (part.Length >= MinPart) secrets.Add(part);
    }

    private static string? Decode(string base64)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// `text` with each of `secrets` (longest first) taken out.
    public static string Redact(string text, IEnumerable<string> secrets)
    {
        foreach (string secret in secrets)
            if (secret.Length > 0) text = text.Replace(secret, Redacted, StringComparison.Ordinal);
        return text;
    }

    /// `node` with `secrets` taken out of every string value and property name in it — of the decoded values, as raw JSON
    /// can hold a secret in escaped form ("\u0074oken") that no search of the text would find.
    public static JsonNode? RedactJson(JsonNode? node, IReadOnlyList<string> secrets)
    {
        switch (node)
        {
            case JsonObject o:
                var copy = new JsonObject();
                foreach (var (k, v) in o) copy[Redact(k, secrets)] = RedactJson(v, secrets);
                return copy;
            case JsonArray a:
                return new JsonArray(a.Select(v => RedactJson(v, secrets)).ToArray());
            case JsonValue v when v.TryGetValue(out string? s):
                return JsonValue.Create(Redact(s, secrets));
            default:
                return node?.DeepClone();
        }
    }

    /// An element as JSON text with `secrets` taken out of its decoded values and names; non-ASCII text stays as it is.
    public static string RedactedJson(JsonElement element, IReadOnlyList<string> secrets) =>
        RedactJson(JsonNode.Parse(element.GetRawText()), secrets)?.ToJsonString(Plain) ?? "null";

    private static readonly JsonSerializerOptions Plain = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}
