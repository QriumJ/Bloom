using System.IO;

namespace Bloom.Ai;

/// Something an API provider or MCP server sent went past a bound (ApiLimits): the connection is closed.
internal sealed class ResponseTooLargeException : IOException
{
    public ResponseTooLargeException() : base("The response is larger than Bloom takes.") { }
}
