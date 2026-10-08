using System;
using System.Runtime.InteropServices;

namespace Bloom.Search;

/// IShellLinkW, only to read a shortcut's target path (no resolve / no disk search).
internal sealed class LinkResolver : IDisposable
{
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int cch, IntPtr fd, uint flags);
        // the rest of the vtable is not used
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid clsid);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? file, bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string file);
    }

    private object? link;

    public string? Target(string lnk)
    {
        try
        {
            link ??= Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!);
            if (link is null) return null;
            ((IPersistFile)link).Load(lnk, 0);
            var sb = new System.Text.StringBuilder(520);
            ((IShellLinkW)link).GetPath(sb, sb.Capacity, IntPtr.Zero, 0x4 /* SLGP_RAWPATH */);
            string s = Environment.ExpandEnvironmentVariables(sb.ToString());
            return s.Length > 0 ? s : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (link is not null) Marshal.ReleaseComObject(link);
        link = null;
    }
}
