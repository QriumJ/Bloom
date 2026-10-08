using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Tests;

/// Matching: initial consonants, keys typed in the wrong input mode, prefix order.
internal static class MatcherTests
{
    private static int Score(string typed, string title) => Matcher.Score(new Query(typed), new MatchTarget(title));

    public static void Chosung()
    {
        Check.True(Score("ㅁㅁㅈ", "메모장") > 0, "ㅁㅁㅈ finds 메모장");
        Check.True(Score("ㅁㅁㅈ", "그림판") == 0, "ㅁㅁㅈ doesn't find 그림판");
    }

    public static void WrongInputMode()
    {
        Check.True(Score("초개ㅡㄷ", "chrome") > 0, "초개ㅡㄷ (chrome typed in Korean mode)");
        Check.True(Score("ㅊ", "chrome") == 0 || Score("초개ㅡㄷ", "chrome") > Score("ㅊ", "chrome"), "more keys, better match");
    }

    public static void PrefixBeatsSubstring()
    {
        Check.True(Score("not", "Notepad") > Score("not", "Bloom Notes"), "a prefix of the whole name first");
        Check.True(Score("code", "Visual Studio Code") > 0, "a word inside the name");
        Check.Eq(Score("notepad", "Notepad"), 1000, "the whole name");
    }

    public static void Aliases()
    {
        var t = new MatchTarget("Settings", new[] { "설정 환경설정" });
        Check.True(Matcher.Score(new Query("설정"), t) > 0, "an alias finds it");
    }
}

/// What was picked for what was typed.
internal static class QueryMemoryTests
{
    private static QueryMemory Fresh() => new();

    public static void Key()
    {
        Check.Eq(QueryMemory.Key("  Chrome   Beta "), "chrome beta", "trimmed, lower case, single spaces");
        Check.Eq(QueryMemory.Key(new string('a', 40)).Length, QueryMemory.MaxKey, "capped length");
    }

    public static void LearnsAndBoosts()
    {
        var m = Fresh();
        m.Add("c", "app:chrome", 1);
        var b = m.BoostsFor("c");
        Check.True(b.TryGetValue("app:chrome", out int one) && one >= 120, "one pick boosts: " + one);
        m.Add("c", "app:chrome", 2);
        m.Add("c", "app:chrome", 3);
        Check.True(m.BoostsFor("c")["app:chrome"] > one, "more picks, more boost");
    }

    public static void NewHabitWins()
    {
        var m = Fresh();
        for (int i = 0; i < 4; i++) m.Add("c", "app:chrome", i);
        for (int i = 0; i < 4; i++) m.Add("c", "app:code", 10 + i);
        var b = m.BoostsFor("c");
        Check.True(b.GetValueOrDefault("app:code") > b.GetValueOrDefault("app:chrome"), "four new picks beat four old ones");
    }

    public static void LongerLinesHelpShorterOnes()
    {
        var m = Fresh();
        m.Add("chr", "app:chrome", 1);
        var b = m.BoostsFor("c");
        Check.True(b.GetValueOrDefault("app:chrome") > 0, "\"chr\" helps \"c\"");
        Check.True(b["app:chrome"] < m.BoostsFor("chr")["app:chrome"], "but less than the exact line");
        Check.True(m.BoostsFor("chrx").Count == 0, "a longer line typed now gets nothing from a shorter one");
    }

    public static void OnlyLastingThings()
    {
        Check.True(QueryMemory.Learnable("app:c:\\x.lnk"), "apps");
        Check.True(QueryMemory.Learnable("sys:dark"), "system toggles");
        Check.True(!QueryMemory.Learnable("calc"), "not the calculator");
        Check.True(!QueryMemory.Learnable("ai:ask"), "not the AI fallback");
        Check.True(!QueryMemory.Learnable("win:1234"), "not a window (it goes away)");
    }

    public static void Pruned()
    {
        var m = Fresh();
        for (int i = 0; i < QueryMemory.MaxLines + 50; i++) m.Add("line" + i, "app:x", i);
        Check.Eq(m.Lines.Count, QueryMemory.MaxLines, "at most MaxLines kept");
        Check.True(!m.Lines.ContainsKey("line0") && m.Lines.ContainsKey("line" + (QueryMemory.MaxLines + 49)), "the oldest go first");
    }
}

/// Emoji and symbols.
internal static class SymbolsTests
{
    public static void Loaded()
    {
        Check.True(Symbols.All.Count > 1500, "the data file is embedded: " + Symbols.All.Count);
        Check.True(Symbols.All.Any(e => !e.Emoji), "symbols too, not only emoji");
    }

    public static void KoreanAndEnglish()
    {
        var hearts = Symbols.Find("하트", 10);
        Check.True(hearts.Count > 3 && hearts[0].Char.StartsWith('❤'), "하트 → ❤️ first, got " + (hearts.Count > 0 ? hearts[0].Char : "-"));
        Check.True(Symbols.Find("heart", 10).Any(e => e.Char.StartsWith('❤')), "heart → ❤️");
        Check.True(Symbols.Find("빨간 하트", 5).First().Char.StartsWith('❤'), "every word must match");
    }

    public static void ArrowsBeforeArrowEmoji()
    {
        var arrows = Symbols.Find("arrow", 5);
        Check.True(!arrows[0].Emoji, "the plain arrow symbols lead, got " + arrows[0].Char);
        Check.True(Symbols.Find("화살표", 5).All(e => e.Korean.Contains("화살", StringComparison.Ordinal)), "화살표");
    }

    public static void Units()
    {
        Check.True(Symbols.Find("섭씨", 3).Any(e => e.Char == "℃"), "섭씨 → ℃");
        Check.True(Symbols.Find("degree", 5).Any(e => e.Char == "°"), "degree → °");
    }
}

/// The interface language.
internal static class LangTests
{
    public static void Switches()
    {
        Check.Eq(Tr("한국어", "English"), "한국어", "Korean");
        Lang.Force(true);
        Check.Eq(Tr("한국어", "English"), "English", "English");
        Check.Eq(Lang.Culture.Name, "en-US", "culture");
    }

    public static void Settings()
    {
        Check.True(Lang.Wanted("en"), "en");
        Check.True(!Lang.Wanted("ko"), "ko");
        Lang.Force(false);
        Check.True(Lang.Pending("en"), "en is pending while Korean shows");
        Check.True(!Lang.Pending("ko"), "ko is not");
    }
}

/// Where translation goes, and the AI actions' prompts.
internal static class AiTextTests
{
    public static void AutoTarget()
    {
        Check.Eq(Translator.TargetFor("안녕하세요 반가워요", "auto"), "en", "Korean → English (Korean UI)");
        Check.Eq(Translator.TargetFor("Hello there", "auto"), "ko", "English → Korean (Korean UI)");
        Check.Eq(Translator.TargetFor("こんにちは", "auto"), "ko", "Japanese → Korean (Korean UI)");
        Lang.Force(true);
        Check.Eq(Translator.TargetFor("안녕하세요", "auto"), "en", "Korean → English (English UI)");
        Check.Eq(Translator.TargetFor("こんにちは", "auto"), "en", "Japanese → English (English UI)");
        Check.Eq(Translator.TargetFor("Hello there", "auto"), "ko", "English → Korean (English UI)");
        Check.Eq(Translator.TargetFor("Hello", "ja"), "ja", "a fixed target stays");
    }

    public static void QuickPrompts()
    {
        Check.Eq(QuickActions.All.Select(a => a.Key).Distinct().Count(), QuickActions.All.Length, "keys are unique");
        string fix = QuickActions.System("fix", null, "teh text");
        Check.True(fix.Contains("spelling", StringComparison.Ordinal) && fix.Contains("ONLY the resulting text", StringComparison.Ordinal), "fix prompt");
        Check.True(QuickActions.System("translate", null, "안녕하세요").Contains("into English", StringComparison.Ordinal), "translate follows the target");
        Check.True(QuickActions.System("custom", "make it rhyme", "x").Contains("make it rhyme", StringComparison.Ordinal), "the user's own words");
        Check.True(QuickActions.System("explain", null, "x").Contains("Answer in Korean", StringComparison.Ordinal), "explain answers in the UI language");
    }
}
