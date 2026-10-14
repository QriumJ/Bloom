using System;
using System.Threading;
using Bloom.Core;

namespace Bloom.Search;

/// One filesystem read at a time; keystrokes replace the pending request, never queue a scan each.
/// Only the current path's snapshot can be returned. Native IO may not cancel, but never holds UI.
internal sealed class PathLookup : IDisposable
{
    private sealed class Request(string path, PathAnswer? previous = null)
    {
        public readonly string Path = path;
        public PathAnswer? Answer = previous;
        public bool Finished;
        public long Expires;
    }

    private readonly object gate = new();
    private readonly Func<string, PathAnswer> read;
    private readonly Func<long> now;
    private Request? current, pending;
    private bool running, disposed;

    public PathLookup(Func<string, PathAnswer>? read = null, Func<long>? now = null)
    {
        this.read = read ?? PathInput.ReadExpanded;
        this.now = now ?? (() => Environment.TickCount64);
    }

    public event Action<string>? Updated;

    public PathAnswer? Read(string full)
    {
        bool start = false;
        PathAnswer? answer;
        lock (gate)
        {
            if (disposed) return null;
            bool same = current?.Path == full;
            if (!same || (current!.Finished && now() >= current.Expires))
            {
                current = pending = new Request(full, same ? current!.Answer : null);
                if (!running) running = start = true;
            }
            answer = current!.Answer;
        }
        if (start)
        {
            try
            {
                new Thread(Run) { IsBackground = true, Name = "typed-path", Priority = ThreadPriority.BelowNormal }.Start();
            }
            catch (Exception e)
            {
                Log.Error("typed path worker", e);
                string? failed;
                lock (gate)
                {
                    failed = pending?.Path;
                    if (pending is { } request) Publish(request, Empty(request.Path));
                    pending = null;
                    running = false;
                }
                if (failed is not null) Notify(failed);
            }
        }
        return answer;
    }

    public void Clear()
    {
        lock (gate) current = pending = null;
    }

    private void Run()
    {
        while (true)
        {
            Request request;
            lock (gate)
            {
                if (disposed || pending is null)
                {
                    running = false;
                    return;
                }
                request = pending;
                pending = null;
            }
            PathAnswer answer;
            try { answer = read(request.Path); }
            catch (Exception e)
            {
                Log.Error("typed path read", e);
                answer = Empty(request.Path);
            }
            bool notify;
            lock (gate)
            {
                notify = !disposed && ReferenceEquals(current, request);
                if (notify) Publish(request, answer);
            }
            if (notify) Notify(request.Path);
        }
    }

    // gate held; TTL starts when IO completes, even if the read was slow.
    private void Publish(Request request, PathAnswer answer)
    {
        request.Answer = answer;
        request.Finished = true;
        request.Expires = now() + 1000;
    }

    private void Notify(string full)
    {
        try { Updated?.Invoke(full); }
        catch (Exception e) { Log.Error("typed path updated", e); }
    }

    private static PathAnswer Empty(string full) => new(full, true, false, Array.Empty<(string, bool)>());

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            current = pending = null;
        }
    }
}
