using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Ai;

namespace Bloom.Tests;

internal static class QueuedSessionInputTests
{
    public static void PayloadsAndWritesRunOffCallerInFifoOrder()
    {
        int owner = Environment.CurrentManagedThreadId, preparedOn = owner, wroteOn = owner, closes = 0;
        Exception? failure = null;
        var written = new List<string>();
        using var input = new QueuedSessionInput((text, _) =>
        {
            wroteOn = Environment.CurrentManagedThreadId;
            written.Add(text);
            return Task.CompletedTask;
        }, () => closes++, e => failure = e);
        Check.True(input.Enqueue(_ => { preparedOn = Environment.CurrentManagedThreadId; return "prompt"; }), "send is accepted");
        Check.True(input.Enqueue(_ => "approval"), "approval is accepted");
        Check.True(input.Enqueue(_ => "interrupt", end: true), "interrupt closes this test stream after its write");
        Check.True(input.Completion.Wait(TimeSpan.FromSeconds(3)), "writer completes");
        Check.True(failure is null, "successful writes do not report failure");
        Check.Eq(string.Join("|", written), "prompt|approval|interrupt", "control frames cannot interleave with the prompt");
        Check.True(preparedOn != owner && wroteOn != owner, "encoding and pipe IO are both off the caller");
        Check.Eq(closes, 1, "stdin has one closing owner");
        Check.True(!input.Enqueue(_ => "late"), "a finished process input rejects further writes");
    }

    public static void BlockedPipeCanBeCancelledWithoutPreparingQueuedPayloads()
    {
        using var started = new ManualResetEventSlim();
        int preparedLater = 0, failures = 0, closes = 0;
        var input = new QueuedSessionInput(async (_, token) =>
        {
            started.Set();
            await Task.Delay(Timeout.Infinite, token);
        }, () => Interlocked.Increment(ref closes), _ => Interlocked.Increment(ref failures));
        try
        {
            Check.True(input.Enqueue(_ => "prompt"), "prompt is accepted");
            Check.True(started.Wait(TimeSpan.FromSeconds(3)), "fake pipe blocks in its writer");
            Check.True(input.Enqueue(_ => { Interlocked.Increment(ref preparedLater); return "approval"; }), "queueing controls does not wait for the blocked pipe");
            input.Dispose();
            Check.True(input.Completion.Wait(TimeSpan.FromSeconds(3)), "cancellation releases the blocked writer");
            Check.Eq(preparedLater, 0, "cancelled payloads are not encoded or written");
            Check.Eq(failures, 0, "ordinary cancellation is not a failed turn");
            Check.Eq(closes, 1, "stdin is closed exactly once");
            Check.True(!input.Enqueue(_ => "late"), "disposed input rejects later work");
        }
        finally { input.Dispose(); }
    }

    public static void EncodingFailureReportsOnceAndDropsFollowingControlFrames()
    {
        int failed = 0, writes = 0, closes = 0;
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var input = new QueuedSessionInput((_, _) => { writes++; return Task.CompletedTask; }, () => closes++, _ => failed++);
        input.Enqueue(_ =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(3), CancellationToken.None);
            throw new InvalidOperationException("synthetic encoding failure");
        });
        Check.True(started.Wait(TimeSpan.FromSeconds(3)), "encoding started");
        input.Enqueue(_ => "must not follow a broken prompt");
        release.Set();
        Check.True(input.Completion.Wait(TimeSpan.FromSeconds(3)), "failure ends the writer");
        Check.Eq(failed, 1, "the reader/process owner receives one failure signal");
        Check.Eq(writes, 0, "no later frame is sent after failure");
        Check.Eq(closes, 1, "failed input is closed");
    }
}
