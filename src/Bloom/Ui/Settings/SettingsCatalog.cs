using System.Collections.Generic;
using Bloom.App;
using Bloom.Core;

namespace Bloom.Ui;

/// Every settings line as text, for the launcher's root search ("유리 효과" finds 설정 › 성능). The lines come from the
/// settings pages themselves (SettingsView in collect mode), so no second list of titles exists. Built on the UI thread
/// at the first search that needs it and kept until the settings are saved again: some lines show only for some
/// settings (번역 생각 수준 not for Haiku), and the language is fixed for a run. What the lines say about the state of the
/// PC (a count, a path) is that of the last build.
internal static class SettingsCatalog
{
    /// One line as the settings window shows it: its title and the line under it, the group and the page it is in.
    internal sealed record Line(string Title, string Sub, string Group, string PageKey, string PageTitle)
    {
        /// Matched the way the settings window's filter does: the title first, the rest as aliases.
        public MatchTarget Match { get; } = new(Title, new[] { Sub, Group, PageTitle });
    }

    private static (int version, List<Line> lines)? cache;

    public static IReadOnlyList<Line> All(Host host)
    {
        if (cache is not { } c || c.version != Settings.Version) cache = c = (Settings.Version, new SettingsView(host).Collect());
        return c.lines;
    }
}
