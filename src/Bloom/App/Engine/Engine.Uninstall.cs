using System;
using System.Threading.Tasks;
using Bloom.Search;

namespace Bloom.App;

/// 제거… on an app (Ctrl+K / right-click): asked once more in the launcher, then the program's own uninstaller (or, for
/// a Store app, Remove-AppxPackage for this user). Bloom itself and parts of Windows get no such action.
internal sealed partial class Engine
{
    private ActionItem? UninstallAction(AppEntry a)
    {
        string name = a.Original.Length > 0 ? a.Original : a.Name;
        if (a.Source == "url") return null;
        if (a.Source == "uwp")
        {
            if (a.Path.Contains(@"\Microsoft.Windows.", StringComparison.OrdinalIgnoreCase) || a.Path.Contains(@"\windows.", StringComparison.OrdinalIgnoreCase)) return null;
            return Ask(a.Name, Tr("제거 · Store 앱, 이 사용자에게서", "Uninstall · Store app, for this user"), () => _ = RemoveStore(a));
        }
        // a program's shortcut only (a manual, a readme or a site in its folder is no program to remove)
        if (a.Target is not { } exe || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || IsBloom(exe)) return null;
        if (Uninstalls.For(exe, name) is not { } p) return null;
        // the answer names the program removed (it can be bigger than the shortcut: Acrobat Distiller → Adobe Acrobat)
        return Ask(a.Name, Tr($"제거 · {Clip(p.Name, 30)}", $"Uninstall · {Clip(p.Name, 30)}"), () =>
        {
            if (!Uninstalls.Run(p))
                Notices.Result(false, Tr("Bloom · 제거", "Bloom · Uninstall"), Tr($"‘{p.Name}’ 제거 프로그램 실행 실패", $"Could not open the uninstaller for ‘{p.Name}’"), Tr("관리자 권한이 거절되었거나 제거 파일이 없습니다. Windows 앱 설정에서 제거하세요.", "Admin permission was declined or the uninstaller is missing. Remove the app in Windows app settings."), Tr("Windows 앱 설정", "Windows app settings"), () => Launch.Open("ms-settings:appsfeatures"));
        });

        ActionItem Ask(string title, string yes, Action remove) => new()
        {
            Title = Tr("제거…", "Uninstall…"),
            Glyph = G.Delete,
            Danger = true,
            StayOpen = true,
            Run = () => host.Window.Confirm(Tr($"‘{Clip(title, 22)}’ 제거", $"Uninstall ‘{Clip(title, 22)}’"), new ActionItem
            {
                Title = yes,
                Keys = "Enter",
                Glyph = G.Delete,
                Danger = true,
                Run = remove,
            }),
        };
    }

    private async Task RemoveStore(AppEntry a)
    {
        var (ok, error) = await Uninstalls.RemoveStore(a.Path);
        Notices.Result(ok, Tr("Bloom · 제거", "Bloom · Uninstall"), ok ? Tr($"‘{Clip(a.Name, 30)}’ 제거 완료", $"Uninstalled ‘{Clip(a.Name, 30)}’") : Tr($"‘{Clip(a.Name, 30)}’ 제거 실패", $"Could not uninstall ‘{Clip(a.Name, 30)}’"),
            ok ? null : error is "not removable" ? Tr("Windows에 딸린 앱이라 지울 수 없습니다", "It comes with Windows and can't be removed") : Tr("Windows 앱 설정에서 제거하세요", "Remove the app in Windows app settings."),
            !ok && error != "not removable" ? Tr("Windows 앱 설정", "Windows app settings") : null, !ok && error != "not removable" ? () => Launch.Open("ms-settings:appsfeatures") : null);
        if (ok) _ = Task.Run(host.Apps.Rescan); // AppsFolder has no watcher
    }

    /// Bloom's own exe (an installed copy is removed from 설정 › 앱, which runs its uninstall hook).
    private static bool IsBloom(string? target) => target is not null && string.Equals(System.IO.Path.GetFileName(target), "Bloom.exe", StringComparison.OrdinalIgnoreCase);
}
