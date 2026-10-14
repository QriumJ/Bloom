using System.Collections.Generic;
using System.Linq;

namespace Bloom.Ai;

/// One model call of an API answer, kept with the answer (ChatMessage.Steps) so a resumed chat sends the tool calls
/// again as they went: the text it wrote and the calls it made with their results (none: the answer's last call), and
/// the reasoning a provider wants back (DeepSeek's `Reasoning`, a Responses model's `Thoughts`) with the model that wrote
/// it (`From`, ApiRequest.Source).
internal sealed record ApiStep(string Text, List<ApiCallRecord> Calls)
{
    /// A saved result is cut to this (a long file read need not live in the chat's file whole).
    public const int MaxSaved = 16_000;

    public string? Reasoning { get; init; }
    public List<ApiThought>? Thoughts { get; init; }
    public string? From { get; init; }

    /// Something in it a later request needs: a tool call, or reasoning to send back.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Keeps => Calls.Count > 0 || Reasoning is not null || Thoughts is { Count: > 0 };

    /// The step of a model call's reply.
    public static ApiStep Of(ApiReply reply, List<ApiCallRecord> calls, string from) => new(reply.Text, calls)
    {
        Reasoning = reply.Reasoning,
        Thoughts = reply.Thoughts.Count > 0 ? reply.Thoughts.ToList() : null,
        From = reply.Reasoning is not null || reply.Thoughts.Count > 0 ? from : null,
    };

    /// The step as conversation turns: the assistant's text, calls and reasoning, then each call's result.
    public IEnumerable<ApiMessage> Messages()
    {
        if (Text.Length == 0 && Calls.Count == 0) yield break;
        yield return new ApiMessage(ApiMessage.Assistant, Text, [])
        {
            Calls = Calls.Select(c => c.ToCall()).ToList(),
            Reasoning = Reasoning,
            Thoughts = Thoughts ?? [],
            From = From,
        };
        foreach (var c in Calls) yield return ApiMessage.ResultOf(c.Id, c.Result, c.Error);
    }
}
