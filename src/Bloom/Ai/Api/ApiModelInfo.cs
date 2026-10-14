namespace Bloom.Ai;

/// A model a provider listed for the user's key: its id in requests, the name shown, and the wire it is spoken to on;
/// `NoTools`: its listing says it takes no tools (OpenRouter's supported_parameters), so its chats are plain ones.
internal sealed record ApiModelInfo(string Id, string Name, ApiWire Wire)
{
    public bool NoTools { get; init; }
}
