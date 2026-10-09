using System.Threading.Tasks;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set keys` (fresh data; run it with --demo): the 단축키 tool beside the launcher with a few shortcuts set (an app,
/// a folder, a Bloom command, a window layout), the bar's 도구 붙이기 button on the home and its absence in the quick AI.
internal static partial class Captures
{
    private static async Task KeysScreens(Host host, Launcher w, string dir)
    {
        foreach (var (query, combo) in new[] { ("chrome", "ctrl+alt+g"), (Tr("다운로드", "Downloads"), "ctrl+alt+d"), (Tr("컬러 피커", "color picker"), "printscreen"), (Tr("왼쪽 절반", "left half"), "win+alt+left") })
        {
            w.Go(Mode.Root, query);
            await Task.Delay(500);
            if (w.Selected?.AsFavorite is { } entry) ItemKeys.Set(entry, combo);
        }
        host.ApplyHotkeys();
        w.SetPinned(true); // the group stays together while its windows take turns being drawn
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            w.Go(Mode.Root);
            host.Tools.Open("keys");
            await Group(host, w, dir, $"{theme}-k1-keys-tool");
            host.Tools.Close(host.Tools.Find("keys")!);
        }
        Theme.Apply("dark");
        w.SetPinned(false);
        await Snap(w, dir, "dark-k2-home-bar");
        w.OpenTools();
        await Snap(w, dir, "dark-k3-tools-menu");
        w.CloseActionsForQa();
        w.Go(Mode.Chat);
        await Snap(w, dir, "dark-k4-quick-ai-bar");
    }
}
