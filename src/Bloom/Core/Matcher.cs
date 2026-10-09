using System;
using System.Collections.Generic;
using System.Text;

namespace Bloom.Core;

/// What a candidate is matched on, computed once per indexed item.
internal sealed class MatchTarget
{
    public readonly string Lower, Compact, Initials, Chosung, Keys;
    public readonly string[] Words;
    public readonly MatchTarget[]? Aliases;

    /// A thing named in both languages: found by the name it shows first, then by its other names and its keywords.
    public static MatchTarget Named(string ko, string en, string words) =>
        new(Tr(ko, en), Code switch { "ko" => new[] { en, words }, "en" => new[] { ko, words }, _ => new[] { ko, en, words } });

    public MatchTarget(string title, IEnumerable<string>? aliases = null)
    {
        Lower = title.ToLowerInvariant().Trim();
        Compact = Lower.Replace(" ", "");
        Words = SplitWords(title);
        var ini = new StringBuilder();
        foreach (string w in Words)
            if (w.Length > 0) ini.Append(w[0]);
        Initials = ini.ToString();
        Chosung = Hangul.HasHangul(Lower) ? Hangul.Chosung(Lower) : "";
        Keys = Hangul.Keys(Lower);
        if (aliases is not null)
        {
            var list = new List<MatchTarget>();
            foreach (string a in aliases)
                if (!string.IsNullOrWhiteSpace(a)) list.Add(new MatchTarget(a, null));
            if (list.Count > 0) Aliases = list.ToArray();
        }
    }

    /// Words split on spaces, punctuation and camelCase ("VisualStudioCode" → visual studio code).
    private static string[] SplitWords(string s)
    {
        var words = new List<string>();
        var cur = new StringBuilder();
        char prev = '\0';
        foreach (char c in s)
        {
            bool sep = char.IsWhiteSpace(c) || c is '-' or '_' or '.' or '(' or ')' or '[' or ']' or '/' or '\\' or ':' or ',' or '+' or '&';
            bool camel = char.IsUpper(c) && char.IsLower(prev);
            if (sep || camel)
            {
                if (cur.Length > 0) words.Add(cur.ToString().ToLowerInvariant());
                cur.Clear();
                if (sep)
                {
                    prev = c;
                    continue;
                }
            }
            cur.Append(c);
            prev = c;
        }
        if (cur.Length > 0) words.Add(cur.ToString().ToLowerInvariant());
        return words.ToArray();
    }
}

internal sealed class Query
{
    public readonly string Raw, Lower, Compact, Keys;
    public readonly bool Chosung, HasHangul;

    public Query(string raw)
    {
        Raw = raw;
        Lower = raw.Trim().ToLowerInvariant();
        Compact = Lower.Replace(" ", "");
        HasHangul = Hangul.HasHangul(Lower);
        Chosung = Hangul.IsChosungQuery(Lower);
        Keys = Hangul.Keys(Lower);
    }

    public bool IsEmpty => Lower.Length == 0;
}

internal static class Matcher
{
    /// 0 = no match; otherwise roughly 300…1000 (higher is better).
    public static int Score(Query q, MatchTarget t)
    {
        if (q.IsEmpty) return 0;
        int best = Core(q, t);
        if (t.Aliases is not null)
            foreach (var a in t.Aliases)
                best = Math.Max(best, Core(q, a) - 40);
        return best;
    }

    private static int Core(Query q, MatchTarget t)
    {
        string s = t.Lower, qs = q.Lower;
        if (s.Length == 0) return 0;
        if (s == qs || t.Compact == q.Compact) return 1000;
        int coverage = Math.Min(60, qs.Length * 60 / Math.Max(1, s.Length));
        if (s.StartsWith(qs, StringComparison.Ordinal)) return 900 + coverage;
        if (t.Compact.StartsWith(q.Compact, StringComparison.Ordinal)) return 880 + coverage;
        foreach (string w in t.Words)
            if (w.StartsWith(qs, StringComparison.Ordinal))
                return 800 + coverage;
        if (q.Compact.Length >= 2 && t.Initials.StartsWith(q.Compact, StringComparison.Ordinal)) return 770 + coverage / 2;
        if (q.Chosung && t.Chosung.Length > 0)
        {
            if (t.Chosung.StartsWith(q.Compact, StringComparison.Ordinal)) return 760 + coverage / 2;
            if (q.Compact.Length >= 2 && t.Chosung.Contains(q.Compact, StringComparison.Ordinal)) return 620;
        }
        int at = s.IndexOf(qs, StringComparison.Ordinal);
        if (at >= 0) return 660 - Math.Min(at, 60);
        // the same keys in the other input mode, or a syllable still being composed
        if (q.Keys.Length >= 2 && q.Keys != q.Compact || (q.Keys.Length >= 3 && t.Keys != t.Compact))
        {
            if (t.Keys.StartsWith(q.Keys, StringComparison.Ordinal)) return 720 + coverage / 2;
            int k = t.Keys.IndexOf(q.Keys, StringComparison.Ordinal);
            if (k >= 0 && q.Keys.Length >= 3) return 560 - Math.Min(k, 60);
        }
        if (q.Compact.Length >= 2) return Fuzzy(q.Compact, t.Compact);
        return 0;
    }

    /// In-order subsequence with bonuses for runs and word starts; 0 if not all chars appear.
    private static int Fuzzy(string q, string s)
    {
        int si = 0, run = 0, score = 0, gaps = 0, first = -1;
        for (int qi = 0; qi < q.Length; qi++)
        {
            int found = s.IndexOf(q[qi], si);
            if (found < 0) return 0;
            if (first < 0) first = found;
            if (found == si && qi > 0)
            {
                run++;
                score += 12 + run * 4;
            }
            else
            {
                run = 0;
                gaps += found - si;
                if (found == 0 || !char.IsLetterOrDigit(s[found - 1])) score += 10;
            }
            si = found + 1;
        }
        int total = 330 + score - Math.Min(gaps * 3, 120) - Math.Min(first * 2, 40);
        return Math.Clamp(total, 300, 540);
    }
}
