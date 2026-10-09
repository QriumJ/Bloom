using System.Threading.Tasks;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

internal static partial class Captures
{
    /// `--qa-set readme` (dark, 2×; run it with --demo so nothing of this PC shows): the search line answering a few
    /// kinds of typing — 초성, a word typed with the keyboard left in Korean, an emoji, a reminder, a unit — for the
    /// README's search board, and 창 배치 for its "more" board. The other README pictures come from the chatwin,
    /// notices, mine and motion sets and --qa-space, each run with --qa-scale 2.
    private static async Task ReadmeScreens(Host host, Launcher w, string dir)
    {
        Shots.Scale = 2;
        Theme.Apply("dark");
        Settings.Current.FilesInRoot = false;
        Settings.Current.WindowsInRoot = false;
        host.Apps.KeepForQa(Host.WindowsOwn); // also without --demo: the browsers and Windows' own tools only
        await Shot(w, dir, "R-chosung", Mode.Root, Tr("ㅁㅁㅈ", "ntpd"), 900);
        await Shot(w, dir, "R-typo", Mode.Root, Tr("초개ㅡㄷ", "chrom"), 700);
        await Shot(w, dir, "R-emoji", Mode.Root, Tr(":하트", ":heart"), 700);
        await Shot(w, dir, "R-reminder", Mode.Root, Tr("30분 뒤 빨래", "laundry in 30 min"), 700);
        await Shot(w, dir, "R-units", Mode.Root, Tr("30평 to m2", "5.9 ft to cm"), 700);
        // the README's "more" board: 창 배치 (no window of this PC is named in recording mode)
        await Shot(w, dir, "R-layout", Mode.Layout, "", 700);
        Shots.Scale = 1;
    }
}
