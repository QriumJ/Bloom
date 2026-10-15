using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Threading;
using Bloom.App;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.Tests;

internal static class LauncherPreviewTests
{
    public static void HiddenSelectedFilePreviewStartsOnShowAndRestartsAfterHide() => WithLauncher(window =>
    {
        int reads = 0;
        const string text = "synthetic selected file preview";
        var section = new Section(null);
        section.Items.Add(new ResultItem
        {
            Id = "synthetic:file",
            Title = "Synthetic file",
            Kind = ItemKind.File,
            Icon = IconSpec.Of(G.Document),
            ReadDetail = () =>
            {
                Interlocked.Increment(ref reads);
                return new Detail { Text = text };
            },
        });
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(Launcher).GetField("views", instance)!.SetValue(window, new Dictionary<Mode, ViewSpec>
        {
            [Mode.Root] = new(),
            [Mode.Clipboard] = new() { Split = true, Context = () => "Synthetic clipboard" },
        });
        typeof(Launcher).GetMethod("SetMode", instance)!.Invoke(window, new object[] { Mode.Clipboard });
        window.QaRender(new List<Section> { section });
        Check.True(!window.IsVisible, "selection is rendered before the actual window becomes visible");
        Check.Eq(Volatile.Read(ref reads), 0, "hidden selected file does not read early");
        Check.Eq(window.Selected?.Id, "synthetic:file", "hidden render has the selected file row");

        window.Show();
        PumpUntil(() => PreviewText(window) == text);
        Check.Eq(Volatile.Read(ref reads), 1, "actual Show starts the selected preview without a new selection");
        window.Hide();
        Check.True(PreviewText(window) is null, "actual Hide clears the preview");
        window.Show();
        PumpUntil(() => PreviewText(window) == text);
        Check.Eq(Volatile.Read(ref reads), 2, "same-id selected file is read again on reopening");
    });

    /// A launcher for a test, off the screen and never activated, its host not started (Host.Start is deliberately not
    /// called: no settings, clipboard, disk index or hooks); closed after `test`.
    internal static void WithLauncher(Action<Launcher> test)
    {
        const BindingFlags fields = BindingFlags.Static | BindingFlags.NonPublic;
        var changed = typeof(Theme).GetField("Changed", fields)!;
        var scaled = typeof(Theme).GetField("ScaleChanged", fields)!;
        var beforeChanged = changed.GetValue(null);
        var beforeScaled = scaled.GetValue(null);
        bool beforeNoActivate = Launcher.NoActivateForQa;
        Launcher? window = null;
        try
        {
            Launcher.NoActivateForQa = true;
            var host = new Host(new Options());
            host.Engine = new Engine(host);
            host.Tools = new ToolDock(host);
            window = new Launcher(host)
            {
                Left = -32000,
                Top = -32000,
                ShowActivated = false,
                Topmost = false,
                Capturing = true,
                AllowClose = true,
            };
            host.Window = window;
            test(window);
        }
        finally
        {
            try { window?.Close(); }
            finally
            {
                // Constructor-only test fixtures must not remain rooted by static theme subscriptions.
                changed.SetValue(null, beforeChanged);
                scaled.SetValue(null, beforeScaled);
                Launcher.NoActivateForQa = beforeNoActivate;
            }
        }
    }

    private static string? PreviewText(Launcher window)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var preview = (PreviewPane)typeof(Launcher).GetField("preview", flags)!.GetValue(window)!;
        var media = (SmoothBorder)typeof(PreviewPane).GetField("media", flags)!.GetValue(preview)!;
        return (media.Child as ScrollViewer)?.Content is TextBlock text ? text.Text : null;
    }

    private static void PumpUntil(Func<bool> complete)
    {
        var frame = new DispatcherFrame();
        var elapsed = Stopwatch.StartNew();
        var check = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        check.Tick += (_, _) =>
        {
            if (complete() || elapsed.Elapsed >= TimeSpan.FromSeconds(5)) frame.Continue = false;
        };
        check.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { check.Stop(); }
        Check.True(complete(), "selected file preview displays before the bounded timeout");
    }
}
