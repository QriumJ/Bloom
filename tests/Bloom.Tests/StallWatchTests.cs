using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Threading;
using Bloom.Core;

namespace Bloom.Tests;

internal static class StallWatchTests
{
    public static void BlockedUiThreadIsLoggedWithOneDump()
    {
        var (lines, dumps) = Watch(d =>
        {
            d.BeginInvoke(() => Thread.Sleep(1200));
            return 1;
        });
        Check.Eq(dumps, 1, "a stall past 0.8 s gets one dump");
        var m = Regex.Match(lines.Single(), @"^ui stall (\d+) ms · dump fake\.dmp \(writing it \d+ ms\)$");
        Check.True(m.Success, "log line names the stall, its dump and how long writing it took: " + lines.Single());
        long ms = long.Parse(m.Groups[1].Value);
        Check.True(ms is >= 1000 and < 2000, $"measured stall {ms} ms is about the 1.2 s block");
    }

    public static void ShortBusyMomentsAreNotRecorded()
    {
        var (lines, dumps) = Watch(d =>
        {
            d.BeginInvoke(() => Thread.Sleep(200));
            return 0;
        });
        Check.Eq(lines.Count, 0, "a 0.2 s busy moment is not a stall");
        Check.Eq(dumps, 0, "and gets no dump");
    }

    public static void SecondStallWithinTheGapIsLoggedWithoutADump()
    {
        var (lines, dumps) = Watch(d =>
        {
            d.BeginInvoke(() => Thread.Sleep(1000));
            Thread.Sleep(1600); // the first stall answered and logged
            d.BeginInvoke(() => Thread.Sleep(1000));
            return 2;
        });
        Check.Eq(lines.Count, 2, "both stalls are logged");
        Check.Eq(dumps, 1, "only one dump per 10 minutes");
        Check.True(lines[0].Contains("dump fake.dmp", StringComparison.Ordinal) && Regex.IsMatch(lines[1], @"^ui stall \d+ ms$"), "the second line has no dump: " + string.Join(" | ", lines));
    }

    public static void TriageDumpWritesAFileAndKeepsTheNewestFive()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bloom-stall-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            for (int i = 1; i <= 6; i++) File.WriteAllText(Path.Combine(dir, $"stall-20000101-00000{i}.dmp"), "old");
            string name = Check.NotNull(StallWatch.TriageDump(dir), "createdump wrote a dump");
            Check.True(new FileInfo(Path.Combine(dir, name)).Length > 1024, "the dump has content");
            var left = Directory.GetFiles(dir, "stall-*.dmp").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Check.Eq(left.Count, StallWatch.KeptDumps, "only the newest five stay");
            Check.True(left.Contains(name) && !left.Contains("stall-20000101-000001.dmp") && !left.Contains("stall-20000101-000002.dmp"), "the oldest go first: " + string.Join(", ", left));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// A watch over a fresh UI dispatcher; `act` blocks it as a test needs and returns how many log lines to wait for.
    private static (System.Collections.Generic.List<string> lines, int dumps) Watch(Func<Dispatcher, int> act)
    {
        Dispatcher? d = null;
        using var ready = new ManualResetEventSlim();
        var t = new Thread(() =>
        {
            d = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        ready.Wait();
        var lines = new ConcurrentQueue<string>();
        int dumps = 0;
        using (new StallWatch(d!, lines.Enqueue, () =>
        {
            Interlocked.Increment(ref dumps);
            return "fake.dmp";
        }))
        {
            Thread.Sleep(300); // quick answers first
            int expected = act(d!);
            WaitUntil(() => lines.Count >= expected, 3000);
            Thread.Sleep(300); // nothing more comes
        }
        d!.InvokeShutdown();
        return (lines.ToList(), dumps);
    }

    private static void WaitUntil(Func<bool> done, int ms)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms) Thread.Sleep(20);
    }
}
