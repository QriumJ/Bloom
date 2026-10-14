using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.App;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.Tests;

internal static class PreviewLoaderTests
{
    public static void InputRunsDuringBlockedReadAndOnlyLastSelectionDisplays()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var loaded = new List<string>();
        var shown = new List<string>();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var loader = new PreviewLoader(dispatcher, () => true, d => shown.Add(d?.Text ?? "null"));
        int uiThread = Environment.CurrentManagedThreadId, readerThread = 0;
        var first = loader.Request(() =>
        {
            readerThread = Environment.CurrentManagedThreadId;
            loaded.Add("A");
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("reader timeout");
            return new Detail { Text = "A" };
        });
        Task last = Task.CompletedTask;
        bool inputRan = false;
        try
        {
            Check.True(entered.Wait(TimeSpan.FromSeconds(3)), "file read starts independently");
            _ = dispatcher.BeginInvoke(() =>
            {
                inputRan = true;
                _ = loader.Request(() => { loaded.Add("B"); return new Detail { Text = "B" }; });
                last = loader.Request(() => { loaded.Add("C"); return new Detail { Text = "C" }; });
                release.Set();
            }, DispatcherPriority.Input);
            Drain(dispatcher);
        }
        finally { release.Set(); }
        Check.True(inputRan, "actual dispatcher Input progresses while the file reader is blocked");
        Check.True(readerThread != uiThread, "the file factory does not run on UI");
        Check.True(first.Wait(TimeSpan.FromSeconds(3)) && last.Wait(TimeSpan.FromSeconds(3)), "active and latest reads finish");
        Drain(dispatcher);
        Check.True(loaded.SequenceEqual(new[] { "A", "C" }), "one active reader and only latest pending selection execute");
        Check.True(shown.SequenceEqual(new[] { "C" }), "older selections cannot display after newer input");
    }

    public static void HiddenWindowRejectsCompletedReadAndCanReadAfterReopening()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        bool visible = true;
        var shown = new List<string>();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var loader = new PreviewLoader(dispatcher, () => visible, d => shown.Add(d?.Text ?? "null"));
        var old = loader.Request(() =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("reader timeout");
            return new Detail { Text = "hidden" };
        });
        try
        {
            Check.True(entered.Wait(TimeSpan.FromSeconds(3)), "old read started");
            visible = false;
        }
        finally { release.Set(); }
        Check.True(old.Wait(TimeSpan.FromSeconds(3)), "read can finish after hiding");
        Drain(dispatcher);
        Check.Eq(shown.Count, 0, "hidden completion does not restart the preview");
        visible = true;
        var fresh = loader.Request(() => new Detail { Text = "reopened" });
        Check.True(fresh.Wait(TimeSpan.FromSeconds(3)), "a reopened selection can read");
        Drain(dispatcher);
        Check.True(shown.SequenceEqual(new[] { "reopened" }), "only the reopened preview displays");
    }

    public static void NullClearRetiresAlreadyQueuedCompletionAndPendingRead()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        int shown = 0;
        using var loader = new PreviewLoader(dispatcher, () => true, _ => shown++);
        var queued = loader.Request(() => new Detail { Text = "already queued" });
        Check.True(queued.Wait(TimeSpan.FromSeconds(3)), "completion posts without waiting for UI");
        loader.Invalidate(); // the ShowPreview(null) lifecycle operation
        Drain(dispatcher);
        Check.Eq(shown, 0, "null clear rejects a completion already waiting on the UI queue");

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int pendingReads = 0;
        var active = loader.Request(() =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("reader timeout");
            return new Detail();
        });
        Task pending = Task.CompletedTask;
        try
        {
            Check.True(entered.Wait(TimeSpan.FromSeconds(3)), "another read is active");
            pending = loader.Request(() => { pendingReads++; return new Detail(); });
            loader.Invalidate();
        }
        finally { release.Set(); }
        Check.True(active.Wait(TimeSpan.FromSeconds(3)) && pending.Wait(TimeSpan.FromSeconds(3)), "logical cancellation releases all request completions");
        Drain(dispatcher);
        Check.Eq(pendingReads, 0, "cleared pending selection never performs IO");
        Check.Eq(shown, 0, "cleared active result never displays");
    }

    public static void FailedReaderReleasesQueueAndDisposalRejectsPostedResult()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var shown = new List<string>();
        using var loader = new PreviewLoader(dispatcher, () => true, d => shown.Add(d?.Text ?? "null"));
        var failed = loader.Request(() => throw new IOException("synthetic preview failure"));
        Check.True(failed.Wait(TimeSpan.FromSeconds(3)), "failure releases active read");
        Drain(dispatcher);
        Check.True(shown.SequenceEqual(new[] { "null" }), "failure clears detail rather than displaying stale media");
        var fresh = loader.Request(() => new Detail { Text = "fresh" });
        Check.True(fresh.Wait(TimeSpan.FromSeconds(3)), "a later selection is not stranded by failure");
        Drain(dispatcher);
        Check.True(shown.SequenceEqual(new[] { "null", "fresh" }), "later selection displays normally");
        var closing = loader.Request(() => new Detail { Text = "closed" });
        Check.True(closing.Wait(TimeSpan.FromSeconds(3)), "closing result posts");
        loader.Dispose();
        Drain(dispatcher);
        Check.Eq(shown.Count, 2, "disposal rejects already posted preview work");
    }

    public static void FilePreviewCapturesMutableSearchMetadataBeforeWorkerRead()
    {
        var engine = new Engine(new Host(new Options()));
        var info = new EvItem { Size = 1024, Modified = new DateTime(2026, 1, 2, 3, 4, 0) };
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "not-created.txt");
        var item = engine.FromPath(path, false, info);
        Check.True(item.Detail is null && item.ReadDetail is not null, "file preview explicitly opts into the worker factory");
        info.Size = 999999;
        info.Modified = new DateTime(2030, 1, 1);
        var detail = item.ReadDetail!();
        Check.True(detail.Meta.Any(m => m.value == "1 KB"), "preview retains the metadata snapshot of the selected row");
        Check.True(detail.Meta.Any(m => m.value == "2026. 1. 2. 03:04"), "mutable source updates cannot change an in-flight preview");
        Check.True(detail.Fill is null && detail.Icon is null, "file factory returns no UI-owned brush/icon objects");
    }

    private static void Drain(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        _ = dispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ApplicationIdle);
        Dispatcher.PushFrame(frame);
    }
}
