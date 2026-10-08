using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Bloom.App;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.Ui;

/// The tool windows (메모 · 타이머 · 계산기 · 클립보드): which are open, which sit on an edge of the launcher and in what
/// order, and the group they make with it. Attached tools share the launcher's edge evenly (a column on the right or
/// left, a row underneath), show and hide with it, and keep it open while you work in them (the launcher pins itself);
/// floating ones live on their own. Settings.Tools remembers them across starts.
internal sealed class ToolDock
{
    private readonly Host host;
    private readonly List<ToolWindow> windows = new(); // in order along each edge

    /// The tools, as the launcher's 도구 menu (Ctrl+J) lists them.
    public static readonly (string key, string name, char glyph)[] Kinds =
    {
        ("notes", Tr("메모", "Notes"), G.Document),
        ("timers", Tr("타이머 · 알림", "Timers"), G.Stopwatch),
        ("calc", Tr("계산기", "Calculator"), G.Calculator),
        ("clipboard", Tr("클립보드", "Clipboard"), G.Paste),
    };

    public ToolDock(Host host)
    {
        this.host = host;
        Theme.ScaleChanged += (from, to) =>
        {
            // floating ones grow or shrink from their top-left corner; attached ones take the launcher's new size
            foreach (var w in windows.Where(w => w.Edge is null && w.Handle != IntPtr.Zero))
            {
                var r = w.Rect;
                w.PlaceAt(r.Left, r.Top, (int)((r.Right - r.Left) * to / from), (int)((r.Bottom - r.Top) * to / from));
            }
            Layout();
            Save();
        };
    }

    private Launcher L => host.Window;

    /// QA: the open tool windows, in order.
    public IReadOnlyList<ToolWindow> WindowsForQa => windows;

    public bool IsOpen(string key) => windows.Exists(w => w.Tool.Key == key);

    public ToolWindow? Find(string key) => windows.Find(w => w.Tool.Key == key);

    private ITool Make(string key, string? state) => key switch
    {
        "notes" => new NotesTool(state),
        "timers" => new TimerTool(),
        "calc" => new CalcTool(),
        _ => new ClipTool(host),
    };

    /// The 도구 menu: open one on the launcher's right, or close it.
    public void Toggle(string key)
    {
        if (Find(key) is { } w) Close(w);
        else Open(key);
    }

    /// Opens a tool (attached to the launcher's right when it shows, else floating near the middle) and puts the
    /// keyboard in it; an open one comes forward. 메모 with a note shows that note.
    public void Open(string key, string? note = null)
    {
        if (Find(key) is { } open)
        {
            if (note is not null && open.Tool is NotesTool notes) notes.Show(note);
            Front(open);
            return;
        }
        var w = new ToolWindow(this, Make(key, note), L.IsVisible ? "right" : null, true);
        windows.Add(w);
        if (w.Edge is null) Float(w, null);
        else Layout();
        Front(w);
        Save();
    }

    private static void Front(ToolWindow w)
    {
        if (!w.IsVisible) w.Show();
        w.Activate();
        w.Tool.FocusFirst();
    }

    public void Close(ToolWindow w)
    {
        windows.Remove(w);
        w.Close();
        Layout();
        Save();
        if (L.IsVisible) L.Activate();
    }

    /// The 📌 button on a tool's head: attached → floating beside where it was; floating → onto the launcher's right.
    public void ToggleAttach(ToolWindow w)
    {
        if (w.Edge is not null)
        {
            var r = w.Rect;
            Detach(w);
            int step = (int)(24 * Dpi(w.Handle));
            Float(w, new ToolPlace { X = r.Left + step, Y = r.Top + step, W = r.Right - r.Left, H = r.Bottom - r.Top });
        }
        else if (L.IsVisible)
        {
            Attach(w, "right");
        }
        else
        {
            host.Window.Summon(); // the launcher comes, then the tool joins it
            Attach(w, "right");
        }
        Save();
    }

    private void Attach(ToolWindow w, string edge)
    {
        w.SetEdge(edge);
        w.Topmost = true;
        Layout();
    }

    private void Detach(ToolWindow w)
    {
        w.SetEdge(null);
        w.Topmost = w.OnTop;
        Layout();
    }

    /// A floating tool at its kept place (on a screen that is still there), else near the middle of the screen.
    private static void Float(ToolWindow w, ToolPlace? at)
    {
        new WindowInteropHelper(w).EnsureHandle();
        w.Topmost = w.OnTop;
        double k = Dpi(w.Handle) * Theme.Scale;
        int width = at is { W: > 0 } ? at.W : (int)(300 * k), height = at is { H: > 0 } ? at.H : (int)(360 * k);
        int x, y;
        if (at is not null && OnScreen(at.X + width / 2, at.Y + 20))
        {
            x = at.X;
            y = at.Y;
        }
        else
        {
            Win32.GetCursorPos(out var p);
            var wa = WorkArea(p.X, p.Y);
            x = wa.Left + (wa.Right - wa.Left - width) * 3 / 4;
            y = wa.Top + (wa.Bottom - wa.Top - height) / 3;
        }
        w.PlaceAt(x, y, width, height);
        if (!w.IsVisible) w.Show();
    }

    /// The attached tools around the launcher: a column on the right or left (the launcher's height shared), a row
    /// underneath (its width shared); a side with no room on the screen goes to the other one.
    public void Layout()
    {
        if (!L.IsVisible || L.Handle == IntPtr.Zero)
        {
            foreach (var w in windows.Where(w => w.Edge is not null && w.IsVisible)) w.Hide();
            return;
        }
        Win32.GetWindowRect(L.Handle, out var r);
        double dpi = Dpi(L.Handle), k = dpi * Theme.Scale;
        int gap = (int)Math.Round(8 * dpi);
        var wa = WorkArea((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
        foreach (string edge in new[] { "right", "left", "bottom" })
        {
            var on = windows.Where(w => w.Edge == edge).ToList();
            for (int i = 0; i < on.Count; i++)
            {
                var w = on[i];
                new WindowInteropHelper(w).EnsureHandle();
                int x, y, width, height;
                if (edge == "bottom")
                {
                    width = (r.Right - r.Left - gap * (on.Count - 1)) / on.Count;
                    height = (int)(260 * k);
                    x = r.Left + i * (width + gap);
                    y = r.Bottom + gap + height <= wa.Bottom ? r.Bottom + gap : r.Top - gap - height;
                }
                else
                {
                    width = (int)(300 * k);
                    height = (r.Bottom - r.Top - gap * (on.Count - 1)) / on.Count;
                    y = r.Top + i * (height + gap);
                    bool right = edge == "right" ? r.Right + gap + width <= wa.Right || r.Left - gap - width < wa.Left : r.Left - gap - width < wa.Left;
                    x = right ? r.Right + gap : r.Left - gap - width;
                }
                w.PlaceAt(x, y, width, height);
                if (!w.IsVisible) w.Show();
            }
        }
    }

    /// A tool was dropped after a drag: near an edge of the open launcher it snaps on (in its place along that edge),
    /// anywhere else it floats there.
    public void Moved(ToolWindow w)
    {
        string? edge = null;
        if (L.IsVisible)
        {
            var t = w.Rect;
            Win32.GetWindowRect(L.Handle, out var r);
            int near = (int)(48 * Dpi(L.Handle));
            bool rows = t.Top < r.Bottom && t.Bottom > r.Top, cols = t.Left < r.Right && t.Right > r.Left;
            if (rows && Math.Abs(t.Left - r.Right) < near) edge = "right";
            else if (rows && Math.Abs(t.Right - r.Left) < near) edge = "left";
            else if (cols && Math.Abs(t.Top - r.Bottom) < near) edge = "bottom";
            if (edge is not null)
            {
                // its place along the edge: before the first one whose middle is past its own
                windows.Remove(w);
                int mid = edge == "bottom" ? (t.Left + t.Right) / 2 : (t.Top + t.Bottom) / 2;
                int at = windows.FindIndex(o => o.Edge == edge && Middle(o, edge) > mid);
                windows.Insert(at < 0 ? windows.Count : at, w);
            }
        }
        if (edge is not null) Attach(w, edge);
        else if (w.Edge is not null) Detach(w);
        Save();
    }

    /// While a tool is dragged: its edges pull (within 14 px) to the edges near it — beside another tool or the
    /// launcher with Bloom's 8 px gap, lined up with them, or against the screen's work area. True: `r` moved.
    public bool Magnet(ToolWindow w, ref Win32.RECT r)
    {
        double dpi = Dpi(w.Handle);
        int pull = (int)(14 * dpi), gap = (int)Math.Round(8 * dpi), near = pull * 3;
        var others = windows.Where(o => o != w && o.IsVisible).Select(o => o.Rect).ToList();
        if (L.IsVisible)
        {
            Win32.GetWindowRect(L.Handle, out var lr);
            others.Add(lr);
        }
        int bestX = pull + 1, bestY = pull + 1;
        void X(int edge, int to)
        {
            if (Math.Abs(to - edge) < Math.Abs(bestX)) bestX = to - edge;
        }
        void Y(int edge, int to)
        {
            if (Math.Abs(to - edge) < Math.Abs(bestY)) bestY = to - edge;
        }
        foreach (var o in others)
        {
            if (r.Top < o.Bottom + near && r.Bottom > o.Top - near)
            {
                X(r.Left, o.Right + gap);
                X(r.Right, o.Left - gap);
                X(r.Left, o.Left);
                X(r.Right, o.Right);
            }
            if (r.Left < o.Right + near && r.Right > o.Left - near)
            {
                Y(r.Top, o.Bottom + gap);
                Y(r.Bottom, o.Top - gap);
                Y(r.Top, o.Top);
                Y(r.Bottom, o.Bottom);
            }
        }
        var wa = WorkArea((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
        X(r.Left, wa.Left);
        X(r.Right, wa.Right);
        Y(r.Top, wa.Top);
        Y(r.Bottom, wa.Bottom);
        bool moved = false;
        if (Math.Abs(bestX) <= pull)
        {
            r.Left += bestX;
            r.Right += bestX;
            moved = true;
        }
        if (Math.Abs(bestY) <= pull)
        {
            r.Top += bestY;
            r.Bottom += bestY;
            moved = true;
        }
        return moved;
    }

    private static int Middle(ToolWindow w, string edge)
    {
        var r = w.Rect;
        return edge == "bottom" ? (r.Left + r.Right) / 2 : (r.Top + r.Bottom) / 2;
    }

    // ── the group with the launcher ─────────────────────────────────────────────────────────────

    /// The window is one of the attached tools.
    public bool Owns(IntPtr hwnd) => hwnd != IntPtr.Zero && windows.Exists(w => w.Edge is not null && w.Handle == hwnd);

    public void LauncherShown() => Layout();

    public void LauncherHidden()
    {
        foreach (var w in windows.Where(w => w.Edge is not null)) w.Hide();
    }

    /// The group in front (one of it has the focus) or behind other windows (the pinned launcher let go of it).
    public void SetTopmost(bool on)
    {
        foreach (var w in windows.Where(w => w.Edge is not null)) w.Topmost = on;
    }

    /// Working in an attached tool keeps the launcher open: it pins itself (자동 고정).
    public void Activated(ToolWindow w)
    {
        if (w.Edge is null) return;
        L.SetPinned(true);
        L.Topmost = true;
        SetTopmost(true);
    }

    /// An attached tool lost the focus: to the launcher or another attached tool, nothing; elsewhere, the group lets go.
    public void Deactivated(ToolWindow w)
    {
        if (w.Edge is null) return;
        L.Dispatcher.BeginInvoke(() =>
        {
            IntPtr fg = Win32.GetForegroundWindow();
            if (fg == L.Handle || Owns(fg)) return;
            L.GroupLeft();
        }, DispatcherPriority.Input);
    }

    /// Esc in a tool: an attached one hides with the launcher; a floating one keeps it (true: handled).
    public bool Escape(ToolWindow w)
    {
        if (w.Edge is null) return false;
        L.Dismiss();
        return true;
    }

    // ── kept across starts ──────────────────────────────────────────────────────────────────────

    public void Save()
    {
        Settings.Current.Tools = windows.Select(w =>
        {
            var r = w.Handle != IntPtr.Zero ? w.Rect : default;
            return new ToolPlace { Key = w.Tool.Key, Edge = w.Edge, X = r.Left, Y = r.Top, W = r.Right - r.Left, H = r.Bottom - r.Top, OnTop = w.OnTop, Note = w.Tool.State };
        }).ToList();
        Settings.Save();
    }

    /// At start: the tools left open come back (floating ones now, attached ones with the launcher).
    public void Restore()
    {
        foreach (var p in Settings.Current.Tools.Where(p => Kinds.Any(k => k.key == p.Key)).GroupBy(p => p.Key).Select(g => g.First()).ToList())
        {
            var w = new ToolWindow(this, Make(p.Key, p.Note), p.Edge, p.OnTop);
            windows.Add(w);
            if (p.Edge is null) Float(w, p);
        }
    }

    // ── screens ─────────────────────────────────────────────────────────────────────────────────

    private static double Dpi(IntPtr hwnd) => hwnd != IntPtr.Zero && Win32.GetDpiForWindow(hwnd) is > 0 and var d ? d / 96.0 : 1;

    private static Win32.RECT WorkArea(int x, int y)
    {
        var mon = Win32.MonitorFromPoint(new Win32.POINT { X = x, Y = y }, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        return Win32.GetMonitorInfo(mon, ref info) ? info.rcWork : new Win32.RECT { Right = 1920, Bottom = 1080 };
    }

    private static bool OnScreen(int x, int y) => Win32.MonitorFromPoint(new Win32.POINT { X = x, Y = y }, 0 /* MONITOR_DEFAULTTONULL */) != IntPtr.Zero;
}
