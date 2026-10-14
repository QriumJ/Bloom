using System.Text.Json;
using Bloom.Core;
using Bloom.Native;
using Bloom.Ui;

namespace Bloom.Tests;

/// The launcher's size (Ui\Launcher\LauncherSize.cs): kept before Bloom's zoom, shown within the monitor's work area.
internal static class LauncherSizeTests
{
    private static Win32.RECT Area(int left, int top, int width, int height) => new() { Left = left, Top = top, Right = left + width, Bottom = top + height };

    public static void FitsTheWorkArea()
    {
        var full = Area(0, 0, 1920, 1032);
        Check.Eq(LauncherSize.Fit(760, 476, 1, full), (760, 476), "the kept size where it fits");
        Check.Eq(LauncherSize.Fit(760, 476, 1.25 * 1.5, full), (1425, 892), "times the zoom and the screen's scale");
        Check.Eq(LauncherSize.Fit(1600, 1200, 1, full), (1600, 1032), "too tall: as tall as the work area");
        Check.Eq(LauncherSize.Fit(2400, 900, 1, full), (1920, 900), "too wide: as wide as the work area");
        var right = Area(1920, -200, 1280, 680); // a smaller monitor right of the main one, its top above it
        Check.Eq(LauncherSize.Fit(1600, 1000, 1, right), (1280, 680), "on a smaller monitor: its work area, wherever it is");
        Check.Eq(LauncherSize.Fit(1000, 500, 1.25, right), (1250, 625), "scaled first, then fitted");
    }

    public static void KeptBeforeTheZoom()
    {
        Check.Eq(LauncherSize.Unscaled(1425, 892, 1.25 * 1.5), (760, 476), "sized at 125 % zoom on a 150 % screen");
        Check.Eq(LauncherSize.Unscaled(900, 500, 1), (900, 500), "at 100 %");
        var full = Area(0, 0, 3840, 2112);
        // let go without a change, the kept size stays (zoom times screen scale of 1 or more: the pixels are finer)
        foreach (double k in new[] { 1.0, 1.1, 1.25, 1.5, 1.875, 2.25, 3.0 })
        {
            var (w, h) = LauncherSize.Fit(913, 587, k, full);
            Check.Eq(LauncherSize.Unscaled(w, h, k), (913, 587), $"shown at {k} and kept again");
        }
        // below 1 a kept size is finer than a pixel: sized there, it opens again as sized
        foreach (double k in new[] { 0.8, 0.9 })
        {
            var (w, h) = LauncherSize.Unscaled(731, 469, k);
            Check.Eq(LauncherSize.Fit(w, h, k, full), (731, 469), $"sized at {k} and shown again");
        }

        // kept, a restart reads it back as it was; the zoom only multiplies it
        var settings = new Settings();
        (settings.Width, settings.Height) = LauncherSize.Unscaled(1425, 892, 1.25 * 1.5);
        var read = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings, Json.Options), Json.Options)!;
        Check.Eq((read.Width, read.Height), (760, 476), "after a restart");
        Check.Eq(LauncherSize.Fit(read.Width, read.Height, 1.5, full), (1140, 714), "at 150 % zoom on a 100 % screen");
        var fresh = JsonSerializer.Deserialize<Settings>("{}", Json.Options)!;
        Check.Eq((fresh.Width, fresh.Height), (Settings.DefaultWidth, Settings.DefaultHeight), "no size kept yet: the default");
    }
}
