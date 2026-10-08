using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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
