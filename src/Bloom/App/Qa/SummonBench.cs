using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Native;
using Bloom.Ocr;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-summon <n>`: n rounds of a cold opening (right after an idle trim) and a warm one (a moment after the last),
/// asked from another thread with the key marked, as the Win key's hook does, and timed by SummonTrace. summon.txt in
/// the data folder gets every opening and the medians, then Bloom quits. The launcher opens without taking the focus,
/// so the user's own work goes on (put it on --display=N).
/// Each opening's first composed look is grabbed off the screen and set against its look half a second later: the
/// share of pixels that changed ("changed") stays near 0 unless it came on screen before WPF had drawn it (a flash).
internal static class SummonBench
{
    private sealed record Opening(bool Cold, SummonTrace.Result Trace, double Changed);

    public static async Task Run(Host host, Dispatcher ui, int rounds)
    {
        Launcher.NoActivateForQa = true;
        host.Window.ShowActivated = false;
        await Task.Delay(8000); // start-up settles: the app index, the first usage look
        var all = new List<Opening>();
        for (int i = 0; i < rounds; i++)
        {
            MemoryTrim.Now(); // explicit cold-memory probe; routine hidden-window cleanup no longer forces GC
            await Task.Delay(2000);
            if (await Open(host, ui, true) is { } cold) all.Add(cold);
            await Task.Delay(1500);
            if (await Open(host, ui, false) is { } warm) all.Add(warm);
        }
        var text = new StringBuilder();
        foreach (var o in all) text.AppendLine((o.Cold ? "cold " : "warm ") + SummonTrace.Line(o.Trace) + $" · changed {o.Changed:0.00}%");
        foreach (bool cold in new[] { true, false })
        {
            var some = all.Where(o => o.Cold == cold).ToList();
            if (some.Count == 0) continue;
            text.AppendLine($"{(cold ? "cold" : "warm")}: median {Median(some.Select(o => o.Trace.Total))} ms · p90 {P90(some.Select(o => o.Trace.Total))} ms · faults {Median(some.Select(o => (double)o.Trace.Faults))} · changed max {some.Max(o => o.Changed):0.00}%");
            // each step over the openings that had it (a guarded one has no "flush"), with how many did
            foreach (string step in some.SelectMany(o => o.Trace.Steps.Select(s => s.Step)).Distinct())
            {
                var ms = some.SelectMany(o => o.Trace.Steps).Where(s => s.Step == step).Select(s => s.Ms).ToList();
                text.AppendLine($"  {step} {Median(ms)} ms" + (ms.Count < some.Count ? $" ({ms.Count}/{some.Count})" : ""));
            }
        }
        await File.WriteAllTextAsync(Paths.In("summon.txt"), text.ToString());
        host.Quit();
    }

    /// One opening from another thread, as the hook asks for it, then hidden again; null if it never came on screen.
    private static async Task<Opening?> Open(Host host, Dispatcher ui, bool cold)
    {
        var done = new TaskCompletionSource<SummonTrace.Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<byte[]?> shown = Task.FromResult<byte[]?>(null);
        ScreenRect rect = default;
        void Finished(SummonTrace.Result r)
        {
            // right as it is uncloaked: the screen once DWM has composed it
            if (Win32.GetWindowRect(host.Window.Handle, out var rc))
            {
                rect = new ScreenRect(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
                var at = rect;
                shown = Task.Run(() =>
                {
                    _ = Win32.DwmFlush();
                    return ScreenGrab.Grab(at);
                });
            }
            done.TrySetResult(r);
        }
        SummonTrace.Finished += Finished;
        await Task.Run(() =>
        {
            SummonTrace.Key();
            ui.BeginInvoke(host.Summon);
        });
        var first = await Task.WhenAny(done.Task, Task.Delay(3000));
        SummonTrace.Finished -= Finished;
        await Task.Delay(500);
        double changed = -1;
        if (await shown is { } a && await Task.Run(() => ScreenGrab.Grab(rect)) is { } b)
        {
            changed = Changed(a, b);
            if (changed > 1) Keep(a, b, rect); // a flash: both looks next to summon.txt
            Save(b, rect, "summon-last.png"); // the last opening as it settled (the kept home, drawn right?)
        }
        await Task.Delay(200);
        host.Window.Dismiss();
        return first == done.Task ? new Opening(cold, done.Task.Result, changed) : null;
    }

    private static int kept;

    private static void Keep(byte[] first, byte[] settled, ScreenRect r)
    {
        int n = ++kept;
        Save(first, r, $"flash-{n}-first.png");
        Save(settled, r, $"flash-{n}-settled.png");
    }

    private static void Save(byte[] px, ScreenRect r, string name)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgra32, null, px, r.Width * 4)));
        using var fs = File.Create(Paths.In(name));
        enc.Save(fs);
    }

    /// The share of pixels (%) whose colour moved by more than 32 in a channel: text and icons missing from a first
    /// look count, a blinking caret doesn't.
    internal static double Changed(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length) / 4, moved = 0;
        for (int i = 0; i < n * 4; i += 4)
            if (Math.Abs(a[i] - b[i]) > 32 || Math.Abs(a[i + 1] - b[i + 1]) > 32 || Math.Abs(a[i + 2] - b[i + 2]) > 32) moved++;
        return n == 0 ? 0 : 100.0 * moved / n;
    }

    private static double Median(IEnumerable<double> values) => Percentile(values, 0.5);

    private static double P90(IEnumerable<double> values) => Percentile(values, 0.9);

    private static double Percentile(IEnumerable<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return Math.Round(sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)]);
    }
}
