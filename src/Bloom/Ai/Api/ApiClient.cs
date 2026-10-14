using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// The HTTP side of the API providers, shared by the chat sessions, OneShot and the 설정 page: one HttpClient for all,
/// every request async (never on the UI thread's time), the key only in the request to its own provider. Errors come
/// back as AiErrors' words; a key never reaches the log.
internal static class ApiClient
{
    /// No byte from the provider for this long ends the call (the same 90 s the CLI calls get).
    public static readonly TimeSpan IdleLimit = TimeSpan.FromSeconds(90);

    private static readonly HttpClient Http = MakeClient();

    private static HttpClient MakeClient()
    {
        var http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5), // DNS changes are followed
            ConnectTimeout = TimeSpan.FromSeconds(20),
            // a key goes to its provider's address only: a redirect is not followed but answered as a refusal (AiErrors)
            AllowAutoRedirect = false,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan, // a stream may last minutes; IdleLimit ends a silent one
        };
        // its own name, as OpenCode Go asks of clients rather than a generic library's
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Bloom", typeof(ApiClient).Assembly.GetName().Version?.ToString(3) ?? "1.0"));
        return http;
    }

    /// The call for an API model id (AiModels.Api), on a worker thread (it reads the key and the cached model list):
    /// its provider, the stored key, the wire the model is on, and the reasoning level when the model takes it (the
    /// same AiModels.Efforts the pickers offer). → (request, null) or (null, error).
    public static async Task<(ApiRequest? request, string? error)> PrepareAsync(string model, string? effort, string system, IReadOnlyList<ApiMessage> messages, string? session)
    {
        if (AiModels.ApiParts(model) is not { } parts || ApiProviders.Find(parts.provider) is not { } provider) return (null, AiErrors.ApiModelGone);
        if (ApiKeys.Get(provider.Id) is not { Length: > 0 } key) return (null, AiErrors.ApiNoKey(provider.Name));
        await ApiCatalog.Loaded().ConfigureAwait(false);
        if ((ApiCatalog.Find(provider.Id, parts.id)?.Wire ?? ApiProviders.WireFor(provider, parts.id)) is not { } wire) return (null, AiErrors.ApiModelGone);
        string? level = effort is null || effort == AiModels.Auto || Array.IndexOf(AiModels.Efforts(model), effort) < 0 ? null : effort;
        return (new ApiRequest(provider, key, parts.id, wire, system, messages, level, session), null);
    }

    /// One streamed answer: each piece of text goes to `delta` as it comes (on a worker thread). Cancelling `stop` ends it
    /// with AiErrors.Stopped and the text so far; a stream that ends without saying the answer is whole (the connection
    /// dropped, the length ceiling) ends with AiErrors.ApiCutOff and the text so far.
    public static async Task<ApiReply> StreamAsync(ApiRequest request, Action<string> delta, CancellationToken stop)
    {
        var wire = IApiWire.Of(request.Wire);
        string name = request.Provider.Name;
        var text = new StringBuilder();
        bool complete = false, cut = false;
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(stop);
        idle.CancelAfter(IdleLimit);
        try
        {
            string body = wire.Body(request);
            using var message = new HttpRequestMessage(HttpMethod.Post, request.Provider.Url(wire.Path))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            wire.Authorize(message, request.Provider, request.Key);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (request.Session is { } session && request.Provider.SessionHeader is { } header) message.Headers.TryAddWithoutValidation(header, session);
            using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, idle.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync(idle.Token).ConfigureAwait(false);
                return new ApiReply(false, "", Status(request.Provider, (int)response.StatusCode, error, request.Key));
            }
            using var stream = await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var sse = new SseParser();
            while (true)
            {
                idle.CancelAfter(IdleLimit);
                string? line = await reader.ReadLineAsync(idle.Token).ConfigureAwait(false);
                if ((line is null ? sse.End() : sse.Line(line)) is { } e)
                {
                    var chunk = wire.Read(e);
                    if (chunk.Text is { Length: > 0 } piece)
                    {
                        text.Append(piece);
                        delta(piece);
                    }
                    if (chunk.Error is { } failure)
                    {
                        Log.Write($"api {request.Provider.Id}: stream error");
                        return new ApiReply(false, text.ToString(), AiErrors.ApiFailed(name, Redact(failure, request.Key)));
                    }
                    complete |= chunk.End == ApiEnd.Complete;
                    cut |= chunk.End == ApiEnd.Cut;
                    if (chunk.Done) break;
                }
                if (line is null) break;
            }
            if (cut || !complete)
            {
                Log.Write($"api {request.Provider.Id}: answer cut off ({(cut ? "the provider said so" : "the stream ended early")})");
                return new ApiReply(false, text.ToString(), AiErrors.ApiCutOff(name));
            }
            return text.Length > 0 ? new ApiReply(true, text.ToString(), null) : new ApiReply(false, "", AiErrors.ApiNoAnswer(name));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return new ApiReply(false, text.ToString(), AiErrors.Stopped);
        }
        catch (OperationCanceledException)
        {
            return new ApiReply(false, text.ToString(), AiErrors.TimedOut);
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            Log.Write($"api {request.Provider.Id}: {e.GetType().Name}");
            return new ApiReply(false, text.ToString(), AiErrors.ApiNetwork(name));
        }
    }

    /// One question, one answer (OneShot: translation, AI 빠른 동작). → (answer, null), (null, error), or (null, null)
    /// when cancelled.
    public static async Task<(string? text, string? error)> AskAsync(string model, string? effort, string system, string text, CancellationToken ct)
    {
        var (request, error) = await Task.Run(() => PrepareAsync(model, effort, system, new[] { new ApiMessage(ApiMessage.User, text, Array.Empty<string>()) }, null)).ConfigureAwait(false);
        if (request is null) return (null, error);
        var reply = await StreamAsync(request, _ => { }, ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested) return (null, null);
        return reply.Ok ? (reply.Text.Trim(), null) : (null, reply.Error);
    }

    /// The provider's models for `key` (also the key's test: a wrong one is refused here). → (models, null) or (null, error).
    public static async Task<(List<ApiModelInfo>? models, string? error)> ListModelsAsync(ApiProvider provider, string key, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(IdleLimit);
        try
        {
            // Anthropic pages its list (20 by default); one page of its maximum holds them all
            string path = provider.Wire == ApiWire.Messages ? "models?limit=1000" : "models";
            using var message = new HttpRequestMessage(HttpMethod.Get, provider.Url(path));
            IApiWire.Of(provider.Wire).Authorize(message, provider, key);
            using var response = await Http.SendAsync(message, limit.Token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return (null, Status(provider, (int)response.StatusCode, body, key));
            return (ApiCatalog.Parse(provider, body), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, AiErrors.TimedOut);
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            Log.Write($"api {provider.Id} models: {e.GetType().Name}");
            return (null, AiErrors.ApiNetwork(provider.Name));
        }
        catch (System.Text.Json.JsonException)
        {
            Log.Write($"api {provider.Id} models: not a model list");
            return (null, AiErrors.ApiFailed(provider.Name, ""));
        }
    }

    /// A refused request in AiErrors' words; the log gets the provider and the status only (a body may echo the key).
    private static string Status(ApiProvider provider, int status, string body, string key)
    {
        Log.Write($"api {provider.Id}: HTTP {status}");
        return AiErrors.ApiStatus(provider.Name, status, Redact(ApiJson.ErrorMessage(body), key));
    }

    /// The key taken out of a provider's message before it is shown.
    internal static string Redact(string text, string key) => key.Length == 0 ? text : text.Replace(key, "***", StringComparison.Ordinal);
}
