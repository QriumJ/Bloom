using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bloom.Core;

namespace Bloom.Search;

/// What a typed path points at: the folder or file itself when it is there, else the entries of its folder whose
/// name starts with the last part ("C:\Us" → C:\Users).
internal sealed record PathAnswer(string Full, bool IsFolder, bool Exists, IReadOnlyList<(string path, bool folder)> Children);

/// A path typed into the search line: "C:\Users", "D:/작업", "C:", "~\Downloads", "%APPDATA%\Bloom", "\\nas\share".
/// Network paths are opened as typed and never listed (a server that doesn't answer would hold the launcher).
internal static partial class PathInput
{
    [GeneratedRegex(@"^(?:[A-Za-z]:(?:[\\/]|$)|\\\\[^\\/]|~(?:[\\/]|$)|%[A-Za-z_][\w()]*%)")]
    private static partial Regex PathStart();

    private const int MaxChildren = 8;
    private const int MaxScanned = 3000; // a huge folder is read only this far for the matches

    /// The line is a path (not just any text with a backslash in it).
    public static bool Looks(string text) => PathStart().IsMatch(Clean(text));

    /// The path as Windows reads it: quotes off, / → \, ~ and %VARS% expanded, "C:" → "C:\".
    public static string? Expand(string text)
    {
        string t = Clean(text);
        if (!PathStart().IsMatch(t)) return null;
        t = t.Replace('/', '\\');
        if (t.StartsWith('~')) t = Paths.Home + t[1..];
        t = Environment.ExpandEnvironmentVariables(t);
        if (t.Length == 2 && t[1] == ':') t += "\\";
        return t;
    }

    public static bool IsNetwork(string full) => full.StartsWith(@"\\", StringComparison.Ordinal);

    public static PathAnswer? Read(string text)
    {
        if (Expand(text) is not { } full) return null;
        if (IsNetwork(full)) return new PathAnswer(full, true, false, Array.Empty<(string, bool)>());
        try
        {
            if (Directory.Exists(full)) return new PathAnswer(full, true, true, List(full, ""));
            if (File.Exists(full)) return new PathAnswer(full, false, true, Array.Empty<(string, bool)>());
            // not there (yet): what in its folder starts with the last part
            int cut = full.LastIndexOf('\\');
            if (cut < 0) return null;
            string dir = full[..(cut + 1)], start = full[(cut + 1)..];
            return Directory.Exists(dir) ? new PathAnswer(full, true, false, List(dir, start)) : new PathAnswer(full, true, false, Array.Empty<(string, bool)>());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new PathAnswer(full, true, false, Array.Empty<(string, bool)>());
        }
    }

    /// Folders first, then files, each by name; hidden and system entries stay out.
    private static List<(string path, bool folder)> List(string dir, string start)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System, RecurseSubdirectories = false };
        var found = new List<(string path, bool folder)>();
        int seen = 0;
        foreach (var info in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", options))
        {
            if (++seen > MaxScanned) break;
            if (start.Length > 0 && !info.Name.StartsWith(start, StringComparison.OrdinalIgnoreCase)) continue;
            found.Add((info.FullName, (info.Attributes & FileAttributes.Directory) != 0));
        }
        return found
            .OrderByDescending(f => f.folder)
            .ThenBy(f => Path.GetFileName(f.path), StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxChildren)
            .ToList();
    }

    private static string Clean(string text) => text.Trim().Trim('"');
}
