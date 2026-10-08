using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Bloom.Ai;

/// A Claude Code / Codex turn that is working or waiting for approval, as the notices see it: who (Tool), on what
/// (Title: the session's name, else what was asked), what was asked last (Prompt), since when. Headless: run by a
/// program (`claude -p`, `codex exec`) rather than typed in a session — often a few seconds' check by some tool.
internal sealed record AgentTask(string Key, string Tool, string Title, string? Prompt, bool Waiting, DateTime Since, bool Headless = false);

/// What ClaudeWatch and CodexWatch share.
internal static partial class AgentText
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^\s*\[[^\]]{1,48}\]\s*")]
    private static partial Regex TagPrefix();

    /// A session name without a leading "[tag]" ("[◆ Opus • Web ] 로그인 수정" → "로그인 수정").
    public static string? Title(string? name) => string.IsNullOrWhiteSpace(name) ? null : TagPrefix().Replace(name, "") is { Length: > 0 } t ? t.Trim() : name.Trim();

    /// A prompt as one short line: the first line with text, spaces folded, at most `max` characters.
    public static string? Line(string? text, int max = 80)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (string raw in text.Split('\n'))
        {
            string s = Spaces().Replace(raw, " ").Trim();
            if (s.Length == 0) continue;
            return s.Length > max ? s[..(max - 1)].TrimEnd() + "…" : s;
        }
        return null;
    }

    private static readonly Lazy<string> BloomDir = new(() => Folder(OneShot.WorkDir()));

    /// Bloom's own CLI runs (AI chats, translation, the usage looks) work in OneShot.WorkDir: never news.
    public static bool IsBloomRun(string? cwd) => !string.IsNullOrEmpty(cwd) && string.Equals(Folder(cwd), BloomDir.Value, StringComparison.OrdinalIgnoreCase);

    private static string Folder(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd('\\', '/');
        }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException)
        {
            return path;
        }
    }
}
