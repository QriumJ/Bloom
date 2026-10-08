using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bloom.App;

/// QA (BLOOM_DESIGN_CHECK=<report file>): every QA picture is also measured. Each small filled box — chip, pill,
/// keycap, button, single-line bubble — has the ink of its text or glyph found in the rendered pixels, and a box whose
/// ink sits off its middle by 1 dip or more (up/down; 1.5 dip sideways in narrow boxes) is written to the report.
/// Every text is also measured against its box: CUT = wider or taller than its box with no ellipsis (it spills or is
/// cut off), TRIM = shortened with an ellipsis (fine for paths, worth a look for names).
internal static class DesignCheck
{
    private static readonly string? Report = Environment.GetEnvironmentVariable("BLOOM_DESIGN_CHECK");

    public static bool On => Report is not null;

    /// `root` was drawn into `bmp` at `scale` pixels per dip.
    public static void Look(FrameworkElement root, BitmapSource bmp, double scale, string picture)
    {
        if (Report is null) return;
        try
        {
            int w = bmp.PixelWidth, h = bmp.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            bmp.CopyPixels(px, stride, 0);
            var lines = new List<string>();
            Walk(root, root, px, w, h, scale, Path.GetFileName(picture), lines);
            if (lines.Count > 0) File.AppendAllLines(Report, lines);
        }
        catch (Exception e)
        {
            File.AppendAllText(Report, $"{Path.GetFileName(picture)}\terror {e.Message}\n");
        }
    }

    private static void Walk(DependencyObject d, FrameworkElement root, byte[] px, int w, int h, double scale, string picture, List<string> lines)
    {
        if (d is Border b && b.IsVisible && Filled(b) && b.Child is FrameworkElement child && TextLike(child)
            && b.ActualHeight is >= 12 and <= 64 && b.ActualWidth >= 12)
            Measure(b, child, root, px, w, h, scale, picture, lines);
        if (d is TextBlock tb && tb.IsVisible && tb.Text.Length > 0 && tb.ActualWidth > 0 && Fits(tb, root) is { } note) lines.Add($"{picture}\t{note}");
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i), root, px, w, h, scale, picture, lines);
    }

    /// Null when the text fits its box; else CUT / CUT-V / TRIM with what it would need.
    private static string? Fits(TextBlock tb, FrameworkElement root)
    {
        if (tb.FontFamily.Source.Contains("Icons", StringComparison.Ordinal)) return null; // icon glyphs
        double dip = VisualTreeHelper.GetDpi(tb).PixelsPerDip;
        string plain = tb.Text.Replace("\u2060", ""); // the word joiners Hangul.KeepAll adds draw nothing
        var ft = new FormattedText(plain, System.Globalization.CultureInfo.CurrentUICulture, tb.FlowDirection, new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch), tb.FontSize, Brushes.Black, dip);
        bool wraps = tb.TextWrapping != TextWrapping.NoWrap;
        if (wraps) ft.MaxTextWidth = Math.Max(1, tb.ActualWidth - tb.Padding.Left - tb.Padding.Right + 0.5); // a box sized to its text exactly
        if (!double.IsNaN(tb.LineHeight)) ft.LineHeight = tb.LineHeight;
        double needW = ft.WidthIncludingTrailingWhitespace + tb.Padding.Left + tb.Padding.Right, needH = ft.Height + tb.Padding.Top + tb.Padding.Bottom;
        string where;
        try
        {
            var r = tb.TransformToAncestor(root).TransformBounds(new Rect(0, 0, tb.ActualWidth, tb.ActualHeight));
            if (r.Right <= 0 || r.Bottom <= 0 || r.X >= root.ActualWidth || r.Y >= root.ActualHeight) return null; // scrolled out of sight
            where = $"at {r.X:0},{r.Y:0}";
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        string text = tb.Text.Replace('\n', ' ');
        if (text.Length > 40) text = text[..40] + "…";
        if (!wraps && needW > tb.ActualWidth + 1)
            return $"{(tb.TextTrimming == TextTrimming.None ? "CUT" : "TRIM")}\t{text}\tbox {tb.ActualWidth:0}x{tb.ActualHeight:0} needs {needW:0}\t{where}";
        if (wraps && needH > tb.ActualHeight + 1)
            return $"{(tb.TextTrimming == TextTrimming.None ? "CUT-V" : "TRIM")}\t{text}\tbox {tb.ActualWidth:0}x{tb.ActualHeight:0} needs h {needH:0}\t{where}";
        return null;
    }

    private static bool Filled(Border b) =>
        b.Background is SolidColorBrush { Color.A: > 8 } || (b.BorderBrush is SolidColorBrush { Color.A: > 20 } && b.BorderThickness.Top > 0);

    /// A TextBlock, or a row of glyphs and text (an icon and its label).
    private static bool TextLike(FrameworkElement el) =>
        el is TextBlock { Text.Length: > 0 } || (el is Panel p && p.Children.Count is > 0 and <= 3 && p.Children.OfType<UIElement>().All(c => c is TextBlock || c.Visibility != Visibility.Visible));

    private static void Measure(Border b, FrameworkElement child, FrameworkElement root, byte[] px, int w, int h, double scale, string picture, List<string> lines)
    {
        Rect r;
        try
        {
            r = b.TransformToAncestor(root).TransformBounds(new Rect(0, 0, b.ActualWidth, b.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            return;
        }
        int x0 = (int)Math.Round(r.X * scale), y0 = (int)Math.Round(r.Y * scale), x1 = (int)Math.Round(r.Right * scale), y1 = (int)Math.Round(r.Bottom * scale);
        if (x0 < 0 || y0 < 0 || x1 > w || y1 > h || x1 - x0 < 8 || y1 - y0 < 8) return; // clipped or off the picture
        int bt = (int)Math.Ceiling(b.BorderThickness.Top * scale), bb = (int)Math.Ceiling(b.BorderThickness.Bottom * scale);
        int bl = (int)Math.Ceiling(b.BorderThickness.Left * scale), br = (int)Math.Ceiling(b.BorderThickness.Right * scale);
        int yMid = (y0 + y1) / 2, xMid = (x0 + x1) / 2;
        int padL = Math.Max(bl + 1, (int)(b.Padding.Left * scale / 2)), padR = Math.Max(br + 1, (int)(b.Padding.Right * scale / 2));
        var samples = new[] { (x0 + padL, yMid), (x1 - 1 - padR, yMid), (xMid, y0 + bt + 1), (xMid, y1 - 1 - bb - 1) };
        var bg = samples.Select(s => Pixel(px, w, s.Item1, s.Item2)).OrderBy(c => c.r + c.g + c.b).ElementAt(1); // the darker middle of four
        int radius = (int)Math.Min(Math.Max(b.CornerRadius.TopLeft, b.CornerRadius.BottomLeft) * scale, (y1 - y0) / 2.0);
        int top = int.MaxValue, bottom = -1, left = int.MaxValue, right = -1;
        for (int y = y0 + bt; y < y1 - bb; y++)
            for (int x = x0 + Math.Max(bl, radius / 2); x < x1 - Math.Max(br, radius / 2); x++)
            {
                var c = Pixel(px, w, x, y);
                if (Math.Abs(c.r - bg.r) + Math.Abs(c.g - bg.g) + Math.Abs(c.b - bg.b) < 90) continue;
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
                left = Math.Min(left, x);
                right = Math.Max(right, x);
            }
        if (bottom < 0) return;
        double over = (top - (y0 + bt)) / scale, under = ((y1 - bb) - 1 - bottom) / scale;
        double before = (left - (x0 + bl)) / scale, after = ((x1 - br) - 1 - right) / scale;
        double v = (over - under) / 2, s = (before - after) / 2;
        bool sideways = (b.ActualWidth <= 140 || child.HorizontalAlignment == HorizontalAlignment.Center) && Math.Abs(s) >= 1.5;
        if (Math.Abs(v) < 1 && !sideways) return;
        string text = string.Join(" ", (child is TextBlock t ? new[] { t } : ((Panel)child).Children.OfType<TextBlock>()).Select(t => t.Text)).Replace('\n', ' ');
        if (text.Length > 30) text = text[..30] + "…";
        lines.Add($"{picture}\t{text}\t{b.ActualWidth:0}x{b.ActualHeight:0}\tv {v:+0.0;-0.0;0}\th {s:+0.0;-0.0;0}\tover {over:0.0} under {under:0.0} before {before:0.0} after {after:0.0}\tpx {x0},{y0},{x1 - x0},{y1 - y0}");
    }

    private static (int r, int g, int b) Pixel(byte[] px, int w, int x, int y)
    {
        int i = (y * w + x) * 4;
        return (px[i + 2], px[i + 1], px[i]);
    }
}
