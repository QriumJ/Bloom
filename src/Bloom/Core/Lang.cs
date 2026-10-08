using System;
using System.Globalization;

namespace Bloom.Core;

/// The interface language, Korean or English, fixed for one run (설정 > 일반 > 언어; changing it restarts Bloom).
/// Every text shown to people is written as `Tr("한국어", "English")`, both next to each other, so a new string
/// can never miss its other half. Search keywords keep both languages whatever the choice.
internal static class Lang
{
    public static bool En { get; private set; }

    public static string Code => En ? "en" : "ko";

    public static readonly (string value, string label)[] Choices = { ("auto", "자동 · Auto"), ("ko", "한국어"), ("en", "English") };

    private static bool fixedForRun;

    /// The language for this run, once: later calls (settings read again after an import) leave it, so one run
    /// never mixes the two — a change waits for the restart.
    public static void Init(string? setting)
    {
        if (fixedForRun) return;
        En = Wanted(setting);
        fixedForRun = true;
    }

    /// What a setting means: "auto" follows Windows' display language — Korean when it is Korean, English otherwise.
    public static bool Wanted(string? setting) => setting switch
    {
        "en" => true,
        "ko" => false,
        _ => !string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "ko", StringComparison.OrdinalIgnoreCase),
    };

    /// The saved choice differs from the language on screen (a restart would switch it).
    public static bool Pending(string? setting) => Wanted(setting) != En;

    /// Only for tests and the off-screen captures (both languages in one run).
    public static void Force(bool en) => En = en;

    public static string Tr(string ko, string en) => En ? en : ko;

    /// The culture dates and numbers are written in for the chosen language.
    public static CultureInfo Culture => En ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("ko-KR");
}
