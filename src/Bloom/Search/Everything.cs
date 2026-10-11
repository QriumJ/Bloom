using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.Search;

internal sealed class EvItem
{
    public string Name = "", Dir = "";
    public bool IsFolder;
    public long Size = -1;
    public DateTime? Modified;

    public string FullPath => Dir.Length == 0 ? Name : Path.Combine(Dir, Name);
}

/// Everything 1.4 over its WM_COPYDATA IPC (EVERYTHING_IPC_QUERY2W), no SDK DLL: the query goes to
/// the EVERYTHING_TASKBAR_NOTIFICATION window and the result list comes back to our message window.
/// Layouts follow Everything's everything_ipc.h (cross-checked with the IbEverythingLib binding).
internal sealed class EverythingClient : IDisposable
{
    private const uint QUERY2W = 18;
    private const uint REQ_NAME = 0x1, REQ_PATH = 0x2, REQ_SIZE = 0x10, REQ_DATE_MODIFIED = 0x40;
    private const uint ITEM_FOLDER = 0x1, ITEM_DRIVE = 0x2;
    public const uint SortName = 1, SortDateModifiedDesc = 14, SortRunCountDesc = 20;

    private readonly HwndSource window;
    private readonly Dictionary<uint, TaskCompletionSource<List<EvItem>?>> pending = new();
    private uint nextId = 0x41530000;

    public EverythingClient()
    {
        var p = new HwndSourceParameters("BloomEverything") { ParentWindow = Win32.HWND_MESSAGE, Width = 0, Height = 0, WindowStyle = 0 };
        window = new HwndSource(p);
        window.AddHook(Hook);
        Win32.ChangeWindowMessageFilterEx(window.Handle, Win32.WM_COPYDATA, 1, IntPtr.Zero); // replies from an elevated Everything
    }

    private static IntPtr Find() => Win32.FindWindow("EVERYTHING_TASKBAR_NOTIFICATION", null);

    public bool Running => !Demo.On && Find() != IntPtr.Zero; // recording mode: as if Everything were off

    public static string? ExePath()
    {
        foreach (string p in new[] { @"C:\Program Files\Everything\Everything.exe", @"C:\Program Files (x86)\Everything\Everything.exe" })
            if (File.Exists(p)) return p;
        return null;
    }

    /// null when Everything is not running or did not answer. Any thread: the bookkeeping stays on the thread of the
    /// reply window (the UI thread), and only the send leaves it. SendMessageTimeout returns once Everything has
    /// searched, so on the UI thread every typed pause in a file search stopped Bloom for as long as Everything took
    /// (up to the 1.5 s time-out on a busy PC); Everything's reply reaches the window meanwhile, as before.
    public Task<List<EvItem>?> Query(string search, int max, uint sort)
    {
        var ui = window.Dispatcher;
        if (!ui.CheckAccess()) return ui.InvokeAsync(() => Query(search, max, sort)).Task.Unwrap();
        IntPtr ev = Find();
        if (ev == IntPtr.Zero) return Task.FromResult<List<EvItem>?>(null);
        uint id = ++nextId;
        var tcs = new TaskCompletionSource<List<EvItem>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = tcs;
        IntPtr reply = window.Handle;
        byte[] text = Encoding.Unicode.GetBytes(search + "\0");
        _ = Task.Run(() =>
        {
            if (!Send(ev, reply, id, text, max, sort)) ui.BeginInvoke(() => Drop(id));
        });
        // a reply normally arrives within milliseconds; never leave a caller hanging
        var timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Drop(id);
        };
        timer.Start();
        return tcs.Task;
    }

    /// A query that will get no answer: its caller gets null (the UI thread).
    private void Drop(uint id)
    {
        if (pending.Remove(id, out var tcs)) tcs.TrySetResult(null);
    }

    /// EVERYTHING_IPC_QUERY2W to Everything's window; false when it could not be delivered or was refused.
    private static bool Send(IntPtr ev, IntPtr reply, uint id, byte[] text, int max, uint sort)
    {
        int size = 28 + text.Length;
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt32(buf, 0, (int)reply.ToInt64()); // reply_hwnd (32 bits are enough)
            Marshal.WriteInt32(buf, 4, (int)id); // reply_copydata_message
            Marshal.WriteInt32(buf, 8, 0); // search flags
            Marshal.WriteInt32(buf, 12, 0); // offset
            Marshal.WriteInt32(buf, 16, max);
            Marshal.WriteInt32(buf, 20, (int)(REQ_NAME | REQ_PATH | REQ_SIZE | REQ_DATE_MODIFIED));
            Marshal.WriteInt32(buf, 24, (int)sort);
            Marshal.Copy(text, 0, buf + 28, text.Length);
            var cds = new Win32.COPYDATASTRUCT { dwData = (UIntPtr)QUERY2W, cbData = size, lpData = buf };
            IntPtr ok = Win32.SendMessageTimeout(ev, Win32.WM_COPYDATA, reply, ref cds, 0x2 /* SMTO_ABORTIFHUNG */, 1500, out IntPtr res);
            return ok != IntPtr.Zero && res != IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg != (int)Win32.WM_COPYDATA) return IntPtr.Zero;
        var cds = Marshal.PtrToStructure<Win32.COPYDATASTRUCT>(l);
        uint id = (uint)cds.dwData.ToUInt64();
        if (!pending.Remove(id, out var tcs)) return IntPtr.Zero;
        try
        {
            byte[] data = new byte[cds.cbData];
            Marshal.Copy(cds.lpData, data, 0, cds.cbData);
            tcs.TrySetResult(Parse(data));
        }
        catch (Exception e)
        {
            Log.Error("everything parse", e);
            tcs.TrySetResult(null);
        }
        handled = true;
        return new IntPtr(1);
    }

    private static List<EvItem> Parse(byte[] d)
    {
        var list = new List<EvItem>();
        if (d.Length < 20) return list;
        int num = BitConverter.ToInt32(d, 4);
        uint flags = BitConverter.ToUInt32(d, 12);
        for (int i = 0; i < num; i++)
        {
            int at = 20 + i * 8;
            if (at + 8 > d.Length) break;
            uint itemFlags = BitConverter.ToUInt32(d, at);
            int p = BitConverter.ToInt32(d, at + 4);
            var item = new EvItem { IsFolder = (itemFlags & (ITEM_FOLDER | ITEM_DRIVE)) != 0 };
            if ((flags & REQ_NAME) != 0) item.Name = ReadStr(d, ref p);
            if ((flags & REQ_PATH) != 0) item.Dir = ReadStr(d, ref p);
            if ((flags & 0x4) != 0) ReadStr(d, ref p); // full path (not requested)
            if ((flags & 0x8) != 0) ReadStr(d, ref p); // extension (not requested)
            if ((flags & REQ_SIZE) != 0)
            {
                item.Size = BitConverter.ToInt64(d, p);
                p += 8;
            }
            if ((flags & 0x20) != 0) p += 8; // date created (not requested)
            if ((flags & REQ_DATE_MODIFIED) != 0)
            {
                long ft = BitConverter.ToInt64(d, p);
                p += 8;
                if (ft > 0 && ft < DateTime.MaxValue.ToFileTimeUtc()) item.Modified = DateTime.FromFileTime(ft);
            }
            list.Add(item);
        }
        return list;
    }

    private static string ReadStr(byte[] d, ref int p)
    {
        int len = BitConverter.ToInt32(d, p);
        p += 4;
        string s = Encoding.Unicode.GetString(d, p, len * 2);
        p += (len + 1) * 2;
        return s;
    }

    public void Dispose() => window.Dispose();
}
