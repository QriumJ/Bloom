namespace Bloom.Ai;

/// One tool call of an API answer as the chat keeps it (ApiStep): the call as the model made it (with the provider's
/// Extra, ApiToolCall) and the result it got.
internal sealed record ApiCallRecord(string Id, string Name, string Arguments, string Result, bool Error)
{
    public string? Extra { get; init; }

    public ApiToolCall ToCall() => new(Id, Name, Arguments) { Extra = Extra };

    public static ApiCallRecord Of(ApiToolCall call, string result, bool error) => new(call.Id, call.Name, call.Arguments, result, error) { Extra = call.Extra };
}
