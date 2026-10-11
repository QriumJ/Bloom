using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.Ui;

/// How long one opening of the launcher takes, step by step: from the key (when its hook or hotkey marked it) to the
/// cloak lifting. Launcher.Summon and Glass.RevealAfterFrame mark the steps; the page faults and GC pauses in between
/// and whether an idle trim ran since the last opening (a cold open pages back what it touches) come with them. A slow
/// opening leaves one line in the log; QA (`--qa-summon`) takes every one through Finished.
internal static class SummonTrace
{
    /// An opening this slow (key to on screen) is written to the log.
    public const double SlowMs = 150;

    /// One opening: the total and each step in ms (since the step before), page faults and GC pause during it, and
    /// whether the idle trim ran before it.
    public sealed record Result(double Total, IReadOnlyList<(string Step, double Ms)> Steps, uint Faults, double GcMs, bool Cold);

    /// Every finished opening, on the UI thread.
    public static event Action<Result>? Finished;

    private static readonly object Gate = new();
    private static readonly List<(string step, long at)> steps = [];
    private static long keyAt; // Stopwatch ticks of the last key that asked for an opening (any thread)
    private static long start;
    private static uint faults;
    private static TimeSpan paused;
    private static bool on, cold;
    private static int trimsSeen;

    /// A key asked for an opening (the Win key's hook thread, a hotkey): its hop to the UI thread counts too.
    public static void Key() => Interlocked.Exchange(ref keyAt, Stopwatch.GetTimestamp());

    /// The opening starts (Launcher.Summon, on the UI thread).
    public static void Begin()
    {
        long now = Stopwatch.GetTimestamp(), key = Interlocked.Exchange(ref keyAt, 0);
        bool fromKey = key > 0 && Stopwatch.GetElapsedTime(key, now) < TimeSpan.FromSeconds(1);
        lock (Gate)
        {
            steps.Clear();
            start = fromKey ? key : now;
            if (fromKey) steps.Add(("hop", now));
            faults = Win32.PageFaults();
            paused = GC.GetTotalPauseDuration();
            int trims = MemoryTrim.Count;
            cold = trims != trimsSeen;
            trimsSeen = trims;
            on = true;
        }
    }

    /// A step done (any thread: DWM's flush is waited for on the pool).
    public static void Mark(string step)
    {
        long now = Stopwatch.GetTimestamp();
        lock (Gate)
            if (on) steps.Add((step, now));
    }

    /// The launcher is on screen (the UI thread).
    public static void End()
    {
        Result r;
        lock (Gate)
        {
            if (!on) return;
            on = false;
            var list = new List<(string, double)>(steps.Count);
            long before = start;
            foreach (var (step, at) in steps)
            {
                list.Add((step, Stopwatch.GetElapsedTime(before, at).TotalMilliseconds));
                before = at;
            }
            r = new Result(Stopwatch.GetElapsedTime(start).TotalMilliseconds, list, Win32.PageFaults() - faults, (GC.GetTotalPauseDuration() - paused).TotalMilliseconds, cold);
        }
        if (r.Total >= SlowMs) Log.Write("slow open: " + Line(r));
        Finished?.Invoke(r);
    }

    /// "182 ms (cold) · hop 2 · go 41 · … · faults 8123 · gc 0 ms".
    public static string Line(Result r) =>
        string.Join(" · ", r.Steps.Select(s => $"{s.Step} {s.Ms:0}").Prepend($"{r.Total:0} ms{(r.Cold ? " (cold)" : "")}").Append($"faults {r.Faults}").Append($"gc {r.GcMs:0} ms"));
}
