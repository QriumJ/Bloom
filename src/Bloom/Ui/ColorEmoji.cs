using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Core;

namespace Bloom.Ui;

/// WPF draws emoji in black and white only (no colour font layers), so emoji are drawn here into a bitmap with
/// Direct2D + DirectWrite, which use Segoe UI Emoji's colours (D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT).
/// Raw COM through vtable slots like Native\WinRt; slot numbers follow d2d1.h / dwrite.h / wincodec.h.
/// Bitmaps are cached per text and pixel size; any failure returns null and the caller draws plain text.
internal static unsafe class ColorEmoji
{
    private static readonly Guid IID_ID2D1Factory = new("06152247-6f50-465a-9245-118bfd3b6007");
    private static readonly Guid IID_IDWriteFactory = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");
    private static readonly Guid CLSID_WICImagingFactory = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid IID_IWICImagingFactory = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
    private static readonly Guid GUID_WICPixelFormat32bppPBGRA = new("6fddc324-4e03-4bfe-b185-3d77768dc910");

    private static IntPtr d2d, dwrite, wic;
    private static bool broken;
    private static readonly Dictionary<(string, int, bool), BitmapSource?> Cache = new();
    private static readonly object Gate = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct RenderTargetProperties
    {
        public int Type; // D2D1_RENDER_TARGET_TYPE_DEFAULT
        public int Format; // DXGI_FORMAT_B8G8R8A8_UNORM = 87
        public int Alpha; // D2D1_ALPHA_MODE_PREMULTIPLIED = 1
        public float DpiX, DpiY;
        public int Usage, MinLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ColorF
    {
        public float R, G, B, A;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectF
    {
        public float Left, Top, Right, Bottom;
    }

    /// Whether colour drawing failed once (plain text from then on).
    public static bool Broken => broken;

    /// A bitmap already drawn for this text, size and theme (null inside: drawing it failed).
    public static bool TryCached(string text, int px, bool dark, out BitmapSource? bmp)
    {
        lock (Gate) return Cache.TryGetValue((text, px, dark), out bmp);
    }

    /// The text drawn in colour, `px` pixels square (physical pixels; WPF shows it at the size it is given), with the
    /// ink of the dark or light theme for symbols that have no colour of their own. Any thread: the icons worker draws
    /// the lists' emoji (Icons.RequestEmoji); one at a time, since the D2D factory is single-threaded.
    public static BitmapSource? Render(string text, int px, bool dark)
    {
        lock (Gate)
        {
            if (broken || text.Length == 0) return null;
            if (Cache.TryGetValue((text, px, dark), out var hit)) return hit;
            BitmapSource? result = null;
            try
            {
                result = Draw(text, px, dark);
            }
            catch (Exception e) when (e is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
            {
                Log.Error("color emoji", e);
                broken = true; // plain text from now on
            }
            if (Cache.Count > 600) Cache.Clear();
            Cache[(text, px, dark)] = result;
            return result;
        }
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    private static IntPtr Slot(IntPtr obj, int index) => (*(IntPtr**)obj)[index];

    private static void Factories()
    {
        if (d2d != IntPtr.Zero) return;
        Guid iid = IID_ID2D1Factory;
        Check(D2D1CreateFactory(0 /* single threaded */, ref iid, IntPtr.Zero, out d2d));
        iid = IID_IDWriteFactory;
        Check(DWriteCreateFactory(0 /* shared */, ref iid, out dwrite));
        Guid clsid = CLSID_WICImagingFactory;
        iid = IID_IWICImagingFactory;
        Check(CoCreateInstance(ref clsid, IntPtr.Zero, 1 /* CLSCTX_INPROC_SERVER */, ref iid, out wic));
    }

    /// One drawing surface per pixel size, made once and drawn on again for every emoji of that size: the WIC bitmap,
    /// its D2D render target, the centred text format and the ink brush per theme (making them each time took ~3 ms an
    /// emoji, half of the emoji view's first draw).
    private sealed class Canvas
    {
        public IntPtr Bitmap, Target, Format, InkDark, InkLight;
    }

    private static readonly Dictionary<int, Canvas> Canvases = new();

    private static Canvas CanvasFor(int px)
    {
        if (Canvases.TryGetValue(px, out var c)) return c;
        Factories();
        IntPtr bitmap = IntPtr.Zero, target = IntPtr.Zero, format = IntPtr.Zero, inkDark = IntPtr.Zero, inkLight = IntPtr.Zero;
        try
        {
            // IWICImagingFactory::CreateBitmap (17): w, h, pixel format, WICBitmapCacheOnLoad (2)
            Guid pbgra = GUID_WICPixelFormat32bppPBGRA;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, Guid*, int, IntPtr*, int>)Slot(wic, 17))(wic, (uint)px, (uint)px, &pbgra, 2, &bitmap));
            // ID2D1Factory::CreateWicBitmapRenderTarget (13)
            var props = new RenderTargetProperties { Type = 0, Format = 87, Alpha = 1, DpiX = 96, DpiY = 96 };
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, RenderTargetProperties*, IntPtr*, int>)Slot(d2d, 13))(d2d, bitmap, &props, &target));
            // IDWriteFactory::CreateTextFormat (15): family, collection, weight, style, stretch, size, locale
            float size = px * 0.78f;
            fixed (char* family = "Segoe UI Emoji")
            fixed (char* locale = "en-us")
                Check(((delegate* unmanaged[Stdcall]<IntPtr, char*, IntPtr, int, int, int, float, char*, IntPtr*, int>)Slot(dwrite, 15))(dwrite, family, IntPtr.Zero, 400, 0, 5, size, locale, &format));
            // IDWriteTextFormat::SetTextAlignment (3) / SetParagraphAlignment (4): centre
            Check(((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Slot(format, 3))(format, 2));
            Check(((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Slot(format, 4))(format, 2));
            // ID2D1RenderTarget::CreateSolidColorBrush (8) — the colour for symbols that have none of their own
            var dark = new ColorF { R = 0.93f, G = 0.91f, B = 0.92f, A = 1 };
            var light = new ColorF { R = 0.12f, G = 0.1f, B = 0.11f, A = 1 };
            Check(((delegate* unmanaged[Stdcall]<IntPtr, ColorF*, IntPtr, IntPtr*, int>)Slot(target, 8))(target, &dark, IntPtr.Zero, &inkDark));
            Check(((delegate* unmanaged[Stdcall]<IntPtr, ColorF*, IntPtr, IntPtr*, int>)Slot(target, 8))(target, &light, IntPtr.Zero, &inkLight));
        }
        catch
        {
            foreach (IntPtr p in new[] { inkLight, inkDark, format, target, bitmap })
                if (p != IntPtr.Zero) Marshal.Release(p);
            throw;
        }
        c = new Canvas { Bitmap = bitmap, Target = target, Format = format, InkDark = inkDark, InkLight = inkLight };
        Canvases[px] = c;
        return c;
    }

    private static BitmapSource? Draw(string text, int px, bool dark)
    {
        var c = CanvasFor(px);
        // BeginDraw (48), Clear (47), DrawText (27) with ENABLE_COLOR_FONT (4), EndDraw (49)
        ((delegate* unmanaged[Stdcall]<IntPtr, void>)Slot(c.Target, 48))(c.Target);
        var clear = new ColorF();
        ((delegate* unmanaged[Stdcall]<IntPtr, ColorF*, void>)Slot(c.Target, 47))(c.Target, &clear);
        var rect = new RectF { Left = 0, Top = -px * 0.04f, Right = px, Bottom = px };
        IntPtr ink = dark ? c.InkDark : c.InkLight;
        fixed (char* s = text)
            ((delegate* unmanaged[Stdcall]<IntPtr, char*, uint, IntPtr, RectF*, IntPtr, int, int, void>)Slot(c.Target, 27))(c.Target, s, (uint)text.Length, c.Format, &rect, ink, 4 | 2 /* colour font, no snap */, 0);
        Check(((delegate* unmanaged[Stdcall]<IntPtr, ulong*, ulong*, int>)Slot(c.Target, 49))(c.Target, null, null));

        // IWICBitmapSource::CopyPixels (7)
        int stride = px * 4;
        var pixels = new byte[stride * px];
        fixed (byte* p = pixels)
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, byte*, int>)Slot(c.Bitmap, 7))(c.Bitmap, IntPtr.Zero, (uint)stride, (uint)pixels.Length, p));
        var src = BitmapSource.Create(px, px, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        src.Freeze();
        return src;
    }

    [DllImport("d2d1.dll")] private static extern int D2D1CreateFactory(int type, ref Guid iid, IntPtr options, out IntPtr factory);
    [DllImport("dwrite.dll")] private static extern int DWriteCreateFactory(int type, ref Guid iid, out IntPtr factory);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, int context, ref Guid iid, out IntPtr obj);
}
