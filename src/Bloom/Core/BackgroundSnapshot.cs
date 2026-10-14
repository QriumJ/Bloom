using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bloom.Core;

/// Native/IO discovery never runs on the caller. Readers keep the last completed snapshot while
/// one STA worker refreshes it; callers that require fresh data can await Refresh instead.
internal sealed class BackgroundSnapshot<T>
{
    private readonly object gate = new();
    private readonly Func<T> load;
    private readonly int lifetimeMs;
    private readonly string name;
    private T value;
    private long nextRead;
    private int generation;
    private TaskCompletionSource<T>? pending;

    public BackgroundSnapshot(T initial, Func<T> load, int lifetimeMs, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lifetimeMs);
        value = initial;
        this.load = load;
        this.lifetimeMs = lifetimeMs;
        this.name = name;
    }

    public event Action? Updated;

    public T Current { get { lock (gate) return value; } }

    public T Read()
    {
        T snapshot;
        bool due;
        lock (gate)
        {
            snapshot = value;
            due = Environment.TickCount64 >= nextRead;
        }
        if (due) _ = Refresh();
        return snapshot;
    }

    public void Invalidate()
    {
        lock (gate)
        {
            generation++;
            nextRead = 0;
        }
    }

    public Task<T> Refresh()
    {
        TaskCompletionSource<T> request;
        lock (gate)
        {
            if (pending is not null) return pending.Task;
            pending = request = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            var thread = new Thread(() => Run(request)) { IsBackground = true, Name = name, Priority = ThreadPriority.BelowNormal };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        catch (Exception e)
        {
            Log.Error(name, e);
            Complete(request, default!, success: false, version: -1);
        }
        return request.Task;
    }

    private void Run(TaskCompletionSource<T> request)
    {
        while (true)
        {
            int version;
            lock (gate) version = generation;
            T result;
            try { result = load(); }
            catch (Exception e)
            {
                Log.Error(name, e);
                Complete(request, default!, success: false, version);
                return;
            }
            if (Complete(request, result, success: true, version)) return;
        }
    }

    private bool Complete(TaskCompletionSource<T> request, T result, bool success, int version)
    {
        bool changed;
        lock (gate)
        {
            // An invalidation during IO needs another pass before the awaited refresh is fresh.
            if (success && version != generation) return false;
            changed = success && !ReferenceEquals(value, result);
            if (success) value = result;
            nextRead = Environment.TickCount64 + lifetimeMs;
            pending = null;
            request.TrySetResult(value);
        }
        if (changed)
        {
            try { Updated?.Invoke(); }
            catch (Exception e) { Log.Error(name + " updated", e); }
        }
        return true;
    }
}
