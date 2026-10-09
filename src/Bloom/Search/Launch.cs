using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.Search;

/// Starting things the way Explorer would: ShellExecute keeps a shortcut's arguments, working
/// folder and run-as settings; AppsFolder entries go through `shell:AppsFolder\<AUMID>`.
internal static class Launch
{
    public static bool Open(string target, string? args = null, string verb = "open", string? dir = null)
    {
        var info = new Win32.SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<Win32.SHELLEXECUTEINFO>(),
            fMask = Win32.SEE_MASK_FLAG_NO_UI | Win32.SEE_MASK_NOASYNC,
            lpVerb = verb,
            lpFile = target,
            lpParameters = args,
            lpDirectory = dir,
            nShow = Win32.SW_SHOW,
        };
        try
        {
            if (Win32.ShellExecuteEx(ref info)) return true;
            Log.Write($"ShellExecute failed {Marshal.GetLastWin32Error()} {verb} {target}");
        }
        catch (Exception e)
        {
            Log.Error("launch", e);
        }
        return false;
    }

    public static bool RunAsAdmin(string target) => Open(target, verb: "runas");

    /// Explorer with the item selected (falls back to opening the folder).
    public static void Reveal(string path)
    {
        try
        {
            IntPtr pidl = Win32.ILCreateFromPath(path);
            if (pidl != IntPtr.Zero)
            {
                try
                {
                    if (Win32.SHOpenFolderAndSelectItems(pidl, 0, null, 0) == 0) return;
                }
                finally
                {
                    Win32.ILFree(pidl);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error("reveal", e);
        }
        string? dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (dir is not null) Open(dir);
    }

    public static void Copy(string text) => Put(text, "clipboard");

    /// A picture to paste anywhere: the bitmap for every app, plus "PNG" (transparency, browsers, Office) when its file is given.
    public static bool CopyImage(BitmapSource picture, string? png = null)
    {
        var obj = new DataObject();
        obj.SetImage(picture);
        if (png is not null && File.Exists(png)) obj.SetData("PNG", new MemoryStream(File.ReadAllBytes(png)));
        return Put(obj, "clipboard image");
    }

    /// Clipboard.SetDataObject, tried a few times while another app holds the clipboard.
    private static bool Put(object data, string what)
    {
        for (int i = 0; i < 4; i++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (COMException)
            {
                System.Threading.Thread.Sleep(30); // clipboard held by another app
            }
            catch (Exception e)
            {
                Log.Error(what, e);
                return false;
            }
        }
        return false;
    }
}
