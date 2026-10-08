using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Bloom.Native;

/// Files renamed, moved, copied or sent to the Recycle Bin the way Explorer does it (SHFileOperation, FOF_ALLOWUNDO):
/// Explorer's Ctrl+Z undoes them, and the shell shows its own progress and asks about name clashes. Bloom asks its own
/// question before a recycle; the shell still warns before anything would be destroyed instead of recycled.
internal static class ShellFiles
{
    private const uint Move = 1, CopyOp = 2, Delete = 3, RenameOp = 4;
    private const ushort AllowUndo = 0x0040, NoConfirmation = 0x0010, NoConfirmMkdir = 0x0200, WantNukeWarning = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    private static bool Run(uint func, string from, string? to, ushort flags, IntPtr owner)
    {
        var op = new SHFILEOPSTRUCT { hwnd = owner, wFunc = func, pFrom = from + "\0\0", pTo = to is null ? null : to + "\0\0", fFlags = flags };
        try
        {
            return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
        }
        catch (Exception e)
        {
            Core.Log.Error("shell file operation", e);
            return false;
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// To the Recycle Bin; true when it is gone from where it was.
    public static bool Recycle(string path, IntPtr owner) => Run(Delete, path, null, AllowUndo | NoConfirmation | WantNukeWarning, owner) && !Exists(path);

    /// Into `folder` (the shell asks if a file of that name is there already).
    public static bool MoveTo(string path, string folder, IntPtr owner) => Run(Move, path, folder, AllowUndo | NoConfirmMkdir, owner) && !Exists(path);

    public static bool CopyTo(string path, string folder, IntPtr owner) => Run(CopyOp, path, folder, AllowUndo | NoConfirmMkdir, owner) && Exists(Path.Combine(folder, Path.GetFileName(path.TrimEnd('\\'))));

    /// The same folder, a new name.
    public static bool Rename(string path, string newName, IntPtr owner)
    {
        string to = Path.Combine(Path.GetDirectoryName(path.TrimEnd('\\')) ?? "", newName);
        return Run(RenameOp, path, to, AllowUndo, owner) && Exists(to);
    }
}
