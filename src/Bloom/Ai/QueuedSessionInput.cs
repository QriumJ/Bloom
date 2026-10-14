using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// A process's sole stdin owner. Encoding and pipe writes run in FIFO order away from the UI;
/// callers never take a lock held across IO, and cancellation never closes/flushed stdin on the UI.
internal sealed class QueuedSessionInput : IDisposable
{
    private sealed record Frame(Func<CancellationToken, string> Make, TaskCompletionSource<bool>? Delivered);
    private readonly object gate = new();
    private readonly Channel<Frame> queue = Channel.CreateUnbounded<Frame>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly CancellationTokenSource cancellation = new();
    private readonly Func<string, CancellationToken, Task> write;
    private readonly Action close;
    private readonly Action<Exception> failed;
    private bool closed, disposed;

    public Task Completion { get; }

    public QueuedSessionInput(StreamWriter writer, bool lines, Action<Exception> failed) : this(async (text, token) =>
    {
        if (lines) await writer.WriteLineAsync(text.AsMemory(), token).ConfigureAwait(false);
        else await writer.WriteAsync(text.AsMemory(), token).ConfigureAwait(false);
        await writer.FlushAsync(token).ConfigureAwait(false);
    }, () =>
    {
        // Close the pipe first: disposing a writer with a cancelled buffered write must not flush again.
        writer.BaseStream.Dispose();
        try { writer.Dispose(); }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { }
    }, failed)
    { }

    internal QueuedSessionInput(Func<string, CancellationToken, Task> write, Action close, Action<Exception> failed)
    {
        this.write = write;
        this.close = close;
        this.failed = failed;
        Completion = Task.Run(Run);
    }

    public bool Enqueue(Func<CancellationToken, string> make, bool end = false) => Enqueue(new Frame(make, null), end);

    /// True only after this frame's write and flush have completed. Failure/cancellation resolve false.
    public Task<bool> EnqueueTracked(Func<CancellationToken, string> make)
    {
        var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Enqueue(new Frame(make, delivered), end: false)) delivered.TrySetResult(false);
        return delivered.Task;
    }

    private bool Enqueue(Frame frame, bool end)
    {
        lock (gate)
        {
            if (closed || !queue.Writer.TryWrite(frame)) return false;
            if (end)
            {
                closed = true;
                queue.Writer.TryComplete();
            }
            return true;
        }
    }

    private async Task Run()
    {
        Frame? active = null;
        try
        {
            await foreach (var frame in queue.Reader.ReadAllAsync(cancellation.Token).ConfigureAwait(false))
            {
                active = frame;
                cancellation.Token.ThrowIfCancellationRequested();
                string text = frame.Make(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                await write(text, cancellation.Token).ConfigureAwait(false);
                frame.Delivered?.TrySetResult(true);
                active = null;
            }
        }
        catch (Exception e)
        {
            if (!cancellation.IsCancellationRequested) failed(e);
        }
        finally
        {
            lock (gate)
            {
                closed = disposed = true;
                queue.Writer.TryComplete();
                cancellation.Dispose();
            }
            active?.Delivered?.TrySetResult(false);
            while (queue.Reader.TryRead(out var frame)) frame.Delivered?.TrySetResult(false);
            try { close(); }
            catch (Exception e) { Log.Error("session input close", e); }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            closed = disposed = true;
            queue.Writer.TryComplete();
            _ = cancellation.CancelAsync();
        }
    }
}
