namespace Bloom.Ai;

/// How a model call ended: the text that came (whole, or as far as it got) and, when it failed, the error to show.
internal sealed record ApiReply(bool Ok, string Text, string? Error);
