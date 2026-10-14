namespace Bloom.Ai;

/// A model a provider listed for the user's key: its id in requests, the name shown, and the wire it is spoken to on.
internal sealed record ApiModelInfo(string Id, string Name, ApiWire Wire);
