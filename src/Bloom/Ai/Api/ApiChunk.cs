namespace Bloom.Ai;

/// What one stream event said: answer text, that the stream is over (`Done`: nothing more to read), the provider's
/// error message, whether the answer ended whole or cut short (`End`), pieces of tool calls (`Calls`), a piece of the
/// model's reasoning as text (`Reasoning`: DeepSeek's reasoning_content) and a whole reasoning item (`Thought`: a
/// Responses one, its JSON).
internal readonly record struct ApiChunk(string? Text = null, bool Done = false, string? Error = null, ApiEnd End = ApiEnd.None, ApiCallPiece[]? Calls = null,
    string? Reasoning = null, string? Thought = null)
{
    public static ApiChunk None => default;

    /// The stream's last event, the answer whole.
    public static readonly ApiChunk Finished = new(Done: true, End: ApiEnd.Complete);
}
