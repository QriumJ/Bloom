using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set guide` (fresh data; run it with `--lang ko|en`): the search line's placeholder with an example, settings
/// lines in the root search, the "?" list (top and end) and an example put into the search line, 설정 › 정보 with its
/// 문제 보고 line (nothing is clicked: the report is neither copied nor sent), and the catalog's lines as catalog.txt.
internal static partial class Captures
{
    private static async Task GuideScreens(Host host, Launcher w, string dir)
    {
        string l = En ? "en" : "ko";
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            string d = $"{l}-{theme}-";
            await Shot(w, dir, d + "g01-placeholder", Mode.Root, "");
            await Shot(w, dir, d + "g02-settings-glass", Mode.Root, Tr("유리 효과", "glass"));
            await Shot(w, dir, d + "g03-settings-shortcut", Mode.Root, Tr("단축키", "shortcut"));
            await Shot(w, dir, d + "g04-guide", Mode.Root, "?");
            w.MoveForQa(-1); // the last row: the list scrolls to the end
            await Snap(w, dir, d + "g05-guide-end");
            w.Go(Mode.Root, "?");
            await Task.Delay(300);
            w.RunSelected(null); // Enter on the first example: it goes into the search line and answers there
            await Snap(w, dir, d + "g06-guide-picked");
        }
        Theme.Apply("dark");
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        sw.Page("about");
        await Task.Delay(400);
        sw.SelectForQa(-1);
        await Task.Delay(300);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, $"{l}-dark-g07-about-report.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        sw.Close();
        File.WriteAllLines(Path.Combine(dir, $"{l}-catalog.txt"), SettingsCatalog.All(host).Select(x => $"{x.PageKey} | {x.PageTitle} | {x.Group} | {x.Title} | {x.Sub}"));
    }
}
