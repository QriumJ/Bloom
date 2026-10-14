using System;
using System.Diagnostics;
using System.Threading;
using Bloom.Native;

namespace Bloom.Core;

/// Explicit memory diagnostics and cleanup after unusually large operations. Normal hidden/idle
/// periods leave collection to the runtime: forced compacting GC also pauses drawing and keyboard hooks.
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

    /// Only explicit aggressive cleanup opts into collection. Routine idle requests do no work:
    /// a hidden launcher can coexist with an active chat, screenshot pen, or another Bloom window.
    public static void Soon(bool aggressive = false)
    {
        if (!aggressive) return;
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
