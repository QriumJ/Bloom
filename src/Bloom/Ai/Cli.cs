using System;
using System.Diagnostics;
using System.Linq;

namespace Bloom.Ai;

internal static class Cli
{
    /// The CLIs run on their own logins only (Claude OAuth, Codex's ChatGPT login): no API keys and
    /// no routing from a Claude / Codex session that may have started us. Their config folders stay.
    public static void CleanEnvironment(ProcessStartInfo psi)
    {
        foreach (string k in psi.Environment.Keys.ToList())
            if ((k.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase) && !k.Equals("CLAUDE_CONFIG_DIR", StringComparison.OrdinalIgnoreCase))
                || k.StartsWith("ANTHROPIC", StringComparison.OrdinalIgnoreCase)
                || k.StartsWith("OPENAI_", StringComparison.OrdinalIgnoreCase)
                || (k.StartsWith("CODEX_", StringComparison.OrdinalIgnoreCase) && !k.Equals("CODEX_HOME", StringComparison.OrdinalIgnoreCase)))
                psi.Environment.Remove(k);
    }
}
