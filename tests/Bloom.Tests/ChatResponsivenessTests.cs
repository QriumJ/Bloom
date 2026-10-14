using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Threading;
using Bloom.Ai;

namespace Bloom.Tests;

internal static class ChatResponsivenessTests
{
    public static void TokenBurstCoalescesBeforeTheDispatcher()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var pump = new ChatEventPump(dispatcher);
        var answer = new StringBuilder();
        int appends = 0, scheduled = 0, inputAt = -1;
        bool done = false;
        void Posted(object? sender, DispatcherHookEventArgs args)
        {
            if (args.Operation.Priority == DispatcherPriority.Background) scheduled++;
        }
        void Append(string text) { answer.Append(text); appends++; }
        dispatcher.Hooks.OperationPosted += Posted;
        try
        {
            for (int i = 0; i < 10000; i++) pump.PostText("x", Append);
            pump.Post(() => done = true);
            dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => inputAt = answer.Length));
            Drain();
            Check.Eq(inputAt, 0, "input precedes a queued token burst");
            Check.True(done, "the final event is delivered");
            Check.Eq(answer.ToString(), new string('x', 10000), "coalescing preserves every token");
            Check.Eq(appends, 1, "a contiguous burst appends the growing answer once");
            Check.True(scheduled <= 2, "the burst schedules bounded UI work instead of 10000 callbacks");
        }
        finally { dispatcher.Hooks.OperationPosted -= Posted; }
    }

    public static void ApprovalAndCompletionRemainOrderedWithText()
    {
        var pump = new ChatEventPump(Dispatcher.CurrentDispatcher);
        var events = new List<string>();
        string text = "";
        void Append(string piece) { text += piece; events.Add("text:" + piece); }
        pump.PostText("be", Append);
        pump.PostText("fore", Append);
        pump.Post(() => events.Add("approval:" + text));
        pump.PostText("af", Append);
        pump.PostText("ter", Append);
        pump.Post(() => events.Add("done:" + text));
        Drain();
        Check.Eq(string.Join("|", events), "text:before|approval:before|text:after|done:beforeafter", "control events form ordering boundaries");
    }

    public static void LongEventBacklogYieldsToNewInput()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var pump = new ChatEventPump(dispatcher);
        int processed = 0, inputAt = -1;
        for (int i = 0; i < 200; i++)
            pump.Post(() =>
            {
                processed++;
                if (processed == 1)
                    dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => inputAt = processed));
            });
        Drain();
        Check.Eq(processed, 200, "all ordered events eventually run");
        Check.True(inputAt is > 0 and <= 32, "input runs after a bounded slice, before the whole backlog");
    }

    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
