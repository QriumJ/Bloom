using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Bloom.Core;

namespace Bloom.Ai;

/// The ChatGPT models Codex offers this login, from Codex's own cache (~/.codex/models_cache.json,
/// refreshed by the Codex CLI and app): the ones its model menu lists, in its order, each with the
/// reasoning levels it takes. Read again whenever the file changes.
internal static class GptCatalog
{
    public sealed record Model(string Slug, string Name, string[] Levels, string Description);

    private static readonly string[] FallbackLevels = { "low", "medium", "high", "xhigh" };
    private static readonly object gate = new();
    private static List<Model> list = new();
    private static DateTime stamp = DateTime.MinValue, configStamp = DateTime.MinValue;
    private static string? configModel;

    public static string Home => Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } h ? h : Path.Combine(Paths.Home, ".codex");

    public static IReadOnlyList<Model> All
    {
        get
        {
            lock (gate)
            {
                Load();
                return list;
            }
        }
    }

    private static string Str(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static void Load()
    {
        string file = Path.Combine(Home, "models_cache.json");
        try
        {
            var t = File.GetLastWriteTimeUtc(file); // 1601 when it is missing
            if (t == stamp) return;
            stamp = t;
            var rows = new List<(int prio, Model m)>();
            if (File.Exists(file))
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
                if (doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
                    foreach (var m in models.EnumerateArray())
                    {
                        string slug = Str(m, "slug");
                        if (slug.Length == 0 || Str(m, "visibility") != "list") continue;
                        var levels = new List<string>();
                        if (m.TryGetProperty("supported_reasoning_levels", out var lv) && lv.ValueKind == JsonValueKind.Array)
                            foreach (var l in lv.EnumerateArray())
                            {
                                string e = l.ValueKind == JsonValueKind.String ? l.GetString() ?? "" : Str(l, "effort");
                                if (e.Length > 0) levels.Add(e);
                            }
                        int prio = m.TryGetProperty("priority", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int pi) ? pi : 999;
                        string name = Str(m, "display_name") is { Length: > 0 } dn ? dn : slug;
                        rows.Add((prio, new Model(slug, name, levels.Count > 0 ? levels.ToArray() : FallbackLevels, Str(m, "description"))));
                    }
            }
            list = rows.OrderBy(r => r.prio).Select(r => r.m).ToList();
        }
        catch (Exception e)
        {
            Log.Error("codex models", e);
        }
    }

    public static Model? Find(string? slug) => slug is null ? null : All.FirstOrDefault(m => m.Slug == slug);

    /// Codex's own default: the top-level `model` of config.toml when the menu has it, else the
    /// menu's first model.
    public static string? DefaultSlug()
    {
        string? cfg = ConfigModel();
        var all = All;
        if (cfg is not null && (all.Count == 0 || all.Any(m => m.Slug == cfg))) return cfg;
        return all.Count > 0 ? all[0].Slug : cfg;
    }

    public static string Name(string? slug) => (slug ?? DefaultSlug()) is { } s ? Find(s)?.Name ?? s : "ChatGPT";

    public static string[] Levels(string? slug) => Find(slug ?? DefaultSlug())?.Levels ?? FallbackLevels;

    /// A short Korean line for the menus (Codex's own words for a kind not known here).
    public static string About(Model m)
    {
        string d = m.Description;
        bool Has(string start) => d.StartsWith(start, StringComparison.OrdinalIgnoreCase);
        if (Has("Latest workhorse")) return Tr("코딩과 일반 작업용 모델", "Model for coding and everyday tasks");
        if (Has("Frontier")) return Tr("복잡한 작업용 모델", "Model for complex tasks");
        if (Has("Previous generation workhorse")) return Tr("이전 세대 모델", "Previous generation model");
        if (Has("Fast and affordable")) return Tr("간단한 작업용 모델", "Model for simple tasks");
        if (Has("Older generation workhorse")) return Tr("이전 모델", "Older model");
        if (Has("Older balanced")) return Tr("구세대 균형형 모델", "Older balanced model");
        if (Has("Older fast")) return Tr("구세대 빠른 모델", "Older fast model");
        return d;
    }

    /// The top-level `model` of ~/.codex/config.toml (the launcher runs Codex with --ignore-user-config,
    /// so it passes the model itself).
    public static string? ConfigModel()
    {
        lock (gate)
        {
            string file = Path.Combine(Home, "config.toml");
            try
            {
                var t = File.GetLastWriteTimeUtc(file);
                if (t == configStamp) return configModel;
                configStamp = t;
                configModel = null;
                if (!File.Exists(file)) return null;
                foreach (string line in File.ReadLines(file))
                {
                    string s = line.Trim();
                    if (s.StartsWith('[')) break;
                    if (s.Split('=', 2) is [var k, var v] && k.Trim() == "model")
                    {
                        configModel = v.Trim().Trim('"', '\'');
                        break;
                    }
                }
            }
            catch
            {
                configModel = null; // no config: Codex's own default
            }
            return configModel;
        }
    }
}
