using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
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

    /// The same client for the API chats' other HTTP (an MCP server's web address): no redirect followed there either,
    /// so its headers go to its own address only.
    internal static HttpClient Shared => Http;

    private static readonly HttpClient Local = MakeClient(loopbackOnly: true);

    /// For an MCP server on this PC over plain http: no proxy is used (a proxy would be handed the traffic of a server
    /// that is "here"), and the connection is made only to a loopback address — a name is resolved by Bloom and the
    /// answers that are not loopback dropped, so a name that resolves elsewhere is never connected to.
    internal static HttpClient Loopback => Local;

    private static HttpClient MakeClient(bool loopbackOnly = false)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5), // DNS changes are followed
            ConnectTimeout = TimeSpan.FromSeconds(20),
            // a key goes to its provider's address only: a redirect is not followed but answered as a refusal (AiErrors)
            AllowAutoRedirect = false,
        };
        if (loopbackOnly)
        {
            handler.UseProxy = false;
            handler.ConnectCallback = ConnectLoopback;
        }
        var http = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan, // a stream may last minutes; IdleLimit ends a silent one
        };
        // its own name, as OpenCode Go asks of clients rather than a generic library's
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Bloom", typeof(ApiClient).Assembly.GetName().Version?.ToString(3) ?? "1.0"));
        return http;
    }

    private static async ValueTask<Stream> ConnectLoopback(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = (await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct).ConfigureAwait(false)).Where(IPAddress.IsLoopback).ToArray();
        if (addresses.Length == 0) throw new HttpRequestException("The address is not on this PC.");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// The call for an API model id (AiModels.Api), on a worker thread (it reads the key and the cached model list):
    /// its provider, the stored key, the wire the model is on, and the reasoning level when the model takes it (the
    /// same AiModels.Efforts the pickers offer). → (request, null) or (null, error).
    public static async Task<(ApiRequest? request, string? error)> PrepareAsync(string model, string? effort, string system, IReadOnlyList<ApiMessage> messages, string? session)
    {
        if (AiModels.ApiParts(model) is not { } parts || ApiProviders.Find(parts.provider) is not { } provider) return (null, AiErrors.ApiModelGone);
        if (ApiKeys.Get(provider.Id) is not { Length: > 0 } key) return (null, AiErrors.ApiNoKey(provider.Name));
        await ApiCatalog.Loaded().ConfigureAwait(false);
        var info = ApiCatalog.Find(provider.Id, parts.id);
        if ((info?.Wire ?? ApiProviders.WireFor(provider, parts.id)) is not { } wire) return (null, AiErrors.ApiModelGone);
        string? level = effort is null || effort == AiModels.Auto || Array.IndexOf(AiModels.Efforts(model), effort) < 0 ? null : effort;
        return (new ApiRequest(provider, key, parts.id, wire, system, messages, level, session) { TakesTools = ApiProviders.TakesTools(provider, parts.id, info) }, null);
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
        var calls = new ApiCallBuilder(); // the tool calls as their pieces come
        // the reasoning to send back later: DeepSeek's text (only for a provider that wants it back), Responses' items
        var reasoning = request.Provider.ReplaysReasoning ? new StringBuilder() : null;
        var thoughts = new List<ApiThought>();
        ApiTokens? tokens = null; // what the provider counted, as its usage events come
        int thoughtSize = 0;
        var clock = Stopwatch.StartNew();
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
                string error;
                try
                {
                    error = await BoundedBody.ReadAsync(response.Content, ApiLimits.MaxErrorBody, idle.Token).ConfigureAwait(false);
                }
                catch (ResponseTooLargeException)
                {
                    error = ""; // a message is short: the status alone tells
                }
                return new ApiReply(false, "", Status(request.Provider, (int)response.StatusCode, error, request.Key));
            }
            using var stream = await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false);
            var reader = new BoundedLineReader(stream, ApiLimits.MaxLine, ApiLimits.MaxStreamBytes);
            var sse = new SseParser();
            while (true)
            {
                // the idle timer restarts with every line, so the whole response has its own bound
                var left = ApiLimits.MaxResponseTime - clock.Elapsed;
                if (left <= TimeSpan.Zero) throw new TimeoutException();
                idle.CancelAfter(left < IdleLimit ? left : IdleLimit);
                string? line = await reader.ReadLineAsync(idle.Token).ConfigureAwait(false);
                if ((line is null ? sse.End() : sse.Line(line)) is { } e)
                {
                    var chunk = wire.Read(e);
                    if (chunk.Text is { Length: > 0 } piece)
                    {
                        if (text.Length + piece.Length > ApiLimits.MaxAnswerText) throw new ResponseTooLargeException();
                        text.Append(piece);
                        delta(piece);
                    }
                    if (chunk.Error is { } failure)
                    {
                        Log.Write($"api {request.Provider.Id}: stream error");
                        return new ApiReply(false, text.ToString(), AiErrors.ApiFailed(name, Redact(failure, request.Key)));
                    }
                    foreach (var part in chunk.Calls ?? []) calls.Add(part);
                    if (reasoning is not null && reasoning.Length + (chunk.Reasoning?.Length ?? 0) > ApiLimits.MaxReasoning) throw new ResponseTooLargeException();
                    reasoning?.Append(chunk.Reasoning);
                    // items stream in order: a reasoning item stands before the text (if none came yet) or the next call
                    if (chunk.Thought is { } item)
                    {
                        thoughtSize += item.Length;
                        if (thoughtSize > ApiLimits.MaxReasoning || thoughts.Count >= ApiLimits.MaxServerTools) throw new ResponseTooLargeException();
                        thoughts.Add(new ApiThought(item, (text.Length > 0 ? 1 : 0) + calls.Count));
                    }
                    if (chunk.Tokens is { } counted) tokens = tokens is { } before ? counted.Over(before) : counted;
                    complete |= chunk.End == ApiEnd.Complete;
                    cut |= chunk.End == ApiEnd.Cut;
                    if (chunk.Done) break;
                }
                if (line is null) break;
            }
            if (cut || !complete)
            {
                Log.Write($"api {request.Provider.Id}: answer cut off ({(cut ? "the provider said so" : "the stream ended early")})");
                return new ApiReply(false, text.ToString(), AiErrors.ApiCutOff(name)) { Tokens = tokens };
            }
            var made = calls.Calls();
            if (made.Count == 0 && text.Length == 0) return new ApiReply(false, "", AiErrors.ApiNoAnswer(name)) { Tokens = tokens };
            return new ApiReply(true, text.ToString(), null) { Calls = made, Reasoning = reasoning is { Length: > 0 } ? reasoning.ToString() : null, Thoughts = thoughts, Tokens = tokens };
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return new ApiReply(false, text.ToString(), AiErrors.Stopped);
        }
        catch (Exception e) when (e is OperationCanceledException or TimeoutException)
        {
            if (clock.Elapsed < ApiLimits.MaxResponseTime) return new ApiReply(false, text.ToString(), AiErrors.TimedOut);
            Log.Write($"api {request.Provider.Id}: the response ran past its time limit");
            return new ApiReply(false, text.ToString(), AiErrors.ApiTooLong(name)) { Tokens = tokens };
        }
        catch (ResponseTooLargeException)
        {
            Log.Write($"api {request.Provider.Id}: the response went past a size limit");
            return new ApiReply(false, text.ToString(), AiErrors.ApiTooLarge(name)) { Tokens = tokens };
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
            using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            string body = await BoundedBody.ReadAsync(response.Content, ApiLimits.MaxBody, limit.Token).ConfigureAwait(false);
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

    /// A plain GET to the provider with its key (ApiBalances): → (status, body); status 0 when it could not be asked (the
    /// network, IdleLimit). The log gets the provider and the status only.
    public static async Task<(int Status, string Body)> GetAsync(ApiProvider provider, string path, string key, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(IdleLimit);
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, provider.Url(path));
            IApiWire.Of(provider.Wire).Authorize(message, provider, key);
            using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            string body = await BoundedBody.ReadAsync(response.Content, ApiLimits.MaxBody, limit.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) Log.Write($"api {provider.Id} {path}: HTTP {(int)response.StatusCode}");
            return ((int)response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (0, "");
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            Log.Write($"api {provider.Id} {path}: {e.GetType().Name}");
            return (0, "");
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
