using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Tests;

/// 쓸 수 있는 표현 ("?" in the search line): every example listed is read by the real parser of its kind, in the Korean
/// and in the English input, and the placeholder's example follows the features that are on.
internal static class ExpressionTests
{
    public static void EveryExampleIsAcceptedToday()
    {
        using var data = new SettingsTransferTests.DataFolder();
        // the exchange rates and site shortcuts as a fresh install has them, so nothing is fetched or asked of the PC
        Json.Save(Paths.InLocal("rates.json"), new { fetchedUtc = DateTime.UtcNow, rates = new Dictionary<string, double> { ["USD"] = 1, ["KRW"] = 1400 } });
        Rates.Load();
        Quicklinks.Load();
        Quicklinks.Flush();
        var engine = new Engine(new Host(new Options()));
        foreach (bool english in new[] { false, true })
        {
            Lang.Force(english);
            foreach (var kind in Expressions.All())
            {
                Check.True(kind.Examples.Length > 0, $"{kind.Id}: has examples");
                foreach (var example in kind.Examples)
                    Check.True(Accepts(engine, kind.Id, example.Input), $"{(english ? "en" : "ko")} {kind.Id}: «{example.Input}» is not read as {kind.Id}");
            }
        }
    }

    /// Whether the real parser behind the kind gives an answer of that kind for the line (as Engine.Root would).
    private static bool Accepts(Engine engine, string kind, string input)
    {
        var q = new Query(input);
        switch (kind)
        {
            case "calc":
                return Calculator.Evaluate(input) is not null;
            case "timer":
                return TimeParse.Read(input) is not null;
            case "date": // the calculator answers first, so a date line must not be taken by it
                return Calculator.Evaluate(input) is null && (DateCalc.Evaluate(input) ?? WorldClock.Evaluate(input)) is not null;
            case "layout":
                return engine.RootLayouts(q).Any();
            case "path":
                return PathInput.Expand(input) is { } full && full.Contains('\\', StringComparison.Ordinal);
            case "quicklink":
                return engine.TypedQuicklink(input) is not null;
            case "emoji":
                return engine.RootSymbols(input) is not null;
            case "install":
                return engine.RootWinget(input) is not null;
            case "system":
                return engine.RootVolume(input) is not null || engine.RootToggles(q).Any();
            case "note":
                return engine.RootNotes(input, q, out bool first) is not null && first; // "first": it keeps a new note
            case "ai":
                return engine.AiItem(input, null) is { Kind: ItemKind.Ai }; // Tab asks with whatever is typed
            default:
                throw new InvalidOperationException("No check for the kind " + kind);
        }
    }

    public static void InputsFollowTheParsersLanguages()
    {
        foreach (var example in Expressions.All().SelectMany(k => k.Examples))
        {
            Check.True(!Hangul.HasHangul(example.En), $"«{example.En}»: the English input has no Korean");
            Lang.Force(false);
            Check.Eq(example.Input, example.Ko, "Korean UI: the Korean input");
            foreach (string code in new[] { "en", "ja", "zh" })
            {
                Lang.Force(code);
                Check.Eq(example.Input, example.En, code + " UI: the English input, never translated");
            }
        }
    }

    public static void OnlyFeaturesThatAreOn()
    {
        var off = Settings.Current.FeaturesOff;
        try
        {
            off.Add("calc");
            off.Add("timers");
            var kinds = Expressions.On();
            Check.True(kinds.All(k => k.Feature is not ("calc" or "timers")), "kinds of a feature switched off are left out");
            Check.True(kinds.Any(k => k.Id == "path"), "a kind with no feature is always there");
            Check.Eq(kinds.Count, Expressions.All().Count - 2, "nothing else is left out");
            var shown = kinds.SelectMany(k => k.Examples).Where(e => e.Hint).Select(e => e.Ko).ToHashSet();
            for (int opening = 100; opening < 120; opening++) // openings no other check asked for (an opening's example is kept)
                Check.True(Expressions.Hint(opening) is { } hint && shown.Contains(hint), "the placeholder's example is from a feature that is on");
        }
        finally
        {
            off.Clear();
        }
    }

    public static void PlaceholderExampleChangesAtEachOpening()
    {
        var hints = Expressions.All().SelectMany(k => k.Examples).Where(e => e.Hint).ToList();
        Check.True(hints.Count > 1, "more than one short example");
        var seen = new HashSet<string>();
        for (int opening = 0; opening < hints.Count; opening++)
        {
            string hint = Check.NotNull(Expressions.Hint(opening), "an example");
            Check.True(hint.Length <= 20, $"«{hint}» fits the search line after the placeholder");
            Check.True(seen.Add(hint), $"«{hint}» comes again before every example was shown once");
        }
        Check.Eq(Expressions.Hint(0), Expressions.Hint(0), "the same opening, the same example");
    }
}
