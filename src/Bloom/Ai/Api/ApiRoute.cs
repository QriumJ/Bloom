namespace Bloom.Ai;

/// A family of a provider's models that is not on the provider's usual endpoint: model ids containing `Pattern`
/// (lower case) are spoken to on `Wire`, or not at all when it is null (a model Bloom has no way to use, left out of
/// the pickers).
internal sealed record ApiRoute(string Pattern, ApiWire? Wire);
