using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Ocr;

namespace Bloom.Tests;

internal static class ScreenGrabTests
{
    public static void CaptureRenderModeIsLocalToItsHiddenWindow()
    {
        var before = RenderOptions.ProcessRenderMode;
        var desktop = ScreenGrab.FrozenPixels(Pixels(128, 96), 128, 96);
        var overlay = ScreenGrab.Overlay(desktop, new ScreenRect(-32000, -32000, 128, 96), -32000, -32000,
            "Synthetic capture", _ => { }, null, () => null, Brushes.White);
        var ordinary = new Window { Width = 100, Height = 80, Left = -32000, Top = -32000, ShowActivated = false };
        try
        {
            // Create real HWNDs without showing, activating or sending desktop mouse input.
            var ordinarySource = HwndSource.FromHwnd(new WindowInteropHelper(ordinary).EnsureHandle())!;
            var ordinaryMode = ordinarySource.CompositionTarget.RenderMode;
            var captureSource = HwndSource.FromHwnd(new WindowInteropHelper(overlay).EnsureHandle())!;
            Check.Eq(captureSource.CompositionTarget.RenderMode, RenderMode.SoftwareOnly, "capture avoids the hardware surface");
            Check.Eq(ordinarySource.CompositionTarget.RenderMode, ordinaryMode, "ordinary window is unaffected");
            Check.Eq(RenderOptions.ProcessRenderMode, before, "global rendering preference is unchanged");
            Check.True(!overlay.IsVisible && !ordinary.IsVisible, "native source test never opens an overlay on the desktop");
        }
        finally
        {
            overlay.Close();
            ordinary.Close();
        }
    }

    public static void BitmapCropMatchesRawBgraAtEveryEdge()
    {
        const int width = 17, height = 13;
        var pixels = Pixels(width, height);
        var desktop = ScreenGrab.FrozenPixels(pixels, width, height);
        Check.True(desktop.IsFrozen, "desktop is immutable across overlay dispatchers");
        foreach (var crop in new[]
        {
            (x: 0, y: 0, w: width, h: height),
            (x: 0, y: 0, w: 1, h: 1),
            (x: width - 1, y: 0, w: 1, h: height),
            (x: 0, y: height - 1, w: width, h: 1),
            (x: 3, y: 4, w: 7, h: 5),
        })
        {
            var expected = ScreenGrab.Crop(pixels, width, crop.x, crop.y, crop.w, crop.h);
            var actual = ScreenGrab.Crop(desktop, crop.x, crop.y, crop.w, crop.h);
            Check.True(actual.SequenceEqual(expected), $"all BGRA bytes agree at {crop}");
        }
    }

    public static void MonitorOffsetsAndBitmapDpiKeepPhysicalPixels()
    {
        var virtualDesktop = new ScreenRect(-24, -12, 64, 48);
        var pixels = Pixels(virtualDesktop.Width, virtualDesktop.Height);
        // Picks from the monitor left/above the primary origin and the primary monitor.
        var picks = new[] { new ScreenRect(-20, -8, 12, 16), new ScreenRect(4, 6, 24, 20) };
        foreach (double dpi in new[] { 96.0, 120.0, 144.0, 192.0 })
        {
            var desktop = BitmapSource.Create(virtualDesktop.Width, virtualDesktop.Height, dpi, dpi,
                PixelFormats.Bgra32, null, pixels, virtualDesktop.Width * 4);
            desktop.Freeze();
            foreach (var pick in picks)
            {
                int x = pick.X - virtualDesktop.X, y = pick.Y - virtualDesktop.Y;
                var expected = ScreenGrab.Crop(pixels, virtualDesktop.Width, x, y, pick.Width, pick.Height);
                var actual = ScreenGrab.Crop(desktop, x, y, pick.Width, pick.Height);
                Check.True(actual.SequenceEqual(expected), $"physical crop at {dpi} DPI and origin {pick.X},{pick.Y}");
            }
        }
    }

    public static void FrozenDesktopOwnsPixelsAfterInputArrayChanges()
    {
        var input = Pixels(23, 19);
        var expected = (byte[])input.Clone();
        var desktop = ScreenGrab.FrozenPixels(input, 23, 19);
        Array.Fill(input, (byte)0);
        Check.True(ScreenGrab.Crop(desktop, 0, 0, 23, 19).SequenceEqual(expected), "WIC storage does not borrow mutable input");
        GC.KeepAlive(input);
    }

    public static void ActiveDesktopDoesNotRetainItsManagedInputBuffer()
    {
        var captured = CreateWithoutRetainingInput();
        var expected = ScreenGrab.Crop(Pixels(512, 256), 512, 7, 11, 19, 13);
        Check.True(ScreenGrab.Crop(captured.desktop, 7, 11, 19, 13).SequenceEqual(expected), "actual buffer supplied to active bitmap before collection");
        // Test-only collection proves ownership; production capture never forces collection.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Check.True(!captured.input.TryGetTarget(out _), "active frozen bitmap does not root raw managed pixels");
        Check.True(ScreenGrab.Crop(captured.desktop, 7, 11, 19, 13).SequenceEqual(expected), "pixels remain available after input is collected");
        GC.KeepAlive(captured.desktop);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (BitmapSource desktop, WeakReference<byte[]> input) CreateWithoutRetainingInput()
    {
        var pixels = Pixels(512, 256);
        var input = new WeakReference<byte[]>(pixels);
        var desktop = ScreenGrab.FrozenPixels(pixels, 512, 256);
        return (desktop, input);
    }

    private static byte[] Pixels(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int at = (y * width + x) * 4;
                pixels[at] = (byte)(x * 17 + y * 11);
                pixels[at + 1] = (byte)(x * 7 + y * 29);
                pixels[at + 2] = (byte)(x * 23 + y * 3);
                pixels[at + 3] = (byte)(x * 13 + y * 5); // check alpha bytes as well as RGB
            }
        return pixels;
    }
}
