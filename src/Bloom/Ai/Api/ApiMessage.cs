using System.Collections.Generic;

namespace Bloom.Ai;

/// One turn of an API conversation as Bloom keeps it: "user" or "assistant", its text, and the pictures sent with it
/// (files, read when a request is built). The wires build each message's content as a list of parts, so tool calls and
/// their results can join as further parts.
internal sealed record ApiMessage(string Role, string Text, IReadOnlyList<string> Images)
{
    public const string User = "user", Assistant = "assistant";
}
