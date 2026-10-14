using System;
using System.Windows.Interop;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.Ui;

/// Sizing the launcher by its edges and corners: every view takes the room (the list's row grows, text and icons keep
/// their size) and the attached tools follow. Let go, the size is kept (Settings.Width/Height, dip at 100 %: 설정 > 일반
/// > 크기 only multiplies it) for the next opening and the next start; 설정 > 일반 > 런처 창 크기 puts it back.
internal sealed partial class Launcher
{
    private bool sizing; // sized by hand: from the first WM_SIZING to WM_EXITSIZEMOVE (a drag by the handles is no sizing)
    private (int w, int h) sizedAt; // meanwhile, the size the attached tools were last laid out for

    private void WireSizing()
    {
        SourceInitialized += (_, _) => HwndSource.FromHwnd(Handle)?.AddHook(SizeHook);
        SizeChanged += (_, _) =>
        {
            if (sizing) SizedByHand(false);
        };
    }

    private IntPtr SizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_SIZING && !sizing)
        {
            sizing = true;
            sizedAt = HandSize(); // WM_SIZING comes before the window changes
            actions.Close(); // placed for the old size
        }
        else if (msg == Win32.WM_EXITSIZEMOVE && sizing) SizedByHand(true);
        return IntPtr.Zero;
    }

    /// The window's size now, as the settings keep it (dip at 100 %).
    private (int w, int h) HandSize()
    {
        Win32.GetWindowRect(Handle, out var r);
        return LauncherSize.Unscaled(r.Right - r.Left, r.Bottom - r.Top, ToolDock.Dpi(Handle) * Theme.Scale);
    }

    /// While it is sized by hand, the attached tools follow; let go (`done`), the size is kept once, and a launcher
    /// dragged somewhere stays where its top-left corner went.
    private void SizedByHand(bool done)
    {
        var now = HandSize();
        host.Tools.LauncherSized(sizedAt, now);
        sizedAt = now;
        if (!done) return;
        sizing = false;
        (Settings.Current.Width, Settings.Current.Height) = now;
        Settings.Save();
        host.Tools.Save();
        if (movedTo is null) return;
        Win32.GetWindowRect(Handle, out var r);
        movedTo = new Win32.POINT { X = r.Left, Y = r.Top };
    }

    /// 설정 > 일반 > 런처 창 크기: the default size again, at once while it shows.
    public void ResetSize()
    {
        var was = (Settings.Current.Width, Settings.Current.Height);
        (Settings.Current.Width, Settings.Current.Height) = (Settings.DefaultWidth, Settings.DefaultHeight);
        Settings.Save();
        if (IsVisible) Place();
        host.Tools.LauncherSized(was, (Settings.DefaultWidth, Settings.DefaultHeight));
        host.Tools.Save();
    }
}
