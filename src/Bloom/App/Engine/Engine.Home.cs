using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// The home: the blocks the user turned on, in their order.
internal sealed partial class Engine
{
    /// The home: the blocks the user turned on, in their order (설정 > 홈 화면).
    private List<Section> RootEmpty(List<Section> sections)
    {
        var shown = new HashSet<string>();
        foreach (var e in Favorites.All()) shown.Add(e.Id); // favorites (and what sits in folders) are not "recent"
        foreach (var w in Home.Order())
        {
            if (!w.On || !Features.Shows(w.Key)) continue; // a block of a feature switched off in 설정 > 기능
            int before = sections.Count;
            switch (w.Key)
            {
                case "usage" when AiUsage.Available:
                    AiUsage.FirstLook();
                    var accounts = AiUsage.Accounts;
                    string? asked = AiUsage.Busy ? Tr("불러오는 중…", "Loading…") : AiUsage.Fetched is { } fetched ? Ago(fetched) : null;
                    sections.Add(new Section(Tr("AI 사용량", "AI usage"), asked) { Widget = () => Ui.HomeViews.Usage(accounts), Reload = () => _ = AiUsage.Refresh() });
                    break;
                case "timers":
                    var timers = new Section(Tr("타이머 · 알림", "Timers · reminders"), Tr("Ctrl+K 5분 더 · 취소", "Ctrl+K 5 more min · cancel"));
                    foreach (var e in Timers.Waiting.Take(3)) timers.Items.Add(FromTimer(e));
                    if (timers.Items.Count > 0) sections.Add(timers);
                    break;
                case "favorites":
                    // a row of icons (Ctrl+1…9, ←→ inside it)
                    var favs = new Section(Tr("즐겨찾기", "Favorites")) { Strip = true };
                    foreach (var e in Favorites.Current.Items)
                        if (FromFavorite(e) is { } item) favs.Items.Add(item);
                    if (favs.Items.Count > 0) sections.Add(favs);
                    break;
                case "week":
                    var marks = CalendarMarks("");
                    int count = Enumerable.Range(0, 7).Sum(i => marks.TryGetValue(DateTime.Today.AddDays(i), out var l) ? l.Count : 0);
                    sections.Add(new Section(Tr("이번 주", "This week"), count > 0 ? count == 1 ? Tr("알림 1개", "1 reminder") : Tr($"알림 {count}개", $"{count} reminders") : Tr("알림 없음", "No reminders")) { Widget = () => Ui.HomeViews.Week(marks, d => host.Window.OpenCalendar(d)) });
                    break;
                case "recent":
                    var recent = new Section(Tr("최근 사용", "Recent"));
                    foreach (var (id, _) in Usage.Current.Items.OrderByDescending(kv => kv.Value.Last))
                    {
                        if (shown.Contains(id)) continue;
                        if (FromId(id) is not { } item) continue;
                        recent.Items.Add(item);
                        shown.Add(id);
                        if (recent.Items.Count >= 5) break;
                    }
                    if (recent.Items.Count > 0) sections.Add(recent);
                    break;
                case "recentfiles":
                    var files = new Section(Tr("최근 파일", "Recent files"));
                    foreach (var f in RecentFiles.List().Where(f => !shown.Contains((f.Folder ? "folder:" : "file:") + f.Path.ToLowerInvariant())).Take(3)) files.Items.Add(FromRecent(f));
                    if (files.Items.Count > 0) sections.Add(files);
                    break;
                case "clipboard":
                    var clips = new Section(Tr("최근 복사", "Recently copied"), Tr("Ctrl+Alt+V 전체", "Ctrl+Alt+V all"));
                    foreach (var c in host.Clipboard.Items.OrderByDescending(c => c.Copied).Take(3)) clips.Items.Add(FromClip(c));
                    if (clips.Items.Count > 0) sections.Add(clips);
                    break;
                case "threads":
                    var threads = new Section(Tr("최근 AI 대화", "Recent AI chats"));
                    foreach (var t in Threads("").SelectMany(x => x.Items).Where(i => i.Id != "thread:new").Take(3)) threads.Items.Add(t);
                    if (threads.Items.Count > 0) sections.Add(threads);
                    break;
                case "commands":
                    var cmds = new Section(Tr("명령", "Command"));
                    foreach (var e in SystemItems.Commands)
                    {
                        if (shown.Contains(e.Id) || !Features.Shows(e.Id) || e.Id is "cmd:reindex" or "cmd:lock" or "cmd:restart" or "cmd:shutdown" or "cmd:quit") continue;
                        cmds.Items.Add(FromSys(e));
                    }
                    if (cmds.Items.Count > 0) sections.Add(cmds);
                    break;
            }
            if (sections.Count > before) sections[^1].HomeKey = w.Key;
        }
        if (host.Window.HomeEditing)
        {
            // 홈 편집: the hidden blocks, Enter puts one back (at its place in the order)
            var hidden = new Section(Tr("숨긴 블록", "Hidden blocks"), Tr("Enter로 다시 켜기", "Enter shows it again"));
            foreach (var w in Home.Order().Where(w => !w.On))
            {
                var kind = Home.Kinds.First(k => k.key == w.Key);
                string key = w.Key;
                hidden.Items.Add(new ResultItem
                {
                    Id = "home:" + key,
                    Kind = ItemKind.Command,
                    Title = kind.name,
                    Subtitle = kind.about,
                    Accessory = Tr("숨김", "Hidden"),
                    Icon = IconSpec.Of(kind.glyph),
                    PrimaryLabel = Tr("다시 켜기", "Show again"),
                    StayOpen = true,
                    Run = () =>
                    {
                        Home.Set(key, true);
                        host.Window.Refresh(true);
                    },
                });
            }
            if (hidden.Items.Count > 0) sections.Add(hidden);
        }
        return sections;
    }
}
