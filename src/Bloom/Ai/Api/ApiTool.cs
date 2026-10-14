namespace Bloom.Ai;

/// A tool offered to the model: its name, what it does, and its arguments as a JSON Schema (JSON text); each wire
/// wraps these in its own tool definition.
internal sealed record ApiTool(string Name, string Description, string Parameters);
