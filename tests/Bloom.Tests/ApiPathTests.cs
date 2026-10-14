using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Bloom.Ai;
using Bloom.Core;
using static Bloom.Tests.ApiToolTests;

namespace Bloom.Tests;

/// How the API chats' file tools decide before they touch a path: its kind from its text alone (a network path or a
/// device is asked about, or refused, with no file system call), the file judged by the handle that is then used (hard
/// links count as outside; a file that turns into a link after the answer is not written), and the cards that show what
/// will be written.
internal static class ApiPathTests
{
    private static void HardLink(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (string a in new[] { "/c", "mklink", "/H", link, target }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Check.True(File.Exists(link), "the hard link was made");
    }

    public static void PathsAreClassedFromTheirTextAlone()
    {
        Check.Eq(RealPath.Kind(@"C:\Users\x\f.txt"), PathKind.Local, "a drive path");
        Check.Eq(RealPath.Kind(@"\\server\share\f.txt"), PathKind.Network, "a UNC path");
        foreach (string device in new[] { @"\\?\C:\x", @"\\.\pipe\x", "//?/C:/x", @"\\.\NUL", @"C:\x\NUL", @"C:\x\com1.txt", @"C:\x\Lpt3", @"C:\x\a.txt:stream", "relative.txt" })
            Check.Eq(RealPath.Kind(device), PathKind.Device, "not a plain path: " + device);
        foreach (string odd in new[] { @"\\server\share\a.txt:stream", @"\\server\share\NUL", @"\\server\share\dir\com1.txt", @"\\server\share\a.", @"\\server\sh \x", @"\\server\share:x\a", @"\\", @"C:\x\a.", @"C:\x\a " })
            Check.Eq(RealPath.Kind(odd), PathKind.Device, "refused, not asked: " + odd);
        Check.Eq(RealPath.Kind(@"\\10.0.0.1\share\dir\a.txt"), PathKind.Network, "a plain share path is still asked");
        Check.Eq(RealPath.Kind(@"C:\x\Nullable.txt"), PathKind.Local, "a name that only starts like a device");
        Check.Eq(RealPath.Kind(@"C:\x\CONSOLE"), PathKind.Local, "another");
    }

    public static void ANetworkPathIsAskedBeforeAnyFileSystemCall()
    {
        using var f = new Folder();
        const string unc = @"\\10.255.255.1\share\x.txt"; // nothing answers there: a file system call would hang for many seconds
        var asked = new List<ToolAsk>();
        var box = Box(f, _ => false, asked);
        var clock = Stopwatch.StartNew();
        Run(box, ApiToolbox.ReadFile, new { path = unc });
        Run(box, ApiToolbox.ListFolder, new { path = unc });
        Run(box, ApiToolbox.WriteFile, new { path = unc, content = "x" });
        Run(box, ApiToolbox.EditFile, new { path = unc, old_text = "a", new_text = "b" });
        Check.True(clock.Elapsed < TimeSpan.FromSeconds(3), "nothing reached out to the network before the answer: " + clock.Elapsed);
        Check.Eq(asked.Count, 4, "all four asked");
        Check.True(asked.All(a => a.Detail.StartsWith(unc, StringComparison.Ordinal)) && asked[0].Key == ToolPermissions.ReadOutside && asked[2].Key == ToolPermissions.Write, "the card names the path, under the action's key");
    }

    public static void DevicePathsAreRefusedWithoutAsking()
    {
        using var f = new Folder();
        var asked = new List<ToolAsk>();
        var box = Box(f, _ => true, asked);
        foreach (string path in new[] { @"\\.\pipe\bloom", @"\\?\C:\Windows", "NUL", "CON", "notes.txt:hidden", @"C:\x\COM1" })
        {
            var (text, error) = Run(box, ApiToolbox.ReadFile, new { path });
            Check.True(error && text.Contains("not supported", StringComparison.Ordinal), "refused: " + path + " → " + text);
        }
        var (written, bad) = Run(box, ApiToolbox.WriteFile, new { path = @"\\.\NUL", content = "x" });
        Check.True(bad && written.Contains("not supported", StringComparison.Ordinal) && asked.Count == 0, "a write to a device is refused before any card");
    }

    public static void AFileWithAnotherNameIsAskedAbout()
    {
        using var f = new Folder();
        using var outside = new Folder();
        var asked = new List<ToolAsk>();
        var box = Box(f, _ => false, asked);
        var (_, error) = Run(box, ApiToolbox.ReadFile, new { path = "notes.txt" });
        Check.True(!error && asked.Count == 0, "one name: a quiet read");
        string other = Path.Combine(outside.Path, "same.txt");
        HardLink(other, Path.Combine(f.Path, "notes.txt"));
        (_, error) = Run(box, ApiToolbox.ReadFile, new { path = "notes.txt" });
        Check.True(error && asked.Count == 1 && asked[0].Key == ToolPermissions.ReadOutside, "a second name (a hard link) may lead outside: asked");
    }

    public static void AFileThatBecomesALinkAfterTheAnswerIsNotWritten()
    {
        using var f = new Folder();
        using var outside = new Folder();
        string secret = Path.Combine(outside.Path, "secret.txt");
        File.WriteAllText(secret, "keep");
        var box = Box(f, ask =>
        {
            HardLink(Path.Combine(f.Path, "new.txt"), secret); // after the card, before the write
            return true;
        });
        var (text, error) = Run(box, ApiToolbox.WriteFile, new { path = "new.txt", content = "overwritten" });
        Check.True(error && text.Contains("so nothing was done", StringComparison.Ordinal), "refused: " + text);
        Check.Eq(File.ReadAllText(secret), "keep", "the file it turned into was not emptied or written");
    }

    public static void WriteAndEditCardsShowTheContentAndTheChange()
    {
        Lang.Force(true);
        using var f = new Folder();
        var asked = new List<ToolAsk>();
        var box = Box(f, _ => false, asked);
        Run(box, ApiToolbox.WriteFile, new { path = "a.txt", content = "line1\nline2\u0007" });
        Check.True(asked[0].Detail.Contains("── Content ──\nline1\nline2␇", StringComparison.Ordinal), "a write's card has the content, made visible: " + asked[0].Detail);
        Run(box, ApiToolbox.EditFile, new { path = "notes.txt", old_text = "contents", new_text = "CONTENTS" });
        Check.True(asked[1].Detail.Contains("── Before ──\ncontents", StringComparison.Ordinal) && asked[1].Detail.Contains("── After ──\nCONTENTS", StringComparison.Ordinal), "an edit's card has the old and the new text: " + asked[1].Detail);
        Check.True(asked[1].Detail.Contains("The file exists already.", StringComparison.Ordinal), "and that the file is there");
        Check.Eq(File.ReadAllText(Path.Combine(f.Path, "notes.txt")), "the notes' contents", "declined: unchanged");
        Run(box, ApiToolbox.WriteFile, new { path = "big.txt", content = new string('x', ToolText.MaxPayload - 1000) });
        Check.True(asked[2].Detail.Contains(new string('x', ToolText.MaxPayload - 1000), StringComparison.Ordinal), "a large content is shown whole, not cut");
        var (refusal, refused) = Run(box, ApiToolbox.WriteFile, new { path = "bigger.txt", content = new string('x', ToolText.MaxPayload + 1) });
        Check.True(refused && refusal.Contains("shown for approval in full", StringComparison.Ordinal) && asked.Count == 3, "one a card cannot show whole is refused with a reason, not asked about");
        Check.True(asked.All(a => a.Says.Length < 120 && !a.Says.Contains('\n')), "the ✓ line stays short");
    }

    public static void AllowedWritesAndEditsGoThroughTheOpenedFile()
    {
        using var f = new Folder();
        var box = Box(f, _ => true);
        Check.True(!Run(box, ApiToolbox.WriteFile, new { path = "sub\\new.txt", content = "héllo" }).Error && File.ReadAllText(Path.Combine(f.Path, "sub", "new.txt")) == "héllo", "a new file in a new folder");
        Check.True(!Run(box, ApiToolbox.WriteFile, new { path = "notes.txt", content = "short" }).Error && File.ReadAllText(Path.Combine(f.Path, "notes.txt")) == "short", "a longer file replaced whole, not overlaid");
        Check.True(!Run(box, ApiToolbox.EditFile, new { path = "notes.txt", old_text = "short", new_text = "a much longer text" }).Error && File.ReadAllText(Path.Combine(f.Path, "notes.txt")) == "a much longer text", "an edit");
        Check.True(!Run(box, ApiToolbox.EditFile, new { path = "notes.txt", old_text = "much longer", new_text = "x" }).Error && File.ReadAllText(Path.Combine(f.Path, "notes.txt")) == "a x text", "an edit that shortens the file");
        Check.True(Run(box, ApiToolbox.EditFile, new { path = "missing.txt", old_text = "a", new_text = "b" }).Error, "an edit of a file that isn't there");
    }

    public static void FencedFilesStayInsideTheOutputFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "bloom-fenced-" + Guid.NewGuid().ToString("N")[..8]), outside = Path.Combine(Path.GetTempPath(), "bloom-fenced-out-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            string secret = Path.Combine(outside, "secret.txt");
            File.WriteAllText(secret, "keep");
            HardLink(Path.Combine(root, "linked.txt"), secret);
            const string text = "```file:NUL\nx\n```\n```file:linked.txt\nx\n```\n```file:fine.txt\nok\n```";
            var result = ChatOutput.Process(new ChatOutputRequest(text, root, null, true, "saved {0}"), CancellationToken.None);
            Check.Eq(File.ReadAllText(secret), "keep", "a file with another name is not written");
            Check.Eq(File.ReadAllText(Path.Combine(root, "fine.txt")), "ok", "a plain one is");
            Check.True(result.Text.Contains("```file:NUL", StringComparison.Ordinal) && result.Text.Contains("```file:linked.txt", StringComparison.Ordinal) && result.Text.Contains("saved fine.txt", StringComparison.Ordinal), "only the saved block is replaced");
            Check.Eq(string.Join(",", ChatOutput.FencedFiles(text).Select(x => x.Name)), "linked.txt,fine.txt", "the device name is not a file to save");
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(outside, true);
        }
    }
}
