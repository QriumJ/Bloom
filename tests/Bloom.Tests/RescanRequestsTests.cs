using System;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Search;

namespace Bloom.Tests;

internal static class RescanRequestsTests
{
    public static void RequestsDuringBlockedScanProduceOneFollowUp()
    {
        var requests = new RescanRequests();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int passes = 0;
        Check.True(requests.Request(), "first request owns the scanner");
        var scanner = Task.Run(() =>
        {
            do
            {
                if (Interlocked.Increment(ref passes) == 1)
                {
                    started.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("scan barrier timed out");
                }
            }
            while (requests.NextPass());
        });
        try
        {
            Check.True(started.Wait(TimeSpan.FromSeconds(5)), "fake scan is running");
            for (int i = 0; i < 100; i++) Check.True(!requests.Request(), "requests during scan do not start parallel workers");
        }
        finally
        {
            release.Set();
        }
        Check.True(scanner.Wait(TimeSpan.FromSeconds(5)), "follow-up finishes");
        Check.Eq(passes, 2, "burst during a scan coalesces to one additional pass");
        Check.True(requests.Request(), "a later request starts a new worker");
        Check.True(!requests.NextPass(), "completed idle scan stops");
    }

    public static void FollowUpAlsoRetainsNewRequests()
    {
        var requests = new RescanRequests();
        Check.True(requests.Request(), "start");
        Check.True(!requests.Request(), "request during first pass");
        Check.True(requests.NextPass(), "follow-up pending");
        Check.True(!requests.Request(), "request during follow-up");
        Check.True(requests.NextPass(), "another follow-up pending");
        Check.True(!requests.NextPass(), "no requests left");
    }
}
