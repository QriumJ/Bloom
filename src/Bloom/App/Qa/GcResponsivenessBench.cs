using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Ocr;

namespace Bloom.App;

/// A separate, bounded synthetic allocation phase. No explicit collections or desktop input.
internal static class GcResponsivenessBench
{
    private const int MiB = 1024 * 1024, Seconds = 4;
    private const long AllocationLimit = 512L * MiB;
    private const int LiveLimit = 96 * MiB;

    internal sealed record PulseStats(double P95Ms, double MaxMs, double[] IntervalsMs);
    internal sealed record Collection(long Index, int Generation, bool Concurrent, bool Compacted, double[] PauseDurationsMs);
    internal sealed record Report(string Scope, int MaxSeconds, long MaxLiveBytes, long MaxAllocatedBytes,
        double DurationMs, long WorkerAllocatedBytes, long SyntheticLiveUpperBoundBytes,
        bool ServerGc, bool? ConcurrentGc, Dictionary<string, object> EffectiveConfiguration,
        int Gen2Before, int Gen2After, bool AutomaticGen2Observed, double ProcessCpuMs,
        long SampledPeakPrivateBytes, long SampledPeakManagedBytes,
        PulseStats MainInput, PulseStats CaptureInput, Collection[] ObservedCollections)
    {
        public string Assessment => AutomaticGen2Observed ? "AutomaticGen2Observed" : "InconclusiveNoAutomaticGen2";
    }

    private sealed class Pulse
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private readonly DispatcherTimer timer;
        private readonly List<double> intervals = new(600);
        private double previous;
        private PulseStats? result;

        public Pulse(Dispatcher dispatcher)
        {
            timer = new DispatcherTimer(DispatcherPriority.Input, dispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += Tick;
            timer.Start();
        }

        private void Tick(object? sender, EventArgs e)
        {
            double now = watch.Elapsed.TotalMilliseconds;
            intervals.Add(now - previous);
            previous = now;
        }

        public PulseStats Stop()
        {
            if (result is not null) return result;
            timer.Stop();
            timer.Tick -= Tick;
            double[] samples = intervals.ToArray();
            double[] sorted = samples.Order().ToArray();
            return result = new(sorted.Length == 0 ? 0 : sorted[(int)Math.Ceiling(.95 * sorted.Length) - 1],
                sorted.Length == 0 ? 0 : sorted[^1], samples);
        }
    }

    private sealed record WorkerStats(long AllocatedBytes, long LiveUpperBoundBytes, long PeakPrivateBytes,
        long PeakManagedBytes, Collection[] Collections);

    public static async Task<Report> Run(Dispatcher ui)
    {
        ui.VerifyAccess();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Seconds));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<PulseStats> capture = CaptureDispatcher.Run(async abort =>
        {
            var pulse = new Pulse(Dispatcher.CurrentDispatcher);
            ready.TrySetResult();
            PulseStats result;
            try { await stop.Task.WaitAsync(abort); }
            finally { result = pulse.Stop(); }
            return result;
        });
        Pulse? main = null;
        Task<WorkerStats>? worker = null;
        try
        {
            // Propagate STA startup failure instead of waiting forever for ready.
            await Task.WhenAny(ready.Task, capture);
            if (capture.IsCompleted) await capture;
            await ready.Task;
            var configuration = GC.GetConfigurationVariables().ToDictionary(pair => pair.Key, pair => pair.Value);
            bool? concurrent = configuration.GetValueOrDefault("ConcurrentGC") is bool value ? value : null;
            int gen2Before = GC.CollectionCount(2);
            long collectionBefore = GC.GetGCMemoryInfo().Index;
            using var process = Process.GetCurrentProcess();
            double cpuBefore = process.TotalProcessorTime.TotalMilliseconds;
            var watch = Stopwatch.StartNew();
            main = new Pulse(ui);
            worker = StartWorker(watch, collectionBefore, cancellation.Token);
            WorkerStats stats = await worker;
            await Task.Delay(32); // allow final Input ticks to observe a pause at the end of allocation
            stop.TrySetResult();
            PulseStats captureStats = await capture;
            PulseStats mainStats = main.Stop();
            int gen2After = GC.CollectionCount(2);
            return new("Synthetic allocation and Input dispatch only; completed GC polling may miss intermediate collections. No automatic Gen2 means inconclusive.",
                Seconds, LiveLimit, AllocationLimit, watch.Elapsed.TotalMilliseconds, stats.AllocatedBytes,
                stats.LiveUpperBoundBytes, GCSettings.IsServerGC, concurrent, configuration,
                gen2Before, gen2After, gen2After > gen2Before,
                process.TotalProcessorTime.TotalMilliseconds - cpuBefore, stats.PeakPrivateBytes,
                stats.PeakManagedBytes, mainStats, captureStats, stats.Collections);
        }
        finally
        {
            cancellation.Cancel();
            main?.Stop();
            stop.TrySetResult();
            // These tasks have no callback dependency on a synchronously blocked owner.
            // Await keeps the owner pumping until both worker and temporary STA retire.
            try { if (worker is not null) await worker; }
            finally { await capture; }
        }
    }

    private static Task<WorkerStats> StartWorker(Stopwatch watch, long previousIndex, CancellationToken cancellation)
    {
        var done = new TaskCompletionSource<WorkerStats>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var observed = new List<Collection>(128);
                long privatePeak = 0, managedPeak = 0;
                using var process = Process.GetCurrentProcess();
                void Poll(bool memory)
                {
                    var info = GC.GetGCMemoryInfo();
                    if (info.Index > previousIndex)
                    {
                        previousIndex = info.Index;
                        var pauses = new double[info.PauseDurations.Length];
                        for (int i = 0; i < pauses.Length; i++) pauses[i] = info.PauseDurations[i].TotalMilliseconds;
                        observed.Add(new(info.Index, info.Generation, info.Concurrent, info.Compacted, pauses));
                    }
                    if (!memory) return;
                    process.Refresh();
                    privatePeak = Math.Max(privatePeak, process.PrivateMemorySize64);
                    managedPeak = Math.Max(managedPeak, GC.GetTotalMemory(false));
                }
                // About 33 MiB of reachable small objects and 16 MiB of rotating LOH payloads.
                // Header/reference allowance plus an in-flight replacement stays below 96 MiB.
                var live = new byte[32 * 1024][];
                for (int i = 0; i < live.Length && !cancellation.IsCancellationRequested && watch.Elapsed.TotalSeconds < Seconds; i++)
                {
                    live[i] = new byte[1024];
                    if (i % 1024 == 0) Poll(false);
                }
                var rotating = new byte[16][];
                const long liveUpperBound = 52L * MiB;
                int iteration = 0;
                Poll(true);
                while (!cancellation.IsCancellationRequested && watch.Elapsed.TotalSeconds < Seconds
                    && GC.GetAllocatedBytesForCurrentThread() - allocatedBefore < AllocationLimit - 2L * MiB)
                {
                    var bytes = new byte[MiB];
                    bytes[0] = (byte)iteration;
                    bytes[^1] = (byte)(iteration >> 8);
                    rotating[iteration % rotating.Length] = bytes;
                    Poll(iteration % 16 == 0);
                    iteration++;
                    Thread.Sleep(10); // one paced, BelowNormal worker; no all-core contention
                }
                Poll(true);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                GC.KeepAlive(live);
                GC.KeepAlive(rotating);
                done.TrySetResult(new(allocated, liveUpperBound, privatePeak, managedPeak, observed.ToArray()));
            }
            catch (Exception e) { done.TrySetException(e); }
        })
        { IsBackground = true, Name = "qa-gc-allocation", Priority = ThreadPriority.BelowNormal };
        try { thread.Start(); }
        catch (Exception e) { done.TrySetException(e); }
        return done.Task;
    }
}
