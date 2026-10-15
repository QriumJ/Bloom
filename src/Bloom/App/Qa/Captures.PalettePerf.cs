using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// palette-perf: how long a palette takes and the memory it needs, on made-up desktops (nothing from the screen) of
/// 1920 × 1080, 3840 × 2160, 7680 × 4320 and three monitors side by side (7680 × 1440) — its points taken from the Lens
/// pixels, read from the Lens copy (PNG), the palette made of them, made again at each count — beside the way − n +
/// read a copy before (decoded whole); then, in the launcher, Lens › 7 and − n + on the palette opened from the history,
/// each step timed with the UI thread's longest stall meanwhile. Written to palette-perf.txt; nothing is copied.
internal static partial class Captures
{
    private static async Task PalettePerf(Host host, Launcher w, string dir)
    {
        var log = new StringBuilder();
        log.AppendLine($"palette-perf · {DateTime.Now:yyyy-MM-dd HH:mm} · {Environment.ProcessorCount} logical CPUs · .NET {Environment.Version}");
        log.AppendLine("Times in ms, the median of 3 runs after a warm-up. Memory in MiB: what the work allocated (managed), and how far the managed heap");
        log.AppendLine("and the private bytes (WIC's native buffers too) rose above where they stood before it, at their peak (the most of the 3 runs).");
        log.AppendLine();
        w.Width = 760;
        w.Height = 476;
        Theme.Apply("dark");

        // warm-up: every path once on a small one, so no time below is the JIT's
        var warm = Desktop(640, 360);
        host.SetLensForQa(new Ocr.ScreenRect(-32000, -32000, 640, 360), warm);
        string warmPng = host.LastLens!.Png;
        PaletteExtract.Of(Host.ReadPoints(warmPng)!);
        OldWay(warmPng);
        await host.LensDo("palette");
        host.OpenPalette(host.Palette!.Saved);
        await host.SetPaletteCount(PaletteExtract.DefaultCount + 1);

        var ui = new StringBuilder();
        foreach (var (pw, ph, name) in new[] { (1920, 1080, "1920×1080"), (3840, 2160, "3840×2160"), (7680, 4320, "7680×4320"), (7680, 1440, "7680×1440 (3 monitors)") })
        {
            var px = Desktop(pw, ph);
            host.SetLensForQa(new Ocr.ScreenRect(-32000, -32000, pw, ph), px);
            string png = host.LastLens!.Png;
            var sample = await Measure(() => PaletteExtract.Sample(px, pw, ph));
            var read = await Measure(() => Host.ReadPoints(png)!);
            var points = read.Value;
            var make = await Measure(() => PaletteExtract.Of(points));
            var old = await Measure(() => OldWay(png));
            log.AppendLine($"── {name} · pixels {pw * ph * 4 / 1048576.0:0.0} MiB · Lens copy {new FileInfo(png).Length / 1048576.0:0.0} MiB (PNG) · {points.Length / 4} points");
            log.AppendLine($"  points from the Lens pixels (in memory)   {Row(sample)}");
            log.AppendLine($"  decode: points from the Lens copy (PNG)   {Row(read)}");
            log.AppendLine($"  extraction: palette of the points ({PaletteExtract.DefaultCount})    {Row(make)}");
            log.AppendLine($"  before: copy decoded whole + palette      {Row(old)}");
            log.AppendLine($"  the copy's points = the pixels' points: {points.AsSpan().SequenceEqual(sample.Value)} · palette = the one decoded whole: {make.Value.SequenceEqual(old.Value)} · {string.Join(" ", make.Value.Select(c => ColorBook.Hex(c.Color)))}");
            var counts = new List<string>();
            for (int n = PaletteExtract.MinCount; n <= PaletteExtract.MaxCount; n++)
            {
                int count = n;
                var again = await Measure(() => PaletteExtract.Of(points, count));
                counts.Add($"{n}: {again.Ms:0.0}");
            }
            log.AppendLine($"  extraction at each count (ms)             {string.Join(" · ", counts)}");

            // in the launcher: Lens › 7 (points from the pixels), the view drawn again with nothing made, the palette
            // opened from the history and − n + up to 10 and down to 3 (points from the Lens copy on the first step),
            // and the way it was before (decoded whole on the UI thread)
            ui.AppendLine($"── {name}");
            var ping = new UiPing(w.Dispatcher);
            var clock = Stopwatch.StartNew();
            await host.LensDo("palette");
            double fresh = clock.Elapsed.TotalMilliseconds;
            ui.AppendLine($"  Lens › 7 to the palette shown: {fresh:0} ms, UI thread's longest stall {await ping.Stop():0} ms");
            var saved = host.Palette!.Saved;
            await Task.Delay(300);
            ping = new UiPing(w.Dispatcher);
            w.Refresh(true);
            await Task.Delay(200);
            ui.AppendLine($"  the palette view drawn again (no extraction): longest stall {await ping.Stop():0} ms");
            host.OpenPalette(saved);
            await Task.Delay(300);
            var steps = new List<string>();
            foreach (int n in new[] { 6, 7, 8, 9, 10, 9, 8, 7, 6, 5, 4, 3 })
            {
                ping = new UiPing(w.Dispatcher);
                clock.Restart();
                var step = host.SetPaletteCount(n);
                bool busy = false;
                while (!step.IsCompleted)
                {
                    busy |= host.Palette is { Busy: true };
                    await Task.Delay(5);
                }
                await step;
                double ms = clock.Elapsed.TotalMilliseconds;
                steps.Add($"{n}: {ms:0} ms / stall {await ping.Stop():0}{(busy ? " (색 추출 중 shown)" : "")}");
            }
            ui.AppendLine($"  − n + from the history, first step decodes: {string.Join(" · ", steps)}");
            ping = new UiPing(w.Dispatcher);
            clock.Restart();
            OldWay(png);
            double before = clock.Elapsed.TotalMilliseconds;
            ui.AppendLine($"  before, on the UI thread (copy decoded whole + palette): {before:0} ms, longest stall {await ping.Stop():0} ms");
        }
        log.AppendLine().AppendLine("UI thread (an Input-priority call asked of it about every 1 ms from another thread; the longest wait):").Append(ui);
        await File.WriteAllTextAsync(Path.Combine(dir, "palette-perf.txt"), log.ToString());
    }

    private static string Row<T>(Measured<T> m) => $"{m.Ms,7:0.0} ms · allocated {m.AllocMb,6:0.00} · heap peak +{m.HeapMb,6:0.00} · private peak +{m.PrivateMb,6:0.0}";

    private sealed record Measured<T>(T Value, double Ms, double AllocMb, double HeapMb, double PrivateMb);

    /// `work` run 3 times on a pool thread: its last value, the median time, the most it allocated and raised the managed
    /// heap and the private bytes by.
    private static async Task<Measured<T>> Measure<T>(Func<T> work)
    {
        var times = new List<double>();
        double alloc = 0, heap = 0, priv = 0;
        T value = default!;
        for (int run = 0; run < 3; run++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var peak = new MemoryPeak();
            var (got, ms, bytes) = await Task.Run(() =>
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                var clock = Stopwatch.StartNew();
                var v = work();
                return (v, clock.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before);
            });
            var (h, p) = await peak.Stop();
            value = got;
            times.Add(ms);
            alloc = Math.Max(alloc, bytes / 1048576.0);
            heap = Math.Max(heap, h);
            priv = Math.Max(priv, p);
        }
        times.Sort();
        return new(value, times[1], alloc, heap, priv);
    }

    /// How − n + read a Lens copy before: the whole picture decoded into one array, the palette made of it.
    private static List<PaletteColor> OldWay(string png)
    {
        using var fs = File.OpenRead(png);
        var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var px = new byte[w * h * 4];
        bgra.CopyPixels(px, w * 4, 0);
        return PaletteExtract.From(px, w, h);
    }

    /// A made-up desktop, the same every time: a soft gradient wallpaper and, on each 1920 × 1080 screen of it, a white
    /// window (a grey side panel, dark and grey lines of "text", a blue button) and a noisy photo-like picture.
    private static byte[] Desktop(int width, int height)
    {
        var px = new byte[width * height * 4];
        var rnd = new Random(42);
        void Put(int x, int y, int r, int g, int b)
        {
            int i = (y * width + x) * 4;
            (px[i], px[i + 1], px[i + 2], px[i + 3]) = ((byte)Math.Clamp(b, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(r, 0, 255), 255);
        }
        void Box(int x0, int y0, int bw, int bh, int rgb)
        {
            for (int y = Math.Max(0, y0); y < Math.Min(height, y0 + bh); y++)
                for (int x = Math.Max(0, x0); x < Math.Min(width, x0 + bw); x++) Put(x, y, rgb >> 16, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) Put(x, y, 40 + 60 * x / width, 70 + 50 * y / height, 120 + 80 * (x + y) / (width + height));
        for (int sx = 0; sx < width; sx += 1920)
            for (int sy = 0; sy < height; sy += 1080)
            {
                Box(sx + 160, sy + 120, 1000, 640, 0xFFFFFF);
                Box(sx + 160, sy + 120, 240, 640, 0xE9ECEF);
                for (int i = 0; i < 14; i++) Box(sx + 440, sy + 170 + i * 30, 300 + (i * 97 % 380), 10, i % 4 == 0 ? 0x343A40 : 0x868E96);
                Box(sx + 440, sy + 660, 180, 48, 0x3B82F6);
                for (int y = sy + 380; y < Math.Min(height, sy + 900); y++)
                    for (int x = sx + 1080; x < Math.Min(width, sx + 1840); x++)
                    {
                        int dy = y - sy - 380, dx = x - sx - 1080;
                        Put(x, y, 235 - dy / 4 + rnd.Next(-12, 13), 150 - dy / 5 + dx / 12 + rnd.Next(-12, 13), 90 + dy / 3 + rnd.Next(-12, 13));
                    }
            }
        return px;
    }

    /// The managed heap's and the private bytes' peak above where they stood when it was made, watched from a thread of
    /// its own until Stop.
    private sealed class MemoryPeak
    {
        private readonly long heap0 = GC.GetTotalMemory(false), private0 = Private();
        private readonly Thread thread;
        private long heap, priv;
        private volatile bool stop;

        public MemoryPeak()
        {
            (heap, priv) = (heap0, private0);
            thread = new Thread(() =>
            {
                long next = 0;
                while (!stop)
                {
                    heap = Math.Max(heap, GC.GetTotalMemory(false));
                    if (Stopwatch.GetTimestamp() >= next)
                    {
                        priv = Math.Max(priv, Private());
                        next = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 500;
                    }
                    Thread.SpinWait(100);
                }
            })
            {
                IsBackground = true,
            };
            thread.Start();
        }

        public async Task<(double heapMb, double privateMb)> Stop()
        {
            stop = true;
            await Task.Run(() => thread.Join());
            heap = Math.Max(heap, GC.GetTotalMemory(false));
            priv = Math.Max(priv, Private());
            return ((heap - heap0) / 1048576.0, (priv - private0) / 1048576.0);
        }

        private static long Private()
        {
            using var me = Process.GetCurrentProcess();
            return me.PrivateMemorySize64;
        }
    }

    /// The UI thread's longest stall while it runs: another thread asks it, about every millisecond, for an empty call at
    /// Input priority (where a click or a key would wait) and times each wait.
    private sealed class UiPing
    {
        private readonly Thread thread;
        private volatile bool stop;
        private long worst;

        public UiPing(Dispatcher ui)
        {
            thread = new Thread(() =>
            {
                while (!stop)
                {
                    long sent = Stopwatch.GetTimestamp();
                    ui.Invoke(static () => { }, DispatcherPriority.Input);
                    worst = Math.Max(worst, Stopwatch.GetTimestamp() - sent);
                    long until = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 1000;
                    while (Stopwatch.GetTimestamp() < until) Thread.SpinWait(50);
                }
            })
            {
                IsBackground = true,
            };
            thread.Start();
        }

        public async Task<double> Stop()
        {
            stop = true;
            await Task.Run(() => thread.Join());
            return worst * 1000.0 / Stopwatch.Frequency;
        }
    }
}
