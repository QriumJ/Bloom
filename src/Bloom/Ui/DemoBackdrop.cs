using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Bloom.Native;

namespace Bloom.Ui;

/// The recording mode's plain backdrop (Core.Demo): one colour, the promo pictures' base, over the whole monitor the
/// launcher opens on (`--display=N`, else the one under the mouse at start). It sits under Bloom's windows and never
/// takes the focus, so neither the desktop nor the windows behind the glass show in a recording. Gone with Bloom.
internal sealed class DemoBackdrop : Window
{
    private readonly int? display;
    private IntPtr mon;

    public DemoBackdrop(int? display)
    {
        this.display = display;
        Title = "Bloom";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x14, 0x16));
        Left = Top = -32000;
        Width = Height = 1;
        SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            long ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW)); // no focus, no Alt+Tab
            Cover();
        };
        DpiChanged += (_, _) => Cover(); // WPF resizes a window that lands on a monitor with another scale: put it back
    }

    private void Cover()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (mon == IntPtr.Zero) // picked once: a later DPI fix keeps the same monitor
        {
            if (display is { } d && Launcher.Monitors() is { Count: > 0 } mons) mon = d >= 1 && d <= mons.Count ? mons[d - 1] : mons.First();
            else
            {
                Win32.GetCursorPos(out var p);
                mon = Win32.MonitorFromPoint(p, Win32.MONITOR_DEFAULTTONEAREST);
            }
        }
        var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(mon, ref info)) return;
        var r = info.rcMonitor;
        Win32.SetWindowPos(hwnd, Win32.HWND_NOTOPMOST, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, Win32.SWP_NOACTIVATE);
    }
}
