using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using Bloom.App;
using Bloom.Search;

namespace Bloom.Tests;

internal static class PathLookupTests
{
    private static PathAnswer Answer(string full) => new(full, true, true, Array.Empty<(string, bool)>());

    public static void UnresolvedEnvironmentVariableKeepsTheOriginalFallback()
    {
        string text = "%BLOOM_QA_UNDEFINED_" + Guid.NewGuid().ToString("N") + "%";
        Check.True(PathInput.Looks(text), "variable syntax still looks like a path");
        Check.Eq(PathInput.Expand(text), text, "unknown variable remains unexpanded");
        Check.Eq(PathInput.Read(text), null, "non-path expansion keeps the existing normal-search fallback");
    }

    public static void BlockedFolderReadLeavesInputFreeAndOnlyLoadsLatestPendingPath()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var ready = new ManualResetEventSlim();
        var reads = new List<string>();
        int owner = Environment.CurrentManagedThreadId, loadedOn = owner;
        using var lookup = new PathLookup(full =>
        {
            loadedOn = Environment.CurrentManagedThreadId;
            reads.Add(full);
            if (full == @"C:\a")
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            }
            return Answer(full);
        });
        string? published = null;
        lookup.Updated += full => { published = full; ready.Set(); };
        try
        {
            Check.Eq(lookup.Read(@"C:\a"), null, "first read returns pending immediately");
            Check.True(entered.Wait(TimeSpan.FromSeconds(3)), "worker entered blocked IO");
            Check.Eq(lookup.Read(@"C:\b"), null, "another path never reuses old results");
            Check.Eq(lookup.Read(@"C:\c"), null, "latest path replaces pending work");
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            bool input = false;
            dispatcher.BeginInvoke(() => { input = true; frame.Continue = false; }, DispatcherPriority.Input);
            Dispatcher.PushFrame(frame);
            Check.True(input && !release.IsSet, "UI input progresses before IO is released");
        }
        finally { release.Set(); }
        Check.True(ready.Wait(TimeSpan.FromSeconds(3)), "latest path is published");
        Check.Eq(published, @"C:\c", "obsolete A cannot publish ahead of latest C");
        Check.True(reads.SequenceEqual(new[] { @"C:\a", @"C:\c" }), "B was coalesced away");
        Check.True(loadedOn != owner, "filesystem work is off caller");
        Check.Eq(lookup.Read(@"C:\c")?.Full, @"C:\c", "fresh completed snapshot is available");
        Check.Eq(reads.Count, 2, "same-key cache does not start another IO");
    }

    public static void ReturningToSamePathAndCacheExpiryDoNotLoseLatestRequest()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var ready = new AutoResetEvent(false);
        long time = 0;
        int loads = 0;
        using var lookup = new PathLookup(full =>
        {
            if (Interlocked.Increment(ref loads) == 1)
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            }
            return Answer(full);
        }, () => Interlocked.Read(ref time));
        lookup.Updated += _ => ready.Set();
        try
        {
            lookup.Read(@"C:\a");
            Check.True(entered.Wait(TimeSpan.FromSeconds(3)), "A starts");
            lookup.Read(@"C:\b");
            lookup.Read(@"C:\a");
            Interlocked.Exchange(ref time, 4000); // long IO does not exhaust the TTL before publication
        }
        finally { release.Set(); }
        Check.True(ready.WaitOne(TimeSpan.FromSeconds(3)), "last A completes");
        var first = lookup.Read(@"C:\a");
        Check.Eq(first?.Full, @"C:\a", "A-B-A converges to final A");
        Check.Eq(loads, 2, "repeated A reads after completion remain cached");
        Interlocked.Exchange(ref time, 4999);
        Check.True(ReferenceEquals(first, lookup.Read(@"C:\a")), "TTL begins at completion");
        Interlocked.Exchange(ref time, 5000);
        Check.True(ReferenceEquals(first, lookup.Read(@"C:\a")), "expired same-key read retains usable snapshot");
        Check.True(ready.WaitOne(TimeSpan.FromSeconds(3)), "expiry refresh completes");
        Check.Eq(loads, 3, "one refresh at expiry");
    }

    public static void FailureCanRetryAndClearDiscardsLateResults()
    {
        using var ready = new AutoResetEvent(false);
        int loads = 0;
        long time = 0;
        using var lookup = new PathLookup(full =>
        {
            if (Interlocked.Increment(ref loads) == 1) throw new InvalidOperationException("synthetic filesystem failure");
            return Answer(full);
        }, () => time);
        lookup.Updated += _ => ready.Set();
        lookup.Read(@"C:\a");
        Check.True(ready.WaitOne(TimeSpan.FromSeconds(3)), "failed read completes without a stuck loader");
        Check.True(lookup.Read(@"C:\a") is { Exists: false }, "failure produces a non-actionable missing path");
        time = 1000;
        lookup.Read(@"C:\a");
        Check.True(ready.WaitOne(TimeSpan.FromSeconds(3)), "next expiry retries");
        Check.True(lookup.Read(@"C:\a") is { Exists: true }, "retry recovers");

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        using var blocked = new PathLookup(full =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            finished.Set();
            return Answer(full);
        });
        int updates = 0;
        blocked.Updated += _ => Interlocked.Increment(ref updates);
        try
        {
            blocked.Read(@"C:\old");
            Check.True(entered.Wait(TimeSpan.FromSeconds(3)), "blocked read starts");
            blocked.Clear();
            blocked.Dispose();
        }
        finally { release.Set(); }
        Check.True(finished.Wait(TimeSpan.FromSeconds(3)), "native-like IO can eventually finish");
        Check.Eq(blocked.Read(@"C:\old"), null, "closed loader never revives a result");
        Check.Eq(updates, 0, "closed lookup does not publish");
    }

    public static void PendingRowsCannotOpenAnOldPathAndNetworkPathsNeverWaitForIo()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var lookup = new PathLookup(full =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return Answer(full);
        });
        var engine = new Engine(new Host(new Options()), lookup);
        try
        {
            var item = engine.Root(@"C:\pending").Single().Items.Single();
            Check.Eq(item.Kind, ItemKind.Info, "loading row is not an old file or folder");
            Check.Eq(item.Run, null, "Enter cannot run a stale action while loading");
            Check.Eq(Engine.DigInto(@"C:\pending", item), null, "Tab cannot enter a stale folder");
            Check.True(entered.Wait(TimeSpan.FromSeconds(3)), "local read is pending");
            var network = engine.Root(@"\\unavailable\share").Single().Items.Single();
            Check.Eq(network.Kind, ItemKind.Folder, "network paths retain open-as-typed action");
            Check.Eq(network.Data, @"\\unavailable\share", "UNC spelling stays intact");
            Check.True(network.Detail is null && network.ReadDetail is null, "no network preview probes");
            Check.Eq(Engine.DigInto(@"\\unavailable\share", network), null, "network Tab never enumerates");
        }
        finally { release.Set(); }
    }
}
