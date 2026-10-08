using System;
using System.IO;
using Bloom.Search;

namespace Bloom.Tests;

/// 내 명령: the placeholders, a chain's steps and waits, and how a script line is started.
internal static class CommandTests
{
    public static void Placeholders()
    {
        var now = new DateTime(2026, 10, 8, 14, 5, 0);
        Check.Eq(UserCommands.Expand("백업 {input} {date} {time}", "  D:\\사진 ", null, now), "백업 D:\\사진 2026-10-08 14:05", "input trimmed, date, time");
        Check.Eq(UserCommands.Expand("붙여: {CLIPBOARD}", "", () => "복사한 글", now), "붙여: 복사한 글", "clipboard, any case");
        Check.Eq(UserCommands.Expand("{clipboard}", "", null, now), "{clipboard}", "no clipboard reader: left as it is");
        Check.Eq(UserCommands.SnippetText("안녕하세요\\n감사합니다"), "안녕하세요\n감사합니다", "\\n is a new line in a snippet");
    }

    public static void ChainSteps()
    {
        Check.Eq(string.Join("|", UserCommands.Steps(" 메모장 ; 볼륨 30;;기다리기 2초 \n yt lofi ")), "메모장|볼륨 30|기다리기 2초|yt lofi", "split by ; and new lines, trimmed");
        Check.Eq(UserCommands.Wait("기다리기 2초"), TimeSpan.FromSeconds(2), "기다리기 2초");
        Check.Eq(UserCommands.Wait("wait 1.5"), TimeSpan.FromSeconds(1.5), "wait 1.5");
        Check.Eq(UserCommands.Wait("대기 600"), TimeSpan.FromSeconds(60), "at most a minute");
        Check.Eq(UserCommands.Wait("기다리기"), null, "no number: not a wait");
        Check.Eq(UserCommands.Wait("볼륨 30"), null, "another step");
    }

    public static void ScriptStart()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bloom-tests", "my scripts");
        Directory.CreateDirectory(dir);
        string ps1 = Path.Combine(dir, "백업.ps1"), bat = Path.Combine(dir, "run.bat");
        File.WriteAllText(ps1, "Write-Output ok");
        File.WriteAllText(bat, "@echo ok");

        Check.Eq(UserCommands.SplitFile($"{ps1} -Path D:\\a"), (ps1, "-Path D:\\a"), "an unquoted path with spaces, then its arguments");
        Check.Eq(UserCommands.SplitFile($"\"{bat}\" x"), (bat, "x"), "a quoted path");
        Check.Eq(UserCommands.SplitFile("Get-Date -Format yyyy").file, null, "a command line has no file");

        var hidden = UserCommands.StartInfo($"{ps1} -Path D:\\a", false);
        Check.True(hidden.FileName.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase), ".ps1 through PowerShell");
        Check.True(hidden.ArgumentList[^1].Contains("UTF8") && hidden.ArgumentList[^1].EndsWith($"& '{ps1}' -Path D:\\a", StringComparison.Ordinal) && !hidden.ArgumentList.Contains("-NoExit"), "hidden: UTF-8 output, the file and its arguments");
        Check.True(hidden.CreateNoWindow && hidden.RedirectStandardOutput && !hidden.UseShellExecute, "hidden: no window, output read");
        Check.Eq(hidden.WorkingDirectory, dir, "runs in the script's folder");

        var shown = UserCommands.StartInfo($"\"{bat}\"", true);
        Check.True(shown.FileName.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) && shown.Arguments.StartsWith("/k ", StringComparison.Ordinal) && shown.UseShellExecute, ".bat in a console that stays open");

        var line = UserCommands.StartInfo("Get-ChildItem \"C:\\\"", false);
        Check.True(line.FileName.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase) && line.ArgumentList[^1].EndsWith("Get-ChildItem \"C:\\\"", StringComparison.Ordinal), "a command line as one PowerShell argument, as typed (ArgumentList escapes it)");
        Check.Eq(UserCommands.LastLine("one\r\ntwo\r\n\r\n"), "two", "the last line with words");

        Check.Eq(UserCommands.ScriptLine($"{ps1} {{input}}", "D:\\내 사진"), $"{ps1} 'D:\\내 사진'", "{input} with a space: one PowerShell argument");
        Check.Eq(UserCommands.ScriptLine($"\"{bat}\" {{input}}", "a b"), $"\"{bat}\" \"a b\"", "for a .bat: double quotes");
        Check.Eq(UserCommands.ScriptLine("Write-Output \"{input}\"", "a b"), "Write-Output \"a b\"", "already quoted in the line: as typed");
        Check.Eq(UserCommands.ScriptLine("Write-Output {input}", "abc"), "Write-Output abc", "one word: as typed");
    }
}
