namespace Bloom.Ai;

/// What one stream event said: answer text, that the stream is over (`Done`: nothing more to read), the provider's
/// error message, and whether the answer ended whole or cut short (`End`).
internal readonly record struct ApiChunk(string? Text = null, bool Done = false, string? Error = null, ApiEnd End = ApiEnd.None)
{
    public static ApiChunk None => default;

    /// The stream's last event, the answer whole.
    public static readonly ApiChunk Finished = new(Done: true, End: ApiEnd.Complete);
}
