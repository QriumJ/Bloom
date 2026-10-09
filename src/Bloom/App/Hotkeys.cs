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
    private readonly Dictionary<string, (int id, string status, string combo)> byName = new();
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
            byName[name] = (0, "off", "off");
            return;
        }
        int id = nextId++;
        bool ok = Win32.RegisterHotKey(window.Handle, id, k.mods | Win32.MOD_NOREPEAT, k.vk);
        if (ok) actions[id] = pressed;
        byName[name] = (ok ? id : 0, ok ? "ok" : "taken", combo);
    }

    public string Status(string name) => byName.TryGetValue(name, out var s) ? s.status : "off";

    /// The name holding `combo` in Bloom (registered or wanted), or null.
    public string? Owner(string combo)
    {
        foreach (var (name, (_, status, c)) in byName)
            if (status != "off" && c == combo) return name;
        return null;
    }

    /// Lets go of every name starting with `prefix` that `keep` doesn't list (item shortcuts that were taken off).
    public void Drop(string prefix, ICollection<string> keep)
    {
        foreach (string name in new List<string>(byName.Keys))
            if (name.StartsWith(prefix, StringComparison.Ordinal) && !keep.Contains(name)) Set(name, "off", () => { });
    }

    /// "Ctrl + Alt + M", "alt+shift+f5", "컨트롤+알트+M" → "ctrl+alt+m" (modifiers in one order); null when it isn't a
    /// combo Windows can register (at least one modifier and one key).
    public static string? Normal(string typed)
    {
        var parts = new List<string>();
        foreach (string raw in typed.ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal).Split('+', StringSplitOptions.RemoveEmptyEntries))
            parts.Add(raw switch { "control" or "ctl" or "컨트롤" => "ctrl", "알트" => "alt", "시프트" => "shift", "windows" or "윈도우" or "윈도" => "win", "스페이스" or "spacebar" => "space", _ => raw });
        string[] order = { "ctrl", "alt", "shift", "win" };
        var mods = Array.FindAll(order, parts.Contains);
        var keys = parts.FindAll(p => Array.IndexOf(order, p) < 0);
        if (mods.Length == 0 || keys.Count != 1) return null;
        string combo = string.Join("+", mods) + "+" + keys[0];
        return Parse(combo) is null ? null : combo;
    }

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
        foreach (var (_, (id, _, _)) in byName)
            if (id != 0) Win32.UnregisterHotKey(window.Handle, id);
        window.Dispose();
    }
}
