using System;
using System.Collections.Generic;
using Microsoft.Win32.SafeHandles;

namespace Bloom.Ai;

/// The links found by RealPath.Trace, kept open (as themselves, shared for reading and writing but not for deleting or
/// renaming) for as long as this is not disposed: a link the user was shown can't be taken away and put back as another
/// while the call that was asked about is still using it.
internal sealed class TraceHold : IDisposable
{
    private readonly List<SafeFileHandle> handles = [];

    public void Add(SafeFileHandle handle) => handles.Add(handle);

    public void Dispose()
    {
        foreach (var h in handles) h.Dispose();
        handles.Clear();
    }
}
