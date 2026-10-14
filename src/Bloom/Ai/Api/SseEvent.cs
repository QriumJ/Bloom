namespace Bloom.Ai;

/// One server-sent event: its `event:` name (null when it has none) and its `data:` lines joined.
internal sealed record SseEvent(string? Name, string Data);
