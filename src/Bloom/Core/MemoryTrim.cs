using System;
using System.Diagnostics;
using Bloom.Native;

namespace Bloom.Core;

/// Giving memory back to Windows while Bloom waits. The managed heap stays small (about 9 MB in a real 0.9.1 session),
/// so the GC seldom runs on its own, while WPF's pictures and render resources keep native memory behind small managed
/// objects until their finalizers run: a forced collection with the finalizers gave 25 MB back in that session.
internal static class MemoryTrim
{
    /// A full collection, the finalizers, one more collection for what they let go, then the working set trimmed.
    /// `aggressive` also hands emptied GC regions back (after something big, like Bloom Space Cleaner's map).
    public static void Now(bool aggressive = false)
    {
        GC.Collect(2, aggressive ? GCCollectionMode.Aggressive : GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        using var me = Process.GetCurrentProcess();
        Win32.SetProcessWorkingSetSize(me.Handle, new IntPtr(-1), new IntPtr(-1));
    }
}
