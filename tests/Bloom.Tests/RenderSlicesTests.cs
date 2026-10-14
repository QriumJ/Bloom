using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using Bloom.Ui;

namespace Bloom.Tests;

internal static class RenderSlicesTests
{
    public static void InputRunsBetweenSlicesAndStepsKeepOrder()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        var order = new List<int>();
        int inputAt = -1, completed = 0;
        dispatcher.BeginInvoke(() => RenderSlices.Run(dispatcher, 24, 100, i =>
        {
            order.Add(i);
            if (i == 24) dispatcher.BeginInvoke(() => inputAt = order.Count, DispatcherPriority.Input);
        }, () => true, () =>
        {
            completed++;
            frame.Continue = false;
        }), DispatcherPriority.Background);
        Dispatcher.PushFrame(frame);
        Check.True(inputAt >= 1 && inputAt <= 4, "input runs after at most four tail steps, before the whole list");
        Check.True(order.SequenceEqual(Enumerable.Range(24, 76)), "section/row/bookkeeping steps retain their order");
        Check.Eq(completed, 1, "completion runs once after the final step");
    }

    public static void NewRenderCancelsQueuedContinuation()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        bool current = true;
        int built = 0, completed = 0;
        dispatcher.BeginInvoke(() => RenderSlices.Run(dispatcher, 0, 100, _ =>
        {
            built++;
            if (built == 1) dispatcher.BeginInvoke(() =>
            {
                current = false;
                dispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ApplicationIdle);
            }, DispatcherPriority.Input);
        }, () => current, () => completed++), DispatcherPriority.Background);
        Dispatcher.PushFrame(frame);
        Check.True(built >= 1 && built <= 4, "stale queued rows stop at the next slice");
        Check.Eq(completed, 0, "stale completion cannot restore old scroll/home state");
    }

    public static void LongStepYieldsAtTimeBudgetAndChecksCancellationPerStep()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        bool current = true;
        int built = 0;
        RenderSlices.Run(dispatcher, 0, 100, _ =>
        {
            built++;
            Thread.Sleep(8); // one expensive row: the elapsed budget must yield even below the count cap
        }, () => current);
        Check.Eq(built, 1, "a slow row ends this slice");
        current = false;
        dispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ApplicationIdle);
        Dispatcher.PushFrame(frame);
        Check.Eq(built, 1, "queued continuation is cancelled");

        current = true;
        RenderSlices.Run(dispatcher, 0, 100, _ => { built++; current = false; }, () => current);
        Check.Eq(built, 2, "invalidated render stops before the next step in the same slice");
    }
}
