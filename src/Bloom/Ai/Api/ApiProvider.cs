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

    /// OpenRouter takes the level as `reasoning: { effort }` rather than `reasoning_effort`.
    public bool NestedEffort { get; init; }

    /// Its /models entries may name each model's endpoint (OpenCode Go): such a name decides before the Routes.
    public bool MixedEndpoints { get; init; }

    /// A header carrying a stable id per conversation (OpenCode Go routes and caches by it).
    public string? SessionHeader { get; init; }

    public string Url(string path) => BaseUrl.TrimEnd('/') + "/" + path;
}
