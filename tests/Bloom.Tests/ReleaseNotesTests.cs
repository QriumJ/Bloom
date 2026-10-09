using Bloom.Core;

namespace Bloom.Tests;

/// 바뀐 점 from the embedded changelog (Core\ReleaseNotes.cs).
internal static class ReleaseNotesTests
{
    private const string Log = "# Changelog\n\n## 1.2.0\n\n- New thing.\n\n### 한국어\n\n- 새 기능.\n\n## 1.1.0\n\n- Old thing.\n";

    public static void PicksTheVersionAndLanguage()
    {
        Check.Eq(ReleaseNotes.For("1.2.0", Log), "- 새 기능.", "Korean part in Korean");
        Lang.Force(true);
        Check.Eq(ReleaseNotes.For("1.2.0", Log), "- New thing.", "English part in English");
        Check.Eq(ReleaseNotes.For("1.1.0", Log), "- Old thing.", "a section without Korean, up to the end");
        Check.True(ReleaseNotes.For("9.9.9", Log) is null, "no section, no notes");
    }

    public static void ThisVersionHasNotes() => Check.True(ReleaseNotes.For(ReleaseNotes.Current) is { Length: > 0 }, "CHANGELOG.md is embedded and has ## " + ReleaseNotes.Current);
}
