using System;
using System.Collections.Generic;
using System.Linq;

namespace Bloom.Core;

/// What was picked for what was typed in the root search: after "c" → Chrome a few times, "c" puts Chrome first.
/// Each typed line keeps the items picked for it with a weight; picking one item lowers the others a little,
/// so a new habit wins in a few picks. Lines typed longer than now count for half ("chr" helps "c").
internal sealed class QueryMemory
{
    public sealed class Line
    {
        public Dictionary<string, double> Picks { get; set; } = new();
        public long Last { get; set; }
    }

    public Dictionary<string, Line> Lines { get; set; } = new();

    public const int MaxLines = 1500, MaxKey = 24;

    private readonly object gate = new();
    private static Saver? saver;
    private static string? file;

    public static QueryMemory Current { get; private set; } = new();

    public static void Load()
    {
        saver?.Dispose();
        file = Paths.In("learned.json");
        Current = Json.Load<QueryMemory>(file);
        saver = new Saver(() =>
        {
            lock (Current.gate) Json.Save(file, Current);
        }, 2000);
    }

    public static void Flush() => saver?.Flush();

    /// The form a typed line is kept under: trimmed, lower case, single spaces, at most 24 characters.
    public static string Key(string typed)
    {
        string k = string.Join(' ', typed.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return k.Length > MaxKey ? k[..MaxKey] : k;
    }

    /// Items worth remembering: things that stay (apps, commands, settings, files, links), not answers or one-offs.
    public static bool Learnable(string id) =>
        id.Length > 0 && (id.StartsWith("app:", StringComparison.Ordinal) || id.StartsWith("uwp:", StringComparison.Ordinal)
            || id.StartsWith("cmd:", StringComparison.Ordinal) || id.StartsWith("set:", StringComparison.Ordinal)
            || id.StartsWith("tool:", StringComparison.Ordinal) || id.StartsWith("folder:", StringComparison.Ordinal)
            || id.StartsWith("file:", StringComparison.Ordinal) || id.StartsWith("ql:", StringComparison.Ordinal)
            || id.StartsWith("url:", StringComparison.Ordinal) || id.StartsWith("sys:", StringComparison.Ordinal)
            || id.StartsWith("group:", StringComparison.Ordinal));

    public static void Learn(string typed, string id)
    {
        if (!Settings.Current.LearnQueries || !Learnable(id)) return;
        string key = Key(typed);
        if (key.Length == 0) return;
        Current.Add(key, id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        saver?.Touch();
    }

    public void Add(string key, string id, long now)
    {
        lock (gate)
        {
            if (!Lines.TryGetValue(key, out var line)) Lines[key] = line = new Line();
            foreach (string other in line.Picks.Keys.ToList())
                if (other != id && (line.Picks[other] *= 0.6) < 0.2) line.Picks.Remove(other);
            line.Picks[id] = Math.Min(line.Picks.GetValueOrDefault(id) + 1, 20);
            line.Last = now;
            if (Lines.Count > MaxLines)
                foreach (var old in Lines.OrderBy(kv => kv.Value.Last).Take(Lines.Count - MaxLines).Select(kv => kv.Key).ToList())
                    Lines.Remove(old);
        }
    }

    private static readonly Dictionary<string, int> None = new();

    /// The extra scores while `typed` is in the search line, by item id: 0 … 320 (match scores run 300 … 1000).
    /// Read once per keystroke, not per candidate.
    public static Dictionary<string, int> Boosts(string typed) => Settings.Current.LearnQueries ? Current.BoostsFor(Key(typed)) : None;

    public Dictionary<string, int> BoostsFor(string key)
    {
        if (key.Length == 0) return None;
        var map = new Dictionary<string, int>();
        lock (gate)
        {
            foreach (var (k, line) in Lines)
            {
                bool exact = k == key;
                if (!exact && !(k.Length > key.Length && k.StartsWith(key, StringComparison.Ordinal))) continue;
                foreach (var (id, picks) in line.Picks)
                {
                    int w = (int)Math.Round(exact ? Weight(picks) : Weight(picks) / 2);
                    if (w > map.GetValueOrDefault(id)) map[id] = w;
                }
            }
        }
        return map;
    }

    // one pick ≈ 130, three ≈ 230, eight and more ≈ 320; a pick that faded (another one won since) less
    private static double Weight(double picks) => picks < 1 ? 130 * picks : Math.Min(320, 130 + 100 * Math.Log2(picks));

    public static void Forget(string id)
    {
        lock (Current.gate)
            foreach (var line in Current.Lines.Values) line.Picks.Remove(id);
        saver?.Touch();
    }

    public static void Clear()
    {
        lock (Current.gate) Current.Lines.Clear();
        saver?.Touch();
    }

    public static int Count
    {
        get
        {
            lock (Current.gate) return Current.Lines.Count;
        }
    }
}
