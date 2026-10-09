using System;
using System.IO;

namespace Bloom.Core;

/// What changed in a version, from CHANGELOG.md (embedded in the dll, the same text the GitHub release shows): the
/// "## <version>" section, its English part or the part under "### 한국어" in Korean. Also whether this start is the
/// first one after an update (Settings.SeenVersion), so the window with them shows once.
internal static class ReleaseNotes
{
    /// This build's version: "0.9.2".
    public static string Current => typeof(ReleaseNotes).Assembly.GetName().Version?.ToString(3) ?? "";

    /// The notes for `version` in the UI language (Markdown list lines), or null when the changelog has none.
    public static string? For(string version, string? changelog = null)
    {
        changelog ??= Embedded();
        if (changelog is null) return null;
        var lines = changelog.Replace("\r\n", "\n").Split('\n');
        int start = Array.FindIndex(lines, l => l.Trim() == "## " + version);
        if (start < 0) return null;
        int end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        var section = lines[(start + 1)..(end < 0 ? lines.Length : end)];
        int ko = Array.FindIndex(section, l => l.Trim() == "### 한국어");
        var part = ko < 0 ? section : En ? section[..ko] : section[(ko + 1)..];
        string text = string.Join("\n", part).Trim();
        return text.Length == 0 ? null : text;
    }

    /// True once per new version: an onboarded Bloom starting on a version it hasn't started on before. The version is
    /// remembered either way (a fresh install gets the first-run screens instead).
    public static bool JustUpdated()
    {
        var s = Settings.Current;
        string now = Current;
        if (s.SeenVersion == now) return false;
        bool updated = s.Onboarded == true; // the settings came from an earlier version (before SeenVersion: 0.9.1 and older)
        s.SeenVersion = now;
        Settings.Save();
        return updated;
    }

    private static string? Embedded()
    {
        using var stream = typeof(ReleaseNotes).Assembly.GetManifestResourceStream("Bloom.CHANGELOG.md");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
