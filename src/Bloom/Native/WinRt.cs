using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Bloom.Native;

/// Calling Windows Runtime classes without the projection packages: activation factories are asked for by
/// name and methods are called through their vtable slots (Bloom Lens' OCR, the Bluetooth / Wi-Fi radios).
/// Interface ids and method order come from this PC's Windows metadata (C:\Windows\System32\WinMetadata).
internal static unsafe class WinRt
{
    // IInspectable takes slots 0–5 (QueryInterface, AddRef, Release, GetIids, GetRuntimeClassName, GetTrustLevel)
    public const int First = 6;

    private static readonly Guid IID_IAsyncInfo = new("00000036-0000-0000-c000-000000000046");

    /// Runs `work` on a multithreaded-apartment thread of its own (WinRT calls from WPF's STA thread would block).
    public static Task<T> OnMta<T>(Func<T> work, T failed, string what)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try
            {
                _ = RoInitialize(1); // multithreaded
                done.SetResult(work());
            }
            catch (Exception e)
            {
                Core.Log.Error(what, e);
                done.SetResult(failed);
            }
        })
        { IsBackground = true, Name = what };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
        return done.Task;
    }

    public static IntPtr Slot(IntPtr obj, int index) => (*(IntPtr**)obj)[index];

    public static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    public static IntPtr Keep(List<IntPtr> list, IntPtr p)
    {
        list.Add(p);
        return p;
    }

    public static void ReleaseAll(List<IntPtr> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i] != IntPtr.Zero) Marshal.Release(list[i]);
        list.Clear();
    }

    public static IntPtr Factory(string cls, Guid iid)
    {
        IntPtr name = HString(cls);
        try
        {
            Check(RoGetActivationFactory(name, ref iid, out IntPtr f));
            return f;
        }
        finally
        {
            _ = WindowsDeleteString(name);
        }
    }

    /// A method with no arguments that returns an object (getters, *Async calls without arguments).
    public static IntPtr Call(IntPtr obj, int slot)
    {
        IntPtr result;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(obj, slot))(obj, &result));
        return result;
    }

    /// A getter that returns a 32-bit number or enum.
    public static int GetInt(IntPtr obj, int slot)
    {
        int v;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(obj, slot))(obj, &v));
        return v;
    }

    /// Waits for an IAsyncOperation / IAsyncAction (IAsyncInfo.Status: 0 started, 1 completed, 2 canceled, 3 error).
    /// True when it completed within `timeoutMs`.
    public static bool Await(IntPtr op, int timeoutMs)
    {
        Guid iid = IID_IAsyncInfo;
        Check(Marshal.QueryInterface(op, in iid, out IntPtr info));
        try
        {
            int status = 0;
            for (int waited = 0; waited < timeoutMs; waited += 5)
            {
                Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(info, First + 1))(info, &status));
                if (status != 0) break;
                Thread.Sleep(5);
            }
            return status == 1;
        }
        finally
        {
            Marshal.Release(info);
        }
    }

    /// IAsyncOperation<T>.GetResults (slot 8) for an object result.
    public static IntPtr Results(IntPtr op) => Call(op, First + 2);

    /// IVectorView<T>: GetAt (slot 6) for 0 … get_Size (slot 7) − 1; each item comes AddRef'd.
    public static List<IntPtr> Items(IntPtr view)
    {
        uint n;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Slot(view, First + 1))(view, &n));
        var items = new List<IntPtr>((int)n);
        for (uint i = 0; i < n; i++)
        {
            IntPtr it;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(view, First))(view, i, &it));
            items.Add(it);
        }
        return items;
    }

    /// A method that returns an HSTRING (a get_Text / get_LanguageTag / get_Name).
    public static string GetString(IntPtr obj, int slot)
    {
        IntPtr h;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(obj, slot))(obj, &h));
        try
        {
            IntPtr raw = WindowsGetStringRawBuffer(h, out uint len);
            return raw == IntPtr.Zero ? "" : new string((char*)raw, 0, (int)len);
        }
        finally
        {
            _ = WindowsDeleteString(h);
        }
    }

    public static IntPtr HString(string s)
    {
        Check(WindowsCreateString(s, (uint)s.Length, out IntPtr h));
        return h;
    }

    public static void FreeHString(IntPtr h) => _ = WindowsDeleteString(h);

    [DllImport("combase.dll")] private static extern int RoInitialize(int type);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string s, uint length, out IntPtr hstring);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr hstring);
    [DllImport("combase.dll")] private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);
}
