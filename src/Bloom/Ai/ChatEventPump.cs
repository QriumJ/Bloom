using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows.Threading;
using Bloom.Core;

namespace Bloom.Ai;

/// One ordered UI queue per session. Consecutive deltas become one append; control events
/// keep their position, and each bounded background drain yields to input and rendering.
internal sealed class ChatEventPump(Dispatcher dispatcher)
{
    private sealed class Work(Action? action, Action<string>? text = null, string? piece = null)
    {
        public readonly Action? Action = action;
        public readonly Action<string>? Text = text;
        public readonly StringBuilder? Pieces = piece is null ? null : new(piece);
    }

    private readonly object gate = new();
    private readonly Queue<Work> queue = new();
    private Work? tail;
    private bool scheduled;
    private const int MaxEvents = 32;
    private static readonly TimeSpan Slice = TimeSpan.FromMilliseconds(4);

    public void Post(Action action)
    {
        lock (gate)
        {
            tail = new Work(action);
            queue.Enqueue(tail);
            Schedule();
        }
    }

    public void PostText(string piece, Action<string> accept)
    {
        if (piece.Length == 0) return;
        lock (gate)
        {
            if (tail?.Text == accept) tail.Pieces!.Append(piece);
            else
            {
                tail = new Work(null, accept, piece);
                queue.Enqueue(tail);
            }
            Schedule();
        }
    }

    private void Schedule()
    {
        if (scheduled) return;
        scheduled = true;
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Drain));
    }

    private void Drain()
    {
        long start = Stopwatch.GetTimestamp();
        int count = 0;
        while (count < MaxEvents && (count == 0 || Stopwatch.GetElapsedTime(start) < Slice))
        {
            count++;
            Work work;
            string? text;
            lock (gate)
            {
                if (queue.Count == 0) break;
                work = queue.Dequeue();
                if (queue.Count == 0) tail = null;
                text = work.Pieces?.ToString();
            }
            try
            {
                if (text is not null) work.Text!(text);
                else work.Action!();
            }
            catch (Exception e)
            {
                Log.Error("chat event", e);
            }
        }
        lock (gate)
        {
            scheduled = false;
            if (queue.Count > 0) Schedule();
        }
    }
}
