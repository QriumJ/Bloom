using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Core;
using Velopack;
using Velopack.Sources;

namespace Bloom.App;

/// Updates from GitHub Releases, through Velopack: one look a little after Bloom starts (설정 > 정보 looks again on a
/// click, and can switch the start's look off). A new version downloads in the background and goes in at 다시 시작,
/// when Bloom is quit on purpose, or at the next start. Only a copy installed with the setup file updates itself; a dev
/// run or a tools\install.ps1 copy says so and points at the releases page.
internal static class Updates
{
    public const string Repo = "https://github.com/hyeon1035298/Bloom";
    public const string Page = Repo + "/releases/latest";

    public enum Stage { Idle, Checking, Latest, Downloading, Ready, Failed, NotInstalled }

    public static Stage Now { get; private set; } = Stage.Idle;

    /// The new version (Downloading, Ready).
    public static string Found { get; private set; } = "";

    /// Now changed (on the UI thread).
    public static event Action? Changed;

    private static UpdateManager? manager;
    private static bool looked;
    private static VelopackAsset? ready;

    /// Installed with the setup file (Velopack), so able to update itself.
    public static bool Installed => Manager() is not null;

    /// The setup's uninstaller (Update.exe beside current\), or null for a copy not installed with the setup.
    public static string? Uninstaller()
    {
        if (!Installed) return null;
        string u = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Update.exe"));
        return File.Exists(u) ? u : null;
    }

    private static UpdateManager? Manager()
    {
        if (looked) return manager;
        looked = true;
        if (Paths.IsDev) return null;
        try
        {
            var m = new UpdateManager(new GithubSource(Repo, null, false));
            if (m.IsInstalled) manager = m;
        }
        catch (Exception e)
        {
            Log.Error("update", e);
        }
        return manager;
    }

    /// The first Bloom of a start: an update downloaded earlier and not put in at a quit (Windows shut down, say) goes
    /// in now, and Bloom starts again in it with the same arguments. Once per version: a failed one isn't retried at
    /// every start. True: this process is ending.
    public static bool ApplyAtStart(string[] args)
    {
        try
        {
            if (Manager() is not { } m || m.UpdatePendingRestart is not { } pending) return false;
            string mark = Paths.InLocal("update-tried.txt"), v = pending.Version.ToString();
            if (File.Exists(mark) && File.ReadAllText(mark).Trim() == v) return false;
            File.WriteAllText(mark, v);
            Log.Write("update: putting in " + v + " at the start");
            m.ApplyUpdatesAndRestart(pending, args);
            return true;
        }
        catch (Exception e)
        {
            Log.Error("update", e);
            return false;
        }
    }

    /// A little after the start, once (설정 > 정보 > 켤 때 업데이트 확인).
    public static void Start(Dispatcher ui)
    {
        if (Manager() is null)
        {
            Now = Stage.NotInstalled;
            return;
        }
        if (!Settings.Current.UpdateCheck) return;
        var wait = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(20) };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            _ = Check(false);
        };
        wait.Start();
    }

    /// Looks for a newer version and downloads it (on the UI thread). `asked`: from 설정, where a failure says so; the
    /// start's look stays quiet about one.
    public static async Task Check(bool asked)
    {
        if (Now is Stage.Checking or Stage.Downloading) return;
        if (Manager() is not { } m)
        {
            Set(Stage.NotInstalled);
            return;
        }
        if (ready is null && m.UpdatePendingRestart is { } pending)
        {
            ready = pending;
            Found = pending.Version.ToString();
        }
        if (ready is not null)
        {
            Set(Stage.Ready);
            return;
        }
        Set(Stage.Checking);
        try
        {
            var info = await Task.Run(m.CheckForUpdatesAsync);
            if (info is null)
            {
                Set(Stage.Latest);
                return;
            }
            Found = info.TargetFullRelease.Version.ToString();
            Set(Stage.Downloading);
            await Task.Run(() => m.DownloadUpdatesAsync(info));
            ready = info.TargetFullRelease;
            Log.Write("update: " + Found + " downloaded");
            Set(Stage.Ready);
            Notices.UpdateReady(Found);
        }
        catch (Exception e)
        {
            Log.Error("update", e);
            Set(asked ? Stage.Failed : Stage.Idle);
        }
    }

    /// 다시 시작해서 업데이트 (after Host has saved): Bloom ends, the update goes in, the new version starts on 설정.
    public static void ApplyAndRestart()
    {
        if (ready is null || manager is null) return;
        Log.Write("update: restarting into " + Found);
        manager.ApplyUpdatesAndRestart(ready, new[] { "--settings" });
    }

    /// Quitting on purpose: the downloaded update goes in once Bloom has ended, without starting it again.
    public static void ApplyOnQuit()
    {
        if (ready is null || manager is null) return;
        try
        {
            manager.WaitExitThenApplyUpdates(ready, silent: true, restart: false);
        }
        catch (Exception e)
        {
            Log.Error("update", e);
        }
    }

    private static void Set(Stage s)
    {
        Now = s;
        Changed?.Invoke();
    }
}
