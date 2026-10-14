using System;
using System.Collections.Generic;
using Bloom.Ai;
using Bloom.Native;

namespace Bloom.Tests;

/// A notice card's click goes to the window of its CLI: the climb up the process tree to the first window, and which
/// Codex process a thread belongs to.
internal static class SessionWindowTests
{
    private static readonly IntPtr Terminal = new(0x100), Folder = new(0x200);

    // 10 claude.exe ← 20 pwsh.exe ← 30 WindowsTerminal.exe (a window); 40 codex.exe ← 50 explorer.exe (windows: folders)
    private static readonly Dictionary<int, (int parent, string exe)> Tree = new()
    {
        [10] = (20, "claude.exe"),
        [20] = (30, "pwsh.exe"),
        [30] = (1, "WindowsTerminal.exe"),
        [1] = (0, "System"),
        [40] = (50, "codex.exe"),
        [50] = (1, "explorer.exe"),
    };

    private static readonly Dictionary<int, IntPtr> Windows = new() { [30] = Terminal, [50] = Folder };

    public static void ClimbsToTheHostWindow()
    {
        Check.Eq(SessionWindow.Climb(10, Tree, Windows, (_, _) => true), Terminal, "claude → shell → terminal window");
        Check.Eq(SessionWindow.Climb(30, Tree, Windows, (_, _) => true), Terminal, "the process itself having the window");
    }

    public static void StopsWhereItShould()
    {
        Check.Eq(SessionWindow.Climb(40, Tree, Windows, (_, _) => true), IntPtr.Zero, "never the shell's folder windows");
        Check.Eq(SessionWindow.Climb(99, Tree, Windows, (_, _) => true), IntPtr.Zero, "a process that is gone");
        Check.Eq(SessionWindow.Climb(10, Tree, Windows, (child, _) => child != 20), IntPtr.Zero, "a parent id that was reused is not climbed to");
        var loop = new Dictionary<int, (int parent, string exe)> { [1] = (2, "a.exe"), [2] = (1, "b.exe") };
        Check.Eq(SessionWindow.Climb(1, loop, new Dictionary<int, IntPtr>(), (_, _) => true), IntPtr.Zero, "a loop of parents ends");
    }

    public static void CodexProcessPick()
    {
        var at = new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc);
        Check.Eq(CodexProcess.Pick(new List<(int, DateTime)>(), at), null, "none running");
        Check.Eq(CodexProcess.Pick(new List<(int, DateTime)> { (7, at.AddHours(-5)) }, at), (int?)7, "the only one running");
        Check.Eq(CodexProcess.Pick(new List<(int, DateTime)> { (7, at.AddMinutes(5)) }, at), null, "the only one running started after the log: not its process");
        var two = new List<(int, DateTime)> { (7, at.AddHours(-5)), (8, at.AddSeconds(-20)) };
        Check.Eq(CodexProcess.Pick(two, at), (int?)8, "the one started just before the log was made");
        Check.Eq(CodexProcess.Pick(new List<(int, DateTime)> { (7, at.AddHours(-5)), (8, at.AddHours(-3)) }, at), null, "two, none near: not told");
        Check.Eq(CodexProcess.Pick(new List<(int, DateTime)> { (7, at.AddSeconds(-30)), (8, at.AddSeconds(-20)) }, at), null, "two near: not told");
    }
}
