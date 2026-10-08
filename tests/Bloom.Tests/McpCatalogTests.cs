using System.IO;
using System.Linq;
using Bloom.Ai;

namespace Bloom.Tests;

/// Reading the MCP servers other programs set up: Codex's TOML and an .mcp.json, as they are written.
internal static class McpCatalogTests
{
    public static void CodexToml()
    {
        string toml = """
            model = "gpt-5"

            [mcp_servers.image-tools]
            command = 'C:\Tools\node.exe'
            args = ["C:\\mcp\\images\\index.js", "--port, 9000"] # a comment
            env = { IMG_HOME = "D:\\Images", MODE = 'quiet' }

            [mcp_servers."two words"]
            url = "http://127.0.0.1:3000/mcp"

            [mcp_servers.notes]
            command = "uvx"
            args = ["notes-mcp"]

            [mcp_servers.notes.env]
            NOTES_PORT = "9876"

            [profiles.fast]
            command = "not a server"
            """;
        var all = McpCatalog.ParseToml(toml);
        Check.Eq(all.Count, 3, "three servers; other tables are left alone");
        var pr = all.First(s => s.Name == "image-tools");
        Check.Eq(pr.Command, @"C:\Tools\node.exe", "a literal string keeps its backslashes");
        Check.True(pr.Args.SequenceEqual(new[] { @"C:\mcp\images\index.js", "--port, 9000" }), "a basic string's escapes; a comma inside quotes stays");
        Check.True(pr.Env["IMG_HOME"] == @"D:\Images" && pr.Env["MODE"] == "quiet", "an inline env table");
        var web = all.First(s => s.Name == "two words");
        Check.True(!web.Stdio && web.Url == "http://127.0.0.1:3000/mcp", "a quoted name, a web address");
        Check.Eq(all.First(s => s.Name == "notes").Env["NOTES_PORT"], "9876", "an [….env] table");
    }

    public static void McpJson()
    {
        string file = Path.Combine(Path.GetTempPath(), "bloom-test-" + System.Guid.NewGuid().ToString("N")[..8] + ".mcp.json");
        File.WriteAllText(file, """
            {
              // Claude Code takes comments and trailing commas in its own files
              "mcpServers": {
                "drawing": { "command": "node", "args": ["server.js"], "env": { "DRAW_PORT": "7001" }, },
                "remote": { "type": "http", "url": "https://example.com/mcp" },
                "broken": { "args": ["no command"] }
              }
            }
            """);
        try
        {
            var ai = McpCatalog.Find(file, "drawing");
            Check.True(ai is { Stdio: true, Command: "node" } && ai.Env["DRAW_PORT"] == "7001" && ai.Raw is not null, "a stdio server, its env, and its definition as written");
            Check.True(McpCatalog.Find(file, "remote") is { Stdio: false, Url: "https://example.com/mcp" }, "a web-address server");
            Check.True(McpCatalog.Find(file, "broken") is null, "neither a command nor a url: skipped");
            Check.Eq(McpCatalog.Label(file).EndsWith(".mcp.json", System.StringComparison.Ordinal), true, "a file is named by its folder");
        }
        finally
        {
            File.Delete(file);
        }
    }
}
