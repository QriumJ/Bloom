using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.Tests;

/// 컬러 팔레트: the colours taken from a picture (Core\PaletteExtract.cs), the texts 전체 복사 makes and the palettes kept.
internal static class PaletteExtractTests
{
    public static void KnownBlocksComeBackInShareOrder()
    {
        // 400 × 200, so the grid looks at every 3rd point: 40 % red, 30 % blue, 20 % green, 10 % yellow, drawn in another order
        var px = Stripes(200, out int w, (80, 0x43A047), (160, 0xE53935), (40, 0xFDD835), (120, 0x1E88E5));
        var p = PaletteExtract.From(px, w, 200);
        Check.Eq(Hexes(p), "#E53935 #1E88E5 #43A047 #FDD835", "the blocks' colours, the largest first");
        double[] shares = { 0.4, 0.3, 0.2, 0.1 };
        for (int i = 0; i < shares.Length; i++) Check.Near(p[i].Share, shares[i], 0.02, $"{Hexes(p.Skip(i).Take(1))}'s share");
        Check.Near(p.Sum(x => x.Share), 1, 1e-9, "the shares cover the picture");
    }

    public static void CountStaysWithinThreeToTen()
    {
        int[] twelve = { 0x000000, 0xFFFFFF, 0xE53935, 0x43A047, 0x1E88E5, 0xFDD835, 0x00BCD4, 0xD81B60, 0xFB8C00, 0x8E24AA, 0x808080, 0x6D4C41 };
        var px = Stripes(4, out int w, twelve.Select((rgb, i) => (12 - i, rgb)).ToArray());
        string First(int n) => string.Join(" ", twelve.Take(n).Select(rgb => ColorBook.Hex(C(rgb))));
        Check.Eq(PaletteExtract.DefaultCount, 5, "five colours by default");
        Check.Eq(new SavedPalette().Count, 5, "… and a kept palette that says no count has five");
        Check.Eq(Hexes(PaletteExtract.From(px, w, 4)), First(5), "5 by default, the largest ones");
        Check.Eq(Hexes(PaletteExtract.Of(PaletteExtract.Sample(px, w, 4))), First(5), "5 by default from the points too");
        Check.Eq(Hexes(PaletteExtract.From(px, w, 4, 8)), First(8), "8 asked");
        Check.Eq(Hexes(PaletteExtract.From(px, w, 4, 2)), First(3), "fewer than 3 asked: 3");
        Check.Eq(Hexes(PaletteExtract.From(px, w, 4, 99)), First(10), "more than 10 asked: 10");
    }

    public static void SamePictureSamePalette()
    {
        // a photo-like picture: gradients across and down, noise from a fixed seed
        const int W = 320, H = 200;
        var rnd = new Random(7);
        var px = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * 4;
                px[i] = Noise(200 - x * 120 / W, rnd);
                px[i + 1] = Noise(60 + y * 150 / H, rnd);
                px[i + 2] = Noise(40 + x * 180 / W, rnd);
                px[i + 3] = 255;
            }
        var a = PaletteExtract.From(px, W, H);
        var b = PaletteExtract.From(px.ToArray(), W, H);
        Check.Eq(a.Count, PaletteExtract.DefaultCount, "a photo has five colours to give");
        Check.True(a.SequenceEqual(b), $"the same pixels give the same palette, shares and all: {Hexes(a)} / {Hexes(b)}");
        Check.True(a.Zip(a.Skip(1)).All(x => x.First.Share >= x.Second.Share), "the largest share first");
        Check.Eq(Hexes(PaletteExtract.From(px, W, H, 9)), Hexes(PaletteExtract.From(px, W, H, 9)), "the same with another count");
    }

    public static void NearDuplicatesBecomeOne()
    {
        // #FF5A8A and #F55A8A are one pink to the eye; a navy beside them
        Check.True(PaletteExtract.Distance(C(0xFF5A8A), C(0xF55A8A)) < PaletteExtract.Merge, "the two pinks are near-duplicates");
        var px = Stripes(10, out int w, (35, 0xFF5A8A), (40, 0x2E3A59), (25, 0xF55A8A));
        var p = PaletteExtract.From(px, w, 10);
        Check.Eq(Hexes(p), "#FF5A8A #2E3A59", "the pinks are one swatch, in the larger one's colour, before the navy");
        Check.Near(p[0].Share, 0.6, 1e-9, "the merged pink has both shares");
    }

    public static void DominantBackgroundTakesOnePlace()
    {
        // a page: #FAFAFA (62 %), a #E6E6E6 panel (16 %, one of its shades), red, blue and green (10, 7 and 5 %)
        var px = Stripes(20, out int w, (62, 0xFAFAFA), (10, 0xE53935), (16, 0xE6E6E6), (7, 0x1E88E5), (5, 0x43A047));
        Check.True(PaletteExtract.Distance(C(0xFAFAFA), C(0xE6E6E6)) is > PaletteExtract.Merge and < PaletteExtract.Shade, "the panel is a shade of the background, no duplicate");
        Check.Eq(Hexes(PaletteExtract.From(px, w, 20, 4)), "#FAFAFA #E53935 #1E88E5 #43A047", "the background once, then the colours, though its shade is larger");
        Check.Eq(Hexes(PaletteExtract.From(px, w, 20, 3)), "#FAFAFA #E53935 #1E88E5", "the fewest: still the background once");
        Check.Eq(Hexes(PaletteExtract.From(px, w, 20, 6)), "#FAFAFA #E6E6E6 #E53935 #1E88E5 #43A047", "room left: the shade comes back, in its place by share");
    }

    public static void TinyAndSingleColourPictures()
    {
        Check.Eq(Hexes(PaletteExtract.From(new byte[] { 0x56, 0x34, 0x12, 0xFF }, 1, 1)), "#123456", "one pixel: its colour");
        var one = PaletteExtract.From(Fill(7, 5, 0x3B82F6), 7, 5);
        Check.Eq(Hexes(one), "#3B82F6", "one colour: one swatch, not five");
        Check.Near(one[0].Share, 1, 1e-9, "all of the picture");
        Check.Eq(PaletteExtract.From(Array.Empty<byte>(), 0, 0).Count, 0, "nothing to look at");
        Check.Eq(PaletteExtract.From(Fill(6, 6, 0xFF0000, 0), 6, 6).Count, 0, "all transparent: no colours");
        var half = Fill(10, 4, 0x22C55E);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 5; x++) Array.Clear(half, (y * 10 + x) * 4, 4); // transparent black
        var h = PaletteExtract.From(half, 10, 4);
        Check.Eq(Hexes(h), "#22C55E", "transparent pixels are not part of the picture");
        Check.Near(h[0].Share, 1, 1e-9, "the share is of the opaque half");
        var speck = Fill(200, 100, 0xFFFFFF);
        int at = (1 * 200 + 1) * 4; // a point the grid looks at
        (speck[at], speck[at + 1], speck[at + 2]) = (0, 0, 255);
        Check.Eq(Hexes(PaletteExtract.From(speck, 200, 100)), "#FFFFFF", "a one-pixel speck is no palette colour");
        bool threw = false;
        try
        {
            PaletteExtract.From(new byte[8], 2, 2);
        }
        catch (ArgumentException)
        {
            threw = true;
        }
        Check.True(threw, "fewer bytes than the size needs");
    }

    public static void ThinPicturesAreLookedAt()
    {
        // 32768 points in one row or one column: the grid takes every 2nd, from the first row (or column), not past it
        var row = Fill(32768, 1, 0x3B82F6);
        Check.Eq(Hexes(PaletteExtract.From(row, 32768, 1)), "#3B82F6", "a picture one pixel tall");
        Check.Eq(Hexes(PaletteExtract.From(row, 1, 32768)), "#3B82F6", "a picture one pixel wide");
        var halves = Stripes(1, out int w, (16384, 0xE53935), (16384, 0x43A047));
        var p = PaletteExtract.From(halves, w, 1);
        Check.Eq(Hexes(p.OrderBy(x => x.Color.R)), "#43A047 #E53935", "both halves of a thin picture");
        Check.Near(p[0].Share, 0.5, 0.001, "each half of it");
        Check.Eq(PaletteExtract.Sample(Fill(40000, 2, 0xFFFFFF), 40000, 2).Length / 4, 13333, "two rows: every 3rd point of the second");
    }

    public static void PointsFromTheLensCopyAreThePixels()
    {
        // a 386 × 299 picture with a transparent corner (the grid looks at every 3rd point): its PNG read a row at a time
        // gives the points its pixels give, so a palette made again from the copy is the one made from the area
        var px = Stripes(299, out int w, (101, 0xFAFAFA), (120, 0x2E3A59), (90, 0xFF5A8A), (75, 0x00AA55));
        for (int y = 0; y < 40; y++) Array.Clear(px, y * w * 4, 60 * 4);
        string png = Path.Combine(Path.GetTempPath(), "bloom-palette-test-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, 299, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
            using (var fs = File.Create(png)) enc.Save(fs);
            var points = Check.NotNull(Host.ReadPoints(png), "the copy reads");
            Check.True(points.AsSpan().SequenceEqual(PaletteExtract.Sample(px, w, 299)), "the copy's points are the pixels' points");
            Check.True(PaletteExtract.Of(points, 4).SequenceEqual(PaletteExtract.From(px, w, 299, 4)), "the same palette, shares and all");
            Check.Eq(Hexes(PaletteExtract.Of(points)), "#2E3A59 #FAFAFA #FF5A8A #00AA55", "its colours, the transparent corner left out");
        }
        finally
        {
            File.Delete(png);
        }
        Check.True(Host.ReadPoints(png) is null, "a copy that is gone: nothing to make it again from");
        Check.True(Host.ReadPoints(null) is null, "no copy");
    }

    public static void ColorsJsonWithNullsLoadsWhatReads()
    {
        var before = ColorBook.Current;
        var saver = typeof(ColorBook).GetField("saver", BindingFlags.Static | BindingFlags.NonPublic)!;
        var priorSaver = saver.GetValue(null);
        using var data = new SettingsTransferTests.DataFolder();
        try
        {
            File.WriteAllText(Paths.In("colors.json"), """{ "recent": null, "palettes": null }""");
            ColorBook.Load();
            Check.Eq(ColorBook.Current.Recent.Count + ColorBook.Current.Palettes.Count, 0, "lists given as null: empty");
            Check.True(ColorBook.Now is null, "no colour now");
            File.WriteAllText(Paths.In("colors.json"), """
                {
                  "recent": [null, "#3b82f6", "nonsense"],
                  "palettes": [
                    null,
                    { "colors": null, "shares": [1] },
                    { "colors": ["zzz", null], "shares": [0.5, 0.5] },
                    { "colors": ["#ff5a8a", null, "nonsense", "#2E3A59"], "shares": [0.6, 0.1], "count": 99, "source": "lens-1.png" },
                    { "colors": ["rgb(0, 170, 85)"], "shares": null, "count": 0 }
                  ]
                }
                """);
            ColorBook.Load();
            var book = ColorBook.Current;
            Check.Eq(string.Join(" ", book.Recent), "#3b82f6", "recent colours: only those that read");
            Check.Eq(book.Palettes.Count, 2, "palettes: null ones and those with no colour left out");
            var first = book.Palettes[0];
            Check.Eq(string.Join(" ", first.Colors), "#FF5A8A #2E3A59", "its colours that read");
            Check.Eq(first.Shares.Count, 2, "a share for each");
            Check.Near(first.Shares[0], 0.6, 1e-9, "its own share");
            Check.Near(first.Shares[1], 0, 1e-9, "no share given: 0");
            Check.Eq(first.Count, PaletteExtract.MaxCount, "count kept to 3–10");
            Check.Eq(string.Join(" ", book.Palettes[1].Colors) + " " + book.Palettes[1].Count, "#00AA55 3", "no shares; count kept to 3");
            // the views that show them: no exception, every palette kept listed
            var view = new Engine(new Host(new Options())).PaletteView();
            Check.Eq(view.SelectMany(s => s.Items).Count(i => i.Id.StartsWith("palette:saved:", StringComparison.Ordinal)), 2, "최근 팔레트 lists both");
        }
        finally
        {
            (saver.GetValue(null) as IDisposable)?.Dispose();
            saver.SetValue(null, priorSaver);
            typeof(ColorBook).GetProperty(nameof(ColorBook.Current))!.SetValue(null, before);
        }
    }

    public static void FailedCopiesSaySo() => LauncherPreviewTests.WithLauncher(window =>
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var host = (Host)typeof(Launcher).GetField("host", flags)!.GetValue(window)!;
        var toast = (TextBlock)typeof(Launcher).GetField("toastText", flags)!.GetValue(window)!;
        var saved = new SavedPalette { Colors = { "#FF5A8A", "#2E3A59" }, Shares = { 0.6, 0.4 } };
        typeof(Host).GetProperty(nameof(Host.Palette))!.SetValue(host, new PaletteShow { Saved = saved, Colors = saved.Read() });
        const string Failed = "클립보드에 복사하지 못했습니다. 다시 시도하세요.";
        var prior = Launch.SetClipboard;
        int tries = 0;
        object? put = null;
        try
        {
            // another app keeps the clipboard: every copy says it failed, after a few tries
            Launch.SetClipboard = _ =>
            {
                tries++;
                throw Marshal.GetExceptionForHR(unchecked((int)0x800401D0))!; // CLIPBRD_E_CANT_OPEN, as Windows gives it
            };
            host.PickSwatch(1, true);
            Check.Eq(toast.Text, Failed, "a swatch clicked");
            Check.Eq(tries, 4, "tried a few times first");
            var items = host.Engine.PaletteView().SelectMany(s => s.Items).ToList();
            toast.Text = "";
            Check.NotNull(items.First(i => i.Id == "palette:color:#2E3A59").Run, "the picked colour's row")();
            Check.Eq(toast.Text, Failed, "the picked colour's HEX (Enter)");
            var choices = Check.NotNull(items.First(i => i.Id == "palette:copy").MoreActions, "전체 복사's choices")();
            Check.Eq(choices.Count, 3, "HEX list, CSS variables, palette image");
            foreach (var choice in choices)
            {
                toast.Text = "";
                choice.Run();
                Check.Eq(toast.Text, Failed, choice.Title);
            }
            // free again: copied, and said so
            Launch.SetClipboard = d => put = d;
            host.PickSwatch(0, true);
            Check.Eq(toast.Text, "#FF5A8A 복사 완료", "a swatch copied");
            Check.Eq(put, "#FF5A8A", "its HEX on the clipboard");
            choices[1].Run();
            Check.Eq(put, "--color-1: #FF5A8A;\n--color-2: #2E3A59;", "the CSS variables on the clipboard");
            Check.Eq(toast.Text, "CSS 변수 2개 복사 완료", "and said so");
        }
        finally
        {
            Launch.SetClipboard = prior;
        }
    });

    public static void CopyFormats()
    {
        var colors = new[] { C(0xFF5A8A), C(0x2E3A59), C(0x00AA55) };
        Check.Eq(ColorBook.HexList(colors), "#FF5A8A\n#2E3A59\n#00AA55", "HEX list: one a line");
        Check.Eq(ColorBook.CssVariables(colors), "--color-1: #FF5A8A;\n--color-2: #2E3A59;\n--color-3: #00AA55;", "CSS variables, numbered from 1");
        Check.Eq(ColorBook.HexList(Array.Empty<Color>()), "", "no colours: no text");
    }

    public static void PalettesKeptAreBoundedAndOnePerPicture()
    {
        var before = ColorBook.Current.Palettes.ToList();
        ColorBook.Current.Palettes.Clear();
        try
        {
            for (int i = 0; i < ColorBook.PalettesKept + 3; i++) ColorBook.AddPalette(new SavedPalette { Source = $"lens-{i}.png", Colors = { "#FF5A8A" }, Shares = { 1 } });
            Check.Eq(ColorBook.Current.Palettes.Count, ColorBook.PalettesKept, "only the newest are kept");
            Check.Eq(ColorBook.Current.Palettes[0].Source, $"lens-{ColorBook.PalettesKept + 2}.png", "the newest first");
            ColorBook.AddPalette(new SavedPalette { Source = "lens-5.png", Colors = { "#2E3A59", "nonsense" }, Shares = { 0.7, 0.3 } });
            Check.Eq(ColorBook.Current.Palettes.Count(x => x.Source == "lens-5.png"), 1, "made again from the same picture: one entry");
            Check.Eq(ColorBook.Current.Palettes.Count, ColorBook.PalettesKept, "… none added");
            var read = ColorBook.Current.Palettes[0].Read();
            Check.Eq(Hexes(read), "#2E3A59", "first, read back without what isn't a colour");
            Check.Near(read[0].Share, 0.7, 1e-9, "with its share");
        }
        finally
        {
            ColorBook.Current.Palettes.Clear();
            ColorBook.Current.Palettes.AddRange(before);
        }
    }

    private static Color C(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static string Hexes(IEnumerable<PaletteColor> p) => string.Join(" ", p.Select(x => ColorBook.Hex(x.Color)));

    private static byte Noise(int v, Random rnd) => (byte)Math.Clamp(v + rnd.Next(-12, 13), 0, 255);

    /// `width` × `height` BGRA pixels all of one colour.
    private static byte[] Fill(int width, int height, int rgb, byte alpha = 255)
    {
        var px = new byte[width * height * 4];
        for (int i = 0; i < px.Length; i += 4) (px[i], px[i + 1], px[i + 2], px[i + 3]) = ((byte)rgb, (byte)(rgb >> 8), (byte)(rgb >> 16), alpha);
        return px;
    }

    /// Opaque vertical stripes, left to right, `height` tall; `width` is their widths added up.
    private static byte[] Stripes(int height, out int width, params (int width, int rgb)[] stripes)
    {
        width = stripes.Sum(s => s.width);
        var px = new byte[width * height * 4];
        int x0 = 0;
        foreach (var (sw, rgb) in stripes)
        {
            for (int y = 0; y < height; y++)
                for (int x = x0; x < x0 + sw; x++)
                {
                    int i = (y * width + x) * 4;
                    (px[i], px[i + 1], px[i + 2], px[i + 3]) = ((byte)rgb, (byte)(rgb >> 8), (byte)(rgb >> 16), 255);
                }
            x0 += sw;
        }
        return px;
    }
}
