using System.Windows.Input;
using Bloom.App;
using Bloom.Search;

namespace Bloom.Tests;

/// winget's table, item shortcuts as typed and as pressed, uninstall commands, the window layouts.
internal static class ToolsTests
{
    public static void WingetTable()
    {
        const string Out = "Name                       Id                         Version                 Match        Source\r\n"
            + "--------------------------------------------------------------------------------------------------\r\n"
            + "VLC                        XPDM1ZW6815MQM             Unknown                              msstore\r\n"
            + "VLC media player           VideoLAN.VLC               3.0.24                  Moniker: vlc winget\r\n";
        var list = Winget.Table(Out);
        Check.Eq(list.Count, 2, "two packages");
        Check.Eq(list[1].Id, "VideoLAN.VLC", "the id");
        Check.Eq(list[1].Name, "VLC media player", "the name");
        Check.Eq(list[1].Version, "3.0.24", "the version");
        Check.Eq(list[1].Source, "winget", "the source after Match");
        Check.Eq(list[0].Source, "msstore", "an empty Match");

        // a Hangul name takes two cells a letter: the next column starts after them
        const string Ko = "Name     Id             Version Source\r\n----------------------------------------\r\n가나다라 XP0000000000AB Unknown msstore\r\n";
        var ko = Winget.Table(Ko);
        Check.Eq(ko.Count, 1, "one Korean package");
        Check.Eq(ko[0].Name, "가나다라", "Korean name");
        Check.Eq(ko[0].Id, "XP0000000000AB", "its id");
        Check.Eq(ko[0].Source, "msstore", "its source");
        Check.Eq(Winget.Table("No package found matching input criteria.").Count, 0, "nothing found");
    }

    public static void ShortcutsAsTyped()
    {
        Check.Eq(Hotkeys.Normal("Ctrl + Alt + M"), "ctrl+alt+m", "spaces and case");
        Check.Eq(Hotkeys.Normal("shift+alt+f5"), "alt+shift+f5", "modifiers in one order");
        Check.Eq(Hotkeys.Normal("컨트롤+알트+1"), "ctrl+alt+1", "Korean names");
        Check.True(Hotkeys.Normal("m") is null, "a key alone");
        Check.True(Hotkeys.Normal("ctrl+alt") is null, "no key");
        Check.True(Hotkeys.Normal("ctrl+a+b") is null, "two keys");
    }

    public static void ShortcutsAsPressed()
    {
        Check.Eq(Hotkeys.FromKey(Key.M, ModifierKeys.Control | ModifierKeys.Alt), "ctrl+alt+m", "ctrl+alt+m");
        Check.Eq(Hotkeys.FromKey(Key.F5, ModifierKeys.Shift | ModifierKeys.Alt), "alt+shift+f5", "modifiers in one order");
        Check.Eq(Hotkeys.FromKey(Key.NumPad3, ModifierKeys.Control), "ctrl+3", "the number pad's digit");
        Check.True(Hotkeys.FromKey(Key.M, ModifierKeys.None) is null, "a key alone");
        Check.True(Hotkeys.FromKey(Key.OemComma, ModifierKeys.Control) is null, "a key Windows can't take here");
        Check.True(Hotkeys.Parse(Hotkeys.FromKey(Key.Space, ModifierKeys.Control)!) is not null, "what is pressed registers");
        Check.Eq(Hotkeys.Label("ctrl+alt+m"), "Ctrl+Alt+M", "shown as pressed");
    }

    public static void UninstallCommands()
    {
        Check.Eq(Uninstalls.Split("\"C:\\Program Files\\App\\unins000.exe\" /SILENT"), (@"C:\Program Files\App\unins000.exe", "/SILENT"), "quoted");
        Check.Eq(Uninstalls.Split(@"C:\Program Files\App\uninstall.exe --remove"), (@"C:\Program Files\App\uninstall.exe", "--remove"), "unquoted with a space in the path");
        Check.Eq(Uninstalls.Split("msiexec.exe /x {11111111-2222-3333-4444-555555555555}"), ("msiexec.exe", "/x {11111111-2222-3333-4444-555555555555}"), "msiexec");
        Check.Eq(Uninstalls.Split("rundll32 shell32,Control_RunDLL"), ("rundll32", "shell32,Control_RunDLL"), "no .exe");
    }

    public static void Layouts()
    {
        var half = Check.NotNull(WindowLayout.Find("layout:right"), "right half");
        Check.Eq((half.X, half.W), (0.5, 0.5), "the right half");
        Check.True(WindowLayout.Find("layout:nope") is null, "unknown");
        foreach (var l in WindowLayout.All)
            Check.True(l.X >= 0 && l.Y >= 0 && l.X + l.W <= 1.0001 && l.Y + l.H <= 1.0001, l.Key + " stays on the screen");
    }
}
