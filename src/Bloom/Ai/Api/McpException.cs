using System;

namespace Bloom.Ai;

/// An MCP server that went away, could not be started, or answered a request with a JSON-RPC error (its message).
internal sealed class McpException : Exception
{
    public McpException() { }

    public McpException(string message) : base(message) { }

    public McpException(string message, Exception inner) : base(message, inner) { }

    /// The server was refused by Bloom's own rules (an unencrypted address, a header that may not be sent), not because it
    /// failed: the chat says why.
    public bool Refused { get; init; }
}
