using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bloom.Browser;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// The root search: everything that answers a typed line, best first.
internal sealed partial class Engine
{
    // ── root ────────────────────────────────────────────────────────────────────────────────────
    public List<Section> Root(string text)
    {
        var sections = new List<Section>();
        var q = new Query(text);
        if (q.IsEmpty) return RootEmpty(sections);
        // a feature switched off in 설정 > 기능 answers nothing here (Features)
        bool noteFirst = false;
        var notes = Features.On("notes") ? RootNotes(text, q, out noteFirst) : null;
        if (notes is not null && noteFirst) sections.Add(notes); // "메모 …": keeping it is what Enter does
        if (Features.On("quicklinks") && TypedQuicklink(text) is { } typed) sections.Add(typed);
        if (Features.On("mine") && TypedCommand(text) is { } mine) sections.Add(mine);
        if (Features.On("timers") && TimeParse.Read(text) is { } ask)
        {
            var ts = new Section(Tr("타이머 · 알림", "Timers · reminders"));
            ts.Items.Add(StartTimerItem(ask, false));
            sections.Add(ts);
        }
        if (Features.On("color") && TypedColor(text) is { } color) sections.Add(color);
        if (Features.On("emoji") && RootSymbols(text) is { } symbols) sections.Add(symbols);
        if (Features.On("system") && RootVolume(text) is { } volume) sections.Add(volume);

        if (Features.On("calc") && Calculator.Evaluate(text) is { } calc)
        {
            var s = new Section(Tr("계산기", "Calculator"));
            s.Items.Add(new ResultItem
            {
                Id = "calc",
                Kind = ItemKind.Calc,
                Title = calc.Value,
                CalcExpr = calc.Expr,
                CalcValue = calc.Value,
                CalcDetail = calc.Detail,
                PrimaryLabel = calc.Copy.Length > 0 ? Tr("결과 복사", "Copy result") : "",
                Run = calc.Copy.Length > 0 ? () => Launch.Copy(calc.Copy) : null,
                MoreActions = calc.Copy.Length > 0 ? () => new List<ActionItem>
                {
                    new() { Title = Tr("서식 포함 복사", "Copy as shown"), Glyph = G.Copy, Run = () => Launch.Copy(calc.Value) },
                    new() { Title = Tr("식과 함께 복사", "Copy with the expression"), Glyph = G.Copy, Run = () => Launch.Copy($"{calc.Expr} = {calc.Value}") },
                } : null,
            });
            sections.Add(s);
        }

        var results = new Section(Tr("결과", "Results"));
        if (Web.AsUrl(text) is { } url) results.Items.Add(FromUrl(url, text.Trim(), Tr("브라우저에서 열기", "Open in the browser"), ItemKind.Url, Tr("링크", "Link")));
        var scored = new List<ResultItem>();
        var learned = QueryMemory.Boosts(text); // what was picked for this line before
        int Learned(string id) => learned.TryGetValue(id, out int b) ? b : 0;
        foreach (var a in Apps.Apps)
        {
            int sc = Matcher.Score(q, a.Match);
            if (sc <= 0) continue;
            var item = FromApp(a);
            item.Score = sc + Usage.Boost(a.Id) + Learned(a.Id) + 10;
            scored.Add(item);
        }
        foreach (var e in SystemItems.All)
        {
            int sc = Matcher.Score(q, e.Match);
            if (sc <= 0 || !Features.Shows(e.Id)) continue;
            var item = FromSys(e);
            item.Score = sc + Usage.Boost(e.Id) + Learned(e.Id) + (e.Kind == "cmd" ? 6 : e.Kind == "setting" ? -12 : -6);
            scored.Add(item);
        }
        foreach (var l in Features.On("quicklinks") ? Quicklinks.Current.Items : new())
        {
            int sc = Matcher.Score(q, l.Match);
            if (sc <= 0) continue;
            var item = FromQuicklink(l, null);
            item.Score = sc + Usage.Boost(item.Id) + Learned(item.Id) + 4;
            scored.Add(item);
        }
        foreach (var c in Features.On("mine") ? UserCommands.Current.Items : new())
        {
            int sc = Matcher.Score(q, c.Match);
            if (sc <= 0) continue;
            var item = FromCommand(c, null);
            item.Score = sc + Usage.Boost(item.Id) + Learned(item.Id) + 5;
            scored.Add(item);
        }
        foreach (var item in Features.On("system") ? RootToggles(q) : Enumerable.Empty<ResultItem>())
        {
            item.Score += Learned(item.Id) + 2;
            scored.Add(item);
        }
        if (Matcher.Score(q, NewGroupMatch) is > 0 and var gs)
        {
            var item = NewGroupItem();
            item.Score = gs + 2;
            scored.Add(item);
        }
        scored.Sort((x, y) => y.Score.CompareTo(x.Score));
        foreach (var it in scored.Take(9)) results.Items.Add(it);
        if (results.Items.Count > 0) sections.Add(results);
        if (notes is not null && !sections.Contains(notes)) sections.Add(notes);
        if (Features.On("windows") && RootWindows(q) is { } windows) sections.Add(windows);
        if (RootRecent(q) is { } recent) sections.Add(recent);
        bool strong = sections.Count > 0; // anything answered the line: the fallbacks go last

        var fall = new Section(strong ? Tr("다른 곳에서 찾기", "Search elsewhere") : Tr("찾는 게 없나요?", "Not finding it?")) { Fallback = true };
        if (Features.On("chat"))
        {
            var continued = host.TabThread;
            fall.Items.Add(AiItem(text.Trim(), continued));
            if (continued is not null) fall.Items.Add(AiNewItem(text.Trim()));
        }
        fall.Items.Add(WebItem(text.Trim()));
        fall.Items.Add(FindFilesItem(text.Trim()));
        if (strong) sections.Add(fall);
        else sections.Insert(0, fall);
        return sections;
    }

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));

    private static IEnumerable<T> Score<T>(IEnumerable<T> items, Func<T, MatchTarget> target, Query q, Func<T, double>? boost = null)
    {
        var list = new List<(T item, double score)>();
        foreach (var it in items)
        {
            int sc = Matcher.Score(q, target(it));
            if (sc >= 560) list.Add((it, sc + (boost?.Invoke(it) ?? 0)));
        }
        list.Sort((a, b) => b.score.CompareTo(a.score));
        return list.Select(x => x.item);
    }

    /// The top files for the root list (Everything), re-ranked by how well the name matches.
    public async Task<Section?> RootFiles(string text)
    {
        if (!Settings.Current.FilesInRoot || text.Trim().Length < 2 || Calculator.Evaluate(text) is not null) return null;
        var list = await host.Everything.Query(text.Trim() + RootNoise, 60, EverythingClient.SortDateModifiedDesc);
        if (list is null || list.Count == 0) return null;
        var q = new Query(text);
        var learned = QueryMemory.Boosts(text);
        var ranked = list
            .Select((f, i) => (f, score: Matcher.Score(q, new MatchTarget(Path.GetFileNameWithoutExtension(f.Name))) - i * 2 + (f.IsFolder ? 4 : 0)
                + learned.GetValueOrDefault((f.IsFolder ? "folder:" : "file:") + f.FullPath.ToLowerInvariant())))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(5)
            .ToList();
        if (ranked.Count == 0) return null;
        var s = new Section(Tr("파일", "File"), "Everything");
        foreach (var (f, _) in ranked) s.Items.Add(FromFile(f, false));
        return s;
    }
}
