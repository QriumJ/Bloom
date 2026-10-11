using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bloom.Core;
using Microsoft.Win32;

namespace Bloom.Search;

/// One program Windows lists under 설정 › 앱 (the Uninstall keys): its name, where it lives and how it is removed.
internal sealed record Installed(string Name, string? Publisher, string? Location, string? Icon, string Command, bool Msi, bool Machine);

/// 제거 for an app in the launcher (Ctrl+K / right-click): the program's own uninstaller found through the Uninstall
/// keys (HKLM 64 + 32-bit, HKCU), matched to the app by its folder or exe, else by its name. A Store app is removed for
/// this user through PowerShell's Remove-AppxPackage. Read once and kept for a minute (the actions menu asks per open).
internal static class Uninstalls
{
    private static List<Installed>? cache;
    private static DateTime read;

    private const string Key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static IReadOnlyList<Installed> All()
    {
        if (cache is not null && DateTime.UtcNow - read < TimeSpan.FromMinutes(1)) return cache;
        var list = new List<Installed>();
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var keys = root.OpenSubKey(Key);
                if (keys is null) continue;
                foreach (string id in keys.GetSubKeyNames())
                {
                    using var k = keys.OpenSubKey(id);
                    if (k?.GetValue("DisplayName") is not string name || name.Trim().Length == 0) continue;
                    if (k.GetValue("SystemComponent") is int sys && sys == 1) continue; // parts of something else
                    if (k.GetValue("ParentKeyName") is string) continue; // an update of another entry
                    if (k.GetValue("UninstallString") is not string cmd || cmd.Trim().Length == 0) continue;
                    bool msi = k.GetValue("WindowsInstaller") is int wi && wi == 1 && id.StartsWith('{');
                    list.Add(new Installed(name.Trim(), k.GetValue("Publisher") as string, Dir(k.GetValue("InstallLocation") as string), Exe(k.GetValue("DisplayIcon") as string),
                        msi ? "msiexec.exe /x " + id : cmd.Trim(), msi, hive == RegistryHive.LocalMachine));
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                Log.Write("uninstalls: " + e.Message);
            }
        }
        read = DateTime.UtcNow;
        return cache = list;
    }

    /// The program that removes `exe` (the app's resolved target), else the one named like the app; null when none
    /// fits or the app is part of Windows.
    public static Installed? For(string? exe, string name)
    {
        string? dir = exe is not null && File.Exists(exe) ? Path.GetDirectoryName(exe) : null;
        if (dir is not null && IsWindows(dir)) return null;
        var all = All();
        if (dir is not null)
        {
            // the app's folder is (inside) the program's folder, or the program's icon / uninstaller sits in the app's
            var byPlace = all.Where(p => Inside(dir, p.Location) || p.Icon is { } i && (Same(i, exe!) || Same(Path.GetDirectoryName(i), dir)) || Inside(Path.GetDirectoryName(Exe(p.Command)), dir))
                .OrderByDescending(p => p.Location?.Length ?? 0).ToList();
            if (byPlace.Count > 0) return byPlace.FirstOrDefault(p => Near(p.Name, name)) ?? byPlace[0];
        }
        var byName = all.Where(p => Near(p.Name, name)).ToList();
        return byName.Count == 1 ? byName[0] : byName.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// Starts the program's own uninstaller (its window asks the rest); a machine-wide one asks Windows for admin first
    /// (msiexec asks by itself). False when it couldn't start (UAC declined, the file gone).
    public static Task<bool> Run(Installed p)
    {
        var (file, args) = Split(p.Command);
        if (file.Length == 0) return Task.FromResult(false);
        return Launch.Start(file, args.Length == 0 ? null : args, p.Machine && !p.Msi ? "runas" : "open");
    }

    /// A Store app (shell:AppsFolder\<family>!<app>) removed for this user; the result as (ok, what PowerShell said).
    public static async Task<(bool ok, string? error)> RemoveStore(string aumid)
    {
        int slash = aumid.LastIndexOf('\\'), bang = aumid.IndexOf('!', StringComparison.Ordinal);
        string family = slash >= 0 && bang > slash ? aumid[(slash + 1)..bang] : "";
        if (family.Length == 0 || family.Any(c => c is '\'' or '"' or '`' or '$' or ';')) return (false, null);
        string script = $"$p = Get-AppxPackage | Where-Object {{ $_.PackageFamilyName -eq '{family}' -and -not $_.NonRemovable -and $_.SignatureKind -ne 'System' }}; " +
            "if (-not $p) { [Console]::Error.Write('not removable'); exit 2 }; $p | Remove-AppxPackage -ErrorAction Stop";
        var psi = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", script },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return (false, null);
            string err = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return (proc.ExitCode == 0, err.Trim().Length > 0 ? err.Trim() : null);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (false, e.Message);
        }
    }

    /// "\"C:\x\unins000.exe\" /SILENT" → (C:\x\unins000.exe, /SILENT); "C:\x y\u.exe -r" (unquoted) up to ".exe".
    public static (string file, string args) Split(string cmd)
    {
        cmd = cmd.Trim();
        if (cmd.StartsWith('"'))
        {
            int end = cmd.IndexOf('"', 1);
            return end < 0 ? (cmd.Trim('"'), "") : (cmd[1..end], cmd[(end + 1)..].Trim());
        }
        int exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0) return (cmd[..(exe + 4)], cmd[(exe + 4)..].Trim());
        int space = cmd.IndexOf(' ');
        return space < 0 ? (cmd, "") : (cmd[..space], cmd[(space + 1)..].Trim());
    }

    private static string? Exe(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        string s = Split(icon).file;
        int comma = s.LastIndexOf(',');
        if (comma > 1 && !File.Exists(s)) s = s[..comma]; // "C:\x\app.exe,0"
        return s.Trim().Trim('"');
    }

    private static string? Dir(string? location) => string.IsNullOrWhiteSpace(location) ? null : location.Trim().Trim('"').TrimEnd('\\');

    /// `path` is `folder` or inside it; a drive root or a shared folder (Program Files) never counts as the folder.
    private static bool Inside(string? path, string? folder)
    {
        if (path is null || folder is null || folder.Length < 4 || IsShared(folder)) return false;
        string p = path.TrimEnd('\\') + "\\", f = folder.TrimEnd('\\') + "\\";
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Same(string? a, string? b) => a is not null && b is not null && string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static readonly string[] Shared = new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    }.Where(s => s.Length > 0).ToArray();

    private static bool IsShared(string folder) => Shared.Any(s => Same(s, folder));

    private static bool IsWindows(string dir) => Inside(dir, Environment.GetFolderPath(Environment.SpecialFolder.Windows));

    /// Names that mean the same program: "VLC media player" ~ "VLC media player 3.0.21 (64-bit)", "Discord" ~ "Discord".
    private static bool Near(string installed, string app)
    {
        string a = installed.Trim(), b = app.Trim();
        if (b.Length < 3) return false;
        return a.Equals(b, StringComparison.OrdinalIgnoreCase) || a.StartsWith(b + " ", StringComparison.OrdinalIgnoreCase) || b.StartsWith(a + " ", StringComparison.OrdinalIgnoreCase);
    }
}
