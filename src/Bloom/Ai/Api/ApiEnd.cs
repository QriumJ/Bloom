namespace Bloom.Ai;

/// What a stream event says about the answer's end: nothing, the answer is whole, or it was cut short (the length
/// ceiling, Responses' `response.incomplete`). An answer is whole only when a Complete came and no Cut did.
internal enum ApiEnd
{
    None,
    Complete,
    Cut,
}
