using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.Ui;

/// Windows 11 acrylic behind a WPF window: the DWM "transient window" backdrop over a client area
/// extended into the frame, rounded corners, our own hairline border, no DWM show/hide animation
/// (the launcher opens 100+ times a day — it must appear instantly).
internal static class Glass
{
    public static void Apply(Window w, bool dark, bool acrylic, Color border)
    {
        IntPtr hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        int v = dark ? 1 : 0;
        _ = Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, 4);
        int corner = Win32.DWMWCP_ROUND;
        _ = Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
        int noAnim = 1;
        _ = Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_TRANSITIONS_FORCEDISABLED, ref noAnim, 4);
        int colorRef = border.R | border.G << 8 | border.B << 16;
        _ = Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_BORDER_COLOR, ref colorRef, 4);
        // no caption fill: the title-bar strip at the top shows the acrylic like the rest
        int noCaption = Win32.DWMWA_COLOR_NONE;
        _ = Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_CAPTION_COLOR, ref noCaption, 4);
        int backdrop = acrylic ? Win32.DWMSBT_TRANSIENTWINDOW : Win32.DWMSBT_NONE;
        _ = Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, 4);
        var m = acrylic ? new Win32.MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 } : new Win32.MARGINS();
        _ = Win32.DwmExtendFrameIntoClientArea(hwnd, ref m);
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
            target.BackgroundColor = acrylic ? Colors.Transparent : Colors.Black;
    }

    /// Hides a window from the screen while it keeps drawing (DWM cloak). Opening under a cloak, the first frame on
    /// screen is the new one: uncloaked, it was the one from before the last hide, over the plain backdrop.
    public static void Cloak(IntPtr hwnd, bool on)
    {
        if (hwnd == IntPtr.Zero) return;
        int v = on ? 1 : 0;
        int hr = Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_CLOAK, ref v, 4);
        if (hr != 0 && !cloakLogged)
        {
            cloakLogged = true;
            Log.Write($"cloak {(on ? "on" : "off")} failed 0x{hr:X8}");
        }
    }

    private static bool cloakLogged;
    private static int openings;

    /// Uncloaks the windows together once WPF has drawn their next frame and DWM has composed it. The Rendering event
    /// comes before the drawing (and can come twice within a couple of ms), so after it: the frame goes out, then one
    /// DWM pass, waited for off the UI thread. The guard never leaves them hidden. Hidden and opened again before
    /// that, the newer opening uncloaks the shown windows; this one only lets go of the ones that stayed hidden.
    public static void RevealAfterFrame(IReadOnlyList<IntPtr> hwnds)
    {
        var ui = Dispatcher.CurrentDispatcher;
        int opening = ++openings;
        bool done = false;
        var guard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        EventHandler? onFrame = null;
        void Reveal()
        {
            if (done) return;
            done = true;
            CompositionTarget.Rendering -= onFrame;
            guard.Stop();
            foreach (var h in hwnds)
                if (opening == openings || !Win32.IsWindowVisible(h)) Cloak(h, false);
        }
        onFrame = (_, _) =>
        {
            CompositionTarget.Rendering -= onFrame;
            ui.BeginInvoke(() => Task.Run(() => Win32.DwmFlush()).ContinueWith(_ => ui.BeginInvoke(Reveal)), DispatcherPriority.Background);
        };
        guard.Tick += (_, _) => Reveal();
        CompositionTarget.Rendering += onFrame;
        guard.Start();
    }

    /// Keep the launcher out of Alt+Tab and the taskbar, and drop the system menu: with the frame
    /// extended over the whole window, DWM would otherwise paint its close button in the corner.
    /// (The caption itself stays: without WS_CAPTION, DWM draws no acrylic at all.)
    public static void ToolWindow(Window w)
    {
        IntPtr hwnd = new WindowInteropHelper(w).Handle;
        long ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        ex = (ex | Win32.WS_EX_TOOLWINDOW) & ~Win32.WS_EX_APPWINDOW;
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex));
        const int GWL_STYLE = -16;
        const long WS_SYSMENU = 0x80000, WS_MINIMIZEBOX = 0x20000, WS_MAXIMIZEBOX = 0x10000;
        long st = Win32.GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        Win32.SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(st & ~(WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX)));
        Win32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
    }
}
