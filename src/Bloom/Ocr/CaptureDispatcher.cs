using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Bloom.Ocr;

/// Mouse ink uses its window's UI dispatcher. Keep it independent of the launcher's searches/chat work.
internal static class CaptureDispatcher
{
    public static Task<T> Run<T>(Func<CancellationToken, Task<T>> operation)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            using var abort = new CancellationTokenSource();
            T result = default!;
            Exception? error = null;
            bool finished = false;
            dispatcher.UnhandledException += (_, e) =>
            {
                // Keep the dispatcher alive long enough for the waiting operation's finally to close its windows.
                error ??= e.Exception;
                e.Handled = true;
                abort.Cancel();
            };
            try
            {
                dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        result = await operation(abort.Token);
                        finished = true;
                    }
                    catch (Exception e)
                    {
                        error ??= e;
                    }
                    finally
                    {
                        dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    }
                }));
                Dispatcher.Run();
            }
            catch (Exception e)
            {
                error = e;
            }
            finally
            {
                try
                {
                    if (!dispatcher.HasShutdownFinished) dispatcher.InvokeShutdown();
                }
                catch (Exception e)
                {
                    error ??= e;
                }
                if (error is not null) done.TrySetException(error);
                else if (finished) done.TrySetResult(result);
                else done.TrySetException(new InvalidOperationException("Capture dispatcher stopped before completion."));
            }
        })
        { IsBackground = true, Name = "screen-capture" };
        try
        {
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        catch (Exception e)
        {
            done.TrySetException(e);
        }
        return done.Task;
    }
}
