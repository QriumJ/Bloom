using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Bloom.Core;

namespace Bloom.Ai;

/// The function name an MCP server's tool goes by in an API chat: "<server>__<tool>". Bloom's own tools have no "__",
/// so the two never meet; and it fits every provider's rule — at most 64 characters of letters, digits, _ and -,
/// starting with a letter or _ (Gemini's is the strictest: no digit first). Anything else becomes _; a name too long
/// keeps its start and ends in a short hash of the whole; a name taken (two servers alike after that) gets _2, _3….
internal static class McpNames
{
    public const int MaxLength = 64;

    /// The server's part is cut to this when the whole would be too long (the tool's name says more).
    private const int MaxServer = 20;

    private const int HashLength = 6;

    /// A new name for `tool` of `server`, not in `taken` (any case), which it is added to.
    public static string Function(string server, string tool, ISet<string> taken)
    {
        string s = Clean(server), t = Clean(tool);
        string name = s + "__" + t;
        if (!char.IsAsciiLetter(name[0]) && name[0] != '_') name = "m" + name;
        if (name.Length > MaxLength)
        {
            if (s.Length > MaxServer) name = s[..MaxServer] + "__" + t;
            if (!char.IsAsciiLetter(name[0]) && name[0] != '_') name = "m" + name;
            if (name.Length > MaxLength) name = name[..(MaxLength - HashLength - 1)] + "_" + Hash(server + "\n" + tool);
        }
        string unique = name;
        for (int n = 2; taken.Contains(unique); n++)
        {
            string suffix = "_" + n;
            unique = (name.Length + suffix.Length > MaxLength ? name[..(MaxLength - suffix.Length)] : name) + suffix;
        }
        taken.Add(unique);
        return unique;
    }

    /// What 항상 허용 remembers for a tool: the server's identity (McpServerDef.Identity: source, name and a hash of its
    /// configuration, shortened here) and the tool's own name — not the function name above, which a second server of
    /// the same name or a longer list can change from one turn to the next.
    public static string ApprovalKey(string identity, string tool) => ToolPermissions.Mcp + ":" + Hash(identity, KeyHashLength) + ":" + tool;

    private const int KeyHashLength = 24;

    private static string Clean(string part)
    {
        var sb = new StringBuilder(part.Length);
        foreach (char c in part) sb.Append(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        return sb.Length == 0 ? "x" : sb.ToString();
    }

    private static string Hash(string text, int length = HashLength) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..length];
}
