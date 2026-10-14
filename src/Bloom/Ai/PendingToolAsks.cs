using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Bloom.Ai;

/// Approval requests belong to one CLI run. Completing that run denies only its own requests
/// and releases every timeout immediately, including requests the user already answered.
internal sealed class PendingToolAsks : IDisposable
{
    private sealed class Pending
    {
        public readonly TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Timer? Timeout;
    }

    private readonly object gate = new();
    private readonly Dictionary<string, Pending> pending = new();
    private readonly Action<string> withdrawn;
    private readonly TimeSpan timeout;
    private bool closed;

    public PendingToolAsks(Action<string> withdrawn, TimeSpan? timeout = null)
    {
        this.withdrawn = withdrawn;
        this.timeout = timeout ?? TimeSpan.FromMinutes(9);
    }

    public Task<bool> Add(string id)
    {
        lock (gate)
        {
            if (closed) return Task.FromResult(false);
            var item = new Pending();
            pending.Add(id, item);
            item.Timeout = new Timer(_ => Complete(id, false, notify: true), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            item.Timeout.Change(timeout, Timeout.InfiniteTimeSpan);
            return item.Completion.Task;
        }
    }

    public void Answer(string id, bool allow) => Complete(id, allow, notify: false);

    private void Complete(string id, bool allow, bool notify)
    {
        Pending? item;
        lock (gate)
        {
            if (!pending.Remove(id, out item)) return;
            item.Timeout?.Dispose();
            item.Completion.TrySetResult(allow);
        }
        if (notify) withdrawn(id);
    }

    public void Dispose()
    {
        string[] ids;
        lock (gate)
        {
            closed = true;
            ids = new string[pending.Count];
            pending.Keys.CopyTo(ids, 0);
            foreach (var item in pending.Values)
            {
                item.Timeout?.Dispose();
                item.Completion.TrySetResult(false);
            }
            pending.Clear();
        }
        foreach (string id in ids) withdrawn(id);
    }
}
