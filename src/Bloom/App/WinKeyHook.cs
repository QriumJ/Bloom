using System;
using System.Runtime.InteropServices;
using System.Threading;
using Bloom.Native;

namespace Bloom.App;

/// A Win key pressed and released on its own opens the launcher instead of the Start menu
/// Win + any other key (Win+E, Win+Shift+S, …) behaves as before; Ctrl+Esc still opens Start.
/// The hook lives on its own thread with its own message loop: if it shared the UI thread, a busy
/// frame would stall everyone's typing and Windows would silently remove the hook.
/// Blind while an elevated window has focus (UIPI), like the helper it replaces.
internal sealed class WinKeyHook : IDisposable
{
    private const uint VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_MASK = 0xE8;
    private const uint WM_APP_FIRE = Win32.WM_APP + 1;
    private static readonly UIntPtr OwnTag = (UIntPtr)0x41534C57u; // "ASLW": marks our own injected keys

    private readonly Action fired;
    private Thread? thread;
    private uint threadId;
    private IntPtr hook;
    private Win32.HookProc? proc; // kept referenced so the GC never collects the callback
    private uint winDown;
    private bool combo;

    public bool Active => hook != IntPtr.Zero;

    public WinKeyHook(Action fired) => this.fired = fired;

    public void Start()
    {
        if (thread is not null) return;
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            threadId = Win32.GetCurrentThreadId();
            proc = OnKey;
            hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, proc, Win32.GetModuleHandle(null), 0);
            ready.Set();
            if (hook == IntPtr.Zero) return;
            while (Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
                if (msg.message == WM_APP_FIRE) Fire((byte)msg.wParam);
            Win32.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        })
        { IsBackground = true, Name = "win-key", Priority = ThreadPriority.AboveNormal };
        thread.Start();
        ready.Wait(2000);
    }

    private IntPtr OnKey(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var k = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            if (k.dwExtraInfo != OwnTag)
            {
                int m = (int)wParam.ToInt64();
                bool down = m is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN;
                bool up = m is Win32.WM_KEYUP or Win32.WM_SYSKEYUP;
                if (k.vkCode is VK_LWIN or VK_RWIN)
                {
                    if (down)
                    {
                        if (winDown == 0)
                        {
                            winDown = k.vkCode;
                            combo = Held(VK_SHIFT) || Held(VK_CONTROL) || Held(VK_MENU);
                        }
                        else if (k.vkCode != winDown) combo = true;
                    }
                    else if (up && k.vkCode == winDown)
                    {
                        bool fire = !combo;
                        winDown = 0;
                        if (fire)
                        {
                            // swallow this release; Fire() replays it behind a mask key so Start stays closed
                            Win32.PostThreadMessage(threadId, WM_APP_FIRE, (IntPtr)k.vkCode, IntPtr.Zero);
                            return new IntPtr(1);
                        }
                    }
                }
                else if (down && winDown != 0) combo = true;
            }
        }
        return Win32.CallNextHookEx(hook, nCode, wParam, lParam);
    }

    private static bool Held(uint vk) => (Win32.GetAsyncKeyState((int)vk) & 0x8000) != 0;

    private static void Key(uint vk, bool up) => Win32.keybd_event((byte)vk, 0, up ? Win32.KEYEVENTF_KEYUP : 0, OwnTag);

    private void Fire(byte winVk)
    {
        Key(VK_MASK, false);
        Key(VK_MASK, true);
        Key(winVk, true);
        // our process injected the last input, so the UI thread may take the foreground
        fired();
    }

    public void Dispose()
    {
        if (threadId != 0) Win32.PostThreadMessage(threadId, Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread = null;
        threadId = 0;
    }
}
