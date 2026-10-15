using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// palette: Bloom Lens with 7 컬러 팔레트 and the palette made of a drawn poster (no screen, nothing personal) — the first
/// swatch picked, another picked (its gradients), 9 colours, 색 추출 중, 전체 복사's choices, a drawn app window's palette opened again
/// from the history (its white background once), the colour view with the palettes kept — at the default 760 × 476 and
/// at 1100 × 700, dark and light; the drawing bar with its palette button; the picture 전체 복사 › 팔레트 이미지 makes; and
/// palette.txt (each palette's colours, its HEX list and CSS variables, the same twice, the time taken; the drawing bar's
/// with the pen's stroke in it). Nothing is copied.
internal static partial class Captures
{
    private static async Task PaletteScreens(Host host, Launcher w, string dir)
    {
        string l = En ? "en" : "ko";
        var log = new StringBuilder();
        var app = AppMock(dir);
        var poster = Poster(dir);
        foreach (var (name, (px, pw, ph)) in new[] { ("poster", poster), ("app window", app) })
        {
            var clock = Stopwatch.StartNew();
            var palette = PaletteExtract.From(px, pw, ph);
            clock.Stop();
            bool same = palette.SequenceEqual(PaletteExtract.From(px.ToArray(), pw, ph));
            var colors = palette.ConvertAll(x => x.Color);
            log.AppendLine($"{name} {pw}×{ph}: {clock.Elapsed.TotalMilliseconds:0.0} ms, the same again: {same}");
            foreach (var c in palette) log.AppendLine($"  {ColorBook.Hex(c.Color)}  {c.Share:P1}");
            log.AppendLine(ColorBook.HexList(colors)).AppendLine(ColorBook.CssVariables(colors)).AppendLine();
        }
        await File.WriteAllTextAsync(Path.Combine(dir, $"{l}-palette.txt"), log.ToString());

        // the app window first (kept in the history), then the poster as the area the Lens menu is about
        host.SetLensForQa(new Ocr.ScreenRect(-32000, -32000, app.w, app.h), app.px);
        await host.LensDo("palette");
        var appPalette = host.Palette!.Saved;
        host.SetLensForQa(new Ocr.ScreenRect(-32000, -32000, poster.w, poster.h), poster.px);
        ColorBook.Add(Color.FromRgb(0xD1, 0x49, 0x5B));
        double width = w.Width, height = w.Height;
        foreach (var (sw, sh) in new[] { (760, 476), (1100, 700) })
        {
            w.Width = sw;
            w.Height = sh;
            foreach (string theme in new[] { "dark", "light" })
            {
                Theme.Apply(theme);
                string p = $"{l}-{theme}-{sw}x{sh}-";
                await Shot(w, dir, p + "p01-lens-menu", Mode.Lens, "", 800);
                await host.LensDo("palette"); // the same Lens copy: its palette stays one entry in the history
                await Task.Delay(400);
                await Snap(w, dir, p + "p02-palette");
                host.PickSwatch(2, false);
                await Snap(w, dir, p + "p03-picked");
                await host.SetPaletteCount(9);
                await Snap(w, dir, p + "p04-nine");
                host.Palette!.Busy = true; // how a slow making-again looks (a big Lens copy read the first time)
                w.Refresh(true);
                await Snap(w, dir, p + "p04b-busy");
                host.Palette.Busy = false;
                await host.SetPaletteCount(PaletteExtract.DefaultCount);
                w.Refresh(true, "palette:copy");
                w.RunSelected(null); // 전체 복사's choices; none is run
                await Snap(w, dir, p + "p05-copy-all");
                w.CloseActionsForQa();
                host.OpenPalette(appPalette); // from the history: its Lens copy makes it again
                await Task.Delay(400);
                await Snap(w, dir, p + "p06-app-window");
                await Shot(w, dir, p + "p07-color-view", Mode.Color, "", 600);
                w.ScrollForQa(5000); // its foot: 최근 팔레트
                await Snap(w, dir, p + "p07b-color-view-palettes");
            }
        }
        // the most swatches (10) at the least width: their HEX and RGB still fit
        w.Width = 760;
        w.Height = 476;
        Theme.Apply("dark");
        int[] board = { 0xFFFFFF, 0xF6EFE4, 0xD1495B, 0xF2A541, 0xC4D5D2, 0x8EE6E0, 0xD9C8FF, 0xFFB347, 0x9CC58A, 0xB86BD9, 0xFDD835, 0x3B82F6 };
        var many = Drawn(dir, "palette-board.png", 390, 120, dc =>
        {
            for (int i = 0, x = 0; i < board.Length; x += (board.Length - i) * 5, i++) dc.DrawRectangle(Fill(board[i]), null, new Rect(x, 0, (board.Length - i) * 5, 120));
        });
        host.SetLensForQa(new Ocr.ScreenRect(-32000, -32000, many.w, many.h), many.px);
        await host.LensDo("palette");
        await Task.Delay(400);
        await host.SetPaletteCount(PaletteExtract.MaxCount);
        await Snap(w, dir, $"{l}-dark-760x476-p10-ten");
        w.Width = width;
        w.Height = height;

        // 전체 복사 › 팔레트 이미지: the picture itself, written to a file instead of the clipboard
        host.SetLensForQa(new Ocr.ScreenRect(-32000, -32000, poster.w, poster.h), poster.px);
        await host.LensDo("palette");
        Png(PaletteViews.Picture(host.Palette!.Colors.ConvertAll(x => x.Color)), Path.Combine(dir, $"{l}-p08-palette-image.png"));

        // 화면 캡처's drawing bar: the palette button between AI and 저장
        var shot = BitmapSource.Create(app.w, app.h, 96, 96, PixelFormats.Bgra32, null, app.px, app.w * 4);
        var canvas = new Canvas { Width = 900, Height = 420, Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x4A, 0x63)) };
        var sel = new Rect(120, 40, app.w, app.h);
        var image = new System.Windows.Controls.Image { Source = shot, Width = app.w, Height = app.h };
        Canvas.SetLeft(image, sel.X);
        Canvas.SetTop(image, sel.Y);
        canvas.Children.Add(image);
        var layer = new Ocr.DrawLayer(canvas, sel, new Size(900, 420), _ => { });
        layer.AddForQa("pen", 0, new Point(250, 214), new Point(300, 196), new Point(350, 214));
        canvas.Measure(new Size(900, 420));
        canvas.Arrange(new Rect(0, 0, 900, 420));
        canvas.UpdateLayout();
        await Task.Delay(50);
        var rtb = new RenderTargetBitmap(1350, 630, 144, 144, PixelFormats.Pbgra32);
        rtb.Render(canvas);
        Png(rtb, Path.Combine(dir, $"{l}-p09-draw-bar.png"));
        // the bar's palette is made of the picture with the ink in it (the red stroke)
        var inked = PaletteExtract.From(layer.Merge(app.px, app.w, app.h), app.w, app.h, 8);
        await File.AppendAllTextAsync(Path.Combine(dir, $"{l}-palette.txt"), "app window with the pen's stroke, 8 asked: " + string.Join(" ", inked.Select(x => $"{ColorBook.Hex(x.Color)} {x.Share:P1}")) + "\n");
    }

    /// A small poster drawn as the picked area: paper, a sun, two hills, a house — flat colours with soft edges.
    private static (byte[] px, int w, int h) Poster(string dir)
    {
        const int W = 480, H = 300;
        return Drawn(dir, "palette-poster.png", W, H, dc =>
        {
            dc.DrawRectangle(Fill(0xF6EFE4), null, new Rect(0, 0, W, H));
            dc.DrawEllipse(Fill(0xF2A541), null, new Point(352, 104), 58, 58);
            dc.DrawGeometry(Fill(0x7FB7BE), null, Geometry.Parse("M0,206 C120,150 250,168 480,186 L480,300 L0,300 Z"));
            dc.DrawGeometry(Fill(0x3C6E71), null, Geometry.Parse("M0,248 C150,204 300,226 480,236 L480,300 L0,300 Z"));
            dc.DrawRectangle(Fill(0xD1495B), null, new Rect(92, 176, 78, 58));
            dc.DrawGeometry(Fill(0x2E294E), null, Geometry.Parse("M84,178 L131,140 L178,178 Z"));
            dc.DrawRectangle(Fill(0xEDAE49), null, new Rect(120, 200, 20, 34));
        });
    }

    /// A plain app window drawn as an area: white, a light side panel, dark and grey lines of text, a blue button, a pink badge.
    private static (byte[] px, int w, int h) AppMock(string dir)
    {
        const int W = 420, H = 260;
        return Drawn(dir, "palette-app.png", W, H, dc =>
        {
            dc.DrawRectangle(Fill(0xFFFFFF), null, new Rect(0, 0, W, H));
            dc.DrawRectangle(Fill(0xE9ECEF), null, new Rect(0, 0, 104, H));
            for (int i = 0; i < 5; i++) dc.DrawRoundedRectangle(Fill(0xADB5BD), null, new Rect(16, 24 + i * 24, 72, 8), 4, 4);
            dc.DrawRoundedRectangle(Fill(0x343A40), null, new Rect(128, 24, 200, 14), 4, 4);
            for (int i = 0; i < 4; i++) dc.DrawRoundedRectangle(Fill(0x868E96), null, new Rect(128, 56 + i * 20, 250 - i * 30, 8), 4, 4);
            dc.DrawRoundedRectangle(Fill(0x3B82F6), null, new Rect(128, 196, 116, 36), 8, 8);
            dc.DrawEllipse(Fill(0xF06595), null, new Point(388, 32), 14, 14);
        });
    }

    private static (byte[] px, int w, int h) Drawn(string dir, string file, int width, int height, System.Action<DrawingContext> draw)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) draw(dc);
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var px = new byte[width * height * 4];
        rtb.CopyPixels(px, width * 4, 0); // opaque, so premultiplied = straight
        Png(rtb, Path.Combine(dir, file));
        return (px, width, height);
    }

    private static SolidColorBrush Fill(int rgb) => new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
}
