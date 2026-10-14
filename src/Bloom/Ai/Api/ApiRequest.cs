using System.Collections.Generic;

namespace Bloom.Ai;

/// One model call, resolved (ApiClient.PrepareAsync): the provider, its key, the model's own id and wire, the
/// instructions, the conversation, a reasoning level to send (null = none), and the conversation id for a SessionHeader;
/// the tools offered (none: a plain conversation), and whether the model takes tools at all.
internal sealed record ApiRequest(ApiProvider Provider, string Key, string Model, ApiWire Wire, string System,
    IReadOnlyList<ApiMessage> Messages, string? Effort, string? Session)
{
    public IReadOnlyList<ApiTool> Tools { get; init; } = [];
    public bool TakesTools { get; init; } = true;

    /// The model answering, as the reasoning it gives back is marked (ApiMessage.From): "<provider>:<model>".
    public string Source => Provider.Id + ":" + Model;
}
