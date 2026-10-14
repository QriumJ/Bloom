using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Bloom.Ai;

/// One account's catalog and the default CLI config. Called only by its serialized background loader.
internal sealed class CatalogReader(string? accountHome, string defaultHome, Func<string, byte[]>? read = null)
{
    internal sealed record Snapshot(IReadOnlyList<GptCatalog.Model> Models, string? ConfigModel, bool Loaded)
    {
        public static readonly Snapshot Empty = new(Array.Empty<GptCatalog.Model>(), null, false);
    }

    private readonly Func<string, byte[]> read = read ?? File.ReadAllBytes;
    private string? loadedFile;
    private DateTime modelStamp, configStamp;
    private Snapshot snapshot = Snapshot.Empty;

    public Snapshot Load()
    {
        string file = accountHome is { } home && File.Exists(Path.Combine(home, "models_cache.json"))
            ? Path.Combine(home, "models_cache.json") : Path.Combine(defaultHome, "models_cache.json");
        string config = Path.Combine(defaultHome, "config.toml");
        DateTime modelsAt = File.GetLastWriteTimeUtc(file), configAt = File.GetLastWriteTimeUtc(config);
        if (snapshot.Loaded && file == loadedFile && modelStamp == modelsAt && configStamp == configAt) return snapshot;

        // Publish timestamps only after both reads succeed, so transient/corrupt writes retry.
        var models = file == loadedFile && modelStamp == modelsAt && snapshot.Loaded
            ? snapshot.Models : File.Exists(file) ? ParseModels(read(file)) : Array.Empty<GptCatalog.Model>();
        string? selected = snapshot.Loaded && configStamp == configAt ? snapshot.ConfigModel
            : File.Exists(config) ? ParseConfig(Encoding.UTF8.GetString(read(config))) : null;
        loadedFile = file;
        modelStamp = modelsAt;
        configStamp = configAt;
        return snapshot = new Snapshot(models, selected, true);
    }

    internal static IReadOnlyList<GptCatalog.Model> ParseModels(byte[] json)
    {
        using var doc = JsonDocument.Parse(json);
        var rows = new List<(int Priority, GptCatalog.Model Model)>();
        if (doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
            foreach (var model in models.EnumerateArray())
            {
                string slug = Str(model, "slug");
                if (slug.Length == 0 || Str(model, "visibility") != "list") continue;
                var levels = new List<string>();
                if (model.TryGetProperty("supported_reasoning_levels", out var supported) && supported.ValueKind == JsonValueKind.Array)
                    foreach (var level in supported.EnumerateArray())
                    {
                        string effort = level.ValueKind == JsonValueKind.String ? level.GetString() ?? "" : Str(level, "effort");
                        if (effort.Length > 0) levels.Add(effort);
                    }
                int priority = model.TryGetProperty("priority", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int value) ? value : 999;
                string name = Str(model, "display_name") is { Length: > 0 } display ? display : slug;
                rows.Add((priority, new(slug, name, levels.Count > 0 ? levels.ToArray() : GptCatalog.FallbackLevels,
                    Str(model, "description"))));
            }
        return rows.OrderBy(row => row.Priority).Select(row => row.Model).ToArray();
    }

    internal static string? ParseConfig(string text)
    {
        foreach (string line in text.TrimStart('\uFEFF').Split('\n'))
        {
            string value = line.Trim();
            if (value.StartsWith('[')) break;
            if (value.Split('=', 2) is [var key, var model] && key.Trim() == "model") return model.Trim().Trim('"', '\'');
        }
        return null;
    }

    private static string Str(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
