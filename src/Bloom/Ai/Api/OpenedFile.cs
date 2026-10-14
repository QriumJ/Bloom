using System;
using Microsoft.Win32.SafeHandles;

namespace Bloom.Ai;

/// A file or folder opened (RealPath.OpenExisting, OpenForWrite) with what the handle itself says of it: the final path
/// Windows resolved (links followed), whether it is a folder, and how many names the file has (more than one: a hard
/// link, so the file may also be somewhere else). What is decided about the file is decided from this, and what is read
/// or written goes through this same handle — a path swapped for a link meanwhile changes nothing.
internal sealed class OpenedFile : IDisposable
{
    public OpenedFile(SafeFileHandle handle, string final, bool isDirectory, uint links, bool created)
    {
        Handle = handle;
        Final = final;
        IsDirectory = isDirectory;
        Links = links;
        Created = created;
    }

    public SafeFileHandle Handle { get; }
    public string Final { get; }
    public bool IsDirectory { get; }
    public uint Links { get; }

    /// OpenForWrite made the file (it was not there).
    public bool Created { get; }

    /// The folder held open above a file made by RealPath.CreateIn, released with it.
    public OpenedFile? Parent { get; set; }

    public void Dispose()
    {
        Handle.Dispose();
        Parent?.Dispose();
    }
}
