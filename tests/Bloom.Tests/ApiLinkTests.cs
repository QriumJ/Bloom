using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Bloom.Ai;
using Bloom.Core;
using static Bloom.Tests.ApiToolTests;

namespace Bloom.Tests;

/// Links on the way to a file (found without being followed, and asked about whatever 권한 says), a folder listed through
/// the handle that was judged, an edit that needs a file that is there, and a write that makes nothing until the folder it
/// goes into has been checked.
internal static class ApiLinkTests
{
    private static void Junction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (string a in new[] { "/c", "mklink", "/J", link, target }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Check.True(Directory.Exists(link), "the junction was made");
    }

    private static Dictionary<string, string> AllowAll() => new()
    {
        [ToolPermissions.ReadOutside] = ToolPermissions.Allow,
        [ToolPermissions.Write] = ToolPermissions.Allow,
        [ToolPermissions.Edit] = ToolPermissions.Allow,
    };

    public static void ALinkIsFoundWithoutBeingFollowedAndItsTargetIsClassed()
    {
        using var f = new Folder();
        using var outside = new Folder();
        string link = Path.Combine(f.Path, "jump");
        Junction(link, outside.Path);
        try
        {
            var trace = RealPath.Trace(f.Path, Path.Combine(link, "deeper", "notes.txt"));
            Check.True(trace.Any && RealPath.Same(trace.Link!, link) && RealPath.Same(trace.Target!, outside.Path) && !trace.Remote, "found at the junction, which stops the look");
            Check.True(!RealPath.Trace(f.Path, Path.Combine(f.Path, "notes.txt")).Any && !RealPath.Trace(f.Path, Path.Combine(f.Path, "sub", "none.txt")).Any, "no link, or nothing there yet");

            // the target gone: the junction is still found, from its own data, with nothing opened beyond it
            string moved = outside.Path + "-moved";
            Directory.Move(outside.Path, moved);
            try
            {
                Check.True(RealPath.Trace(f.Path, Path.Combine(link, "notes.txt")).Any, "a link to nowhere is found all the same");
            }
            finally
            {
                Directory.Move(moved, outside.Path);
            }

            var unc = RealPath.Linking(@"C:\a\link", @"\??\UNC\host\share\dir");
            Check.True(unc.Remote && unc.Target == @"\\host\share\dir", "a share is remote");
            Check.True(RealPath.Linking(@"C:\a\link", @"\\?\Volume{1234}\").Remote && RealPath.Linking(@"C:\a\link", @"\??\Volume{1234}\").Remote, "a device or volume is remote");
            var relative = RealPath.Linking(@"C:\a\b\link", @"..\x");
            Check.True(relative.Target == @"C:\a\x" && !relative.Remote, "a relative target is taken from the link's folder");
            Check.True(RealPath.Linking(@"C:\a\link", null) is { Any: true, Target: null, Remote: false }, "a reparse point with no told target is still a link");
        }
        finally
        {
            Directory.Delete(link); // the junction alone: its folder is not followed
        }
    }

    public static void APathThroughALinkIsAlwaysPutToTheUserAndNothingIsOpenedBefore()
    {
        Lang.Force(true);
        using var f = new Folder();
        using var outside = new Folder();
        string link = Path.Combine(f.Path, "jump");
        Junction(link, outside.Path);
        try
        {
            var asked = new List<ToolAsk>();
            var box = Box(f, _ => false, asked, rules: AllowAll());
            var (text, error) = Run(box, ApiToolbox.ReadFile, new { path = "jump\\notes.txt" });
            Check.True(error && asked.Count == 1 && asked[0].MustAsk && asked[0].Key == ToolPermissions.ReadOutside, "asked although reading outside is allowed: MustAsk");
            Check.True(asked[0].Detail.Contains("Goes through a link", StringComparison.Ordinal) && asked[0].Detail.Contains(outside.Path, StringComparison.OrdinalIgnoreCase), "the card shows the link and where it leads: " + asked[0].Detail);
            Check.True(!text.Contains("the notes' contents", StringComparison.Ordinal), "declined: nothing read");
            Run(box, ApiToolbox.WriteFile, new { path = "jump\\new.txt", content = "x" });
            Run(box, ApiToolbox.EditFile, new { path = "jump\\notes.txt", old_text = "notes", new_text = "NOTES" });
            Run(box, ApiToolbox.ListFolder, new { path = "jump" });
            Check.True(asked.Count == 4 && asked.All(a => a.MustAsk), "write, edit and list too");
            Check.True(!File.Exists(Path.Combine(outside.Path, "new.txt")) && File.ReadAllText(Path.Combine(outside.Path, "notes.txt")) == "the notes' contents", "declined: nothing written");
            Check.True(!asked.Any(a => a.Key == ToolPermissions.ReadOutside && a.MustAsk == false), "no quiet question among them");

            var yes = Box(f, _ => true, rules: AllowAll());
            Check.Eq(Run(yes, ApiToolbox.ReadFile, new { path = "jump\\notes.txt" }), ("the notes' contents", false), "allowed: it is read");
            Check.True(asked.Count == 4 && !new ToolAsk("a", "t", "{}", "s").MustAsk, "an ordinary question has no MustAsk");
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    public static void AFolderIsListedThroughItsOpenedHandle()
    {
        using var f = new Folder();
        Directory.CreateDirectory(Path.Combine(f.Path, "sub"));
        File.WriteAllText(Path.Combine(f.Path, "b.txt"), "12345");
        var box = Box(f, _ => true);
        var (text, error) = Run(box, ApiToolbox.ListFolder, new { path = "." });
        var lines = text.Split('\n').ToList();
        Check.True(!error && lines.Contains("sub\\") && lines.Contains("b.txt (5 bytes)") && lines.Any(l => l.StartsWith("notes.txt (", StringComparison.Ordinal)) && lines.Count == 3, "folders end with a backslash, files carry their size: " + text);
        for (int i = 0; i < 10; i++) File.WriteAllText(Path.Combine(f.Path, $"many{i}.txt"), "");
        using var folder = RealPath.OpenExisting(f.Path, false, out _)!;
        Check.Eq(RealPath.Entries(folder, 5).Count, 5, "a list stops at the most asked for");
        Check.Eq(RealPath.Entries(folder, 1000).Count, 13, "and otherwise gives all");
    }

    public static void AnEditNeedsAFileThatIsThereAndIsRefusedBeforeTheCard()
    {
        using var f = new Folder();
        var asked = new List<ToolAsk>();
        var box = Box(f, _ => true, asked);
        var (text, error) = Run(box, ApiToolbox.EditFile, new { path = "nothing.txt", old_text = "a", new_text = "b" });
        Check.True(error && asked.Count == 0 && text.Contains("no file", StringComparison.Ordinal) && !File.Exists(Path.Combine(f.Path, "nothing.txt")), "refused, nothing asked, nothing made: " + text);
    }

    public static void ARejectedWriteMakesNothing()
    {
        using var f = new Folder();
        var declined = Box(f, _ => false);
        Run(declined, ApiToolbox.WriteFile, new { path = "a\\b\\c.txt", content = "x" });
        Check.True(!Directory.Exists(Path.Combine(f.Path, "a")) && !File.Exists(Path.Combine(f.Path, "a", "b", "c.txt")), "declined: no folder, no empty file");

        // a path that does not lead where the card said: refused before anything is made
        using var elsewhere = new Folder();
        string target = Path.Combine(f.Path, "x", "y", "z.txt");
        var made = RealPath.CreateIn(target, Path.Combine(elsewhere.Path, "x", "y", "z.txt"), out int error);
        Check.True(made is null && error == RealPath.Diverged && !Directory.Exists(Path.Combine(f.Path, "x")), "a wrong account of where it leads: nothing made");

        var yes = Box(f, _ => true);
        Check.True(!Run(yes, ApiToolbox.WriteFile, new { path = "a\\b\\c.txt", content = "héllo" }).Error && File.ReadAllText(Path.Combine(f.Path, "a", "b", "c.txt")) == "héllo", "allowed: the folders and the file are made");
        using (var again = RealPath.CreateIn(Path.Combine(f.Path, "a", "b", "c.txt"), null, out _))
            Check.True(again is { Created: false, Parent: not null }, "an existing file is opened, with its folder held");
        Check.Eq(File.ReadAllText(Path.Combine(f.Path, "a", "b", "c.txt")), "héllo", "and not emptied");
    }

    public static void AChatFolderThatIsItselfALinkIsFoundAndAsked()
    {
        Lang.Force(true);
        using var real = new Folder();
        string root = real.Path + "-root";
        Junction(root, real.Path);
        try
        {
            var trace = RealPath.Trace(root, Path.Combine(root, "notes.txt"));
            Check.True(trace.Any && RealPath.Same(trace.Link!, root) && RealPath.Same(trace.Target!, real.Path) && RealPath.Same(trace.Expected!, Path.Combine(real.Path, "notes.txt")), "the chat's own folder is looked at: " + trace);
            Check.True(!RealPath.Trace(real.Path, Path.Combine(real.Path, "notes.txt")).Any, "a plain chat folder is no link");

            var asked = new List<ToolAsk>();
            var no = Box(real, _ => false, asked, rules: AllowAll(), folder: root);
            var (text, error) = Run(no, ApiToolbox.ReadFile, new { path = "notes.txt" });
            Check.True(error && asked.Count == 1 && asked[0].MustAsk && asked[0].Detail.Contains("Goes through a link", StringComparison.Ordinal) && !text.Contains("contents", StringComparison.Ordinal), "asked, never read quietly, however 권한 is set");
            Check.Eq(Run(Box(real, _ => true, rules: AllowAll(), folder: root), ApiToolbox.ReadFile, new { path = "notes.txt" }), ("the notes' contents", false), "allowed: read through the verified target");
        }
        finally
        {
            Directory.Delete(root);
        }
    }

    public static void ALinkTheUserWasShownIsKeptAndATargetThatChangedIsRefused()
    {
        Lang.Force(true);
        using var f = new Folder();
        using var outside = new Folder();
        using var elsewhere = new Folder();
        File.WriteAllText(Path.Combine(elsewhere.Path, "f.txt"), "the secret");
        string link = Path.Combine(f.Path, "jump");
        Junction(link, outside.Path);
        try
        {
            // while a call is asked about, its link can't be taken away
            bool blocked = false, removed = false;
            var box = Box(f, _ =>
            {
                try
                {
                    Directory.Delete(link);
                    removed = true;
                }
                catch (IOException)
                {
                    blocked = true;
                }
                return true;
            }, rules: AllowAll());
            Check.Eq(Run(box, ApiToolbox.ReadFile, new { path = "jump\\notes.txt" }), ("the notes' contents", false), "read through the link as shown");
            Check.True(blocked && !removed, "the link was held: it could not be deleted meanwhile");

            // a link that appears below it after the card: the path is not what was shown
            string below = Path.Combine(outside.Path, "sub");
            var swap = Box(f, _ =>
            {
                Junction(below, elsewhere.Path);
                return true;
            }, rules: AllowAll());
            var (text, error) = Run(swap, ApiToolbox.ReadFile, new { path = "jump\\sub\\f.txt" });
            Check.True(error && text.Contains("somewhere else", StringComparison.Ordinal) && !text.Contains("secret", StringComparison.Ordinal), "refused, not read: " + text);
            Directory.Delete(below);

            var made = Box(f, _ =>
            {
                Junction(below, elsewhere.Path);
                return true;
            }, rules: AllowAll());
            (text, error) = Run(made, ApiToolbox.WriteFile, new { path = "jump\\sub\\new.txt", content = "x" });
            Check.True(error && text.Contains("somewhere else", StringComparison.Ordinal) && !File.Exists(Path.Combine(elsewhere.Path, "new.txt")), "a write is refused too, and makes nothing there: " + text);
            Directory.Delete(below);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    public static void ALinkTraceKnowsWhenALinkWasReplaced()
    {
        using var f = new Folder();
        using var one = new Folder();
        using var two = new Folder();
        string link = Path.Combine(f.Path, "jump");
        Junction(link, one.Path);
        var before = RealPath.Trace(f.Path, Path.Combine(link, "notes.txt"));
        Check.True(before.Any && before.Identity is { Length: > 0 }, "the link's identity is recorded");
        Check.Eq(RealPath.Trace(f.Path, Path.Combine(link, "notes.txt")), before, "read again unchanged: equal");
        Directory.Delete(link);
        Junction(link, two.Path);
        try
        {
            Check.True(RealPath.Trace(f.Path, Path.Combine(link, "notes.txt")) != before, "replaced by a link elsewhere: not equal");
        }
        finally
        {
            Directory.Delete(link);
        }
        // a link held can't be replaced until the hold ends
        Junction(link, one.Path);
        var hold = new TraceHold();
        RealPath.Trace(f.Path, Path.Combine(link, "notes.txt"), hold);
        bool blocked = false;
        try
        {
            Directory.Delete(link);
        }
        catch (IOException)
        {
            blocked = true;
        }
        hold.Dispose();
        Check.True(blocked && Directory.Exists(link), "held: not deleted");
        Directory.Delete(link);
    }

    public static void ANewFileIsMadeExclusivelyAndNothingIsMadeThroughALeafLink()
    {
        using var f = new Folder();
        using var outside = new Folder();
        string leaf = Path.Combine(f.Path, "leaf.txt"), target = outside.Path + "-gone";
        Directory.CreateDirectory(target);
        Junction(leaf, target);
        Directory.Delete(target); // the link now leads nowhere
        try
        {
            using var file = RealPath.OpenForWrite(leaf, out int error);
            Check.True(file is null && error == RealPath.Diverged && !Directory.Exists(target) && !File.Exists(target), "a link at the leaf is refused, nothing is made where it leads");
            using var viaCreate = RealPath.CreateIn(leaf, null, out error);
            Check.True(viaCreate is null && !Directory.Exists(target) && !File.Exists(target), "CreateIn too");
        }
        finally
        {
            Directory.Delete(leaf);
        }
        string fresh = Path.Combine(f.Path, "fresh.txt");
        using (var made = RealPath.OpenForWrite(fresh, out _))
            Check.True(made is { Created: true } && File.Exists(fresh) && new FileInfo(fresh).Length == 0, "a new file is made, empty");
        File.WriteAllText(fresh, "kept");
        using (var again = RealPath.OpenForWrite(fresh, out _))
            Check.True(again is { Created: false }, "a file that is there is opened, not made");
        Check.Eq(File.ReadAllText(fresh), "kept", "and not emptied");
    }
}
