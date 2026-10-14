using System;
using Bloom.Native;

namespace Bloom.Ui;

/// The launcher's size as the settings keep it (Settings.Width/Height: dip at 100 %, before 설정 > 일반 > 크기) and as a
/// screen shows it (physical pixels: the kept size times the zoom and the screen's scale, never past the work area).
internal static class LauncherSize
{
    /// The least it can be sized to by hand (dip at 100 %): the chat's footer hints and the calendar's six weeks still fit.
    public const int MinWidth = 600, MinHeight = 360;

    /// The size to keep for a window of `width` × `height` physical pixels shown at `scale` (the zoom times the screen's).
    public static (int width, int height) Unscaled(int width, int height, double scale) =>
        ((int)Math.Round(width / scale), (int)Math.Round(height / scale));

    /// The window in physical pixels for the kept size at `scale` (the zoom times the screen's), no wider or taller than
    /// the monitor's work area `work` (another monitor, a lower resolution).
    public static (int width, int height) Fit(int width, int height, double scale, Win32.RECT work) =>
        (Math.Min((int)Math.Round(width * scale), work.Right - work.Left), Math.Min((int)Math.Round(height * scale), work.Bottom - work.Top));
}
