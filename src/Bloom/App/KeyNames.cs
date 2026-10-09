using System;
using System.Linq;
using System.Windows.Input;

namespace Bloom.App;

/// The keys a global shortcut can use, by the name kept in the settings ("ctrl+alt+m", "printscreen", "shift+f13"):
/// any key Windows has a virtual-key code for, except the modifiers themselves. Letters and digits are their own
/// character, the keys below have the name people call them by, and the rest (media, volume, browser, launch keys …)
/// keep WPF's key name in lower case.
internal static class KeyNames
{
    private static readonly (string name, Key key, string label)[] Named =
    {
        ("printscreen", Key.Snapshot, "PrintScreen"), ("pause", Key.Pause, "Pause"), ("scrolllock", Key.Scroll, "ScrollLock"),
        ("insert", Key.Insert, "Insert"), ("delete", Key.Delete, "Delete"), ("home", Key.Home, "Home"), ("end", Key.End, "End"),
        ("pageup", Key.PageUp, "PageUp"), ("pagedown", Key.PageDown, "PageDown"),
        ("up", Key.Up, "↑"), ("down", Key.Down, "↓"), ("left", Key.Left, "←"), ("right", Key.Right, "→"),
        ("space", Key.Space, "Space"), ("enter", Key.Enter, "Enter"), ("tab", Key.Tab, "Tab"), ("backspace", Key.Back, "Backspace"),
        ("esc", Key.Escape, "Esc"), ("capslock", Key.CapsLock, "CapsLock"), ("numlock", Key.NumLock, "NumLock"), ("menu", Key.Apps, "Menu"),
        ("`", Key.OemTilde, "`"), ("-", Key.OemMinus, "-"), ("=", Key.OemPlus, "="), ("[", Key.OemOpenBrackets, "["),
        ("]", Key.OemCloseBrackets, "]"), ("\\", Key.OemPipe, "\\"), (";", Key.OemSemicolon, ";"), ("'", Key.OemQuotes, "'"),
        (",", Key.OemComma, ","), (".", Key.OemPeriod, "."), ("/", Key.OemQuestion, "/"),
        ("numtimes", Key.Multiply, "Num *"), ("numplus", Key.Add, "Num +"), ("numminus", Key.Subtract, "Num -"),
        ("numslash", Key.Divide, "Num /"), ("numdot", Key.Decimal, "Num ."),
    };

    /// The name of `key` for a shortcut, or null for a modifier or a key Windows has no code for.
    public static string? Name(Key key)
    {
        switch (key)
        {
            case >= Key.A and <= Key.Z: return ((char)('a' + (key - Key.A))).ToString();
            case >= Key.D0 and <= Key.D9: return ((char)('0' + (key - Key.D0))).ToString();
            case >= Key.NumPad0 and <= Key.NumPad9: return "num" + (char)('0' + (key - Key.NumPad0));
            case >= Key.F1 and <= Key.F24: return "f" + (key - Key.F1 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            case Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
                or Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed:
                return null;
        }
        foreach (var n in Named)
            if (n.key == key) return n.name;
        return KeyInterop.VirtualKeyFromKey(key) == 0 ? null : key.ToString().ToLowerInvariant();
    }

    /// The key of a name (Name's inverse), or null.
    public static Key? Of(string name)
    {
        if (name.Length == 1 && name[0] is >= 'a' and <= 'z') return Key.A + (name[0] - 'a');
        if (name.Length == 1 && name[0] is >= '0' and <= '9') return Key.D0 + (name[0] - '0');
        if (name.Length == 4 && name.StartsWith("num", StringComparison.Ordinal) && name[3] is >= '0' and <= '9') return Key.NumPad0 + (name[3] - '0');
        if (name.Length >= 2 && name[0] == 'f' && int.TryParse(name.AsSpan(1), out int f) && f is >= 1 and <= 24) return Key.F1 + (f - 1);
        foreach (var n in Named)
            if (n.name == name) return n.key;
        // the rest by WPF's name (letters first: a number would parse as any enum value)
        return char.IsLetter(name[0]) && Enum.TryParse<Key>(name, true, out var k) && Name(k) == name ? k : null;
    }

    /// Keys that don't type anything, so one alone can be a shortcut without taking a key away from typing:
    /// F1–F24, PrintScreen, Pause, ScrollLock, Insert and the media, volume, browser and launch keys.
    public static bool Alone(string name) =>
        Of(name) is { } k && (k is >= Key.F1 and <= Key.F24 or Key.Snapshot or Key.Pause or Key.Scroll or Key.Insert
            || k is >= Key.BrowserBack and <= Key.LaunchApplication2);

    /// What a name looks like on a key cap: "PrintScreen", "Num +", "F13", "M".
    public static string Label(string name)
    {
        if (Named.FirstOrDefault(n => n.name == name) is { name: not null } n) return n.label;
        if (name.Length == 1) return name.ToUpperInvariant();
        if (name.Length == 4 && name.StartsWith("num", StringComparison.Ordinal)) return "Num " + name[3];
        if (name[0] == 'f' && name.Length <= 3 && char.IsDigit(name[^1])) return name.ToUpperInvariant();
        return Of(name) is { } k ? k.ToString() : name;
    }
}
