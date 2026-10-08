using System;
using System.IO;
using System.Windows;
using Bloom.Core;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.App;

/// Settings out to a file and back in (설정 > 정보, the first-run screens, the 설정 내보내기 / 불러오기 commands).
internal sealed partial class Host
{
    /// Asks where, writes the file and shows it in Explorer. False when cancelled or it failed (the user was told).
    public bool ExportSettings(Window? owner)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = Tr("Bloom 설정 내보내기", "Export Bloom settings"),
            FileName = SettingsTransfer.SuggestedName(),
            DefaultExt = ".zip",
            Filter = Tr("Bloom 설정 (*.zip)|*.zip", "Bloom settings (*.zip)|*.zip"),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(owner) != true) return false;
        try
        {
            SaveAll();
            string ver = typeof(Host).Assembly.GetName().Version?.ToString(3) ?? "";
            SettingsTransfer.Export(dlg.FileName, ver);
            Launch.Open("explorer.exe", $"/select,\"{dlg.FileName}\"");
            Log.Write("settings exported");
            return true;
        }
        catch (Exception e)
        {
            Log.Error("export", e);
            Tell(owner, Tr("내보내지 못했어요", "Couldn't export"), e.Message);
            return false;
        }
    }

    /// Asks for a file, puts its settings in place (what they replace goes to backups\) and applies them at once.
    public bool ImportSettings(Window? owner)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Tr("Bloom 설정 불러오기", "Import Bloom settings"),
            Filter = Tr("Bloom 설정 (*.zip)|*.zip", "Bloom settings (*.zip)|*.zip"),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(owner) != true) return false;
        try
        {
            SaveAll();
            var files = SettingsTransfer.Import(dlg.FileName);
            Reload();
            Log.Write($"settings imported ({files.Count} files)");
            Tell(owner, Tr("설정을 불러왔어요", "Settings imported"), Tr("지금 쓰던 설정은 데이터 폴더의 backups에 남겨 뒀어요.", "The settings you had are kept in the data folder's backups."));
            if (Lang.Pending(Settings.Current.Language)
                && Ask(owner, "Bloom", Tr("불러온 설정의 언어로 바꾸려면 Bloom을 다시 시작해야 해요. 지금 다시 시작할까요?", "The imported settings use another language. Restart Bloom now to switch?"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                Restart();
            return true;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log.Error("import", e);
            Tell(owner, Tr("불러오지 못했어요", "Couldn't import"), e is InvalidDataException ? e.Message : Tr("파일을 읽지 못했어요 — Bloom에서 내보낸 설정 파일인지 확인해 주세요.", "Couldn't read the file — check that it is a settings file exported from Bloom."));
            return false;
        }
    }

    /// 설정 > 정보 > Bloom 초기화: asked twice (sure? export first?), then a new Bloom is started that waits for this
    /// one to end, deletes both data folders and starts like a first install (the first-run screens). Returns false
    /// when cancelled.
    public bool ResetAll(Window? owner)
    {
        string what = Tr("설정, 즐겨찾기, 앱 이름, 브라우저 바로 가기, 타이머 · 알림, 클립보드 기록, AI 대화, 사용 기록이 모두 지워지고 처음 설정 화면부터 다시 시작해요.\n\n이 작업은 되돌릴 수 없어요.", "Settings, favorites, app names, browser shortcuts, timers and reminders, clipboard history, AI chats and usage history are all deleted, and Bloom starts again from the first-run screens.\n\nThis can't be undone.");
        if (Ask(owner, Tr("Bloom을 처음 상태로 되돌릴까요?", "Reset Bloom to how it was first installed?"), what, MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return false;
        var first = Ask(owner, Tr("지우기 전에 설정을 내보낼까요?", "Export your settings before deleting?"), Tr("지금 설정과 즐겨찾기를 파일 하나로 남겨 두면, 처음 설정 화면에서 '설정 불러오기'로 되살릴 수 있어요.", "Keep your settings and favorites in one file, and bring them back with 'Import settings' on the first-run screens."), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (first == MessageBoxResult.Cancel) return false;
        if (first == MessageBoxResult.Yes && !ExportSettings(owner)) return false;
        if (!StartSuccessor("--reset-from"))
        {
            Tell(owner, Tr("초기화하지 못했어요", "Couldn't reset"), Tr("새 Bloom을 시작하지 못했어요. 아무것도 지우지 않았어요.", "Couldn't start a new Bloom. Nothing was deleted."));
            return false;
        }
        Log.Write("reset: quitting for the new start");
        Quit(save: false);
        return true;
    }

    /// A new Bloom that waits for this one to end (`--reset-from` / `--restart-from`), with the same dev options.
    private bool StartSuccessor(string flag, params string[] extra)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            psi.ArgumentList.Add(flag);
            psi.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (Paths.IsDev)
            {
                psi.ArgumentList.Add("--data");
                psi.ArgumentList.Add(Paths.Data);
            }
            if (Opt.NoHook) psi.ArgumentList.Add("--no-hook");
            if (Opt.NoTray) psi.ArgumentList.Add("--no-tray");
            foreach (string a in extra) psi.ArgumentList.Add(a);
            System.Diagnostics.Process.Start(psi);
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error(flag, e);
            return false;
        }
    }

    /// A new language (and anything else read once per run) takes effect in a fresh Bloom; `then` = --settings / --show.
    public void Restart(string? then = null)
    {
        SaveAll();
        if (!StartSuccessor("--restart-from", then is null ? Array.Empty<string>() : new[] { then })) return;
        Log.Write("restart");
        Quit();
    }

    private static MessageBoxResult Ask(Window? owner, string title, string text, MessageBoxButton buttons, MessageBoxImage image) =>
        owner is not null ? MessageBox.Show(owner, text, title, buttons, image) : MessageBox.Show(text, title, buttons, image);

    public void ReloadForQa() => Reload();

    /// Everything that was read from the data folder, read again and applied (look, keys, names, lists).
    private void Reload()
    {
        Settings.Load();
        Favorites.Load();
        AppNames.Load();
        Quicklinks.Load();
        UserCommands.Load();
        ColorBook.Load();
        Settings.Current.Onboarded = true;
        Settings.Save();
        if (!Paths.IsDev) Autostart.Set(Settings.Current.Autostart);
        Theme.Apply(Settings.Current.Theme);
        ApplyHotkeys();
        AppsChanged();
        Window.DataChanged();
        settingsWindow?.Refresh();
    }

    private static void Tell(Window? owner, string title, string text)
    {
        if (owner is not null) MessageBox.Show(owner, text, title, MessageBoxButton.OK, MessageBoxImage.Information);
        else MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
