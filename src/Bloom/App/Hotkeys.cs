using System;
using System.Collections.Generic;
using System.Windows.Interop;
using Bloom.Native;

namespace Bloom.App;

/// Global shortcuts picked in the settings ("ctrl+alt+o" …), registered on one message window.
/// Each has a status for the settings line: ok | taken (another app has it) | off.
internal sealed class Hotkeys : IDisposable
{
    private readonly HwndSource window;
    private readonly Dictionary<int, Action> actions = new();
    private readonly Dictionary<string, (int id, string status)> byName = new();
    private int nextId = 0x200;

    public Hotkeys()
    {
        window = new HwndSource(new HwndSourceParameters("BloomHotkeys") { ParentWindow = Win32.HWND_MESSAGE, Width = 0, Height = 0, WindowStyle = 0 });
        window.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == (int)Win32.WM_HOTKEY && actions.TryGetValue((int)w.ToInt64(), out var a))
            {
                a();
                handled = true;
            }
            return IntPtr.Zero;
        });
    }

    /// (Re)binds `name` to `combo`; "off" or an unknown combo frees it.
    public void Set(string name, string combo, Action pressed)
    {
        if (byName.TryGetValue(name, out var old))
        {
            Win32.UnregisterHotKey(window.Handle, old.id);
            actions.Remove(old.id);
            byName.Remove(name);
        }
        if (Parse(combo) is not { } k)
        {
            byName[name] = (0, "off");
            return;
        }
        int id = nextId++;
        bool ok = Win32.RegisterHotKey(window.Handle, id, k.mods | Win32.MOD_NOREPEAT, k.vk);
        if (ok) actions[id] = pressed;
        byName[name] = (ok ? id : 0, ok ? "ok" : "taken");
    }

    public string Status(string name) => byName.TryGetValue(name, out var s) ? s.status : "off";

    /// "ctrl+alt+o" → (MOD_CONTROL | MOD_ALT, 'O'); letters, digits and F1–F12.
    public static (uint mods, uint vk)? Parse(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo) || combo == "off") return null;
        uint mods = 0, vk = 0;
        foreach (string part in combo.ToLowerInvariant().Split('+'))
        {
            switch (part)
            {
                case "ctrl": mods |= Win32.MOD_CONTROL; break;
                case "alt": mods |= Win32.MOD_ALT; break;
                case "shift": mods |= Win32.MOD_SHIFT; break;
                case "win": mods |= Win32.MOD_WIN; break;
                case "space": vk = 0x20; break;
                case { Length: 1 } c when char.IsLetterOrDigit(c[0]): vk = char.ToUpperInvariant(c[0]); break;
                case { Length: >= 2 } f when f[0] == 'f' && int.TryParse(f[1..], out int n) && n is >= 1 and <= 12: vk = (uint)(0x6F + n); break;
                default: return null;
            }
        }
        return vk == 0 || mods == 0 ? null : (mods, vk);
    }

    /// "ctrl+alt+o" → "Ctrl+Alt+O"
    public static string Label(string combo) => combo == "off" ? Tr("끔", "Off") : string.Join("+", Array.ConvertAll(combo.Split('+'), p => p.Length == 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p[1..]));

    public void Dispose()
    {
        foreach (var (_, (id, _)) in byName)
            if (id != 0) Win32.UnregisterHotKey(window.Handle, id);
        window.Dispose();
    }
}
