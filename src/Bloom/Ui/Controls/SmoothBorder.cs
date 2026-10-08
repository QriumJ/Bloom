using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Bloom.Ui;

/// A Border whose rounded corners have continuous curvature (the "squircle" of iOS / macOS and Figma's corner
/// smoothing): the edge eases into the curve instead of meeting a circular arc at a kink. Layout, padding and
/// hit-testing are Border's own; only the drawing differs. CornerRadius keeps its meaning (the visible radius).
internal class SmoothBorder : Border
{
    /// 0 = plain circular corners, 0.6 = the iOS look.
    public const double Smoothing = 0.6;

    private Geometry? fill, stroke;
    private Size builtFor;
    private CornerRadius builtRadius;
    private double builtThickness = -1;

    protected override void OnRender(DrawingContext dc)
    {
        var size = new Size(ActualWidth, ActualHeight);
        if (size.Width <= 0 || size.Height <= 0) return;
        var cr = CornerRadius;
        if (cr.TopLeft + cr.TopRight + cr.BottomRight + cr.BottomLeft <= 0)
        {
            base.OnRender(dc);
            return;
        }
        double t = BorderBrush is null ? 0 : BorderThickness.Left;
        if (fill is null || size != builtFor || CornerRadius != builtRadius || t != builtThickness)
        {
            fill = Shape(new Rect(size), CornerRadius, 0);
            stroke = t > 0 ? Shape(new Rect(t / 2, t / 2, Math.Max(0, size.Width - t), Math.Max(0, size.Height - t)), CornerRadius, t / 2) : null;
            builtFor = size;
            builtRadius = CornerRadius;
            builtThickness = t;
        }
        if (Background is { } bg) dc.DrawGeometry(bg, null, fill);
        if (stroke is not null && BorderBrush is { } line) dc.DrawGeometry(null, new Pen(line, t), stroke);
    }

    private static readonly Dictionary<(double w, double h, double tl, double tr, double br, double bl), Geometry> Cache = new();

    /// The outline of `r` with smoothed corners (each corner its own radius, less `inset` for a stroke drawn inside).
    public static Geometry Shape(Rect r, CornerRadius radius, double inset = 0)
    {
        double tl = Math.Max(0, radius.TopLeft - inset), tr = Math.Max(0, radius.TopRight - inset), br = Math.Max(0, radius.BottomRight - inset), bl = Math.Max(0, radius.BottomLeft - inset);
        if (tl + tr + br + bl <= 0) return new RectangleGeometry(r);
        var key = (r.Width, r.Height, tl, tr, br, bl);
        lock (Cache)
            if (r.X == 0 && r.Y == 0 && Cache.TryGetValue(key, out var hit)) return hit;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double w = r.Width, h = r.Height, x = r.X, y = r.Y;
            // each corner: where it starts, the direction along the edge (u) and toward the next edge (v)
            var corners = new (Point start, Vector u, Vector v, double radius)[]
            {
                (new Point(x, y + Reach(tl, w, h)), new Vector(0, -1), new Vector(1, 0), tl),
                (new Point(x + w - Reach(tr, w, h), y), new Vector(1, 0), new Vector(0, 1), tr),
                (new Point(x + w, y + h - Reach(br, w, h)), new Vector(0, 1), new Vector(-1, 0), br),
                (new Point(x + Reach(bl, w, h), y + h), new Vector(-1, 0), new Vector(0, -1), bl),
            };
            c.BeginFigure(corners[0].start, true, true);
            foreach (var (start, u, v, rad) in corners)
            {
                c.LineTo(start, true, false);
                Corner(c, start, u, v, rad, w, h);
            }
        }
        g.Freeze();
        lock (Cache)
            if (r.X == 0 && r.Y == 0 && Cache.Count < 512) Cache[key] = g;
        return g;
    }

    /// How far along each edge a corner reaches (the radius grown by the smoothing, kept inside half the box).
    private static double Reach(double radius, double w, double h)
    {
        if (radius <= 0) return 0;
        var (_, p) = Fit(radius, w, h);
        return p;
    }

    /// The radius and smoothing a corner can have in this box: a pill's ends stay round (no room to smooth).
    private static (double s, double p) Fit(double radius, double w, double h)
    {
        double max = Math.Min(w, h) / 2;
        radius = Math.Min(radius, max);
        double s = Math.Clamp(Math.Min(Smoothing, max / radius - 1), 0, Smoothing);
        return (s, Math.Min((1 + s) * radius, max));
    }

    /// One corner as Figma draws it: a cubic easing off the edge, a circular arc in the middle, a cubic onto the next edge.
    private static void Corner(StreamGeometryContext c, Point start, Vector u, Vector v, double radius, double w, double h)
    {
        if (radius <= 0) return;
        radius = Math.Min(radius, Math.Min(w, h) / 2);
        var (s, p) = Fit(radius, w, h);
        static double Rad(double deg) => deg * Math.PI / 180;
        double arcMeasure = 90 * (1 - s);
        double arcLen = Math.Sin(Rad(arcMeasure / 2)) * radius * Math.Sqrt(2);
        double alpha = (90 - arcMeasure) / 2;
        double p3p4 = radius * Math.Tan(Rad(alpha / 2));
        double beta = 45 * s;
        double cc = p3p4 * Math.Cos(Rad(beta));
        double d = cc * Math.Tan(Rad(beta));
        double b = (p - arcLen - cc - d) / 3, a = 2 * b;

        var e1 = start + (a + b + cc) * u + d * v;
        c.BezierTo(start + a * u, start + (a + b) * u, e1, true, true);
        var e2 = e1 + arcLen * u + arcLen * v;
        if (arcLen > 0.001) c.ArcTo(e2, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, true);
        var e3 = e2 + d * u + (a + b + cc) * v;
        c.BezierTo(e2 + d * u + cc * v, e2 + d * u + (b + cc) * v, e3, true, true);
    }
}
