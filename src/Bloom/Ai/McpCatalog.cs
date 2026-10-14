using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Bloom.Core;

namespace Bloom.Ai;

/// The MCP servers already set up on this PC, read where each program keeps them, never copied and never named in
/// Bloom: Claude Code (~/.claude.json, and the .mcp.json of every project it has opened), Claude's desktop app,
/// Cursor, VS Code, Windsurf, Codex (~/.codex/config.toml), and any .mcp.json the user brings in. Every person's
/// list and paths are their own; a chat gets the ones switched on, read fresh when it starts, so their commands and
/// env values never sit in Bloom's files.
internal static class McpCatalog
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// The programs' own files: source id, where it is, the JSON key its servers sit under, how the settings name it.
    private static readonly (string id, Func<string> path, string key, string label)[] Known =
    {
        ("claude", () => Path.Combine(Home, ".claude.json"), "mcpServers", "Claude Code"),
        ("desktop", () => Path.Combine(Roaming, "Claude", "claude_desktop_config.json"), "mcpServers", "Claude Desktop"),
        ("cursor", () => Path.Combine(Home, ".cursor", "mcp.json"), "mcpServers", "Cursor"),
        ("vscode", () => Path.Combine(Roaming, "Code", "User", "mcp.json"), "servers", "VS Code"),
        ("windsurf", () => Path.Combine(Home, ".codeium", "windsurf", "mcp_config.json"), "mcpServers", "Windsurf"),
    };

    private static string CodexFile => Path.Combine(Home, ".codex", "config.toml");

    /// Every server found (a server several programs share shows once, from the first); the bloom name is Bloom's own.
    public static List<McpServerDef> Found(IEnumerable<string> files, bool? demo = null)
    {
        if (demo ?? Demo.On) return new(); // recording mode: this PC's servers stay out
        var all = new List<McpServerDef>();
        foreach (var k in Known) all.AddRange(FromJson(k.path(), k.id, k.key));
        foreach (string f in ProjectFiles().Concat(files).Distinct(StringComparer.OrdinalIgnoreCase)) all.AddRange(FromJson(f, f, "mcpServers"));
        all.AddRange(FromToml(CodexFile));
        return all.Where(s => s.Name != "bloom")
            .GroupBy(s => s.Name + "|" + (s.Command ?? s.Url) + "|" + string.Join(" ", s.Args), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()).ToList();
    }

    /// One server by its source and name (the relay reads it this way when Codex starts it).
    public static McpServerDef? Find(string source, string name)
    {
        var list = source == "codex" ? FromToml(CodexFile)
            : Array.Find(Known, k => k.id == source) is { id: not null } k ? FromJson(k.path(), k.id, k.key)
            : FromJson(source, source, "mcpServers");
        return list.FirstOrDefault(s => s.Name == name);
    }

    /// How the settings say where a server came from: the program, or the project folder of an .mcp.json.
    public static string Label(string source) =>
        source == "codex" ? "Codex"
        : Array.Find(Known, k => k.id == source) is { id: not null } k ? k.label
        : Path.GetFileName(Path.GetDirectoryName(source) ?? source) + " · .mcp.json";

    /// The .mcp.json at the root of each project Claude Code has opened (its ~/.claude.json "projects").
    private static List<string> ProjectFiles()
    {
        var files = new List<string>();
        try
        {
            string path = Path.Combine(Home, ".claude.json");
            if (!File.Exists(path)) return files;
            using var doc = JsonDocument.Parse(Read(path), new JsonDocumentOptions { AllowTrailingCommas = true });
            if (!doc.RootElement.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Object) return files;
            foreach (var p in projects.EnumerateObject())
            {
                string file = Path.Combine(p.Name, ".mcp.json");
                if (File.Exists(file)) files.Add(Path.GetFullPath(file));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            Log.Error("claude projects", ex);
        }
        return files;
    }

    /// The servers switched on in the settings, as found now.
    public static List<McpServerDef> Picked()
    {
        return PreparePicked()?.Invoke() ?? new();
    }

    /// Capture selection on its UI owner; the returned reader owns only value snapshots.
    public static Func<List<McpServerDef>>? PreparePicked()
    {
        var on = new HashSet<string>(Settings.Current.McpOn, StringComparer.Ordinal);
        string[] files = Settings.Current.McpFiles.ToArray();
        bool demo = Demo.On;
        return on.Count == 0 || demo ? null : () => Found(files, demo).Where(s => on.Contains(s.Key)).ToList();
    }

    private static string Read(string path)
    {
        // Claude Code may be writing its own file: read it without locking it
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs, Encoding.UTF8);
        return sr.ReadToEnd();
    }

    private static List<McpServerDef> FromJson(string path, string source, string key)
    {
        var list = new List<McpServerDef>();
        try
        {
            if (!File.Exists(path)) return list;
            using var doc = JsonDocument.Parse(Read(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (!doc.RootElement.TryGetProperty(key, out var servers) || servers.ValueKind != JsonValueKind.Object) return list;
            foreach (var s in servers.EnumerateObject())
            {
                var v = s.Value;
                if (v.ValueKind != JsonValueKind.Object) continue;
                string? command = Str(v, "command"), url = Str(v, "url");
                if (command is null && url is null) continue;
                var args = v.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.ToString()).ToList() : new List<string>();
                var env = new Dictionary<string, string>();
                if (v.TryGetProperty("env", out var e) && e.ValueKind == JsonValueKind.Object)
                    foreach (var kv in e.EnumerateObject()) env[kv.Name] = kv.Value.ToString();
                list.Add(new McpServerDef(s.Name, source, command, args, env, command is null ? url : null, v.Clone()));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error("mcp servers in " + Path.GetFileName(path), ex);
        }
        return list;
    }

    private static string? Str(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// Codex's [mcp_servers.<name>] tables (and their [mcp_servers.<name>.env]): command, args, env, url — the plain
    /// TOML those use (strings, arrays of strings, inline tables of strings); anything else is left alone.
    private static List<McpServerDef> FromToml(string path)
    {
        try
        {
            return File.Exists(path) ? ParseToml(Read(path)) : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("mcp servers in config.toml", ex);
            return new();
        }
    }

    /// A Codex server's table as it is read: what the CLI chats take (command, args, env, url) and what only the API
    /// chats' MCP client uses (the headers a web-address server is sent).
    private sealed class TomlServer
    {
        public string? Command, Url, BearerEnv;
        public List<string> Args = new();
        public readonly Dictionary<string, string> Env = new(), Headers = new(), EnvHeaders = new(); // EnvHeaders: header → the variable holding its value
    }

    /// The servers in a config.toml's text. A web-address server's `http_headers`, `env_http_headers` (header → the
    /// environment variable that holds its value) and `bearer_token_env_var` become its Headers, resolved now from this
    /// PC's environment (a variable not set sends no header); the other fields of a definition are read as before.
    public static List<McpServerDef> ParseToml(string text)
    {
        var list = new List<McpServerDef>();
        var tables = new Dictionary<string, TomlServer>();
        string? name = null, section = null; // section: the sub-table being read (env, http_headers, env_http_headers)
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line[0] == '[')
            {
                string head = line.Trim('[', ']').Trim();
                name = null;
                section = null;
                if (!head.StartsWith("mcp_servers.", StringComparison.Ordinal)) continue;
                var parts = SplitKey(head["mcp_servers.".Length..]);
                if (parts.Count == 0 || parts.Count > 2) continue;
                if (parts.Count == 2)
                {
                    if (parts[1] is not ("env" or "http_headers" or "env_http_headers")) continue;
                    section = parts[1];
                }
                name = parts[0];
                if (!tables.ContainsKey(name)) tables[name] = new TomlServer();
                continue;
            }
            if (name is null) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = Unquote(line[..eq].Trim()), value = line[(eq + 1)..].Trim();
            var t = tables[name];
            if (section == "env") t.Env[key] = Value(value);
            else if (section == "http_headers") t.Headers[key] = Value(value);
            else if (section == "env_http_headers") t.EnvHeaders[key] = Value(value);
            else if (key == "command") t.Command = Value(value);
            else if (key == "url") t.Url = Value(value);
            else if (key == "args") t.Args = Strings(value);
            else if (key == "bearer_token_env_var") t.BearerEnv = Value(value);
            else if (key == "env") foreach (var (k, v) in Inline(value)) t.Env[k] = v;
            else if (key == "http_headers") foreach (var (k, v) in Inline(value)) t.Headers[k] = v;
            else if (key == "env_http_headers") foreach (var (k, v) in Inline(value)) t.EnvHeaders[k] = v;
        }
        foreach (var (n, t) in tables)
            if (t.Command is not null || t.Url is not null)
                list.Add(new McpServerDef(n, "codex", t.Command, t.Args, t.Env, t.Command is null ? t.Url : null, null) { Headers = t.Command is null ? ResolvedHeaders(t) : new Dictionary<string, string>() });
        return list;
    }

    /// A web-address server's headers: the plain ones, those whose value is in an environment variable, and a Bearer token
    /// from one.
    private static Dictionary<string, string> ResolvedHeaders(TomlServer t)
    {
        var headers = new Dictionary<string, string>(t.Headers);
        foreach (var (header, variable) in t.EnvHeaders)
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value) headers[header] = value;
        if (t.BearerEnv is { Length: > 0 } bearer && Environment.GetEnvironmentVariable(bearer) is { Length: > 0 } token) headers["Authorization"] = "Bearer " + token;
        return headers;
    }

    /// "a.\"b c\".env" → [a, b c, env].
    private static List<string> SplitKey(string key)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        char quote = '\0';
        foreach (char c in key)
        {
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else sb.Append(c);
            }
            else if (c is '"' or '\'') quote = c;
            else if (c == '.')
            {
                parts.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else sb.Append(c);
        }
        parts.Add(sb.ToString().Trim());
        return parts.Where(p => p.Length > 0).ToList();
    }

    private static string Unquote(string s) => s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[^1] == s[0] ? s[1..^1] : s;

    /// A TOML string: "basic" (with \\ \" \n escapes) or 'literal'; anything else as written.
    private static string Value(string v)
    {
        v = v.Trim();
        if (v.StartsWith('\'')) return v.Length >= 2 && v.IndexOf('\'', 1) is > 0 and var end ? v[1..end] : v.Trim('\'');
        if (!v.StartsWith('"')) return v.Split('#')[0].Trim();
        var sb = new StringBuilder();
        for (int i = 1; i < v.Length; i++)
        {
            char c = v[i];
            if (c == '"') break;
            if (c == '\\' && i + 1 < v.Length)
            {
                char n = v[++i];
                sb.Append(n switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => n });
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// ["a", 'b'] → [a, b].
    private static List<string> Strings(string v) => Items(v.Trim().TrimStart('[').TrimEnd(']')).Select(Value).ToList();

    /// { A = "x", B = 'y' } → (A, x), (B, y).
    private static IEnumerable<(string, string)> Inline(string v)
    {
        foreach (string item in Items(v.Trim().TrimStart('{').TrimEnd('}')))
        {
            int eq = item.IndexOf('=');
            if (eq > 0) yield return (Unquote(item[..eq].Trim()), Value(item[(eq + 1)..]));
        }
    }

    /// The comma-separated items of an array or inline table, commas inside quotes kept.
    private static List<string> Items(string s)
    {
        var items = new List<string>();
        var sb = new StringBuilder();
        char quote = '\0';
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (quote != '\0')
            {
                sb.Append(c);
                if (c == '\\' && quote == '"' && i + 1 < s.Length) sb.Append(s[++i]);
                else if (c == quote) quote = '\0';
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                sb.Append(c);
            }
            else if (c == ',')
            {
                if (sb.ToString().Trim().Length > 0) items.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else sb.Append(c);
        }
        if (sb.ToString().Trim().Length > 0) items.Add(sb.ToString().Trim());
        return items;
    }
}
