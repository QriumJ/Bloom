using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Search;

/// One file or folder Windows remembers as recently opened.
internal sealed record RecentFile(string Path, bool Folder, DateTime Opened);

/// Windows' own recent items (%APPDATA%\Microsoft\Windows\Recent\*.lnk, what Explorer's 홈 lists), newest first.
/// Works without Everything. Shortcut IO stays on one background STA; UI reads the previous snapshot.
internal static class RecentFiles
{
    private static readonly BackgroundSnapshot<List<RecentFile>> snapshot = new(new(), Scan, 5000, "recent files");
    private static DateTime folderStamp;
    private static bool scanned;
    public static event Action? Updated { add => snapshot.Updated += value; remove => snapshot.Updated -= value; }

    public static string Folder => Environment.GetFolderPath(Environment.SpecialFolder.Recent);

    public static List<RecentFile> List(int max = 150)
    {
        if (Demo.On) return new();
        var list = snapshot.Read();
        return max >= list.Count ? list : list.Take(Math.Max(0, max)).ToList();
    }

    public static Task<List<RecentFile>> Refresh() => Demo.On ? Task.FromResult(new List<RecentFile>()) : snapshot.Refresh();

    private static List<RecentFile> Scan()
    {
        const int max = 150;
        DateTime stamp;
        try { stamp = Directory.GetLastWriteTimeUtc(Folder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return snapshot.Current; }
        if (scanned && stamp == folderStamp) return snapshot.Current;
        var list = new List<RecentFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var resolver = new LinkResolver();
        IEnumerable<FileInfo> links;
        try
        {
            links = new DirectoryInfo(Folder).EnumerateFiles("*.lnk").OrderByDescending(f => f.LastWriteTimeUtc).Take(max * 2).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return snapshot.Current;
        }
        foreach (var lnk in links)
        {
            if (resolver.Target(lnk.FullName) is not { } target || !seen.Add(target)) continue;
            if (target.StartsWith(@"\\", StringComparison.Ordinal)) continue; // network paths can hang the check below
            bool folder = Directory.Exists(target);
            if (!folder && !File.Exists(target)) continue;
            list.Add(new RecentFile(target, folder, lnk.LastWriteTime));
            if (list.Count >= max) break;
        }
        folderStamp = stamp;
        scanned = true;
        return list;
    }
}
