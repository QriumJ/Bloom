using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// One file-detail read at a time. Selection changes replace the pending read and retire old completions.
internal sealed class PreviewLoader : IDisposable
{
    private sealed record Read(long Version, Func<Detail> Load, TaskCompletionSource Done);
    private readonly object gate = new();
    private readonly Dispatcher dispatcher;
    private readonly Func<bool> visible;
    private readonly Action<Detail?> show;
    private Read? pending;
    private bool running, disposed;
    private long version;

    public PreviewLoader(Dispatcher dispatcher, Func<bool> visible, Action<Detail?> show)
    {
        this.dispatcher = dispatcher;
        this.visible = visible;
        this.show = show;
    }

    public Task Request(Func<Detail> load)
    {
        dispatcher.VerifyAccess();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (disposed) { done.SetResult(); return done.Task; }
            pending?.Done.TrySetResult();
            pending = new Read(++version, load, done);
            if (!running)
            {
                running = true;
                _ = Task.Run(Run);
            }
        }
        return done.Task;
    }

    public void Invalidate()
    {
        dispatcher.VerifyAccess();
        lock (gate)
        {
            ++version;
            pending?.Done.TrySetResult();
            pending = null;
        }
    }

    public void Dispose()
    {
        dispatcher.VerifyAccess();
        lock (gate)
        {
            disposed = true;
            ++version;
            pending?.Done.TrySetResult();
            pending = null;
        }
    }

    private void Run()
    {
        while (true)
        {
            Read read;
            lock (gate)
            {
                if (pending is not { } next) { running = false; return; }
                read = next;
                pending = null;
            }
            Detail? detail = null;
            try { detail = read.Load(); }
            catch (Exception e) { Log.Error("preview read", e); }
            try
            {
                if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                    _ = dispatcher.BeginInvoke(() =>
                    {
                        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
                        lock (gate)
                            if (disposed || read.Version != version) return;
                        // Visibility and rendering belong to this dispatcher, outside the worker lock.
                        if (visible()) show(detail);
                    }, DispatcherPriority.Background);
            }
            catch (InvalidOperationException) { /* dispatcher closed while the file read was in progress */ }
            finally { read.Done.TrySetResult(); }
        }
    }
}
