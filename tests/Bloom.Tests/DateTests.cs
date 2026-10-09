using System;
using System.Globalization;
using Bloom.Search;

namespace Bloom.Tests;

/// Day counting, other cities' clocks and typed paths in the search line.
internal static class DateTests
{
    private static readonly DateTime Today = new(2026, 10, 9);

    private static CalcAnswer Date(string text) => Check.NotNull(DateCalc.Evaluate(text, Today), text);

    public static void Offsets()
    {
        Check.Eq(Date("100일 뒤").Copy, "2027-01-17", "100일 뒤");
        Check.Eq(Date("3주 후 무슨 요일").Copy, "2026-10-30", "3주 후");
        Check.Eq(Date("2개월 전").Copy, "2026-08-09", "2개월 전");
        Check.Eq(Date("in 3 weeks").Copy, "2026-10-30", "in 3 weeks");
        Check.Eq(Date("10 days ago").Copy, "2026-09-29", "10 days ago");
    }

    public static void Until()
    {
        Check.Eq(Date("크리스마스까지").Value, "D-77", "크리스마스까지");
        Check.Eq(Date("12월 25일까지 며칠").Value, "D-77", "12월 25일까지 며칠");
        Check.Eq(Date("days until 2026-12-25").Value, "D-77", "days until");
        Check.Eq(Date("d-day 12/25").Value, "D-77", "d-day 12/25");
        Check.Eq(Date("한글날까지").Value, "D-Day", "today is D-Day");
        Check.Eq(Date("3/1까지").Value, "D-143", "a passed date without a year is next year's");
    }

    public static void SinceAndBetween()
    {
        Check.Eq(Date("2026-03-01부터").Copy, "222", "since 3/1");
        Check.Eq(Date("days since 2026-01-01").Copy, "281", "days since");
        Check.Eq(Date("2026-01-01 ~ 2026-12-25").Copy, "358", "between two dates");
    }

    public static void Weekdays()
    {
        Check.Eq(Date("2026-12-25").Value, "금요일", "a date alone: its weekday");
        Check.Eq(Date("10월 9일 무슨 요일").Value, "금요일", "무슨 요일");
    }

    public static void NotDates()
    {
        Check.True(DateCalc.Evaluate("12/25", Today) is null, "a bare slash stays maths");
        Check.True(DateCalc.Evaluate("크리스마스", Today) is null, "a holiday name alone is a search");
        Check.True(DateCalc.Evaluate("100일", Today) is null, "a length with no direction");
        Check.True(DateCalc.Evaluate("chrome", Today) is null, "a word");
    }

    public static void WorldClocks()
    {
        var at = new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Local);
        Check.True(WorldClock.Evaluate("뉴욕 시간", at) is not null, "뉴욕 시간");
        Check.True(WorldClock.Evaluate("tokyo time", at) is not null, "tokyo time");
        Check.True(WorldClock.Evaluate("3pm in london", at) is not null, "3pm in london");
        Check.True(WorldClock.Evaluate("오후 3시 런던", at) is not null, "오후 3시 런던");
        Check.True(WorldClock.Evaluate("도쿄 오후 3시", at) is not null, "도쿄 오후 3시");
        // UTC noon as the time here (whatever zone this PC is in)
        var noonUtc = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        string want = TimeZoneInfo.ConvertTimeFromUtc(noonUtc, TimeZoneInfo.Local).ToString("tt h:mm", CultureInfo.GetCultureInfo("ko-KR"));
        Check.Eq(Check.NotNull(WorldClock.Evaluate("utc 12:00", noonUtc), "utc 12:00").Copy, want, "utc 12:00 here");
        Check.True(WorldClock.Evaluate("뉴욕", at) is null, "a city alone is a search");
        Check.True(WorldClock.Evaluate("뉴욕 3", at) is null, "a bare number is no time");
    }

    public static void Paths()
    {
        Check.True(PathInput.Looks(@"C:\Users"), @"C:\Users");
        Check.True(PathInput.Looks("D:/작업"), "D:/");
        Check.True(PathInput.Looks("C:"), "a drive");
        Check.True(PathInput.Looks(@"~\Downloads"), "~");
        Check.True(PathInput.Looks("%APPDATA%"), "%APPDATA%");
        Check.True(PathInput.Looks(@"\\nas\share"), "UNC");
        Check.True(!PathInput.Looks("a:b"), "not a path");
        Check.True(!PathInput.Looks("10:30"), "a time");
        Check.Eq(PathInput.Expand("C:"), @"C:\", "C: → C:\\");
        Check.Eq(PathInput.Expand("c:/Windows"), @"c:\Windows", "slashes");
        var win = Check.NotNull(PathInput.Read(Environment.GetFolderPath(Environment.SpecialFolder.Windows)), "Windows");
        Check.True(win.Exists && win.IsFolder && win.Children.Count > 0, "the Windows folder and what is in it");
    }
}
