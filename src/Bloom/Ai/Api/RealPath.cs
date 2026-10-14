using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Bloom.Ai;

/// Where a path really leads, for the API chats' quiet reads (ApiToolbox): a junction, symbolic link or mount point
/// inside the chat's folder could point anywhere while the path still reads as inside it.
internal static class RealPath
{
    /// `path` is lexically inside `root` (both full paths), no reparse point lies on the way from `root` down to it (root
    /// itself included; a cloud placeholder counts too), and the final path Windows resolves for it — or for its nearest
    /// existing ancestor, for a path not made yet — is inside root's own final path. Anything unknown (a step that can't
    /// be read) counts as outside. Above root lies Bloom's own data folder, which the model has no hand in, so it is only
    /// compared through the final paths.
    public static bool Within(string root, string path)
    {
        if (!Lexically(root, path)) return false;
        string existing = root;
        if (Step(root) is not { } top || top.Reparse) return false;
        string relative = Path.GetRelativePath(root, path);
        if (relative != ".")
        {
            string at = root;
            foreach (string part in relative.Split(Path.DirectorySeparatorChar))
            {
                at = Path.Combine(at, part);
                if (Step(at) is not { } step) return false;
                if (!step.Exists) break;
                if (step.Reparse) return false;
                existing = at;
            }
        }
        return Final(root) is { } realRoot && Final(existing) is { } real && Lexically(realRoot, real);
    }

    /// The final path of what `path` names, its own or its nearest existing ancestor's plus the rest; null when it can't
    /// be told. Shown on the approval card when it differs from the path asked for.
    public static string? Of(string path)
    {
        string at = path, rest = "";
        while (Step(at) is { Exists: false })
        {
            if (Path.GetDirectoryName(at) is not { } parent) return null;
            rest = Path.Combine(Path.GetFileName(at), rest);
            at = parent;
        }
        return Final(at) is { } real ? Path.TrimEndingDirectorySeparator(Path.Combine(real, rest)) : null;
    }

    private static bool Lexically(string root, string path) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + "\\", StringComparison.OrdinalIgnoreCase);

    /// One step of a path: whether it exists and is a reparse point (its own attributes, the link not followed); null
    /// when that can't be read.
    private static (bool Exists, bool Reparse)? Step(string path)
    {
        try
        {
            return (true, File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint));
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return (false, false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private const uint ReadAttributes = 0x80, ShareAll = 0x7, OpenExisting = 3, BackupSemantics = 0x02000000; // the last opens folders too
    private const int MaxPath = 32_768;

    /// GetFinalPathNameByHandle on the opened file or folder (links followed), without its \\?\ prefix.
    private static string? Final(string path)
    {
        using var handle = CreateFileW(path, ReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        var buffer = new char[MaxPath];
        uint n = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (n == 0 || n >= buffer.Length) return null;
        string real = new(buffer, 0, (int)n);
        return real.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + real[8..] : real.StartsWith(@"\\?\", StringComparison.Ordinal) ? real[4..] : real;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] path, uint size, uint flags);
}
