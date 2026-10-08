using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bloom.Core;
using Bloom.Native;
using Bloom.Ui;

namespace Bloom.App;

internal static partial class Captures
{
    /// `--qa-set tools`: the tool windows with the launcher, as one picture each: 메모 (list, then a note) on the right,
    /// 메모 + 타이머 stacked with 계산기 underneath, the launcher and 메모 at 125 %, the 도구 menu, the pinned launcher, and
    /// the search line keeping and finding notes (tools.txt: what each step left).
    private static async Task ToolsScreens(Host host, Launcher w, string dir)
    {
        var log = new System.Text.StringBuilder();
        var shop = Notes.New(Tr("장보기\n- 우유\n- 계란\n- 식빵", "Groceries\n- milk\n- eggs\n- bread"));
        var meet = Notes.New(Tr("금요일 회의\n견적서 다시 보내기 · 일정 확정", "Friday meeting\nresend the quote · fix the date"));
        Notes.SetPinned(meet, true);
        Notes.New(Tr("여행 아이디어\n제주 3박 · 렌터카", "Trip ideas\nJeju 3 nights · rent a car"));
        Timers.Add("timer", Tr("집중", "Focus"), DateTime.Now.AddMinutes(25));
        Timers.Add("reminder", Tr("회의", "Meeting"), DateTime.Today.AddHours(15).AddMinutes(DateTime.Now.Hour >= 15 ? 24 * 60 : 0));
        // a capture run can't hold the foreground, so the group would let go as its windows take turns: pinned throughout
        w.SetPinned(true);
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            w.Go(Mode.Root);
            host.Tools.Open("notes");
            await Group(host, w, dir, $"{theme}-t1-notes-list");
            if (theme == "light") break;
            ((NotesTool)host.Tools.Find("notes")!.Tool).Show(shop.Id);
            await Group(host, w, dir, $"{theme}-t2-note-open");
            host.Tools.Open("timers");
            host.Tools.Open("calc");
            // 계산기 to the bottom: as if it had been dropped under the launcher
            var calc = host.Tools.Find("calc")!;
            Win32.GetWindowRect(w.Handle, out var lr);
            var cr = calc.Rect;
            Win32.SetWindowPos(calc.Handle, Win32.HWND_TOPMOST, lr.Left, lr.Bottom + 10, cr.Right - cr.Left, cr.Bottom - cr.Top, Win32.SWP_NOACTIVATE);
            host.Tools.Moved(calc);
            await Group(host, w, dir, $"{theme}-t3-stacked-and-bottom");
            log.AppendLine("edges: " + string.Join(", ", host.Tools.WindowsForQa.Select(t => $"{t.Tool.Key}={t.Edge ?? "floating"}")));
        }
        Theme.Apply("dark");
        host.Tools.Close(host.Tools.Find("timers")!);
        host.Tools.Close(host.Tools.Find("calc")!);
        Theme.SetScale(1.25);
        await Task.Delay(300);
        await Group(host, w, dir, "dark-t4-scale-125");
        log.AppendLine($"scale 125: launcher {w.Width:0}x{w.Height:0} (dip)");
        Theme.SetScale(1);
        w.OpenTools();
        await Group(host, w, dir, "dark-t5-tools-menu-pinned");
        w.CloseActionsForQa();
        w.SetPinned(false);
        host.Tools.Close(host.Tools.Find("notes")!);
        w.Go(Mode.Root);
        w.SetTextForQa(Tr("메모 우유 사기", "note buy milk"));
        await Snap(w, dir, "dark-t6-keep-note");
        log.AppendLine("메모 …: " + string.Join(" ", w.ResultIdsForQa().Take(3)));
        w.SetTextForQa(Tr("견적서", "quote"));
        await Snap(w, dir, "dark-t7-find-note");
        log.AppendLine("find: " + string.Join(" ", w.ResultIdsForQa().Take(5)));
        File.WriteAllText(Path.Combine(dir, "tools.txt"), log.ToString());
    }

    /// The launcher and its tool windows as they sit, in one picture.
    private static async Task Group(Host host, Launcher w, string dir, string name)
    {
        await Task.Delay(450);
        await w.Dispatcher.InvokeAsync(() =>
        {
            Win32.GetWindowRect(w.Handle, out var lr);
            var parts = new System.Collections.Generic.List<(System.Windows.FrameworkElement, Win32.RECT)> { (w.FrameForQa, lr) };
            parts.AddRange(host.Tools.WindowsForQa.Where(t => t.IsVisible).Select(t => ((System.Windows.FrameworkElement)t.FrameForQa, t.Rect)));
            Shots.SaveGroup(parts, Path.Combine(dir, name + ".png"));
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
