using System.Net.Http;

namespace Bloom.Ai;

/// What differs between the wires: the endpoint, how the key goes along, the request body, and how a stream event reads.
/// ApiClient does the HTTP and the stream around them.
internal interface IApiWire
{
    /// The endpoint, relative to the provider's base address.
    string Path { get; }

    void Authorize(HttpRequestMessage request, ApiProvider provider, string key);

    /// The streaming request for `request` as JSON (reads its pictures).
    string Body(ApiRequest request);

    ApiChunk Read(SseEvent e);

    static IApiWire Of(ApiWire wire) => wire switch
    {
        ApiWire.Messages => MessagesWire.Instance,
        ApiWire.Responses => ResponsesWire.Instance,
        _ => ChatCompletionsWire.Instance,
    };
}
