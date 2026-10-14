namespace Bloom.Ai;

/// An API key's money on its usage card instead of limits (ApiBalances): what the amount is ("잔액", "사용"), the
/// amount as the provider's currency writes it, and the card's tooltip saying where it comes from.
internal sealed record UsageBalance(string Label, string Amount, string Tip);
