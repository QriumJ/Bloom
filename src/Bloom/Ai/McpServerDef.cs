using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bloom.Ai;

/// An MCP server this PC already has (Claude Code's user settings, an .mcp.json file, Codex's config.toml), as Bloom's
/// chats may borrow it. `Source`: "claude", "codex" or the .mcp.json path; `Raw`: the definition as Claude Code takes
/// it (null for Codex's, which Bloom writes out itself). Http servers have a Url instead of a Command.
internal sealed record McpServerDef(string Name, string Source, string? Command, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string> Env, string? Url, JsonElement? Raw)
{
    public bool Stdio => Command is not null;

    /// The settings' key for its switch: "source|name".
    public string Key => Source + "|" + Name;

    /// The headers a web-address server is sent that its definition spells out beyond Raw's `headers` (Codex's
    /// http_headers, env_http_headers and bearer_token_env_var, resolved when the file is read). Only the API chats' MCP
    /// client reads them; what the CLI chats get of a definition is unchanged.
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// Every header the server is sent: Raw's `headers` object (Claude Code, .mcp.json, VS Code, Cursor; strings only),
    /// then Headers.
    public IReadOnlyDictionary<string, string> AllHeaders
    {
        get
        {
            var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Raw is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty("headers", out var h) && h.ValueKind == JsonValueKind.Object)
                foreach (var p in h.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) all[p.Name] = p.Value.GetString() ?? "";
            foreach (var (name, value) in Headers) all[name] = value;
            return all;
        }
    }

    /// A hash of what the server runs or reaches, normalised: the command, its arguments in order, the env and header
    /// names without case and sorted, the address. A changed definition has another hash.
    public string ConfigHash
    {
        get
        {
            JsonObject Sorted(IEnumerable<KeyValuePair<string, string>> pairs)
            {
                var o = new JsonObject();
                foreach (var (k, v) in pairs.Select(p => (Key: p.Key.ToLowerInvariant(), p.Value)).OrderBy(p => p.Key, StringComparer.Ordinal)) o[k] = v;
                return o;
            }
            var config = new JsonObject
            {
                ["command"] = Command?.Trim(),
                ["args"] = new JsonArray(Args.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
                ["env"] = Sorted(Env),
                ["url"] = Url?.Trim(),
                ["headers"] = Sorted(AllHeaders),
            };
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(config.ToJsonString())));
        }
    }

    /// Which server this is, for what is remembered about it (항상 허용): its source, name and ConfigHash — another
    /// command, address or secret is another server.
    public string Identity => Source + "|" + Name + "|" + ConfigHash;
}
