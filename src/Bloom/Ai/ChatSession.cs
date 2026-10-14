using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

internal readonly record struct SessionStart(bool Success, string? Error = null);

/// One conversation's link to a model: Claude Code (a long-lived `claude -p` stream), Codex for ChatGPT (one
/// `codex exec` per turn), or an API provider (ApiSession, one HTTP stream per turn). All events are raised on a worker
/// thread.
internal interface IChatSession : IDisposable
{
    string Model { get; }
    string Effort { get; }
    string? SessionId { get; }
    bool Busy { get; }
    bool Alive { get; }
    DateTime LastUsed { get; }

    event Action<string>? SessionStarted;
    event Action<string>? TextDelta;
    event Action<string?>? Status; // "웹 검색 중…", null = clear
    event Action<string>? AssistantText; // a whole message (when no deltas came)
    event Action<bool, string?>? TurnEnded; // ok, error text
    event Action<string>? Exited; // stderr tail
    event Action<ToolAsk>? ToolAsked; // a tool call waits for the user's yes (Claude with Bloom's tools)
    event Action<string>? ToolAskWithdrawn; // the CLI took the question back (request id)

    bool Start(string? resume, out string? error);
    Task<SessionStart> StartAsync(string? resume, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        bool success = Start(resume, out string? error);
        return Task.FromResult(new SessionStart(success, error));
    }
    /// The user's answer to a ToolAsk (Bloom remembers 항상 허용 itself, per action; the CLI is never told to stop asking).
    void Answer(ToolAsk ask, bool allow);
    /// `images`: pictures sent along (Bloom Lens, attached photos) — Claude gets them in the message, Codex through
    /// --image. Other attached files are named in the text; the session may read them and write new ones (Chats.FilesOf).
    bool Send(string text, IReadOnlyList<string> images);
    Task<bool> SendAsync(string text, IReadOnlyList<string> images, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        return Task.FromResult(Send(text, images));
    }
    void Interrupt();
}

/// Models and thinking levels as the launcher names them. A model id is a Claude model ("sonnet",
/// "opus", "haiku"), a ChatGPT one: "chatgpt:<Codex slug>" ("chatgpt" alone, from older threads,
/// is Codex's own default model), or an API one: "api:<provider id>:<the provider's model id>".
internal static class AiModels
{
    public static readonly string[] Claude = { "sonnet", "opus", "haiku" };

    /// Claude: "fast" = thinking off (the launcher's quick default); the rest are Claude Code's --effort levels.
    public static readonly string[] ClaudeEfforts = { "fast", "low", "medium", "high", "xhigh", "max" };

    /// An API model's level that sends none: the model's own default (the only one for a model that has no levels).
    public const string Auto = "auto";

    private const string GptPrefix = "chatgpt:", ApiPrefix = "api:";

    /// Every level name from lowest to highest, to find the nearest one a model takes.
    private static readonly string[] Rank = { Auto, "fast", "none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra" };

    public static bool IsChatGpt(string model) => model == "chatgpt" || model.StartsWith(GptPrefix, StringComparison.Ordinal);

    public static bool IsApi(string model) => model.StartsWith(ApiPrefix, StringComparison.Ordinal);

    /// The side a model is on: "claude", "chatgpt" or "api" (Settings.AiProvider's values).
    public static string Provider(string model) => IsChatGpt(model) ? "chatgpt" : IsApi(model) ? "api" : "claude";

    /// ChatGPT and the APIs have no file tools: attachments go into the message, files come back as ```file: blocks.
    public static bool Inline(string model) => IsChatGpt(model) || IsApi(model);

    public static string Gpt(string slug) => GptPrefix + slug;

    public static string Api(string provider, string id) => ApiPrefix + provider + ":" + id;

    /// An API model id's provider and the provider's own model id (which may hold ':' itself, as OpenRouter's do).
    public static (string provider, string id)? ApiParts(string model)
    {
        if (!IsApi(model)) return null;
        int colon = model.IndexOf(':', ApiPrefix.Length);
        return colon < 0 ? null : (model[ApiPrefix.Length..colon], model[(colon + 1)..]);
    }

    public static ApiProvider? ApiProviderOf(string model) => ApiParts(model) is { } p ? ApiProviders.Find(p.provider) : null;

    /// An API model that can be used now: its provider has a key and listed it.
    public static bool ApiReady(string model) => ApiParts(model) is { } p && ApiKeys.Has(p.provider) && ApiCatalog.Find(p.provider, p.id) is not null;

    /// Every model for the pickers: Claude's, ChatGPT's, then each API provider's with a key, in that order.
    public static List<string> All() => Claude.Concat(GptCatalog.All.Select(m => Gpt(m.Slug))).Concat(ApiCatalog.Ids()).ToList();

    /// The Codex slug of a ChatGPT model id; null = Codex's own default.
    public static string? GptSlug(string model) => model.StartsWith(GptPrefix, StringComparison.Ordinal) ? model[GptPrefix.Length..] : null;

    /// "chatgpt" (older threads) as the model it stands for, so it compares with the menu's ids.
    public static string Normalize(string model) => model == "chatgpt" && GptCatalog.DefaultSlug() is { } s ? Gpt(s) : model;

    /// Haiku has no effort setting, nor an API model that takes no reasoning level.
    public static bool HasEffort(string model) => IsApi(model) ? Efforts(model).Length > 1 : model != "haiku";

    /// The levels a model takes: Claude's six, the ChatGPT model's own list from Codex, or the API model's (its
    /// provider's rule for it, Auto first; ApiClient sends only these).
    public static string[] Efforts(string model) => IsChatGpt(model) ? GptCatalog.Levels(GptSlug(model))
        : IsApi(model) ? ApiParts(model) is { } p && ApiProviders.Find(p.provider) is { } provider ? ApiProviders.EffortsFor(provider, p.id) : new[] { Auto }
        : ClaudeEfforts;

    /// The same level when the model takes it, else the nearest one below it, else its lowest.
    public static string Fit(string model, string effort)
    {
        // A cold catalog cannot decide that a saved level is unsupported. Resolve again before sending.
        if (IsChatGpt(model) && GptCatalog.Find(GptSlug(model) ?? GptCatalog.DefaultSlug()) is null && Array.IndexOf(Rank, effort) >= 0) return effort;
        var levels = Efforts(model);
        if (Array.IndexOf(levels, effort) >= 0) return effort;
        int want = Array.IndexOf(Rank, effort);
        string? best = null;
        int bestRank = -1;
        foreach (string l in levels)
        {
            int r = Array.IndexOf(Rank, l);
            if (r <= want && r > bestRank)
            {
                best = l;
                bestRank = r;
            }
        }
        return best ?? levels[0];
    }

    /// An API side with no model to use: a send names the problem (AiErrors.ApiModelGone) rather than going to Claude.
    public const string ApiUnset = ApiPrefix;

    /// The API model new conversations use, for the settings page and the chats alike: the one set when it can be
    /// used, else the first listed, else the one set (its list not read yet, or its key gone: the send says so), else
    /// ApiUnset.
    public static string ApiDefault()
    {
        string? set = Settings.Current.ApiModel;
        if (set is not null && ApiReady(set)) return set;
        return ApiCatalog.Ids().FirstOrDefault() ?? set ?? ApiUnset;
    }

    /// New conversations: the model of the side chosen in the settings.
    public static string DefaultModel()
    {
        var s = Settings.Current;
        if (s.AiProvider == "api") return ApiDefault();
        if (s.AiProvider != "chatgpt") return Array.IndexOf(Claude, s.AiModel) >= 0 ? s.AiModel : "sonnet";
        string? slug = s.GptModel is { Length: > 0 } g && (GptCatalog.All.Count == 0 || GptCatalog.Find(g) is not null) ? g : GptCatalog.DefaultSlug();
        return slug is null ? "chatgpt" : Gpt(slug);
    }

    /// The settings' level for that side, fitted to the model.
    public static string DefaultEffort(string model) => Fit(model, IsChatGpt(model) ? Settings.Current.GptEffort : IsApi(model) ? Settings.Current.ApiEffort : Settings.Current.AiEffort);

    public static string Name(string m) => IsChatGpt(m) ? GptCatalog.Name(GptSlug(m))
        : ApiParts(m) is { } api ? ApiCatalog.Find(api.provider, api.id)?.Name ?? api.id
        : IsApi(m) ? Tr("모델 없음", "No model")
        : m switch { "opus" => "Opus", "haiku" => "Haiku", _ => "Sonnet" };

    /// "Claude · Sonnet" / "ChatGPT · GPT-6-Astra" / "OpenRouter · Kimi K3"
    public static string Who(string m) => (IsChatGpt(m) ? "ChatGPT" : IsApi(m) ? ApiProviderOf(m)?.Name ?? "API" : "Claude") + " · " + Name(m);

    public static string EffortName(string e) => e switch
    {
        Auto => Tr("기본", "Default"),
        "none" => Tr("끔", "Off"),
        "minimal" => Tr("최소", "Minimal"),
        "low" => Tr("낮음", "Low"),
        "medium" => Tr("중간", "Medium"),
        "high" => Tr("높음", "High"),
        "xhigh" => Tr("더 높음", "Extra high"),
        "max" => Tr("최대", "Max"),
        "ultra" => Tr("울트라", "Ultra"),
        _ => Tr("빠름", "Fast"),
    };

    public static string Next(string[] list, string cur) => list[(Array.IndexOf(list, cur) + 1) % list.Length];
}
