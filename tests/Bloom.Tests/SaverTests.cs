using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Core;

namespace Bloom.Tests;

internal static class SaverTests
{
    public static void JsonSnapshotUsesTheOwningThread()
    {
        string directory = Scratch();
        try
        {
            string file = Path.Combine(directory, "store.json");
            int owner = Environment.CurrentManagedThreadId, captured = 0;
            var values = new List<int> { 1 };
            using var saver = Saver.ForJson(file, () =>
            {
                captured = Environment.CurrentManagedThreadId;
                return values;
            }, 1);
            saver.Touch();
            // No dispatcher work can serialize this UI-owned collection during its mutation.
            for (int i = 2; i <= 1000; i++) values.Add(i);
            PumpUntil(() => File.Exists(file));
            Check.Eq(captured, owner, "mutable data is captured on its owning thread");
            Check.Eq(JsonSerializer.Deserialize<List<int>>(File.ReadAllText(file))!.Count, 1000, "the completed snapshot is saved");
        }
        finally { Directory.Delete(directory, true); }
    }

    public static void FlushSupersedesQueuedSnapshot()
    {
        string directory = Scratch();
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var posted = new ManualResetEventSlim();
        void Posted(object? sender, DispatcherHookEventArgs args)
        {
            if (args.Operation.Priority == DispatcherPriority.Background) posted.Set();
        }
        dispatcher.Hooks.OperationPosted += Posted;
        try
        {
            string file = Path.Combine(directory, "store.json");
            int value = 1, captures = 0;
            using var saver = Saver.ForJson(file, () => { captures++; return value; }, 1);
            saver.Touch();
            Check.True(posted.Wait(TimeSpan.FromSeconds(3)), "debounce queued an owner-thread snapshot");
            value = 2;
            saver.Flush();
            bool drained = false;
            dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => drained = true);
            PumpUntil(() => drained);
            Check.Eq(captures, 1, "queued old work does not duplicate the explicit flush");
            Check.Eq(File.ReadAllText(file), "2", "newest value remains on disk");
            value = 3;
            saver.Flush();
            Check.Eq(File.ReadAllText(file), "3", "explicit flush preserves callers that did not Touch");
        }
        finally
        {
            dispatcher.Hooks.OperationPosted -= Posted;
            Directory.Delete(directory, true);
        }
    }

    public static void FlushesNeverOverlapAndDisposeCancelsWork()
    {
        using var firstEntered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int active = 0, calls = 0, overlaps = 0;
        using var saver = new Saver(() =>
        {
            if (Interlocked.Increment(ref active) != 1) Interlocked.Increment(ref overlaps);
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstEntered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(3))) throw new InvalidOperationException("first writer timed out");
            }
            Interlocked.Decrement(ref active);
        }, 1);
        saver.Touch();
        Check.True(firstEntered.Wait(TimeSpan.FromSeconds(3)), "background write started");
        using var flushing = new ManualResetEventSlim();
        var flush = Task.Run(() => { flushing.Set(); saver.Flush(); });
        try
        {
            Check.True(flushing.Wait(TimeSpan.FromSeconds(3)), "explicit flush started");
            Check.True(!flush.Wait(50), "explicit flush waits for the active writer");
        }
        finally { release.Set(); }
        Check.True(flush.Wait(TimeSpan.FromSeconds(3)), "explicit flush finishes");
        Check.Eq(overlaps, 0, "writes never overlap");
        Check.Eq(calls, 2, "explicit flush saves after the previous write");
        saver.Dispose();
        saver.Touch();
        saver.Flush();
        Check.Eq(calls, 2, "disposed stores cannot write again");
    }

    public static void ReloadDiscardsQueuedOldSnapshot()
    {
        string directory = Scratch();
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var posted = new ManualResetEventSlim();
        void Posted(object? sender, DispatcherHookEventArgs args)
        {
            if (args.Operation.Priority == DispatcherPriority.Background) posted.Set();
        }
        dispatcher.Hooks.OperationPosted += Posted;
        try
        {
            string file = Path.Combine(directory, "store.json");
            int oldCaptures = 0;
            using var old = Saver.ForJson(file, () => { oldCaptures++; return "old"; }, 1);
            old.Touch();
            Check.True(posted.Wait(TimeSpan.FromSeconds(3)), "old store queued its snapshot");
            old.Dispose();
            using var replacement = Saver.ForJson(file, () => "imported");
            replacement.Flush();
            bool drained = false;
            dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => drained = true);
            PumpUntil(() => drained);
            Check.Eq(oldCaptures, 0, "reload cancels capture from the old store");
            Check.Eq(JsonSerializer.Deserialize<string>(File.ReadAllText(file)), "imported", "the replaced store remains on disk");
        }
        finally
        {
            dispatcher.Hooks.OperationPosted -= Posted;
            Directory.Delete(directory, true);
        }
    }

    private static string Scratch()
    {
        string path = Path.Combine(Path.GetTempPath(), "Bloom-SaverTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void PumpUntil(Func<bool> done)
    {
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(4);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            if (done() || DateTime.UtcNow >= deadline) frame.Continue = false;
        };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Check.True(done(), "dispatcher work completes within the deadline");
    }
}
