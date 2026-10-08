using System.Collections.Generic;
using System.Text.Json;

namespace Bloom.Ai;

/// An MCP server this PC already has (Claude Code's user settings, an .mcp.json file, Codex's config.toml), as Bloom's
/// chats may borrow it. `Source`: "claude", "codex" or the .mcp.json path; `Raw`: the definition as Claude Code takes
/// it (null for Codex's, which Bloom writes out itself). Http servers have a Url instead of a Command.
internal sealed record McpServerDef(string Name, string Source, string? Command, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string> Env, string? Url, JsonElement? Raw)
{
    public bool Stdio => Command is not null;

    /// The settings' key for its switch: "source|name".
    public string Key => Source + "|" + Name;
}
