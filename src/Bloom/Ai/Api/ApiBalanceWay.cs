namespace Bloom.Ai;

/// How a provider tells a key's balance (ApiBalances), where it does: OpenRouter's GET /credits (a management key) or
/// else /key (the key's own limit and spend); DeepSeek's GET /user/balance. The others have no such endpoint.
internal enum ApiBalanceWay
{
    None,
    OpenRouter,
    DeepSeek,
}
