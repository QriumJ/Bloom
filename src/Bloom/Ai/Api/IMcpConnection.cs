using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Bloom.Ai;

/// How JSON-RPC reaches one MCP server: a process's stdin and stdout (McpStdioConnection) or streamable HTTP
/// (McpHttpConnection). McpClient speaks the protocol over it. Disposing ends the server (or its HTTP session).
internal interface IMcpConnection : IDisposable
{
    /// One request → its `result`. Throws McpException (an error answer, the server gone) or OperationCanceledException.
    Task<JsonElement> RequestAsync(string method, JsonObject? parameters, CancellationToken ct);

    /// A notification (no answer).
    Task NotifyAsync(string method, JsonObject? parameters, CancellationToken ct);

    /// The server dropped this connection's session (an HTTP server's 404): initialize must run again before the next
    /// request. A process has no session to lose.
    bool SessionLost => false;
}
