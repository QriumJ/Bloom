using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Bloom.Core;

/// Names the user gave apps inside Bloom only (the shortcut itself keeps its name): app id → name.
internal sealed class AppNames
{
    public Dictionary<string, string> Items { get; set; } = new();

    [JsonIgnore] private static readonly object gate = new();
    [JsonIgnore] private static Saver? saver;
    [JsonIgnore] public static AppNames Current { get; private set; } = new();

    public static void Load()
    {
        Current = Json.Load<AppNames>(Paths.In("names.json"));
        saver = new Saver(() =>
        {
            lock (gate) Json.Save(Paths.In("names.json"), Current);
        });
    }

    public static int Count
    {
        get
        {
            lock (gate) return Current.Items.Count;
        }
    }

    public static string? Get(string id)
    {
        lock (gate) return Current.Items.TryGetValue(id, out var n) ? n : null;
    }

    /// null or empty = back to the original name.
    public static void Set(string id, string? name)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(name)) Current.Items.Remove(id);
            else Current.Items[id] = name.Trim();
        }
        saver?.Touch();
    }

    public static void ClearAll()
    {
        lock (gate) Current.Items.Clear();
        saver?.Touch();
    }

    public static void Flush() => saver?.Flush();
}
