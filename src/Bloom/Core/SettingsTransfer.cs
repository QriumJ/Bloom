using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bloom.Core;

/// Settings to and from one file (a zip): the settings, favorites (with their drawn icons), Bloom-only app names,
/// quick searches and recent colours, plus a small manifest. Usage history, clipboard history, AI chats and running
/// timers stay on this PC. Importing keeps a copy of what it replaces in backups\<time> next to the data.
internal static class SettingsTransfer
{
    /// The files that travel, in the data folder.
    public static readonly string[] Files = { "settings.json", "favorites.json", "names.json", "quicklinks.json", "usercommands.json", "colors.json" };

    /// Settings that belong to this PC, not to the person: they keep their local value on import.
    private static readonly string[] Local = { "claudePath", "codexPath", "onboarded" };

    private const string Manifest = "bloom-settings.json";

    private static InvalidDataException Invalid(string text)
    {
        var error = new InvalidDataException(text);
        error.Data["BloomCopy"] = text;
        return error;
    }

    public static string SuggestedName() => Tr($"Bloom 설정 {DateTime.Now:yyyy-MM-dd}.zip", $"Bloom settings {DateTime.Now:yyyy-MM-dd}.zip");

    /// Writes the current settings (flush them first) into `path`; returns how many files went in.
    public static int Export(string path, string version)
    {
        string tmp = path + ".tmp";
        int n = 0;
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (string f in Files)
            {
                string src = Paths.In(f);
                if (!File.Exists(src)) continue;
                zip.CreateEntryFromFile(src, f, CompressionLevel.Optimal);
                n++;
            }
            var manifest = new JsonObject { ["app"] = "Bloom", ["version"] = version, ["exported"] = DateTimeOffset.Now.ToString("o"), ["files"] = new JsonArray(Files.Select(f => (JsonNode)f).ToArray()) };
            using var w = new StreamWriter(zip.CreateEntry(Manifest).Open());
            w.Write(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        File.Move(tmp, path, true);
        return n;
    }

    /// Checks `path` and puts its files into the data folder (the app reloads them afterwards). Throws with a message
    /// for the user when the file is not a Bloom settings file; returns the names of the files that were replaced.
    public static List<string> Import(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.GetEntry(Manifest) is not { } m) throw Invalid(Tr("Bloom 설정 파일이 아닙니다. Bloom에서 내보낸 파일을 선택하세요.", "Not a Bloom settings file. Choose a file exported from Bloom."));
        using (var r = new StreamReader(m.Open()))
            if (JsonNode.Parse(r.ReadToEnd())?["app"]?.GetValue<string>() != "Bloom") throw Invalid(Tr("Bloom 설정 파일이 아닙니다. Bloom에서 내보낸 파일을 선택하세요.", "Not a Bloom settings file. Choose a file exported from Bloom."));

        // read and check everything before anything on disk changes
        var incoming = new Dictionary<string, string>();
        foreach (string f in Files)
        {
            if (zip.GetEntry(f) is not { } e) continue;
            if (e.Length > 8 * 1024 * 1024) throw Invalid(Tr("설정 파일이 너무 큽니다. 더 작은 Bloom 설정 파일을 선택하세요.", "Settings file too large. Choose a smaller Bloom settings file."));
            using var r = new StreamReader(e.Open());
            string text = r.ReadToEnd();
            if (JsonNode.Parse(text) is not JsonObject) throw Invalid(Tr("설정 파일 형식이 잘못되었습니다. Bloom에서 내보낸 파일을 선택하세요.", "Invalid settings format. Choose a file exported from Bloom."));
            incoming[f] = text;
        }
        if (incoming.Count == 0) throw Invalid(Tr("설정이 없는 파일입니다. Bloom에서 내보낸 파일을 선택하세요.", "No settings in this file. Choose a file exported from Bloom."));
        if (incoming.TryGetValue("settings.json", out string? settings)) incoming["settings.json"] = KeepLocal(settings);

        string backup = Paths.In(Path.Combine("backups", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        Directory.CreateDirectory(backup);
        foreach (string f in incoming.Keys)
            if (File.Exists(Paths.In(f))) File.Copy(Paths.In(f), Path.Combine(backup, f), true);
        foreach (var (f, text) in incoming)
        {
            string dest = Paths.In(f);
            File.WriteAllText(dest + ".tmp", text);
            File.Move(dest + ".tmp", dest, true);
        }
        return incoming.Keys.ToList();
    }

    /// The imported settings with this PC's own values (CLI paths, first-run state) left as they are here.
    private static string KeepLocal(string imported)
    {
        var node = (JsonObject)JsonNode.Parse(imported)!;
        JsonObject? here = File.Exists(Paths.In("settings.json")) ? JsonNode.Parse(File.ReadAllText(Paths.In("settings.json"))) as JsonObject : null;
        foreach (string key in Local)
        {
            string? match = node.Select(kv => kv.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (match is not null) node.Remove(match);
            string? mine = here?.Select(kv => kv.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (mine is not null) node[mine] = here![mine]?.DeepClone();
        }
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
