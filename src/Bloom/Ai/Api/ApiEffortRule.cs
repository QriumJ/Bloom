namespace Bloom.Ai;

/// The reasoning levels a family of a provider's models takes: model ids starting with `Prefix` (lower case; "" = every
/// model) take `Levels`, lowest first; none = the model takes no level (AiModels.Auto alone, nothing sent).
internal sealed record ApiEffortRule(string Prefix, params string[] Levels);
