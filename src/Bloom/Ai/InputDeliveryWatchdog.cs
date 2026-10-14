using System;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// Watches delivery, not AI response time. A new turn cancels the old watch; the process owner
/// must also verify its captured turn/input identity immediately before aborting that process.
internal sealed class InputDeliveryWatchdog : IDisposable
{
    private readonly object gate = new();
    private CancellationTokenSource? active;

    public Task Watch(Task<bool> delivery, Action abort, TimeSpan? timeout = null)
    {
        var source = new CancellationTokenSource();
        lock (gate)
        {
            active?.Cancel();
            active = source;
        }
        return Task.Run(async () =>
        {
            bool delivered = false;
            try
            {
                try { delivered = await delivery.WaitAsync(timeout ?? TimeSpan.FromSeconds(1), source.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (source.IsCancellationRequested) { return; }
                catch (Exception) { /* failed, cancelled, or timed-out delivery also needs to release the turn */ }
                lock (gate)
                {
                    if (!ReferenceEquals(active, source) || source.IsCancellationRequested) return;
                    active = null;
                }
                if (!delivered) abort();
            }
            catch (Exception e) { Log.Error("interrupt delivery", e); }
            finally
            {
                lock (gate)
                {
                    if (ReferenceEquals(active, source)) active = null;
                    source.Dispose();
                }
            }
        });
    }

    public void Cancel()
    {
        lock (gate)
        {
            active?.Cancel();
            active = null;
        }
    }

    public void Dispose() => Cancel();
}
