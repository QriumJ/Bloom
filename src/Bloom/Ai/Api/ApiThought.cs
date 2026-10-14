namespace Bloom.Ai;

/// A reasoning item a Responses answer gave (`Item`: its JSON as it came, encrypted_content and all), sent back in the
/// conversation so a reasoning model keeps its thinking across its tool calls (nothing is stored on the provider:
/// store:false). `At`: where it stood among the answer's other items — the text (when there is any) and then each call,
/// in order: it goes back just before the one at that position, as each reasoning item must be followed by its own.
internal sealed record ApiThought(string Item, int At);
