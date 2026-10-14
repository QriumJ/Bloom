namespace Bloom.Ai;

/// How a model is spoken to over HTTP: OpenAI's Chat Completions, Anthropic's Messages, or OpenAI's Responses (the only
/// way to some OpenCode Go models).
internal enum ApiWire
{
    ChatCompletions,
    Messages,
    Responses,
}
