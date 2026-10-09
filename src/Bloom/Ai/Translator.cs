using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// Translation for screen OCR: one short CLI call per text, no conversation kept (each call is a
/// fresh, small prompt — what realtime translation repeats). Claude = the model picked for translation
/// (Haiku by default, no thinking); ChatGPT = the Codex model picked for translation. Each side has its
/// own thinking level (lowest by default). Both use the CLIs' own logins, like the chat. Every call
/// counts against the user's plan.
internal static class Translator
{
    public static readonly (string value, string label)[] Targets = { ("auto", Tr("자동", "Auto")), ("ko", Tr("한국어", "Korean")), ("en", Tr("영어", "English")), ("ja", Tr("일본어", "Japanese")), ("zh", Tr("중국어", "Chinese")) };

    /// The model a side translates with: Claude Haiku, or the ChatGPT model set for translation
    /// (default: the catalog's lightest, its name containing "Luna" or "mini").
    public static string Model(string provider)
    {
        if (provider != "chatgpt") return Array.IndexOf(AiModels.Claude, Settings.Current.TranslateClaudeModel) >= 0 ? Settings.Current.TranslateClaudeModel : "haiku";
        var s = Settings.Current;
        if (s.TranslateGptModel is { Length: > 0 } g && (GptCatalog.All.Count == 0 || GptCatalog.Find(g) is not null)) return AiModels.Gpt(g);
        var light = GptCatalog.All.FirstOrDefault(m => m.Name.Contains("Luna", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("mini", StringComparison.OrdinalIgnoreCase));
        string? slug = light?.Slug ?? GptCatalog.DefaultSlug();
        return slug is null ? "chatgpt" : AiModels.Gpt(slug);
    }

    /// The levels the side's translation model takes (Haiku: none).
    public static string[] Levels(string provider)
    {
        string m = Model(provider);
        return AiModels.HasEffort(m) ? AiModels.Efforts(m) : Array.Empty<string>();
    }

    /// The side's level, fitted to its model; null = the model has none (Haiku).
    public static string? Effort(string provider)
    {
        string m = Model(provider);
        if (!AiModels.HasEffort(m)) return null;
        return AiModels.Fit(m, provider == "chatgpt" ? Settings.Current.TranslateGptEffort : Settings.Current.TranslateClaudeEffort);
    }

    public static void SetEffort(string provider, string e)
    {
        if (provider == "chatgpt") Settings.Current.TranslateGptEffort = e;
        else Settings.Current.TranslateClaudeEffort = e;
        Settings.Save();
    }

    /// "Claude · Haiku" / "ChatGPT · GPT-6-Luna · 최소"
    public static string Who(string provider)
    {
        string m = Model(provider);
        string who = (provider == "chatgpt" ? "ChatGPT · " : "Claude · ") + AiModels.Name(m);
        return Effort(provider) is { } e ? who + " · " + AiModels.EffortName(e) : who;
    }

    /// "auto": text in the interface's language goes to another one (Korean ↔ English; Japanese and Chinese → English),
    /// anything else to the interface's language.
    public static string TargetFor(string text, string setting)
    {
        if (setting != "auto") return setting;
        int hangul = 0, latin = 0, kana = 0, han = 0, letters = 0;
        foreach (char c in text)
        {
            if (c is >= '가' and <= '힣') hangul++;
            else if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z') latin++;
            else if (c is >= '\u3040' and <= '\u30FF') kana++;
            else if (c is >= '\u4E00' and <= '\u9FFF') han++;
            if (char.IsLetter(c)) letters++;
        }
        bool Most(int n) => letters > 0 && n * 2 > letters;
        return Code switch
        {
            "en" => Most(latin) ? "ko" : "en",
            "ja" => Most(kana + han) && kana > 0 ? "en" : "ja",
            "zh" => Most(han) && kana == 0 ? "en" : "zh",
            _ => Most(hangul) ? "en" : "ko",
        };
    }

    /// A language code's English name, for the prompts.
    public static string LanguageName(string code) => code switch { "en" => "English", "ja" => "Japanese", "zh" => "Simplified Chinese", _ => "Korean" };

    public static string TargetName(string t) => t switch { "en" => Tr("영어", "English"), "ja" => Tr("일본어", "Japanese"), "zh" => Tr("중국어", "Chinese"), _ => Tr("한국어", "Korean") };

    private static string Prompt(string target) =>
        $"You translate text that was read from the user's screen by OCR. Translate it into {LanguageName(target)}. " +
        "Fix obvious OCR mistakes (broken words, wrong letters) silently. Keep the line structure when it carries meaning (lists, dialogue, UI labels); join lines that are one sentence. " +
        "Output only the translation — no notes, quotes, or explanations. If the text is already in the target language, output it cleaned up.";

    /// → (translation, null) or (null, error).
    public static Task<(string? text, string? error)> Run(string text, string provider, string target, CancellationToken ct)
    {
        text = text.Trim();
        if (text.Length > 6000) text = text[..6000];
        return OneShot.Ask(Prompt(target), text, provider, Model(provider), Effort(provider), "translate-" + target, ct);
    }
}
