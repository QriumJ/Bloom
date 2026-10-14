using System;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Ai;

namespace Bloom.Tests;

internal static class InputDeliveryWatchdogTests
{
    public static void StopBehindBlockedPromptAbortsOnlyThatInput()
    {
        using var entered = new ManualResetEventSlim();
        int aborts = 0, laterFrames = 0;
        using var input = new QueuedSessionInput(async (_, token) =>
        {
            entered.Set();
            await Task.Delay(Timeout.Infinite, token);
        }, () => { }, _ => { });
        input.Enqueue(_ => "prompt");
        Check.True(entered.Wait(TimeSpan.FromSeconds(3)), "prompt is blocked in the fake pipe");
        var delivered = input.EnqueueTracked(_ => { laterFrames++; return "interrupt"; });
        using var watchdog = new InputDeliveryWatchdog();
        var watch = watchdog.Watch(delivered, () => { Interlocked.Increment(ref aborts); input.Dispose(); }, TimeSpan.FromMilliseconds(20));
        Check.True(watch.Wait(TimeSpan.FromSeconds(3)), "undelivered Stop reaches its bounded fallback");
        Check.True(input.Completion.Wait(TimeSpan.FromSeconds(3)), "fallback releases the blocked input");
        Check.Eq(aborts, 1, "only one abort is requested");
        Check.True(delivered.IsCompletedSuccessfully && !delivered.Result, "cancelled interrupt delivery resolves false");
        Check.Eq(laterFrames, 0, "no partial/new frame follows the abandoned prompt");
    }

    public static void FlushedInterruptKeepsTheProcessAlive()
    {
        using var input = new QueuedSessionInput((_, _) => Task.CompletedTask, () => { }, _ => { });
        using var watchdog = new InputDeliveryWatchdog();
        int aborts = 0;
        var delivery = input.EnqueueTracked(_ => "interrupt");
        var watch = watchdog.Watch(delivery, () => aborts++);
        Check.True(watch.Wait(TimeSpan.FromSeconds(3)), "successful flush completes the watch");
        Check.True(delivery.Result, "flush completion is reported");
        Check.Eq(aborts, 0, "a delivered control frame does not kill the reusable process");
        Check.True(input.Enqueue(_ => "next turn"), "the input owner remains usable");
    }

    public static void NewTurnCancelsTheOldInterruptTimeout()
    {
        using var watchdog = new InputDeliveryWatchdog();
        var oldDelivery = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextDelivery = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int oldAborts = 0, nextAborts = 0;
        var old = watchdog.Watch(oldDelivery.Task, () => oldAborts++, TimeSpan.FromSeconds(1));
        watchdog.Cancel(); // old turn completes; the session increments its turn identity before Send
        var next = watchdog.Watch(nextDelivery.Task, () => nextAborts++);
        oldDelivery.TrySetResult(false); // a late failed delivery from the prior turn
        nextDelivery.TrySetResult(true);
        Check.True(Task.WaitAll(new[] { old, next }, TimeSpan.FromSeconds(3)), "both watches settle");
        Check.Eq(oldAborts, 0, "late prior failure cannot abort a replacement turn");
        Check.Eq(nextAborts, 0, "current successful delivery is preserved");
    }

    public static void FailedAndCancelledDeliveryTasksAreObserved()
    {
        using var watchdog = new InputDeliveryWatchdog();
        int aborts = 0;
        var failed = watchdog.Watch(Task.FromException<bool>(new InvalidOperationException("synthetic delivery failure")), () => aborts++);
        Check.True(failed.Wait(TimeSpan.FromSeconds(3)), "faulted delivery is observed without a faulted watchdog");
        var cancelled = watchdog.Watch(Task.FromCanceled<bool>(new CancellationToken(true)), () => aborts++);
        Check.True(cancelled.Wait(TimeSpan.FromSeconds(3)), "externally cancelled delivery is observed");
        Check.Eq(aborts, 2, "both undelivered interrupts request bounded cleanup");
    }
}
