using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Search;

namespace Bloom.Core;

/// One block of the empty view (the home): which, and whether it shows.
internal sealed class HomeWidget
{
    public string Key { get; set; } = "";
    public bool On { get; set; } = true;
}

/// The home (the launcher with an empty search line) as blocks the user turns on and orders
/// (설정 > 홈 화면, or Ctrl+K "홈에 고정" on the calendar / clipboard / AI chat commands).
internal static class Home
{
    public static readonly (string key, string name, string about, char glyph, bool on)[] Kinds =
    {
        ("usage", Tr("AI 사용량", "AI usage"), Tr("Claude Code · Codex에 물어본 5시간 · 주간 사용량", "5-hour and weekly usage, asked of Claude Code and Codex"), G.Model, true),
        ("timers", Tr("타이머 · 알림", "Timers · reminders"), Tr("진행 중인 타이머와 알림 (있을 때만 보여요)", "Running timers and reminders (only while there are some)"), G.Stopwatch, true),
        ("week", Tr("이번 주", "This week"), Tr("오늘부터 7일의 알림 — 날짜를 누르면 캘린더", "Reminders for the next 7 days — click a day for the calendar"), G.Calendar, true),
        ("favorites", Tr("즐겨찾기", "Favorites"), Tr("아이콘 줄 · Ctrl+1…9", "A row of icons · Ctrl+1…9"), G.Star, true),
        ("recent", Tr("최근 사용", "Recent"), Tr("최근에 연 앱 · 파일 5개", "The last 5 apps and files you opened"), G.History, true),
        ("recentfiles", Tr("최근 파일", "Recent files"), Tr("Windows가 기억하는 최근에 연 파일 3개", "3 files Windows remembers you opened"), G.Document, false),
        ("clipboard", Tr("최근 복사", "Recently copied"), Tr("클립보드 기록의 최근 3개", "The last 3 clipboard entries"), G.Paste, false),
        ("threads", Tr("최근 AI 대화", "Recent AI chats"), Tr("이어 가기 좋은 대화 3개", "3 chats to pick up again"), G.Chat, false),
        ("commands", Tr("명령", "Commands"), Tr("Bloom의 기능 목록", "Bloom's features"), G.Settings, true),
    };

    /// The commands whose feature can sit on the home (Ctrl+K → 홈에 고정).
    public static string? ForCommand(string id) => id switch
    {
        "cmd:calendar" => "week",
        "cmd:clipboard" => "clipboard",
        "cmd:threads" => "threads",
        _ => null,
    };

    public static string Name(string key) => Kinds.FirstOrDefault(k => k.key == key).name ?? key;

    /// Every known block once, in the user's order (new kinds join at the end with their default).
    public static List<HomeWidget> Order()
    {
        var s = Settings.Current;
        if (s.Home is not { Count: > 0 })
            s.Home = Kinds.Select(k => new HomeWidget { Key = k.key, On = k.on }).ToList();
        var known = Kinds.Select(k => k.key).ToHashSet();
        s.Home = s.Home.Where(w => known.Contains(w.Key)).GroupBy(w => w.Key).Select(g => g.First()).ToList();
        foreach (var k in Kinds)
            if (!s.Home.Exists(w => w.Key == k.key)) s.Home.Add(new HomeWidget { Key = k.key, On = k.on });
        return s.Home;
    }

    public static bool IsOn(string key) => Order().Find(w => w.Key == key)?.On ?? false;

    public static void Set(string key, bool on)
    {
        if (Order().Find(w => w.Key == key) is not { } w) return;
        w.On = on;
        Settings.Save();
    }

    /// On the home itself: past the next block that shows (hidden ones are skipped over).
    public static void MoveShown(string key, int d)
    {
        var list = Order();
        int i = list.FindIndex(w => w.Key == key), j = i + d;
        while (j >= 0 && j < list.Count && !list[j].On) j += d;
        if (i < 0 || j < 0 || j >= list.Count) return;
        var w = list[i];
        list.RemoveAt(i);
        list.Insert(j, w);
        Settings.Save();
    }

    /// Dragged in the settings list: the block goes before what was at `before` (Count = at the end).
    public static void MoveTo(string key, int before)
    {
        var list = Order();
        int i = list.FindIndex(w => w.Key == key);
        if (i < 0) return;
        var w = list[i];
        list.RemoveAt(i);
        if (before > i) before--;
        list.Insert(Math.Clamp(before, 0, list.Count), w);
        Settings.Save();
    }

    /// First / last of the blocks that show (their ▲ / ▼ do nothing).
    public static (string? first, string? last) Ends()
    {
        var on = Order().Where(w => w.On).ToList();
        return (on.FirstOrDefault()?.Key, on.LastOrDefault()?.Key);
    }

    /// One place up (-1) or down (+1).
    public static void Move(string key, int d)
    {
        var list = Order();
        int i = list.FindIndex(w => w.Key == key), j = i + d;
        if (i < 0 || j < 0 || j >= list.Count) return;
        (list[i], list[j]) = (list[j], list[i]);
        Settings.Save();
    }
}
