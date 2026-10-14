using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Bloom.Core;

namespace Bloom.Ai;

/// Bloom's entry in Claude Code's own settings.json (ClaudeWatch.Home): a PermissionRequest hook that starts
/// `Bloom.exe --claude-approve` directly, without a shell (App\ClaudeApprovalHook). Only this Bloom's entry goes in or
/// out; the rest keeps its content. A file laid out as Claude Code writes it (any line ends, indent, BOM) changes only
/// there; one laid out by hand comes back in that layout, its original kept once as settings.json.bloom-backup. A file
/// that isn't strict JSON (comments, trailing commas) is left alone.
internal static class ClaudeHooks
{
    /// How long Claude Code lets the hook — a card waiting for its answer — run; then it asks in the terminal itself.
    public const int TimeoutSeconds = 600;

    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };
    private static readonly Mutex Turn = new(false, @"Local\Bloom.ClaudeSettings"); // Blooms (installed, dev copies) take turns on the file

    public static string SettingsFile => Path.Combine(ClaudeWatch.Home, "settings.json");

    /// What the hook passes to Bloom.exe: a dev copy names its data folder, so each Bloom keeps its own entry.
    private static string[] Args => Paths.IsDev ? new[] { App.ClaudeApprovalHook.Arg, "--data", Paths.Data } : new[] { App.ClaudeApprovalHook.Arg };

    /// On: this Bloom's hook in, at this Bloom's path (an old path replaced); off: out. False when the file couldn't be
    /// read or written (logged): it stays as it was.
    public static bool Set(bool on) => Write(() => on);

    /// The hook as 알림 카드에서 승인 says now (Bloom's start): read in Bloom's turn on the file, so a switch flipped
    /// meanwhile wins.
    public static bool Follow() => Write(() => Settings.Current.NoticeApprove);

    /// Uninstall: the hook would start a removed exe.
    public static void Remove() => Set(false);

    private static bool Write(Func<bool> on)
    {
        bool held;
        try
        {
            held = Turn.WaitOne(5000);
        }
        catch (AbandonedMutexException)
        {
            held = true; // a Bloom ended in its turn: the file was replaced whole or not at all
        }
        if (!held)
        {
            Log.Write("claude hook: settings.json busy");
            return false;
        }
        try
        {
            string file = SettingsFile;
            byte[]? old = File.Exists(file) ? File.ReadAllBytes(file) : null;
            bool bom = old is [0xEF, 0xBB, 0xBF, ..];
            string? text = old is null ? null : Utf8.GetString(old, bom ? 3 : 0, old.Length - (bom ? 3 : 0));
            bool want = on();
            string? next = Edit(text, Args, want ? Environment.ProcessPath : null);
            if (next is null) return true; // already so
            // a file laid out by hand is written in Claude Code's layout: its original first goes aside, once
            string aside = file + ".bloom-backup";
            if (old is not null && !KeepsLayout(text!) && !File.Exists(aside))
            {
                // whole or not at all: a half-written copy aside would stand for the original from then on
                string staging = aside + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(staging, old);
                    File.Move(staging, aside); // never over a copy already aside
                }
                finally
                {
                    if (File.Exists(staging)) File.Delete(staging);
                }
                Log.Write("claude hook: settings.json kept as settings.json.bloom-backup (laid out by hand)");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            AtomicFile.Write(file, s =>
            {
                if (bom) s.Write(Bom);
                s.Write(Utf8.GetBytes(next));
            });
            Log.Write(want ? "claude hook: added" : "claude hook: removed");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error("claude hook", e);
            return false;
        }
        finally
        {
            Turn.ReleaseMutex();
        }
    }

    /// settings.json's text with the entries for `args` taken out and, with `exe`, one put back for it; null when the
    /// text is that already. A file that isn't a JSON object, or whose hooks aren't where Claude Code keeps them, throws.
    internal static string? Edit(string? json, string[] args, string? exe)
    {
        var root = string.IsNullOrWhiteSpace(json) ? new JsonObject()
            : JsonNode.Parse(json) as JsonObject ?? throw new JsonException("settings.json is not an object");
        var hooks = root["hooks"] is { } h ? h as JsonObject ?? throw new JsonException("hooks is not an object") : null;
        var asks = hooks?["PermissionRequest"] is { } p ? p as JsonArray ?? throw new JsonException("hooks.PermissionRequest is not a list") : null;
        int ours = 0;
        bool current = false;
        foreach (var group in asks?.OfType<JsonObject>().ToList() ?? new())
        {
            if (group["hooks"] is not JsonArray list) continue;
            var mine = list.OfType<JsonObject>().Where(x => Ours(x, args)).ToList();
            foreach (var hook in mine)
            {
                ours++;
                current |= exe is not null && Str(hook["command"]) == exe && hook["timeout"] is JsonValue t && t.TryGetValue(out int seconds) && seconds == TimeoutSeconds && Str(group["matcher"]) == "*";
                list.Remove(hook);
            }
            if (mine.Count > 0 && list.Count == 0) asks!.Remove(group); // the group was Bloom's alone
        }
        if (exe is null ? ours == 0 : ours == 1 && current) return null;
        if (exe is not null)
        {
            if (hooks is null) root["hooks"] = hooks = new JsonObject();
            if (asks is null) hooks["PermissionRequest"] = asks = new JsonArray();
            asks.Add(new JsonObject
            {
                ["matcher"] = "*",
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = exe,
                    ["args"] = new JsonArray(args.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
                    ["timeout"] = TimeoutSeconds,
                }),
            });
        }
        else
        {
            if (asks is { Count: 0 }) hooks!.Remove("PermissionRequest");
            if (hooks is { Count: 0 }) root.Remove("hooks");
        }
        return Text(root, json);
    }

    /// The document written in the file's own layout (Claude Code writes \n and 2 spaces).
    private static string Text(JsonNode root, string? json)
    {
        string newline = json?.Contains("\r\n", StringComparison.Ordinal) == true ? "\r\n" : "\n";
        var (indentChar, indentSize) = Indent(json);
        string text = root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NewLine = newline,
            IndentCharacter = indentChar,
            IndentSize = indentSize,
        });
        return json is not null && json.EndsWith('\n') ? text + newline : text;
    }

    /// The file comes back byte for byte when written again: laid out as Claude Code writes it. A file laid out by hand
    /// (one line, arrays on a line, escapes) gets its contents back but not its layout.
    internal static bool KeepsLayout(string json) => JsonNode.Parse(json) is { } root && Text(root, json) == json;

    /// The file's indent: the whitespace that starts its second line.
    private static (char Char, int Size) Indent(string? json)
    {
        int line = json?.IndexOf('\n') ?? -1;
        if (line < 0) return (' ', 2);
        int start = line + 1, end = start;
        while (end < json!.Length && json[end] is ' ' or '\t') end++;
        return end == start ? (' ', 2) : (json[start], Math.Min(end - start, 127));
    }

    /// A hook of this Bloom's: one started with exactly these arguments (the first, --claude-approve, is Bloom's alone).
    private static bool Ours(JsonObject hook, string[] args) => hook["args"] is JsonArray a && a.Select(Str).SequenceEqual(args);

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : null;
}
