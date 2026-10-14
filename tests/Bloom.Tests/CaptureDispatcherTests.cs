using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Bloom.Ocr;

namespace Bloom.Tests;

internal static class CaptureDispatcherTests
{
    public static void MouseDrawingDispatcherWorksWhileCallerIsBlocked()
    {
        var owner = Dispatcher.CurrentDispatcher;
        int ownerThread = Environment.CurrentManagedThreadId, savedOn = -1;
        var theme = DrawTheme.Capture(); // initialize/copy the application's mutable palette on its owner
        var preferences = new DrawPreferences("pen", 0, 1, 1, 1);
        var persist = DrawPreferences.PersistOn(owner, _ => savedOn = Environment.CurrentManagedThreadId);
        var result = CaptureDispatcher.Run(async abort =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            Check.Eq(Thread.CurrentThread.GetApartmentState(), ApartmentState.STA, "capture uses STA");
            Check.True(!ReferenceEquals(dispatcher, owner), "drawing has its own dispatcher");
            int thread = Environment.CurrentManagedThreadId;
            var canvas = new Canvas();
            var layer = new DrawLayer(canvas, new Rect(0, 0, 64, 48), new Size(300, 200), _ => { }, preferences, theme, persist);
            await dispatcher.InvokeAsync(() => layer.AddForQa("pen", 2, new Point(4, 16), new Point(40, 16)), DispatcherPriority.Input, abort);
            await Task.Delay(10, abort);
            Check.Eq(Environment.CurrentManagedThreadId, thread, "await resumes on capture dispatcher");
            byte[] pixels = Enumerable.Repeat((byte)255, 64 * 48 * 4).ToArray();
            Check.True(!layer.Merge(pixels, 64, 48).SequenceEqual(pixels), "drawing and export finish independently");
            return (thread, dispatcher);
        });
        // Deliberately don't pump the caller dispatcher: drawing input/export must still finish.
        Check.True(result.Wait(TimeSpan.FromSeconds(10)), "capture completes with main UI blocked");
        Check.True(result.Result.thread != ownerThread, "capture executes on another thread");
        Check.True(result.Result.dispatcher.HasShutdownFinished, "completion includes dispatcher teardown");
        Check.Eq(savedOn, -1, "preference persistence waits for its owner rather than running on capture thread");
        var frame = new DispatcherFrame();
        owner.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ApplicationIdle);
        Dispatcher.PushFrame(frame);
        Check.Eq(savedOn, ownerThread, "preferences are stored only on the application's owner");
    }

    public static void FailureAndCancellationShutDownTheirDispatcher()
    {
        Dispatcher? failed = null;
        var failure = CaptureDispatcher.Run<int>(async abort =>
        {
            failed = Dispatcher.CurrentDispatcher;
            await Task.Delay(1, abort);
            throw new InvalidOperationException("synthetic capture failure");
        });
        bool rejected = false;
        try { failure.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException e) { rejected = e.InnerException is InvalidOperationException; }
        Check.True(rejected, "capture errors reach the caller");
        Check.True(failed?.HasShutdownFinished == true, "failed capture dispatcher is torn down");

        Dispatcher? cancelled = null;
        var cancellation = CaptureDispatcher.Run<ScreenRect?>(_ =>
        {
            cancelled = Dispatcher.CurrentDispatcher;
            return Task.FromResult<ScreenRect?>(null);
        });
        Check.True(cancellation.Wait(TimeSpan.FromSeconds(5)), "cancelled capture completes");
        Check.Eq(cancellation.Result, null, "cancellation keeps the public null result");
        Check.True(cancelled?.HasShutdownFinished == true, "cancelled capture dispatcher is torn down");
    }

    public static void RoutedCallbackFailureClosesWaitingOperationBeforeShutdown()
    {
        Dispatcher? worker = null;
        bool closed = false, cleaned = false;
        int workerThread = -1, cleanupThread = -1;
        var result = CaptureDispatcher.Run<int>(async abort =>
        {
            worker = Dispatcher.CurrentDispatcher;
            workerThread = Environment.CurrentManagedThreadId;
            var window = new Window { ShowInTaskbar = false, ShowActivated = false };
            _ = new WindowInteropHelper(window).EnsureHandle(); // an owned native source without showing a window
            window.Closed += (_, _) => closed = true;
            try
            {
                var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = worker.BeginInvoke(new Action(() => throw new InvalidOperationException("synthetic routed callback")), DispatcherPriority.Input);
                return await pending.Task.WaitAsync(abort);
            }
            finally
            {
                window.Close();
                cleanupThread = Environment.CurrentManagedThreadId;
                cleaned = true;
            }
        });
        bool rejected = false;
        try { result.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException e) { rejected = e.InnerException is InvalidOperationException { Message: "synthetic routed callback" }; }
        Check.True(rejected, "the original routed failure reaches the caller");
        Check.True(cleaned && closed, "operation finally and native-window closure finish before return");
        Check.Eq(cleanupThread, workerThread, "cleanup happens on the window's owner");
        Check.True(worker?.HasShutdownFinished == true, "dispatcher shuts down after operation cleanup");
    }

    public static void PreferenceEditsPrecedeOwnerCompletionAtNormalPriority()
    {
        var owner = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        int saved = 0, observed = -1;
        var persist = DrawPreferences.PersistOn(owner, value => saved = value.PenColor);
        var result = CaptureDispatcher.Run(_ =>
        {
            persist(new DrawPreferences("pen", 3, 1, 1, 1));
            owner.BeginInvoke(() => { observed = saved; frame.Continue = false; }, DispatcherPriority.Normal);
            return Task.FromResult(0);
        });
        Check.True(result.Wait(TimeSpan.FromSeconds(5)), "capture finishes while owner is blocked");
        Dispatcher.PushFrame(frame);
        Check.Eq(observed, 3, "normal owner completion sees the preceding preference edit");
    }
}
