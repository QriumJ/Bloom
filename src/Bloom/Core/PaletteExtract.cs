using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace Bloom.Core;

/// One colour of a palette and how much of the picture it covers (0–1 of the opaque points looked at).
internal readonly record struct PaletteColor(Color Color, double Share);

/// A palette made from a picture's pixels on this PC (no AI, no network); the same picture always gives the same one.
/// The picture is looked at on a grid of at most ~16k points (Sample; those under half opaque skipped), their colours
/// binned at 5 bits a channel, then k-means in OKLab from a fixed start (the heaviest bin, then each time the bin
/// farthest from the centres so far by its weight), near-duplicates merged and the colours taken by share. A dominant
/// colour (a flat background, a fifth of the picture or more) takes one place: its own shades come only when no other
/// colour is left. Specks (under 0.1 %) are no palette colour.
internal static class PaletteExtract
{
    public const int DefaultCount = 5, MinCount = 3, MaxCount = 10;

    /// OKLab distances: under Merge two colours are one (a just-visible step is about 0.02); within Shade of a dominant
    /// colour a colour counts as one of its shades.
    public const double Merge = 0.035, Shade = 0.10;

    private const double Dominant = 0.2, Speck = 0.001;
    private const int MaxPoints = 128 * 128, Centres = 24, Rounds = 16; // more centres than any palette: merging narrows them

    /// The palette of `width` × `height` straight (not premultiplied) BGRA pixels: up to `count` colours (kept to 3–10),
    /// fewer when the picture has fewer, the largest share first; none for an empty or wholly transparent picture.
    public static List<PaletteColor> From(ReadOnlySpan<byte> bgra, int width, int height, int count = DefaultCount) => Of(Sample(bgra, width, height), count);

    /// The points of a picture a palette is made of (BGRA, one after another): every `step`-th pixel across and down,
    /// `step` the least that keeps them to about 16k, from half a step in (or the first row or column of a picture
    /// thinner than that). A palette made again with another count needs only these.
    public static byte[] Sample(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width < 0 || height < 0 || bgra.Length < (long)width * height * 4)
            throw new ArgumentException($"{bgra.Length} bytes for {width} × {height} BGRA pixels", nameof(bgra));
        var (step, x0, y0, points) = Grid(width, height);
        int at = 0;
        for (int y = y0; y < height; y += step) at = Take(bgra.Slice(y * width * 4, width * 4), step, x0, points, at);
        return points;
    }

    /// The same points read a row at a time (`row(y, buffer)` puts row y's BGRA in `buffer`; the rows are asked for top
    /// to bottom), so a big picture never has to be in memory whole.
    public static byte[] Sample(int width, int height, Action<int, byte[]> row)
    {
        var (step, x0, y0, points) = Grid(width, height);
        var buffer = new byte[width * 4];
        int at = 0;
        for (int y = y0; y < height; y += step)
        {
            row(y, buffer);
            at = Take(buffer, step, x0, points, at);
        }
        return points;
    }

    /// The palette of the points Sample gave (up to `count` colours, kept to 3–10).
    public static List<PaletteColor> Of(ReadOnlySpan<byte> points, int count = DefaultCount)
    {
        if (points.Length % 4 != 0) throw new ArgumentException($"{points.Length} bytes are no whole BGRA points", nameof(points));
        var bins = Bins(points);
        double total = bins.Sum(b => b.Weight);
        if (total == 0) return new List<PaletteColor>();
        var groups = Cluster(bins);
        MergeNear(groups);
        return Pick(groups, total, Math.Clamp(count, MinCount, MaxCount));
    }

    /// How far apart two colours look (OKLab, straight-line): about 0.02 is a just-visible step, black to white is 1.
    public static double Distance(Color x, Color y)
    {
        var (l1, a1, b1) = Lab(x.R, x.G, x.B);
        var (l2, a2, b2) = Lab(y.R, y.G, y.B);
        return Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
    }

    /// Points of the picture together: a bin, or bins grouped. `R`, `G`, `B` are sums over the points; `L`, `A`, `Bo`
    /// their mean in OKLab; `Top` the colour of the heaviest bin in it, which a flat area keeps exactly.
    private sealed class Group
    {
        public double Weight, R, G, B, L, A, Bo, TopWeight;
        public Color Top;

        public Color Colour => TopWeight * 2 >= Weight ? Top : Color.FromRgb(Round(R / Weight), Round(G / Weight), Round(B / Weight));
    }

    /// Sample's grid for a picture: its step, where it starts across and down, and room for its points.
    private static (int step, int x0, int y0, byte[] points) Grid(int width, int height)
    {
        int step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)width * height / MaxPoints)));
        int x0 = Math.Max(0, Math.Min(step / 2, width - 1)), y0 = Math.Max(0, Math.Min(step / 2, height - 1));
        static int Along(int size, int step, int start) => size <= start ? 0 : (size - 1 - start) / step + 1;
        return (step, x0, y0, new byte[Along(width, step, x0) * Along(height, step, y0) * 4]);
    }

    /// One row's grid points copied to `points` from `at`; where the next row's go.
    private static int Take(ReadOnlySpan<byte> row, int step, int x0, byte[] points, int at)
    {
        for (int x = x0 * 4; x < row.Length; x += step * 4, at += 4) row.Slice(x, 4).CopyTo(points.AsSpan(at));
        return at;
    }

    /// The points binned (5 bits a channel), in bin order.
    private static List<Group> Bins(ReadOnlySpan<byte> points)
    {
        var counts = new int[1 << 15];
        var sums = new int[3 << 15];
        for (int i = 0; i < points.Length; i += 4)
        {
            if (points[i + 3] < 128) continue; // transparent: not part of the picture
            int b = points[i], g = points[i + 1], r = points[i + 2];
            int key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
            counts[key]++;
            sums[key * 3] += r;
            sums[key * 3 + 1] += g;
            sums[key * 3 + 2] += b;
        }
        var bins = new List<Group>();
        for (int key = 0; key < counts.Length; key++)
        {
            if (counts[key] == 0) continue;
            double n = counts[key], r = sums[key * 3], g = sums[key * 3 + 1], b = sums[key * 3 + 2];
            var (l, a, bo) = Lab(r / n, g / n, b / n);
            bins.Add(new Group { Weight = n, R = r, G = g, B = b, L = l, A = a, Bo = bo, TopWeight = n, Top = Color.FromRgb(Round(r / n), Round(g / n), Round(b / n)) });
        }
        return bins;
    }

    /// k-means over the bins, each weighted by its points, from a fixed start; the groups that kept any bin.
    private static List<Group> Cluster(List<Group> bins)
    {
        int k = Math.Min(Centres, bins.Count);
        var centres = new List<(double l, double a, double b)>(k);
        int first = 0;
        for (int i = 1; i < bins.Count; i++)
            if (bins[i].Weight > bins[first].Weight) first = i;
        centres.Add((bins[first].L, bins[first].A, bins[first].Bo));
        var near = new double[bins.Count]; // each bin's squared distance to its nearest centre so far
        for (int i = 0; i < bins.Count; i++) near[i] = D2(bins[i], centres[0]);
        while (centres.Count < k)
        {
            int far = -1;
            double score = 0;
            for (int i = 0; i < bins.Count; i++)
                if (bins[i].Weight * near[i] > score)
                {
                    score = bins[i].Weight * near[i];
                    far = i;
                }
            if (far < 0) break; // every bin sits on a centre
            var c = (bins[far].L, bins[far].A, bins[far].Bo);
            centres.Add(c);
            for (int i = 0; i < bins.Count; i++) near[i] = Math.Min(near[i], D2(bins[i], c));
        }
        var of = new int[bins.Count];
        for (int round = 0; round < Rounds; round++)
        {
            bool moved = false;
            for (int i = 0; i < bins.Count; i++)
            {
                int best = 0;
                double bestD = double.MaxValue;
                for (int j = 0; j < centres.Count; j++)
                {
                    double d = D2(bins[i], centres[j]);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = j;
                    }
                }
                if (round == 0 || of[i] != best) moved = true;
                of[i] = best;
            }
            if (!moved) break;
            var mean = new (double w, double l, double a, double b)[centres.Count];
            for (int i = 0; i < bins.Count; i++)
            {
                var g = bins[i];
                ref var m = ref mean[of[i]];
                m.w += g.Weight;
                m.l += g.L * g.Weight;
                m.a += g.A * g.Weight;
                m.b += g.Bo * g.Weight;
            }
            for (int j = 0; j < centres.Count; j++)
                if (mean[j].w > 0) centres[j] = (mean[j].l / mean[j].w, mean[j].a / mean[j].w, mean[j].b / mean[j].w);
        }
        var at = new Group?[centres.Count];
        for (int i = 0; i < bins.Count; i++)
        {
            if (at[of[i]] is { } g) Join(g, bins[i]);
            else at[of[i]] = bins[i]; // the bins are not looked at again: the first one becomes its group
        }
        return at.OfType<Group>().ToList();
    }

    /// The two nearest groups made one while they are near-duplicates (under Merge apart), the nearest pair first.
    private static void MergeNear(List<Group> groups)
    {
        while (true)
        {
            int a = -1, b = -1;
            double best = Merge * Merge;
            for (int i = 0; i < groups.Count; i++)
                for (int j = i + 1; j < groups.Count; j++)
                {
                    double d = D2(groups[i], groups[j]);
                    if (d < best)
                    {
                        best = d;
                        a = i;
                        b = j;
                    }
                }
            if (a < 0) return;
            Join(groups[a], groups[b]);
            groups.RemoveAt(b);
        }
    }

    /// Up to `count` groups by share, specks left out; a dominant colour's shades only after all the others.
    private static List<PaletteColor> Pick(List<Group> groups, double total, int count)
    {
        var chosen = new List<Group>();
        var shades = new List<Group>();
        foreach (var g in groups.OrderByDescending(g => g.Weight))
        {
            if (chosen.Count == count) break;
            if (chosen.Count > 0 && g.Weight / total < Speck) break; // the rest are smaller still
            if (chosen.Exists(c => c.Weight / total >= Dominant && D2(c, g) < Shade * Shade)) shades.Add(g);
            else chosen.Add(g);
        }
        chosen.AddRange(shades.Take(count - chosen.Count));
        return chosen.OrderByDescending(g => g.Weight).Select(g => new PaletteColor(g.Colour, g.Weight / total)).ToList();
    }

    /// `from`'s points added to `into`: weights and sums add up, OKLab is their weighted mean, the heavier top stays.
    private static void Join(Group into, Group from)
    {
        double w = into.Weight + from.Weight;
        into.L = (into.L * into.Weight + from.L * from.Weight) / w;
        into.A = (into.A * into.Weight + from.A * from.Weight) / w;
        into.Bo = (into.Bo * into.Weight + from.Bo * from.Weight) / w;
        into.R += from.R;
        into.G += from.G;
        into.B += from.B;
        into.Weight = w;
        if (from.TopWeight > into.TopWeight)
        {
            into.TopWeight = from.TopWeight;
            into.Top = from.Top;
        }
    }

    private static double D2(Group g, (double l, double a, double b) c) => (g.L - c.l) * (g.L - c.l) + (g.A - c.a) * (g.A - c.a) + (g.Bo - c.b) * (g.Bo - c.b);

    private static double D2(Group g, Group h) => D2(g, (h.L, h.A, h.Bo));

    /// sRGB (0–255 a channel) to OKLab (Björn Ottosson's matrices).
    private static (double l, double a, double b) Lab(double r, double g, double b)
    {
        static double Lin(double v)
        {
            v /= 255;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        double R = Lin(r), G = Lin(g), B = Lin(b);
        double l = Math.Cbrt(0.4122214708 * R + 0.5363325363 * G + 0.0514459929 * B);
        double m = Math.Cbrt(0.2119034982 * R + 0.6806995451 * G + 0.1073969566 * B);
        double s = Math.Cbrt(0.0883024619 * R + 0.2817188376 * G + 0.6299787005 * B);
        return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    private static byte Round(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
}
