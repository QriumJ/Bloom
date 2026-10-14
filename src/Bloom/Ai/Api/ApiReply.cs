using System.Collections.Generic;

namespace Bloom.Ai;

/// How a model call ended: the text that came (whole, or as far as it got), when it failed the error to show, the
/// tool calls a whole answer asked for (none: the answer is final), and the reasoning it gave back to be sent again
/// (ApiStep): DeepSeek's as text, a Responses model's as items; and the tokens the provider counted (null: it said none).
internal sealed record ApiReply(bool Ok, string Text, string? Error)
{
    public ApiTokens? Tokens { get; init; }

    public IReadOnlyList<ApiToolCall> Calls { get; init; } = [];
    public string? Reasoning { get; init; }
    public IReadOnlyList<ApiThought> Thoughts { get; init; } = [];
}
