using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Bloom.Core;

namespace Bloom.Search;

/// One emoji or symbol to paste.
internal sealed class SymbolEntry
{
    public string Char = "";
    public bool Emoji; // false: a text symbol (arrows, maths, units, brackets …)
    public int Rank; // the place in the starter list, 0 = not in it
    public string English = "", Korean = "";
    public string[] KoWords = Array.Empty<string>(), EnWords = Array.Empty<string>(); // split once
    public MatchTarget Match = null!;

    /// "U+1F600" (each code point of a sequence).
    public string Code => string.Join(' ', Char.EnumerateRunes().Where(r => r.Value is not 0xFE0F and not 0x200D).Select(r => "U+" + r.Value.ToString("X4", CultureInfo.InvariantCulture)));

    /// The name shown under it: the Korean words' first ones, or the English name.
    public string Name => En || Korean.Length == 0 ? string.Join(' ', English.Split(' ').Take(5)) : string.Join(' ', Korean.Split(' ').Take(3));
}

/// The emoji and symbols Bloom offers (Assets\symbols.tsv, made by tools\make_symbols.py and embedded in the dll):
/// Korean and English words, matched like everything else. Read on first use.
internal static class Symbols
{
    private static List<SymbolEntry>? all;

    public static List<SymbolEntry> All => all ??= Load();

    private static List<SymbolEntry> Load()
    {
        var list = new List<SymbolEntry>(1700);
        using var stream = typeof(Symbols).Assembly.GetManifestResourceStream("Bloom.Assets.symbols.tsv");
        if (stream is null) return list;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] f = line.Split('\t');
            if (f.Length < 5) continue;
            var e = new SymbolEntry
            {
                Char = f[0],
                Emoji = f[1] == "e",
                Rank = int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : 0,
                English = f[3],
                Korean = f[4],
            };
            // matched on the Korean words first in Korean, the English ones first in English; both always count
            string first = En || e.Korean.Length == 0 ? e.English : e.Korean;
            string second = first == e.English ? e.Korean : e.English;
            e.Match = new MatchTarget(first, second.Length > 0 ? new[] { second } : null);
            e.KoWords = e.Korean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            e.EnWords = e.English.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            list.Add(e);
        }
        return list;
    }

    /// The typed words, best first; every word of a multi-word query has to match ("빨간 하트").
    public static List<SymbolEntry> Find(string text, int max)
    {
        var words = text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return new();
        var queries = words.Select(w => new Query(w)).ToArray();
        var scored = new List<(SymbolEntry e, int score)>();
        foreach (var e in All)
        {
            int total = 0;
            foreach (var q in queries)
            {
                int s = WordScore(q, e);
                if (s <= 0)
                {
                    total = 0;
                    break;
                }
                total += s;
            }
            if (total <= 0) continue;
            if (e.Rank > 0) total += 40; // the common ones before the rare ones with the same word
            total -= 3 * (e.KoWords.Length + e.EnWords.Length); // fewer words: more about this word (❤️ before 😍 for 하트)
            scored.Add((e, total + Usage.Boost("sym:" + e.Char)));
        }
        // ties: the starter list's order (the plain arrows before the arrow emoji)
        return scored.OrderByDescending(x => x.score).ThenBy(x => x.e.Rank > 0 ? x.e.Rank : int.MaxValue).Take(max).Select(x => x.e).ToList();
    }

    /// A word against each of the entry's words (prefix wins), not against the whole line, so "하트" finds every heart.
    /// The earlier the word sits in its name, the more the entry is about it ("arrow": → before 💘 heart with arrow).
    private static int WordScore(Query q, SymbolEntry e)
    {
        int best = Math.Max(WordsScore(q, e.KoWords), WordsScore(q, e.EnWords));
        if (best == 0 && q.Chosung && e.Match.Chosung.StartsWith(q.Compact, StringComparison.Ordinal)) best = 500;
        if (best == 0 && e.Char == q.Raw.Trim()) best = 1000; // the symbol itself pasted in
        return best;
    }

    private static int WordsScore(Query q, string[] words)
    {
        int best = 0;
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i];
            int early = Math.Min(i, 8) * 8;
            if (w == q.Lower) best = Math.Max(best, 1000 - early);
            else if (w.StartsWith(q.Lower, StringComparison.Ordinal)) best = Math.Max(best, 900 - early - Math.Min(w.Length - q.Lower.Length, 20));
            else if (q.Lower.Length >= 2 && w.Contains(q.Lower, StringComparison.Ordinal)) best = Math.Max(best, 600 - early);
        }
        return best;
    }

    public static SymbolEntry? Of(string ch) => All.Find(e => e.Char == ch);
}
