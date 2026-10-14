using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Ai;
using Bloom.Core;

namespace Bloom.Tests;

internal static class CatalogReaderTests
{
    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "bloom-catalog-" + Guid.NewGuid().ToString("N"));
        public string Default => Path.Combine(Root, "default");
        public string Account => Path.Combine(Root, "account");
        public Fixture() { Directory.CreateDirectory(Default); Directory.CreateDirectory(Account); }
        public void Dispose() => Directory.Delete(Root, true);
    }

    private static byte[] Models(string slug) => Encoding.UTF8.GetBytes("{\"models\":[{\"slug\":\"" + slug
        + "\",\"visibility\":\"list\",\"supported_reasoning_levels\":[\"high\",{\"effort\":\"ultra\"}]}]}");

    private static void PumpUntil(Func<bool> done)
    {
        var frame = new DispatcherFrame();
        DateTime timeout = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(2) };
        timer.Tick += (_, _) => { if (done() || DateTime.UtcNow >= timeout) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Check.True(done(), "catalog operation completes within the bounded wait");
    }

    public static void AccountFallbackAndConfigChangesKeepCorrectSources()
    {
        using var f = new Fixture();
        File.WriteAllBytes(Path.Combine(f.Default, "models_cache.json"), Models("default-model"));
        File.WriteAllText(Path.Combine(f.Default, "config.toml"), "model = 'default-model'\n[profiles.x]\nmodel = 'ignored'");
        var reader = new CatalogReader(f.Account, f.Default);
        var first = reader.Load();
        Check.Eq(first.Models[0].Slug, "default-model", "account without cache uses the default catalog");
        Check.Eq(first.ConfigModel, "default-model", "only the top-level configured model is read");
        Check.True(ReferenceEquals(reader.Load(), first), "unchanged timestamps retain the immutable snapshot");
        File.WriteAllBytes(Path.Combine(f.Account, "models_cache.json"), Models("account-model"));
        var next = reader.Load();
        Check.Eq(next.Models[0].Slug, "account-model", "new account cache takes precedence");
        File.Delete(Path.Combine(f.Account, "models_cache.json"));
        File.Delete(Path.Combine(f.Default, "config.toml"));
        var fallback = reader.Load();
        Check.Eq(fallback.Models[0].Slug, "default-model", "removing the account cache restores fallback");
        Check.True(fallback.ConfigModel is null, "deleted config cannot leave an old default selected");
    }

    public static void InvalidPriorityRetainsRowsAndSupportedLevels()
    {
        var models = CatalogReader.ParseModels(Encoding.UTF8.GetBytes("""
            {"models":[
              {"slug":"string-priority","visibility":"list","priority":"bad"},
              {"slug":"null-priority","visibility":"list","priority":null},
              {"slug":"first","visibility":"list","priority":1,"supported_reasoning_levels":["high",{"effort":"ultra"}]},
              {"slug":"hidden","visibility":"hidden","priority":0}]}
            """));
        Check.Eq(models.Count, 3, "invalid priority falls back without dropping the catalog");
        Check.Eq(models[0].Slug, "first", "priority ordering remains stable");
        Check.Eq(string.Join(",", models[0].Levels), "high,ultra", "both level schema variants are retained");
        Check.Eq(CatalogReader.ParseConfig("\uFEFFmodel = \"first\"\r\n[profile]\r\nmodel='other'"), "first", "UTF8 BOM and top-level config are preserved");
    }

    public static void FailedReadDoesNotPoisonTimestampRetry()
    {
        using var f = new Fixture();
        File.WriteAllBytes(Path.Combine(f.Default, "models_cache.json"), Models("retry"));
        int calls = 0;
        var reader = new CatalogReader(null, f.Default, path => ++calls == 1 ? throw new IOException("temporarily locked") : File.ReadAllBytes(path));
        bool failed = false;
        try { reader.Load(); } catch (IOException) { failed = true; }
        Check.True(failed, "the snapshot worker can observe a failed read");
        Check.Eq(reader.Load().Models[0].Slug, "retry", "unchanged timestamp is retried after failure");
    }

    public static void BlockedCatalogAllowsInputAndAccountSwitchAwaitsNewSource()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var a = new BackgroundSnapshot<CatalogReader.Snapshot>(CatalogReader.Snapshot.Empty, () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return new(CatalogReader.ParseModels(Models("a")), "a", true);
        }, 2000, "catalog test a");
        var b = new BackgroundSnapshot<CatalogReader.Snapshot>(CatalogReader.Snapshot.Empty,
            () => new(CatalogReader.ParseModels(Models("b")), "b", true), 2000, "catalog test b");
        var selected = a;
        int captures = 0;
        var owner = Dispatcher.CurrentDispatcher;
        Task<bool> pending = GptCatalog.RefreshCurrentAsync(() => { owner.VerifyAccess(); captures++; return selected; });
        try
        {
            Check.True(entered.Wait(TimeSpan.FromSeconds(3)), "first account reader entered");
            bool input = false;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { input = true; selected = b; }));
            PumpUntil(() => input);
            Check.True(!pending.IsCompleted, "disk is still blocked while UI input changes account");
            Check.Eq(b.Current.Models.Count, 0, "new account never exposes old account models");
        }
        finally { release.Set(); }
        PumpUntil(() => pending.IsCompleted);
        Check.True(pending.GetAwaiter().GetResult(), "fresh selected source was loaded");
        Check.Eq(b.Current.Models[0].Slug, "b", "await follows an account change before resolving model");
        Check.True(captures >= 3, "account selection is checked again after each asynchronous read");
    }

    public static void UnknownCatalogDoesNotPermanentlyClampSavedEffort()
    {
        bool before = Demo.On;
        try
        {
            Demo.On = true; // no real account files are read
            Check.Eq(AiModels.Fit("chatgpt:unknown", "ultra"), "ultra", "unknown model capabilities cannot lower saved effort");
            Check.Eq(AiModels.Fit("sonnet", "ultra"), "max", "known Claude levels still fit normally");
        }
        finally { Demo.On = before; }
    }

    public static void CancelledFreshWaitLeavesDispatcherFreeAndReadCanFinish()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var cache = new BackgroundSnapshot<CatalogReader.Snapshot>(CatalogReader.Snapshot.Empty, () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return new(Array.Empty<GptCatalog.Model>(), null, true);
        }, 2000, "catalog cancel test");
        Task<bool> pending = GptCatalog.RefreshCurrentAsync(() => cache, cancellation.Token);
        try
        {
            Check.True(entered.Wait(TimeSpan.FromSeconds(3)), "uncancellable file read entered");
            cancellation.Cancel();
            PumpUntil(() => pending.IsCompleted);
            Check.True(pending.IsCanceled, "the caller can cancel without waiting for native IO");
        }
        finally { release.Set(); }
        Check.True(cache.Refresh().Wait(TimeSpan.FromSeconds(3)), "the single worker may finish and publish after caller cancellation");
    }

    public static void ModelResolutionAndFinalAccountCheckAreOneUiOperation()
    {
        var owner = Dispatcher.CurrentDispatcher;
        var a = new BackgroundSnapshot<CatalogReader.Snapshot>(CatalogReader.Snapshot.Empty,
            () => new(CatalogReader.ParseModels(Models("a")), "a", true), 2000, "catalog resolve a");
        var b = new BackgroundSnapshot<CatalogReader.Snapshot>(CatalogReader.Snapshot.Empty,
            () => new(CatalogReader.ParseModels(Models("b")), "b", true), 2000, "catalog resolve b");
        var selected = a;
        int captures = 0;
        bool changed = false;
        var pending = GptCatalog.ResolveCurrentAsync(() =>
        {
            owner.VerifyAccess();
            if (++captures == 2)
                owner.BeginInvoke(DispatcherPriority.Normal, new Action(() => { selected = b; changed = true; }));
            return selected;
        }, snapshot =>
        {
            owner.VerifyAccess();
            return (Model: snapshot.Models[0].Slug, Account: ReferenceEquals(selected, a) ? "a" : "b");
        });
        PumpUntil(() => pending.IsCompleted && changed);
        var resolved = pending.GetAwaiter().GetResult();
        Check.Eq(resolved.Model, resolved.Account, "queued account change cannot separate model and account resolution");
        Check.Eq(resolved.Model, "a", "the operation captures the account at the validated resolution point");
    }
}
