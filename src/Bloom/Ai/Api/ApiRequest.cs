using System.Collections.Generic;

namespace Bloom.Ai;

/// One model call, resolved (ApiClient.Prepare): the provider, its key, the model's own id and wire, the instructions,
/// the conversation, a reasoning level to send (null = none), and the conversation id for a SessionHeader.
internal sealed record ApiRequest(ApiProvider Provider, string Key, string Model, ApiWire Wire, string System,
    IReadOnlyList<ApiMessage> Messages, string? Effort, string? Session);
