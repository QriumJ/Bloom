namespace Bloom.Ai;

/// One tool an MCP server listed (tools/list): its own name, its description, and its input schema (JSON text).
internal sealed record McpTool(string Name, string Description, string Schema);
