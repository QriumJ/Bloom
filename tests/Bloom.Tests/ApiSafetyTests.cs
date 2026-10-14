using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Bloom.Ai;
using Bloom.Core;
using static Bloom.Tests.ApiToolTests;

namespace Bloom.Tests;

/// What keeps an API chat's file and command tools in bounds: paths taken as written, links out of the chat's folder
/// asked about, the card showing a command whole, a command's job and environment, secrets out of every result, and
/// 설정's 권한.
internal static class ApiSafetyTests
{
    public static void PathsExpandNoVariables()
    {
        using var f = new Folder();
        const string name = "BLOOM_TEST_PATH_VALUE", value = "leaked-value-4711";
        Environment.SetEnvironmentVariable(name, value);
        try
        {
            var asked = new List<ToolAsk>();
            var box = Box(f, _ => true, asked);
            var (text, error) = Run(box, ApiToolbox.ReadFile, new { path = $"%{name}%" });
            Check.True(error && !text.Contains(value, StringComparison.Ordinal) && text.Contains($"%{name}%", StringComparison.Ordinal), "the name read as written, its value nowhere");
            Check.Eq(asked.Count, 0, "a literal name in the chat's folder is a quiet read");
            (text, _) = Run(box, ApiToolbox.ListFolder, new { path = $"%{name}%\\sub" });
            Check.True(!text.Contains(value, StringComparison.Ordinal), "nor in a listing's error");
            (text, _) = Run(box, ApiToolbox.WriteFile, new { path = $"%{name}%.txt", content = "x" });
            Check.True(!text.Contains(value, StringComparison.Ordinal) && asked.All(a => !a.Says.Contains(value, StringComparison.Ordinal) && !a.Detail.Contains(value, StringComparison.Ordinal)),
                "nor on the card or in a write's result");
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public static void LinksOutOfTheChatFolderAreAsked()
    {
        using var f = new Folder();
        using var outside = new Folder();
        string link = Path.Combine(f.Path, "link"), root = Path.Combine(outside.Path, "chat-link");
        try
        {
            Links(f, outside, link, root);
        }
        finally
        {
            // the links themselves first (a recursive delete is refused on a junction)
            foreach (string made in new[] { link, root })
                if (Directory.Exists(made)) Directory.Delete(made);
        }
    }

    private static void Links(Folder f, Folder outside, string link, string root)
    {
        Junction(link, outside.Path);
        var asked = new List<ToolAsk>();
        var box = Box(f, _ => false, asked);
        var (text, error) = Run(box, ApiToolbox.ReadFile, new { path = "link\\notes.txt" });
        Check.True(error && !text.Contains("the notes' contents", StringComparison.Ordinal) && asked.Count == 1 && asked[0].Key == ToolPermissions.ReadOutside,
            "a read through a junction inside the folder is asked about");
        Check.True(asked[0].Detail.Contains(Path.GetFileName(outside.Path), StringComparison.OrdinalIgnoreCase), "the card says where it really leads");
        Run(box, ApiToolbox.ListFolder, new { path = "link" });
        Check.Eq(asked.Count, 2, "so is a listing of it");
        (_, error) = Run(box, ApiToolbox.ReadFile, new { path = "notes.txt" });
        Check.True(!error && asked.Count == 2, "a plain file in the folder is still a quiet read");
        (_, error) = Run(box, ApiToolbox.ReadFile, new { path = "link\\..\\notes.txt" });
        Check.True(!error && asked.Count == 2, "a path that only names the link on the way back out of it stays inside");

        // the chat's folder itself a link: nothing in it is quiet
        Junction(root, f.Path);
        (_, error) = Run(Box(f, _ => false, asked, folder: root), ApiToolbox.ReadFile, new { path = "notes.txt" });
        Check.True(error && asked.Count == 3, "a linked chat folder asks too");
    }

    public static void CardsShowTheWholeCommand()
    {
        using var f = new Folder();
        var asked = new List<ToolAsk>();
        string command = "Write-Output 'a'\r\n" + new string('x', 300) + "\u202Etxt.exe\u0007";
        Run(Box(f, _ => false, asked), ApiToolbox.RunCommand, new { command });
        var ask = asked.Single();
        Check.True(ask.Detail.Contains(new string('x', 300), StringComparison.Ordinal) && ask.Detail.Contains("⟨U+202E⟩", StringComparison.Ordinal) && ask.Detail.EndsWith('␇')
            && ask.Detail.Contains('\n') && !ask.Detail.Contains('\r') && !ask.Detail.Contains('\u202E'), "the card gets all of it, the hidden characters made visible");
        Check.True(ask.Says.Length < 120 && !ask.Says.Contains('\n') && ask.Key == ToolPermissions.Command, "the ✓ line keeps one short line");
        Check.Eq(ToolText.Visible("a\tb\rc\r\nd\0\u007f\u200b"), "a␉b␍c\nd␀␡⟨U+200B⟩", "control and format characters as pictures");

        string deep = Path.Combine(f.Path, string.Join("\\", Enumerable.Repeat("a-long-folder-name", 8)), "file.txt");
        Run(Box(f, _ => false, asked), ApiToolbox.WriteFile, new { path = deep, content = "x" });
        Check.True(asked[^1].Detail.StartsWith(deep, StringComparison.Ordinal) && !asked[^1].Says.Contains(deep, StringComparison.Ordinal) && asked[^1].Says.Length < 120,
            "a write's card names the whole path; the ✓ line a short one");
    }

    public static void DetachedProcessesEndWithTheCommand()
    {
        using var f = new Folder();
        string pidFile = Path.Combine(f.Path, "child.txt");
        string start = "$c = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 60' -WindowStyle Hidden -PassThru; "
            + $"Set-Content -LiteralPath '{pidFile}' -Value $c.Id";
        var (text, error) = ApiCommand.RunAsync(start, f.Path, CancellationToken.None).GetAwaiter().GetResult();
        Check.True(!error, "the command ran: " + text);
        Check.True(Gone(int.Parse(File.ReadAllText(pidFile).Trim())), "what it started in the background ends when it does");

        File.Delete(pidFile);
        using var stop = new CancellationTokenSource();
        var run = ApiCommand.RunAsync(start + "; Start-Sleep -Seconds 30", f.Path, stop.Token);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!File.Exists(pidFile) && DateTime.UtcNow < deadline) Thread.Sleep(50);
        Thread.Sleep(200); // the id written whole
        stop.Cancel();
        bool cancelled = false;
        try
        {
            run.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        Check.True(cancelled && Gone(int.Parse(File.ReadAllText(pidFile).Trim())), "a stop ends the detached process too");
    }

    public static void CommandsAndResultsCarryNoSecrets()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var f = new Folder();
        const string name = "BLOOM_TEST_SERVICE_TOKEN", value = "tok-secret-value-31337", key = "sk-stored-key-1234567890";
        Environment.SetEnvironmentVariable(name, value);
        try
        {
            Check.True(ApiCommand.Secret("OPENAI_ORG_ID") && ApiCommand.Secret("my_api_base") && ApiCommand.Secret("GITHUB_TOKEN") && ApiCommand.Secret("db_password")
                && !ApiCommand.Secret("PATH") && !ApiCommand.Secret("TEMP") && !ApiCommand.Secret("LOCALAPPDATA") && !ApiCommand.Secret("SystemRoot"), "names that look like credentials, and ones that don't");
            var (kept, hidden) = ApiCommand.Environment();
            Check.True(!kept.ContainsKey(name) && hidden.Contains(value) && kept.ContainsKey("PATH"), "the environment without it, its value noted");
            var (text, _) = ApiCommand.RunAsync($"Write-Output \"[$env:{name}]\"", f.Path, CancellationToken.None).GetAwaiter().GetResult();
            Check.True(text.Contains("[]", StringComparison.Ordinal) && !text.Contains(value, StringComparison.Ordinal), "a command doesn't get it");

            ApiKeys.Set("openai", key);
            try
            {
                var secrets = ApiToolbox.Secrets();
                Check.True(secrets.Contains(key) && secrets.Contains(value), "the stored keys and the hidden values");
                File.WriteAllText(Path.Combine(f.Path, "settings.env"), $"OPENAI_API_KEY={key}\nTOKEN={value}\n");
                var box = Box(f, _ => true, secrets: secrets);
                (text, _) = Run(box, ApiToolbox.ReadFile, new { path = "settings.env" });
                Check.True(text.Contains("OPENAI_API_KEY=***", StringComparison.Ordinal) && !text.Contains(key, StringComparison.Ordinal) && !text.Contains(value, StringComparison.Ordinal),
                    "a read result carries neither");
                (text, _) = Run(box, ApiToolbox.RunCommand, new { command = $"Write-Output '{key}'; Write-Output '{value}'" });
                Check.True(!text.Contains(key, StringComparison.Ordinal) && !text.Contains(value, StringComparison.Ordinal), "nor a command's output");
            }
            finally
            {
                ApiKeys.Remove("openai");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public static void PermissionsDecidePerAction()
    {
        var rules = new Dictionary<string, string> { [ToolPermissions.Pc] = ToolPermissions.Off, [ToolPermissions.Command] = ToolPermissions.Allow };
        Check.True(ToolPermissions.Of(rules, "pc:move") == ToolPermissions.Off && ToolPermissions.Of(rules, ToolPermissions.Command) == ToolPermissions.Allow
            && ToolPermissions.Of(rules, ToolPermissions.Write) == ToolPermissions.Ask && ToolPermissions.Of(rules, "Write") == ToolPermissions.Ask, "each action by its key; other tools always ask");
        Check.True(ToolPermissions.IsFileOrCommand(ToolPermissions.Command) && !ToolPermissions.IsFileOrCommand("pc:move") && !ToolPermissions.IsFileOrCommand("Write"), "which card title");

        using var f = new Folder();
        var off = ToolPermissions.FileActions.ToDictionary(a => a, _ => ToolPermissions.Off);
        var asked = new List<ToolAsk>();
        var box = Box(f, _ => true, asked, rules: off);
        Check.Eq(string.Join(",", box.Tools.Select(t => t.Name)), "read_file,list_folder", "사용 안 함 takes the tool away; reading the chat's folder stays");
        var (text, error) = Run(box, ApiToolbox.WriteFile, new { path = "a.txt", content = "x" });
        Check.True(error && text.Contains("switched this off", StringComparison.Ordinal) && !File.Exists(Path.Combine(f.Path, "a.txt")), "a call to it anyway is refused");
        (text, error) = Run(box, ApiToolbox.ReadFile, new { path = Path.Combine(Path.GetTempPath(), "elsewhere.txt") });
        Check.True(error && text.Contains("switched this off", StringComparison.Ordinal) && asked.Count == 0, "a read outside, refused without asking");
        (_, error) = Run(box, ApiToolbox.ReadFile, new { path = "notes.txt" });
        Check.True(!error, "a read inside still works");

        var old = JsonSerializer.Deserialize<Settings>("""{"apiFileTools":false}""", Json.Options)!;
        old.MoveApiFileTools();
        Check.True(old.ApiFileTools is null && ToolPermissions.FileActions.All(a => old.Permissions[a] == ToolPermissions.Off) && !old.Permissions.ContainsKey(ToolPermissions.Pc),
            "the old switch off is the four file and command actions off");
        var on = JsonSerializer.Deserialize<Settings>("""{"apiFileTools":true}""", Json.Options)!;
        on.MoveApiFileTools();
        Check.True(on.ApiFileTools is null && on.Permissions.Count == 0, "on changes nothing");

        var prior = new Dictionary<string, string>(Settings.Current.Permissions);
        try
        {
            Settings.Current.Permissions[ToolPermissions.Pc] = ToolPermissions.Off;
            Check.True(!Bloom.App.Mcp.BloomTools.OfferedTo("claude") && !Bloom.App.Mcp.BloomTools.OfferedTo("api"), "PC 조작 사용 안 함 takes Bloom's tool from every chat");
        }
        finally
        {
            Settings.Current.Permissions = prior;
        }
    }

    /// A junction at `link` to `target` (no administrator needed, unlike a symbolic link).
    private static void Junction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (string a in new[] { "/c", "mklink", "/J", link, target }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Check.True(Directory.Exists(link) && File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint), "the junction was made");
    }

    /// The process ended (or is no longer there) within 5 s.
    private static bool Gone(int pid)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited) return true;
            }
            catch (ArgumentException)
            {
                return true;
            }
            Thread.Sleep(100);
        }
        return false;
    }
}
