using System;
using System.Threading;
using Bloom.Core;

namespace Bloom.Tests;

internal static class BackgroundSnapshotTests
{
    public static void SlowDiscoveryDoesNotBlockReadersAndIsShared()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int loads = 0;
        ApartmentState apartment = ApartmentState.Unknown;
        var cache = new BackgroundSnapshot<string>("previous", () =>
        {
            Interlocked.Increment(ref loads);
            apartment = Thread.CurrentThread.GetApartmentState();
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("loader timeout");
            return "new";
        }, 10000, "test snapshot");
        var first = cache.Refresh();
        try
        {
            Check.True(entered.Wait(TimeSpan.FromSeconds(3)), "the loader starts independently");
            for (int i = 0; i < 100; i++) Check.Eq(cache.Read(), "previous", "UI can use the previous snapshot while native IO waits");
            Check.True(ReferenceEquals(first, cache.Refresh()), "concurrent refresh callers share one load");
            Check.Eq(loads, 1, "no queue of redundant native calls");
            Check.Eq(apartment, ApartmentState.STA, "shell link discovery runs on STA");
        }
        finally { release.Set(); }
        Check.True(first.Wait(TimeSpan.FromSeconds(3)), "fresh-data caller completes");
        Check.Eq(first.Result, "new", "awaiting clients receive fresh data");
        Check.Eq(cache.Read(), "new", "fresh snapshot is published");
        Check.Eq(loads, 1, "cached reads do not restart the loader");
    }

    public static void InvalidationDuringIoCannotPublishAnOldSnapshot()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int loads = 0;
        var cache = new BackgroundSnapshot<string>("previous", () =>
        {
            int n = Interlocked.Increment(ref loads);
            if (n == 1)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("loader timeout");
            }
            return "generation-" + n;
        }, 10000, "test invalidation");
        var fresh = cache.Refresh();
        try
        {
            Check.True(entered.Wait(TimeSpan.FromSeconds(3)), "first load started");
            cache.Invalidate();
            cache.Invalidate();
            Check.Eq(cache.Read(), "previous", "an invalidated load does not replace the completed cache");
        }
        finally { release.Set(); }
        Check.True(fresh.Wait(TimeSpan.FromSeconds(3)), "latest generation eventually completes");
        Check.Eq(fresh.Result, "generation-2", "fresh waiter sees the follow-up generation");
        Check.Eq(loads, 2, "invalidations are coalesced into one follow-up load");
    }

    public static void FailedDiscoveryRetainsDataAndCanRetry()
    {
        int loads = 0;
        var cache = new BackgroundSnapshot<string>("previous", () =>
        {
            if (Interlocked.Increment(ref loads) == 1) throw new InvalidOperationException("synthetic failure");
            return "recovered";
        }, 10000, "test failure");
        var failed = cache.Refresh();
        Check.True(failed.Wait(TimeSpan.FromSeconds(3)), "failed discovery releases waiters");
        Check.Eq(failed.Result, "previous", "previous data survives transient errors");
        var retry = cache.Refresh();
        Check.True(retry.Wait(TimeSpan.FromSeconds(3)), "explicit retry completes");
        Check.Eq(retry.Result, "recovered", "a failure cannot leave discovery permanently busy");
    }
}
