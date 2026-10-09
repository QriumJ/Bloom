using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Bloom.Core;

/// The interface language, fixed for one run (설정 > 일반 > 언어; changing it restarts Bloom): Korean, English,
/// Japanese or Simplified Chinese. Every text shown to people is written as `Tr("한국어", "English")`, both next to
/// each other, so a new string can never miss its other half. Japanese and Chinese come from tables keyed by the
/// English text (Assets\Lang\ja.tsv, zh.tsv; tools\lang.py lists what they miss); a text they lack shows in English.
/// Search keywords keep Korean and English whatever the choice.
internal static class Lang
{
    /// ko | en | ja | zh
    public static string Code { get; private set; } = "ko";

    /// Not Korean: English, or a language translated from it (dates, names and keywords follow English there).
    public static bool En => Code != "ko";

    /// Japanese or Chinese: texts come from the table.
    private static bool Translated => Code is "ja" or "zh";

    public static readonly (string value, string label)[] Choices =
        { ("auto", "자동 · Auto"), ("ko", "한국어"), ("en", "English"), ("ja", "日本語"), ("zh", "简体中文") };

    private static bool fixedForRun;
    private static Dictionary<string, string>? table;
    private static readonly HashSet<string> missed = new(StringComparer.Ordinal);

    /// The language for this run, once: later calls (settings read again after an import) leave it, so one run
    /// never mixes two — a change waits for the restart.
    public static void Init(string? setting)
    {
        if (fixedForRun) return;
        Use(Wanted(setting));
        fixedForRun = true;
    }

    /// What a setting means: "auto" follows Windows' display language — Korean, Japanese and Chinese as themselves,
    /// English otherwise.
    public static string Wanted(string? setting) => setting switch
    {
        "ko" or "en" or "ja" or "zh" => setting,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch { "ko" => "ko", "ja" => "ja", "zh" => "zh", _ => "en" },
    };

    /// The saved choice differs from the language on screen (a restart would switch it).
    public static bool Pending(string? setting) => Wanted(setting) != Code;

    /// Only for tests and the off-screen captures (several languages in one run).
    public static void Force(bool en) => Use(en ? "en" : "ko");

    public static void Force(string code, Dictionary<string, string>? rows = null)
    {
        Use(code);
        if (rows is not null) table = rows;
    }

    private static void Use(string code)
    {
        Code = code;
        table = Translated ? Load(code) : null;
    }

    public static string Tr(string ko, string en) => Code switch
    {
        "ko" => ko,
        "en" => en,
        _ => Find(en) ?? Missed(en),
    };

    /// An English text with holes: the table is keyed by its pattern ("{0} files"), the holes filled in after.
    public static string Tr(string ko, EnText en) => Code == "ko" ? ko : en.Text();

    public static string Tr(KoText ko, EnText en) => Code == "ko" ? ko.Text() : en.Text();

    /// The translation of an English text or pattern, or null (English, Korean, or a text the table lacks).
    public static string? Find(string en) => table is { } t && t.TryGetValue(en, out var s) ? s : null;

    /// The texts the table lacked this run, shown in English (`--qa-set lang` writes them out).
    public static string[] Misses()
    {
        lock (missed) return [.. missed];
    }

    private static string Missed(string en)
    {
        lock (missed) missed.Add(en);
        return en;
    }

    /// The culture dates and numbers are written in for the chosen language.
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Code switch { "ko" => "ko-KR", "ja" => "ja-JP", "zh" => "zh-CN", _ => "en-US" });

    /// A table's rows: `key<TAB>translation`, \n \t \\ escaped (tools\lang.py writes them).
    private static Dictionary<string, string> Load(string code)
    {
        var rows = new Dictionary<string, string>(4096, StringComparer.Ordinal);
        using var stream = typeof(Lang).Assembly.GetManifestResourceStream($"Bloom.Assets.Lang.{code}.tsv");
        if (stream is null) return rows;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            int tab = line.IndexOf('\t', StringComparison.Ordinal);
            if (tab <= 0 || line[0] == '#') continue;
            rows[Unescape(line.AsSpan(0, tab))] = Unescape(line.AsSpan(tab + 1));
        }
        return rows;
    }

    private static string Unescape(ReadOnlySpan<char> s)
    {
        if (s.IndexOf('\\') < 0) return s.ToString();
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 == s.Length)
            {
                sb.Append(s[i]);
                continue;
            }
            char c = s[++i];
            sb.Append(c switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => c });
        }
        return sb.ToString();
    }

    /// The Korean half of an interpolated Tr: built only when Korean shows (its holes aren't even evaluated otherwise).
    [InterpolatedStringHandler]
    public ref struct KoText
    {
        private DefaultInterpolatedStringHandler text;

        public KoText(int literalLength, int formattedCount, out bool shouldAppend)
        {
            shouldAppend = Code == "ko";
            text = shouldAppend ? new DefaultInterpolatedStringHandler(literalLength, formattedCount) : default;
        }

        public void AppendLiteral(string s) => text.AppendLiteral(s);

        public void AppendFormatted<T>(T value) => text.AppendFormatted(value);

        public void AppendFormatted<T>(T value, string? format) => text.AppendFormatted(value, format);

        public void AppendFormatted<T>(T value, int alignment) => text.AppendFormatted(value, alignment);

        public void AppendFormatted<T>(T value, int alignment, string? format) => text.AppendFormatted(value, alignment, format);

        internal string Text() => text.ToStringAndClear();
    }

    /// The English half of an interpolated Tr. In English it is the text; in Japanese and Chinese it keeps the
    /// pattern ("{0} files") and the values, then fills the table's pattern ("{0} 个文件") — or the English one.
    [InterpolatedStringHandler]
    public ref struct EnText
    {
        private DefaultInterpolatedStringHandler text;
        private readonly StringBuilder? pattern;
        private readonly List<object?>? values;

        public EnText(int literalLength, int formattedCount, out bool shouldAppend)
        {
            shouldAppend = Code != "ko";
            text = default;
            pattern = null;
            values = null;
            if (!shouldAppend) return;
            if (Translated)
            {
                pattern = new StringBuilder(literalLength + formattedCount * 4);
                values = new List<object?>(formattedCount);
            }
            else text = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
        }

        public void AppendLiteral(string s)
        {
            if (pattern is null) text.AppendLiteral(s);
            else pattern.Append(s.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal));
        }

        public void AppendFormatted<T>(T value) => AppendFormatted(value, 0, null);

        public void AppendFormatted<T>(T value, string? format) => AppendFormatted(value, 0, format);

        public void AppendFormatted<T>(T value, int alignment) => AppendFormatted(value, alignment, null);

        public void AppendFormatted<T>(T value, int alignment, string? format)
        {
            if (pattern is null || values is null)
            {
                text.AppendFormatted(value, alignment, format);
                return;
            }
            pattern.Append('{').Append(values.Count);
            if (alignment != 0) pattern.Append(',').Append(alignment);
            if (format is not null) pattern.Append(':').Append(format);
            pattern.Append('}');
            values.Add(value);
        }

        internal string Text()
        {
            if (pattern is null || values is null) return text.ToStringAndClear();
            string key = pattern.ToString();
            return string.Format(CultureInfo.CurrentCulture, Find(key) ?? Missed(key), values.ToArray());
        }
    }
}
