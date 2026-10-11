using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Bloom.Native;

// Every Win32 call the launcher makes, in one place.
internal static class Win32
{
    // ── hooks and input ─────────────────────────────────────────────────────────────────────────
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    public const uint WM_QUIT = 0x12, WM_APP = 0x8000, WM_COPYDATA = 0x004A, WM_HOTKEY = 0x0312;
    public const uint WM_NULL = 0, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_LBUTTONDBLCLK = 0x0203, WM_CONTEXTMENU = 0x007B;
    public const uint KEYEVENTF_KEYUP = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public MOUSEINPUT mi; } // 40 bytes on x64 (MOUSEINPUT is the union's largest member)

    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo; }

    /// INPUT with the keyboard member of the union (40 bytes on x64, like INPUT).
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct KINPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [DllImport("user32.dll", EntryPoint = "SendInput")] private static extern uint SendKeyInput(uint n, KINPUT[] inputs, int size);

    /// Presses the keys in order and lets go in reverse (0x11, 0x56 = Ctrl+V).
    public static void SendChord(params ushort[] vks)
    {
        var inputs = new KINPUT[vks.Length * 2];
        for (int i = 0; i < vks.Length; i++)
        {
            inputs[i] = new KINPUT { type = 1, ki = new KEYBDINPUT { wVk = vks[i] } };
            inputs[^(i + 1)] = new KINPUT { type = 1, ki = new KEYBDINPUT { wVk = vks[i], dwFlags = 2 /* KEYEVENTF_KEYUP */ } };
        }
        _ = SendKeyInput((uint)inputs.Length, inputs, Marshal.SizeOf<KINPUT>());
    }

    private static readonly int[] ModifierKeys = { 0x10, 0x11, 0x12, 0x5B, 0x5C }; // shift, ctrl, alt, both Win keys

    /// A modifier still held (the keys of the hotkey that started something): wait before sending keys.
    public static bool ModifiersDown() => Array.Exists(ModifierKeys, vk => (GetAsyncKeyState(vk) & 0x8000) != 0);

    // ── windows ─────────────────────────────────────────────────────────────────────────────────
    public const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4, SW_SHOW = 5, SW_RESTORE = 9;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_FRAMECHANGED = 0x20;
    public static readonly IntPtr HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2), HWND_MESSAGE = new(-3);
    public const int WM_MOVING = 0x0216, WM_SIZING = 0x0214, WM_ENTERSIZEMOVE = 0x0231, WM_EXITSIZEMOVE = 0x0232;
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? cls, string? name);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] public static extern bool LockWorkStation();
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc fn, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc fn, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    /// A process's program path (null when it can't be asked: elevated, protected or gone).
    public static string? ProcessPath(uint pid)
    {
        IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, (int)pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int len = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref len) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO info);
    public delegate bool MonitorEnumProc(IntPtr mon, IntPtr hdc, ref RECT rect, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc fn, IntPtr data);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dpiX, out uint dpiY);

    // ── hotkeys ─────────────────────────────────────────────────────────────────────────────────
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    // ── WM_COPYDATA (Everything IPC) ──────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct COPYDATASTRUCT { public UIntPtr dwData; public int cbData; public IntPtr lpData; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, ref COPYDATASTRUCT lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")]
    public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, ref COPYDATASTRUCT lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint msg, uint action, IntPtr changeInfo);

    // ── DWM ─────────────────────────────────────────────────────────────────────────────────────
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34,
        DWMWA_CAPTION_COLOR = 35, DWMWA_SYSTEMBACKDROP_TYPE = 38, DWMWA_TRANSITIONS_FORCEDISABLED = 3, DWMWA_CLOAK = 13;
    public const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    public const int DWMWCP_ROUND = 2, DWMSBT_NONE = 1, DWMSBT_TRANSIENTWINDOW = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }

    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();

    // ── shell ───────────────────────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    public const uint SEE_MASK_NOCLOSEPROCESS = 0x40, SEE_MASK_FLAG_NO_UI = 0x400, SEE_MASK_INVOKEIDLIST = 0xC, SEE_MASK_NOASYNC = 0x100;
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[]? apidl, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr ILCreateFromPath(string path);
    [DllImport("shell32.dll")] public static extern void ILFree(IntPtr pidl);

    /// Whether Windows would show a notification now: 1 not present (locked, screensaver), 2 busy (a full-screen app),
    /// 3 a full-screen Direct3D game, 4 presentation mode, 5 accepts, 6 quiet time, 7 a Windows Store app in front.
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_SHOWTIP = 0x80;
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);

    [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] public static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    public const uint MF_STRING = 0, MF_SEPARATOR = 0x800, TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2, TPM_BOTTOMALIGN = 0x20;

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

    [DllImport("user32.dll")] public static extern IntPtr CreateIconIndirect(ref ICONINFO info);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);

    // ── GDI ─────────────────────────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [DllImport("gdi32.dll")] public static extern int GetObject(IntPtr h, int size, ref BITMAP bmp);
    [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[]? bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateBitmap(int w, int h, uint planes, uint bpp, byte[]? bits);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    // ── misc ────────────────────────────────────────────────────────────────────────────────────
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);
    [DllImport("ole32.dll")] public static extern void CoTaskMemFree(IntPtr p);

    // ── power ───────────────────────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public long Luid; public uint Attributes; }

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, int length, IntPtr previous, IntPtr returnLength);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ExitWindowsEx(uint flags, uint reason);

    /// Restart or power off the way the Start menu does: no EWX_FORCE, so apps still get to ask
    /// about unsaved work. SeShutdownPrivilege is held but off by default, so it is switched on first.
    public static bool ExitWindows(bool restart)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0020 /* TOKEN_ADJUST_PRIVILEGES */ | 0x0008 /* TOKEN_QUERY */, out IntPtr token)) return false;
        try
        {
            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out long luid)) return false;
            var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = 0x2 /* SE_PRIVILEGE_ENABLED */ };
            // success with ERROR_NOT_ALL_ASSIGNED (1300) still means the privilege is off
            if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) || Marshal.GetLastWin32Error() == 1300) return false;
        }
        finally
        {
            CloseHandle(token);
        }
        // EWX_REBOOT 0x2 / EWX_POWEROFF 0x8; reason = SHTDN_REASON_FLAG_PLANNED | MAJOR_OTHER | MINOR_OTHER
        return ExitWindowsEx(restart ? 0x2u : 0x8u, 0x800000FFu);
    }

    // ── this process: priority, page faults ─────────────────────────────────────────────────────
    [DllImport("kernel32.dll")] private static extern bool SetPriorityClass(IntPtr process, uint priorityClass);

    /// ABOVE_NORMAL_PRIORITY_CLASS or back to NORMAL: every thread of Bloom's (UI, WPF's render thread, the pool, the
    /// GC) goes ahead of the PC's ordinary work. Programs Bloom starts don't inherit it (only IDLE and BELOW_NORMAL pass on).
    public static void AboveNormal(bool on) => SetPriorityClass(GetCurrentProcess(), on ? 0x8000u : 0x20u);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_MEMORY_COUNTERS
    {
        public uint cb, PageFaultCount;
        public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage, QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
    }

    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")] private static extern bool GetProcessMemoryInfo(IntPtr process, ref PROCESS_MEMORY_COUNTERS counters, uint cb);

    /// Page faults since Bloom started, soft (a page back from the standby list) and hard (from disk) together.
    public static uint PageFaults()
    {
        var c = new PROCESS_MEMORY_COUNTERS { cb = (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS>() };
        return GetProcessMemoryInfo(GetCurrentProcess(), ref c, c.cb) ? c.PageFaultCount : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DATA_BLOB { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CryptProtectData(ref DATA_BLOB input, string? desc, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DATA_BLOB output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CryptUnprotectData(ref DATA_BLOB input, IntPtr desc, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DATA_BLOB output);
    [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr p);

    /// Brings our window to the front. A launcher shown from a hotkey or a keyboard hook may not own
    /// the foreground right yet; an empty injected mouse event makes this process the source of the
    /// last input (the PowerToys trick), and AttachThreadInput is the fallback.
    public static void ForceForeground(IntPtr hwnd)
    {
        if (GetForegroundWindow() == hwnd) return;
        var inputs = new[] { new INPUT { type = 0 } };
        _ = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd) return;
        IntPtr fg = GetForegroundWindow();
        uint fgThread = GetWindowThreadProcessId(fg, out _), me = GetCurrentThreadId();
        if (fgThread != 0 && fgThread != me)
        {
            AttachThreadInput(me, fgThread, true);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            AttachThreadInput(me, fgThread, false);
        }
    }
}
