using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bloom.Core;

namespace Bloom.Search;

/// One of 내 명령: a script (a file, or a PowerShell line) run from the launcher, a snippet pasted into the window in
/// front, or a chain of launcher lines run one after another. `{input}` takes what is typed after the keyword.
internal sealed class UserCommand
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "script"; // script | snippet | chain
    public string Name { get; set; } = "";
    public string? Keyword { get; set; }
    public string Body { get; set; } = ""; // script: a file or a command line · snippet: the text · chain: steps split by ';'
    public bool ShowWindow { get; set; } // script: in a console window (else hidden; the result comes as a notice)

    [JsonIgnore] public MatchTarget Match = null!;

    public bool TakesInput => Body.Contains("{input}", StringComparison.OrdinalIgnoreCase);
}

/// 내 명령 (usercommands.json): made in the launcher (내 명령 view, or 내 명령으로 등록 on a script file), found by
/// name or keyword like a quicklink ("bk D:\사진" runs 백업 with D:\사진 for {input}).
internal sealed partial class UserCommands
{
    public List<UserCommand> Items { get; set; } = new();

    [JsonIgnore] private static Saver? saver;
    [JsonIgnore] public static UserCommands Current { get; private set; } = new();

    public static event Action? Changed;

    public static void Load()
    {
        saver?.Dispose();
        Current = Json.Load<UserCommands>(Paths.In("usercommands.json"));
        Index();
        saver = Saver.ForJson(Paths.In("usercommands.json"), () => Current);
    }

    public static UserCommand New(string kind, string name, string body, string? keyword) => new() { Id = Guid.NewGuid().ToString("N")[..12], Kind = kind, Name = name, Body = body, Keyword = keyword };

    private static void Index()
    {
        foreach (var c in Current.Items)
        {
            var aliases = new List<string> { c.Kind switch { "snippet" => "snippet 스니펫 상용구", "chain" => "chain macro 묶음 매크로", _ => "script 스크립트 명령" } };
            if (c.Keyword is { Length: > 0 } k) aliases.Add(k);
            c.Match = new MatchTarget(c.Name, aliases);
        }
    }

    private static void Touch()
    {
        Index();
        saver?.Touch();
        Changed?.Invoke();
    }

    public static void Upsert(UserCommand c)
    {
        int at = Current.Items.FindIndex(x => x.Id == c.Id);
        if (at >= 0) Current.Items[at] = c;
        else Current.Items.Add(c);
        Touch();
    }

    public static void Remove(UserCommand c)
    {
        if (Current.Items.RemoveAll(x => x.Id == c.Id) > 0) Touch();
    }

    public static void Flush() => saver?.Flush();

    /// "bk D:\사진" → (백업, "D:\사진"); the keyword alone with a space waits for the words.
    public static (UserCommand command, string input)? Typed(string text)
    {
        int sp = text.IndexOf(' ');
        if (sp <= 0) return null;
        string kw = text[..sp];
        var c = Current.Items.FirstOrDefault(x => x.TakesInput && string.Equals(x.Keyword, kw, StringComparison.OrdinalIgnoreCase));
        return c is null ? null : (c, text[(sp + 1)..]);
    }

    /// Is the keyword free (among 내 명령 and the browser shortcuts)?
    public static bool KeywordFree(string keyword, string? self) =>
        !Current.Items.Any(x => x.Id != self && string.Equals(x.Keyword, keyword, StringComparison.OrdinalIgnoreCase))
        && !Quicklinks.Current.Items.Any(x => x.Id != self && string.Equals(x.Keyword, keyword, StringComparison.OrdinalIgnoreCase));

    // ── what a command does ─────────────────────────────────────────────────────────────────────
    /// {input}, {date} (2026-10-08), {time} (14:05), {clipboard}; a snippet's "\n" is a new line.
    public static string Expand(string text, string input, Func<string>? clipboard = null, DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        string Put(string s, string key, Func<string> value) => s.Contains(key, StringComparison.OrdinalIgnoreCase) ? s.Replace(key, value(), StringComparison.OrdinalIgnoreCase) : s;
        text = Put(text, "{input}", () => input.Trim());
        text = Put(text, "{date}", () => t.ToString("yyyy-MM-dd"));
        text = Put(text, "{time}", () => t.ToString("HH:mm"));
        if (clipboard is not null) text = Put(text, "{clipboard}", clipboard);
        return text;
    }

    public static string SnippetText(string body) => body.Replace("\\n", "\n");

    [GeneratedRegex("""["']\{input\}["']""", RegexOptions.IgnoreCase)]
    private static partial Regex QuotedInputRx();

    private static readonly System.Buffers.SearchValues<char> Specials = System.Buffers.SearchValues.Create(" \t;&|(),'\"");

    /// A script line with {input} as one argument: quoted when it has spaces or signs (PowerShell '…', cmd and programs
    /// "…"), unless the line already puts quotes around {input}.
    public static string ScriptLine(string body, string input, Func<string>? clipboard = null, DateTime? now = null)
    {
        string value = input.Trim();
        if (value.Length > 0 && !QuotedInputRx().IsMatch(body) && value.AsSpan().IndexOfAny(Specials) >= 0)
        {
            string ext = Path.GetExtension(SplitFile(Expand(body, "x", null, now).Trim()).file ?? "").ToLowerInvariant();
            value = ext is ".bat" or ".cmd" or ".exe" or ".com" ? "\"" + value.Replace("\"", "") + "\"" : "'" + value.Replace("'", "''") + "'";
        }
        return Expand(body, value, clipboard, now);
    }

    /// A chain's steps: what would be typed in the search line, one after another.
    public static List<string> Steps(string body) =>
        body.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    [GeneratedRegex(@"^(?:wait|기다리기|대기)\s*(\d+(?:\.\d+)?)\s*(?:초|s|sec|seconds?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex WaitRx();

    /// "기다리기 2초" / "wait 2" → 2 s (at most a minute); null for any other step.
    public static TimeSpan? Wait(string step) =>
        WaitRx().Match(step) is { Success: true } m && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s)
            ? TimeSpan.FromSeconds(Math.Min(s, 60)) : null;

    /// The program that runs a script line: a .ps1 through PowerShell, a .bat/.cmd through cmd, a program as it is,
    /// anything else as a PowerShell command. Hidden runs read the output as UTF-8; shown ones stay open at the end.
    public static ProcessStartInfo StartInfo(string line, bool show)
    {
        line = line.Trim();
        var (file, args) = SplitFile(line);
        string ext = file is null ? "" : Path.GetExtension(file).ToLowerInvariant();
        var psi = new ProcessStartInfo
        {
            UseShellExecute = show,
            CreateNoWindow = !show,
            RedirectStandardOutput = !show,
            RedirectStandardError = !show,
            StandardOutputEncoding = show ? null : System.Text.Encoding.UTF8,
            StandardErrorEncoding = show ? null : System.Text.Encoding.UTF8,
            WorkingDirectory = file is not null && Path.GetDirectoryName(file) is { Length: > 0 } dir ? dir : Paths.Home,
        };
        // PowerShell gets its words through ArgumentList (quotes and the backslashes before them escaped the Windows
        // way); cmd reads its own line, so it gets the line as it is
        void PowerShell(string command)
        {
            psi.FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            foreach (string a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass" }) psi.ArgumentList.Add(a);
            if (show) psi.ArgumentList.Add("-NoExit");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add((show ? "" : "[Console]::OutputEncoding=[Text.Encoding]::UTF8; ") + command.Trim());
        }
        switch (ext)
        {
            case ".ps1":
                PowerShell($"& '{file!.Replace("'", "''")}' {args}");
                break;
            case ".bat" or ".cmd":
                psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                psi.Arguments = $"{(show ? "/k" : "/c")} \"{(show ? "" : "chcp 65001>nul & ")}\"{file}\" {args}\"";
                break;
            case ".exe" or ".com":
                psi.FileName = file!;
                psi.Arguments = args;
                break;
            default:
                PowerShell(line);
                break;
        }
        return psi;
    }

    /// The file a script line starts with ("C:\a b\x.ps1" -arg, or a path with no spaces), and the rest.
    public static (string? file, string args) SplitFile(string line)
    {
        if (line.StartsWith('"') && line.IndexOf('"', 1) is > 1 and var close)
        {
            string quoted = line[1..close];
            return File.Exists(quoted) ? (quoted, line[(close + 1)..].Trim()) : (null, line);
        }
        if (File.Exists(line)) return (line, "");
        // an unquoted path with spaces: the shortest start that is a file
        for (int sp = line.IndexOf(' '); sp > 0; sp = line.IndexOf(' ', sp + 1))
            if (File.Exists(line[..sp])) return (line[..sp], line[(sp + 1)..].Trim());
        return (null, line);
    }

    /// The last line worth showing from a run's output.
    public static string? LastLine(string output) =>
        output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) is { } l ? (l.Length > 160 ? l[..160] + "…" : l) : null;
}
