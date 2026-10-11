using System;
using System.Diagnostics;
using System.Threading;
using Bloom.Native;

namespace Bloom.Core;

/// Giving memory back to Windows while Bloom waits. The managed heap stays small (about 9 MB in a real 0.9.1 session),
/// so the GC seldom runs on its own, while WPF's pictures and render resources keep native memory behind small managed
/// objects until their finalizers run: a forced collection with the finalizers gave 25 MB back in that session.
internal static class MemoryTrim
{
    private static int running;
    private static int count;

    /// How many trims have run (SummonTrace: an opening after one pages back what it touches).
    public static int Count => Volatile.Read(ref count);

    /// A full collection, the finalizers, one more collection for what they let go, then the working set trimmed.
    /// `aggressive` also hands emptied GC regions back (after something big, like Bloom Space Cleaner's map).
    public static void Now(bool aggressive = false)
    {
        GC.Collect(2, aggressive ? GCCollectionMode.Aggressive : GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        using var me = Process.GetCurrentProcess();
        Win32.SetProcessWorkingSetSize(me.Handle, new IntPtr(-1), new IntPtr(-1));
        Interlocked.Increment(ref count);
    }

    /// Now, on a thread of its own at the highest priority, and at most one at a time. A blocking GC runs at the
    /// priority of the thread that started it and stops every thread meanwhile, the Win key's hook included (it is
    /// managed code): started on the UI thread at Normal on a busy PC it waited its turn with the UI and the keyboard
    /// stopped. The finalizers and the working-set trim no longer hold the UI thread at all.
    public static void Soon(bool aggressive = false)
    {
        if (Interlocked.Exchange(ref running, 1) == 1) return;
        new Thread(() =>
        {
            try
            {
                Now(aggressive);
            }
            catch (Exception e)
            {
                Log.Error("memory trim", e);
            }
            finally
            {
                Volatile.Write(ref running, 0);
            }
        })
        { IsBackground = true, Name = "memory trim", Priority = ThreadPriority.Highest }.Start();
    }
}
