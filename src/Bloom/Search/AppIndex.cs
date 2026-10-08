using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.Search;

internal sealed class AppEntry
{
    public string Id = "";
    public string Name = ""; // what Bloom shows (the user's name for it, if they gave one)
    public string Original = ""; // the shortcut's / app's own name
    public List<string>? BaseAliases;
    public string Path = ""; // shortcut file, or shell:AppsFolder\<AUMID>
    public string Source = ""; // lnk | url | uwp
    public string? Folder; // Start Menu sub-folder, shown as the subtitle
    public string? Target; // resolved .lnk target (exe), for "파일 위치 열기"
    public MatchTarget Match = null!;
}

/// Start Menu and Desktop shortcuts plus AppsFolder apps (Store / packaged apps
/// such as Claude, 메모장, Windows 녹음기 that have no .lnk). Rescanned when the folders change.
internal sealed class AppIndex : IDisposable
{
    private volatile List<AppEntry> apps = new();
    private readonly List<FileSystemWatcher> watchers = new();
    private readonly Timer debounce;
    private int scanning;

    public event Action? Changed;

    public IReadOnlyList<AppEntry> Apps => apps;

    public bool Ready { get; private set; }

    private volatile Func<AppEntry, bool>? keep;

    /// QA pictures and the recording mode: only these apps are searched, now and after every rescan (the PC's own
    /// stay out of promo frames).
    public void KeepForQa(Func<AppEntry, bool> only)
    {
        keep = only;
        apps = apps.Where(only).ToList();
    }

    private static readonly string[] StartMenus =
    {
        System.IO.Path.Combine(Paths.AppData, @"Microsoft\Windows\Start Menu\Programs"),
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs"),
    };

    private static readonly string[] Desktops =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
    };

    public AppIndex()
    {
        debounce = new Timer(_ => Rescan(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        Rescan();
        foreach (string dir in StartMenus) Watch(dir, true);
        foreach (string dir in Desktops) Watch(dir, false);
    }

    private void Watch(string dir, bool sub)
    {
        if (!Directory.Exists(dir)) return;
        try
        {
            var w = new FileSystemWatcher(dir) { IncludeSubdirectories = sub, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
            FileSystemEventHandler h = (_, _) => debounce.Change(2500, Timeout.Infinite);
            w.Created += h;
            w.Deleted += h;
            w.Changed += h;
            w.Renamed += (_, _) => debounce.Change(2500, Timeout.Infinite);
            w.EnableRaisingEvents = true;
            watchers.Add(w);
        }
        catch (Exception e)
        {
            Log.Error("watch " + dir, e);
        }
    }

    /// Re-reads everything on a background STA thread (the shell objects want one).
    public void Rescan()
    {
        if (Interlocked.Exchange(ref scanning, 1) == 1) return;
        var t = new Thread(() =>
        {
            try
            {
                var started = DateTime.UtcNow;
                var list = Scan();
                if (keep is { } only) list = list.Where(only).ToList();
                apps = list;
                Ready = true;
                Log.Write($"apps: {list.Count} in {(DateTime.UtcNow - started).TotalMilliseconds:0} ms");
                Changed?.Invoke();
            }
            catch (Exception e)
            {
                Log.Error("app scan", e);
            }
            finally
            {
                Interlocked.Exchange(ref scanning, 0);
            }
        })
        { IsBackground = true, Name = "app-scan" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    private static bool Skip(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains("uninstall") || n.Contains("uninst") || n.Contains("언인스톨") || n.EndsWith(" 제거", StringComparison.Ordinal) || n.StartsWith("제거 ", StringComparison.Ordinal);
    }

    private static List<AppEntry> Scan()
    {
        var byName = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        var resolver = new LinkResolver();
        foreach (string root in StartMenus)
        {
            if (!Directory.Exists(root)) continue;
            foreach (string file in SafeFiles(root, true)) AddFile(byName, resolver, file, root);
        }
        foreach (string root in Desktops)
        {
            if (!Directory.Exists(root)) continue;
            foreach (string file in SafeFiles(root, false)) AddFile(byName, resolver, file, null);
        }
        foreach (var (name, aumid) in AppsFolder())
        {
            if (Skip(name) || byName.ContainsKey(name)) continue;
            if (aumid.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            byName[name] = new AppEntry
            {
                Id = "uwp:" + aumid,
                Name = name,
                Path = @"shell:AppsFolder\" + aumid,
                Source = "uwp",
                BaseAliases = Aliases(name, null),
            };
        }
        resolver.Dispose();
        return Named(byName.Values);
    }

    /// The user's Bloom-only names on top (the original name stays searchable), sorted by what is shown.
    private static List<AppEntry> Named(IEnumerable<AppEntry> entries)
    {
        var list = new List<AppEntry>();
        foreach (var a in entries)
        {
            if (a.Original.Length == 0) a.Original = a.Name;
            string? custom = AppNames.Get(a.Id);
            a.Name = custom ?? a.Original;
            var aliases = a.BaseAliases is null ? new List<string>() : new List<string>(a.BaseAliases);
            if (custom is not null) aliases.Add(a.Original);
            a.Match = new MatchTarget(a.Name, aliases.Count == 0 ? null : aliases);
            list.Add(a);
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return list;
    }

    /// After a rename: the names and order again without a rescan.
    public void ApplyNames()
    {
        apps = Named(apps);
        Changed?.Invoke();
    }

    private static IEnumerable<string> SafeFiles(string root, bool recurse)
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = recurse, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        string[] files;
        try
        {
            files = Directory.GetFiles(root, "*", opts);
        }
        catch
        {
            yield break;
        }
        foreach (string f in files)
        {
            string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
            if (ext is ".lnk" or ".url" or ".appref-ms") yield return f;
        }
    }

    private static void AddFile(Dictionary<string, AppEntry> byName, LinkResolver resolver, string file, string? root)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(file);
        if (Skip(name) || byName.ContainsKey(name)) return;
        string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
        string? target = ext == ".lnk" ? resolver.Target(file) : null;
        if (target is not null && Skip(System.IO.Path.GetFileNameWithoutExtension(target))) return;
        string? folder = null;
        if (root is not null)
        {
            string? dir = System.IO.Path.GetDirectoryName(file);
            if (dir is not null && dir.Length > root.Length) folder = dir[(root.Length + 1)..];
        }
        byName[name] = new AppEntry
        {
            Id = "app:" + file.ToLowerInvariant(),
            Name = name,
            Path = file,
            Source = ext == ".url" ? "url" : "lnk",
            Folder = folder,
            Target = target,
            BaseAliases = Aliases(name, target),
        };
    }

    /// The target exe's own name ("msedge", "Code") and a few English names users type.
    private static List<string>? Aliases(string name, string? target)
    {
        List<string>? list = null;
        if (target is not null)
        {
            string exe = System.IO.Path.GetFileNameWithoutExtension(target);
            if (!string.Equals(exe, name, StringComparison.OrdinalIgnoreCase) && exe.Length >= 2) (list ??= new()).Add(exe);
        }
        string? extra = name switch
        {
            "메모장" => "notepad",
            "계산기" => "calculator",
            "그림판" => "paint mspaint",
            "캡처 도구" => "snipping tool",
            "Windows 녹음기" => "sound recorder voice",
            "설정" => "settings",
            "파일 탐색기" => "explorer",
            "명령 프롬프트" => "cmd command prompt",
            "작업 관리자" => "task manager taskmgr",
            "제어판" => "control panel",
            "사진" => "photos",
            "카메라" => "camera",
            "시계" => "clock alarm",
            "터미널" => "terminal wt",
            "Windows 터미널" => "terminal wt",
            _ => null,
        };
        if (extra is not null) (list ??= new()).Add(extra);
        return list;
    }

    private static List<(string name, string aumid)> AppsFolder()
    {
        var result = new List<(string, string)>();
        IntPtr pFolder = IntPtr.Zero, pEnum = IntPtr.Zero;
        try
        {
            if (ShellCom.SHCreateItemFromParsingName("shell:AppsFolder", IntPtr.Zero, ShellCom.IID_IShellItem, out pFolder) != 0 || pFolder == IntPtr.Zero) return result;
            var folder = (IShellItem)Marshal.GetObjectForIUnknown(pFolder);
            folder.BindToHandler(IntPtr.Zero, ShellCom.BHID_EnumItems, ShellCom.IID_IEnumShellItems, out pEnum);
            if (pEnum == IntPtr.Zero) return result;
            var e = (IEnumShellItems)Marshal.GetObjectForIUnknown(pEnum);
            while (e.Next(1, out IShellItem? item, out uint got) == 0 && got == 1 && item is not null)
            {
                string? name = ShellCom.Name(item, ShellCom.SIGDN_NORMALDISPLAY);
                string? id = ShellCom.Name(item, ShellCom.SIGDN_PARENTRELATIVEPARSING);
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(id)) result.Add((name, id));
                Marshal.ReleaseComObject(item);
            }
            Marshal.ReleaseComObject(e);
            Marshal.ReleaseComObject(folder);
        }
        catch (Exception ex)
        {
            Log.Error("appsfolder", ex);
        }
        finally
        {
            if (pEnum != IntPtr.Zero) Marshal.Release(pEnum);
            if (pFolder != IntPtr.Zero) Marshal.Release(pFolder);
        }
        return result;
    }

    public void Dispose()
    {
        foreach (var w in watchers) w.Dispose();
        debounce.Dispose();
    }
}
