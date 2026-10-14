using System.Collections.Generic;

namespace Bloom.Ai;

/// One turn of an API conversation as Bloom keeps it: "user", "assistant" (with the tool calls it made, if any) or
/// "tool" (one call's result: `CallId`, its text, `IsError`), its text, and the pictures sent with it (files, read when
/// a request is built). Each wire turns these into its own shape.
internal sealed record ApiMessage(string Role, string Text, IReadOnlyList<string> Images)
{
    public const string User = "user", Assistant = "assistant", Tool = "tool";

    /// An assistant turn's tool calls, in the order the model made them.
    public IReadOnlyList<ApiToolCall> Calls { get; init; } = [];

    /// An assistant turn's reasoning to send back (ApiStep) and the model that wrote it (ApiRequest.Source): it goes back
    /// only to that model.
    public string? Reasoning { get; init; }
    public IReadOnlyList<ApiThought> Thoughts { get; init; } = [];
    public string? From { get; init; }

    /// A tool turn: the call it answers and whether the call failed.
    public string CallId { get; init; } = "";
    public bool IsError { get; init; }

    /// A tool turn's result for `call`.
    public static ApiMessage ResultOf(string callId, string text, bool error) => new(Tool, text, []) { CallId = callId, IsError = error };
}
