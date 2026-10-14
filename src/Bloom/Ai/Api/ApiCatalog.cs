using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// The models each API provider listed for the user's key, kept in api-models.json (data folder) so the pickers have
/// them at once; asked again when a key is added and from 설정 > AI 계정's refresh. Only providers with a stored key
/// show their models. The recording mode lists none.
///
/// The UI reads a published snapshot (a dictionary replaced whole, never changed in place) under a short lock; the file
/// is read once on a worker thread (Loaded) and written in order on worker threads after each change. Each provider has
/// a generation: a key change, a removal or a newer refresh moves it on, and a refresh that finds it moved publishes
/// nothing (its list may be the old key's).
internal static class ApiCatalog
{
    private const string FileName = "api-models.json";

    private static readonly object Gate = new();
    private static Dictionary<string, ApiModelInfo[]> lists = new(); // the published snapshot
    private static Task? loading;
    private static Task saving = Task.CompletedTask;
    private static readonly HashSet<string> changed = new(); // providers published or removed this run: the file's older lists don't replace them
    private static readonly Dictionary<string, string> failures = new(); // the last refresh's error per provider (this run)
    private static readonly Dictionary<string, CancellationTokenSource> running = new(); // the refresh under way per provider
    private static readonly Dictionary<string, long> generations = new();

    /// Raised (on any thread) when a provider's list or state changed.
    public static event Action? Updated;

    /// The saved lists, read once on a worker thread; the pickers show none until it is done (Updated tells them).
    public static Task Loaded()
    {
        lock (Gate) return loading ??= Task.Run(Load);
    }

    private static void Load()
    {
        var saved = Json.Load<Dictionary<string, List<ApiModelInfo>>>(Paths.In(FileName));
        lock (Gate)
        {
            var next = new Dictionary<string, ApiModelInfo[]>(lists);
            foreach (var (provider, models) in saved)
                if (!changed.Contains(provider)) next[provider] = models.ToArray();
            lists = next;
        }
        Updated?.Invoke();
    }

    /// The writes so far, done (tests wait on it before their data folder goes).
    internal static Task Saving()
    {
        lock (Gate) return saving;
    }

    public static IReadOnlyList<ApiModelInfo> Models(string provider)
    {
        if (Demo.On) return Array.Empty<ApiModelInfo>();
        _ = Loaded();
        lock (Gate) return lists.TryGetValue(provider, out var list) ? list : Array.Empty<ApiModelInfo>();
    }

    public static ApiModelInfo? Find(string provider, string model) => Models(provider).FirstOrDefault(m => m.Id == model);

    /// Every usable API model id (AiModels.Api), provider by provider in the table's order.
    public static List<string> Ids() => ApiProviders.All
        .Where(p => ApiKeys.Has(p.Id))
        .SelectMany(p => Models(p.Id).Select(m => AiModels.Api(p.Id, m.Id)))
        .ToList();

    public static string? Failure(string provider)
    {
        lock (Gate) return failures.GetValueOrDefault(provider);
    }

    public static bool Refreshing(string provider)
    {
        lock (Gate) return running.ContainsKey(provider);
    }

    /// Asks the provider for its models with the stored key and keeps them; a refresh already under way for it is
    /// cancelled (this one reads the key as it is now, so a new key starts a fresh look). → Current false when a key
    /// change, a removal or a newer refresh overtook it (nothing published: the newer one reports); else the error, or
    /// null (the list is kept either way).
    public static async Task<(bool Current, string? Error)> RefreshAsync(ApiProvider provider, CancellationToken ct = default)
    {
        CancellationTokenSource look;
        long generation;
        lock (Gate)
        {
            generation = Advance(provider.Id);
            look = CancellationTokenSource.CreateLinkedTokenSource(ct);
            running[provider.Id] = look;
            failures.Remove(provider.Id);
        }
        Updated?.Invoke();
        (List<ApiModelInfo>? models, string? error) result;
        try
        {
            result = await Task.Run(async () =>
            {
                await Loaded().ConfigureAwait(false); // a list published before the file is read would be overwritten by it
                return ApiKeys.Get(provider.Id) is { } key
                    ? await ApiClient.ListModelsAsync(provider, key, look.Token).ConfigureAwait(false)
                    : (null, AiErrors.ApiNoKey(provider.Name));
            }, look.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = (null, AiErrors.Stopped);
        }
        bool current;
        lock (Gate)
        {
            current = generations[provider.Id] == generation;
            if (current)
            {
                running.Remove(provider.Id);
                if (result.models is { } models) Publish(provider.Id, models.ToArray());
                else if (result.error is { } error) failures[provider.Id] = error;
            }
        }
        look.Dispose();
        if (!current) return (false, null);
        Updated?.Invoke();
        return (true, result.error);
    }

    /// A removed key's list goes with it, and a refresh still under way (with that key) publishes nothing.
    public static void Forget(string provider)
    {
        lock (Gate)
        {
            Advance(provider);
            failures.Remove(provider);
            Publish(provider, null);
        }
        Updated?.Invoke();
    }

    /// Moves the provider's generation on and cancels its refresh under way (under Gate). → the new generation.
    private static long Advance(string provider)
    {
        if (running.Remove(provider, out var older)) older.Cancel();
        long next = generations.GetValueOrDefault(provider) + 1;
        generations[provider] = next;
        return next;
    }

    /// A provider's new list (null = none) as the snapshot, and its write queued after the earlier ones (under Gate).
    /// The file is written only once it has been read, so no list in it is lost.
    private static void Publish(string provider, ApiModelInfo[]? models)
    {
        var next = new Dictionary<string, ApiModelInfo[]>(lists);
        if (models is null) next.Remove(provider);
        else next[provider] = models;
        lists = next;
        changed.Add(provider);
        string file = Paths.In(FileName);
        saving = Task.WhenAll(saving, Loaded()).ContinueWith(_ =>
        {
            Dictionary<string, ApiModelInfo[]> now;
            lock (Gate) now = lists; // the latest snapshot: a later change's write then repeats it, in order
            Json.Save(file, now);
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    // ids that name no chat model (embeddings, speech, pictures, video, moderation, old completions), left out of the
    // pickers; OpenAI's and Gemini's lists carry them all (Gemini's also its Live API and computer-use models, which its
    // chat endpoint doesn't serve)
    private static readonly string[] NotChat = { "embed", "tts", "whisper", "dall-e", "moderation", "transcribe", "realtime", "audio", "image", "imagen", "veo", "sora", "davinci", "babbage", "aqa", "-live", "computer-use" };

    /// A /models answer (`data: [{ id, display_name | name }]`) → the chat models, by name, each with the wire it is on.
    /// Gemini's "models/…" ids lose the prefix its chat endpoint doesn't take; a model whose metadata says it writes no
    /// text, or that is on no endpoint Bloom speaks (ApiProviders.WireFor), is left out.
    internal static List<ApiModelInfo> Parse(ApiProvider provider, string json)
    {
        var list = new List<ApiModelInfo>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return list;
        foreach (var m in data.EnumerateArray())
        {
            string id = ApiJson.Str(m, "id");
            if (id.StartsWith("models/", StringComparison.Ordinal)) id = id["models/".Length..];
            if (id.Length == 0 || NotChat.Any(n => id.Contains(n, StringComparison.OrdinalIgnoreCase)) || !WritesText(m)) continue;
            if (ApiProviders.WireFor(provider, id, m) is not { } wire) continue;
            string name = ApiJson.Str(m, "display_name") is { Length: > 0 } d ? d : ApiJson.Str(m, "name") is { Length: > 0 } n2 ? n2 : id;
            list.Add(new ApiModelInfo(id, name, wire));
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    /// OpenRouter's `architecture.output_modalities`; a list without it is taken as text.
    private static bool WritesText(JsonElement m) =>
        !(m.TryGetProperty("architecture", out var a) && a.ValueKind == JsonValueKind.Object && a.TryGetProperty("output_modalities", out var o) && o.ValueKind == JsonValueKind.Array)
        || o.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == "text");
}
