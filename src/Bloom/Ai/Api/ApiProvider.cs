using System;

namespace Bloom.Ai;

/// One API provider in ApiProviders' table: where it is, the wire its models speak, and what differs between providers.
internal sealed record ApiProvider(string Id, string Name, string BaseUrl, ApiWire Wire, string KeyPage)
{
    /// The reasoning levels its models take, model family by family (the first rule that matches decides); a model no
    /// rule matches takes none (ApiProviders.EffortsFor).
    public ApiEffortRule[] EffortRules { get; init; } = Array.Empty<ApiEffortRule>();

    /// Model families served on another endpoint than `Wire`, or on none Bloom speaks (ApiProviders.WireFor).
    public ApiRoute[] Routes { get; init; } = Array.Empty<ApiRoute>();

    /// Model ids containing one of these (lower case) take no tools: their chats are plain conversations.
    public string[] NoToolModels { get; init; } = Array.Empty<string>();

    /// OpenRouter takes the level as `reasoning: { effort }` rather than `reasoning_effort`.
    public bool NestedEffort { get; init; }

    /// Its thinking models want their `reasoning_content` back on every earlier assistant turn of a request with tools
    /// (DeepSeek: a 400 without it); kept with the answer's steps for that.
    public bool ReplaysReasoning { get; init; }

    /// Its /models entries may name each model's endpoint (OpenCode Go): such a name decides before the Routes.
    public bool MixedEndpoints { get; init; }

    /// Model ids known to work for a new key, best first: the default API model is the first of these the provider lists
    /// (AiModels.ApiDefault), not the first in the list (older models an account can't use come first by name).
    public string[] Preferred { get; init; } = Array.Empty<string>();

    /// A header carrying a stable id per conversation (OpenCode Go routes and caches by it).
    public string? SessionHeader { get; init; }

    /// Its endpoint for the key's balance, shown on the home's AI 사용량 (ApiBalances); None: no card.
    public ApiBalanceWay Balance { get; init; }

    public string Url(string path) => BaseUrl.TrimEnd('/') + "/" + path;
}
