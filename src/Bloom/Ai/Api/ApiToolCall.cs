namespace Bloom.Ai;

/// A tool call a model made: the provider's id for it (its result names it), the tool, and the arguments as the JSON
/// text the model wrote. `Extra`: what the provider attached to the call for itself (Gemini's `extra_content`, holding
/// the thought signature its Gemini 3 models require back), as raw JSON, sent back with the call unchanged.
internal sealed record ApiToolCall(string Id, string Name, string Arguments)
{
    public string? Extra { get; init; }
}
