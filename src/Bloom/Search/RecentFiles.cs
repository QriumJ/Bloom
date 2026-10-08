using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.Core;

namespace Bloom.Search;

/// One file or folder Windows remembers as recently opened.
internal sealed record RecentFile(string Path, bool Folder, DateTime Opened);

/// Windows' own recent items (%APPDATA%\Microsoft\Windows\Recent\*.lnk, what Explorer's 홈 lists), newest first.
/// Works without Everything. The shortcuts are read at most every 5 s; targets that are gone are left out.
internal static class RecentFiles
{
    private static List<RecentFile> cache = new();
    private static long cachedAt;
    private static DateTime folderStamp;

    public static string Folder => Environment.GetFolderPath(Environment.SpecialFolder.Recent);

    public static List<RecentFile> List(int max = 150)
    {
        if (Demo.On) return new(); // recording mode: nothing of this PC
        long now = Environment.TickCount64;
        DateTime stamp;
        try
        {
            stamp = Directory.GetLastWriteTimeUtc(Folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return cache;
        }
        if (cache.Count > 0 && (now - cachedAt < 5000 || stamp == folderStamp)) return cache;
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
            return cache;
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
        cache = list;
        cachedAt = now;
        folderStamp = stamp;
        return list;
    }
}
