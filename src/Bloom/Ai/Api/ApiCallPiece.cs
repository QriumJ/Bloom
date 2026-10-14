namespace Bloom.Ai;

/// A piece of a tool call as a stream brings it: which call (`Index`, the wire's position for it), its id and name when
/// this event names them, argument text to add (or, `Whole`, the arguments complete, replacing what came so far), and
/// the provider's Extra for the call (ApiToolCall) when this piece carries it.
internal readonly record struct ApiCallPiece(int Index, string? Id = null, string? Name = null, string? Arguments = null, bool Whole = false, string? Extra = null);
