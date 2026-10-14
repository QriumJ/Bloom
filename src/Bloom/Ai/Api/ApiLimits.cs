using System;

namespace Bloom.Ai;

/// How much the API chats take in from outside before they stop: a provider's stream, an MCP server's lines, bodies and
/// tool lists, a command's output. A connection past a bound is closed with a clear error rather than left to grow.
internal static class ApiLimits
{
    /// One line (stdio message, event-stream line), in bytes; one event's data, in characters.
    public const int MaxLine = 4 * 1024 * 1024, MaxEvent = 4 * 1024 * 1024;

    /// One HTTP body read whole; an error body (a message only); everything one event stream may carry.
    public const int MaxBody = 8 * 1024 * 1024, MaxErrorBody = 64 * 1024;
    public const long MaxStreamBytes = 64L * 1024 * 1024;

    /// One answer's text and its reasoning, in characters; one tool call's arguments.
    public const int MaxAnswerText = 1_000_000, MaxReasoning = 1_000_000, MaxCallArguments = 256 * 1024;

    /// Tool calls in one response and in one answer (all its steps).
    public const int MaxCallsPerResponse = 32, MaxCallsPerTurn = 200;

    /// One streamed response, however it keeps the idle timer alive; one answer's whole (user waits included).
    public static readonly TimeSpan MaxResponseTime = TimeSpan.FromMinutes(15), MaxTurnTime = TimeSpan.FromHours(2);

    /// What one MCP server may list: tools, a tool's name, description and schema (characters).
    public const int MaxServerTools = 256, MaxToolName = 128, MaxToolDescription = 8_000, MaxToolSchema = 64 * 1024;

    /// A tools/call result as text (characters).
    public const int MaxToolResult = 200_000;

    /// A stdio server's stderr is read in pieces of this and dropped.
    public const int DrainChunk = 4096;

    /// What may wait to be written to a stdio server that has stopped reading: messages and their bytes. Past either the
    /// connection is closed (a cancelled request never waits on a full queue).
    public const int MaxQueuedMessages = 256;
    public const long MaxQueuedBytes = 32L * 1024 * 1024;
}
