using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// An MCP server at a web address (streamable HTTP): each message a POST, answered with JSON or with a short event
/// stream that carries the answer (and may carry requests of the server's own, answered here as the stdio connection
/// does); the session id the server gives at initialize goes with every later message, and the headers its config
/// names (a token) with all of them — to that address only (ApiClient.Shared follows no redirect). The address must be
/// https, or http on this PC itself (loopback), and the headers plain headers (CheckHeaders): a server that is neither
/// is refused (McpException.Refused). A 404 on a session means the server dropped it: the session is retired
/// (SessionLost) and the call that met it fails; McpClient initializes again before the next one. Bodies and events are
/// bounded (ApiLimits). Every request is linked to the connection's lifetime: disposing cancels what is in flight and
/// refuses what follows, and ends the session (DELETE), once, in the background and within DeleteLimit. A message that
/// breaks the protocol fails that call with a clear error.
internal sealed class McpHttpConnection : IMcpConnection
{
    private static readonly TimeSpan DeleteLimit = TimeSpan.FromSeconds(3);

    /// Headers a config may not set: the ones that frame the message or the connection (the request's own, set by
    /// HttpClient), and the two this connection sets itself.
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Transfer-Encoding", "Connection", "Proxy-Connection", "Keep-Alive", "TE", "Trailer", "Upgrade", "Expect",
        "Content-Type", "Content-Encoding", "Mcp-Session-Id", "MCP-Protocol-Version",
    };

    private readonly Uri url;
    private readonly IReadOnlyDictionary<string, string> headers;
    private readonly HttpClient http;
    private volatile string? session;
    private volatile bool lost;
    private long next;
    private int disposed;
    private readonly CancellationTokenSource life = new(); // the connection's lifetime: Dispose cancels it

    private const string Closed = "The server's connection was closed.";

    public McpHttpConnection(McpServerDef def)
    {
        url = Uri.TryCreate(def.Url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            ? u : throw new McpException("The server's address is not a web address.");
        // a token and the calls' own arguments travel here: not in the clear, unless it never leaves this PC
        if (url.Scheme == Uri.UriSchemeHttp && !IsHere(HostWritten(def.Url!))) throw new McpException("The server's address is not encrypted (http).") { Refused = true };
        http = url.Scheme == Uri.UriSchemeHttp ? ApiClient.Loopback : ApiClient.Shared; // a server here: no proxy, loopback only
        headers = CheckHeaders(def.AllHeaders);
    }

    /// The protocol version initialize settled on, sent with every later message as the spec asks.
    public string? Version { get; set; }

    public bool SessionLost => lost;

    /// The host as the address writes it: System.Uri reads some names (loopback) as localhost, which would let them pass.
    internal static string HostWritten(string address)
    {
        string rest = address[(address.IndexOf("://", StringComparison.Ordinal) + 3)..];
        string authority = rest.Split('/', '?', '#')[0];
        authority = authority[(authority.LastIndexOf('@') + 1)..];
        if (authority.StartsWith('[')) return authority[..(authority.IndexOf(']') + 1)];
        return authority.Split(':')[0];
    }

    /// Plain http is for a server on this PC only: a loopback IP written out (127.0.0.0/8, ::1) or the name localhost (which
    /// ApiClient.Loopback connects to only if it resolves to loopback). Any other name — "loopback" included — is not
    /// trusted to mean here: it needs https.
    internal static bool IsHere(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (System.Net.IPAddress.TryParse(host.Trim('[', ']'), out var ip) && System.Net.IPAddress.IsLoopback(ip));

    /// `headers` if each is a valid, allowed header; else McpException (Refused).
    internal static IReadOnlyDictionary<string, string> CheckHeaders(IReadOnlyDictionary<string, string> headers)
    {
        foreach (var (name, value) in headers)
        {
            if (name.Length == 0 || !name.All(IsTokenChar) || Reserved.Contains(name)) throw new McpException("A header of the server is not one Bloom sends.") { Refused = true };
            if (value.Any(c => c is < ' ' and not '\t' || c > '~')) throw new McpException("A header value of the server holds characters a header may not.") { Refused = true };
        }
        return headers;
    }

    /// A character of an HTTP header name (RFC 9110's token).
    private static bool IsTokenChar(char c) => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c, StringComparison.Ordinal);

    /// A request linked to the connection's lifetime: Dispose cancels it, and a request after Dispose is refused.
    private CancellationTokenSource Linked(CancellationToken ct)
    {
        if (disposed != 0) throw new McpException(Closed);
        try
        {
            return CancellationTokenSource.CreateLinkedTokenSource(ct, life.Token);
        }
        catch (ObjectDisposedException)
        {
            throw new McpException(Closed); // Dispose came between
        }
    }

    public async Task<JsonElement> RequestAsync(string method, JsonObject? parameters, CancellationToken ct)
    {
        using var linked = Linked(ct);
        try
        {
            return await Request(method, parameters, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (disposed != 0 && !ct.IsCancellationRequested)
        {
            throw new McpException(Closed);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or FormatException or KeyNotFoundException or InvalidCastException)
        {
            // a message that broke a rule of the protocol: this call fails with a clear error, Bloom goes on
            Log.Write($"mcp http: {e.GetType().Name}");
            throw new McpException("The server sent something Bloom could not follow.");
        }
    }

    private async Task<JsonElement> Request(string method, JsonObject? parameters, CancellationToken ct)
    {
        long id = Interlocked.Increment(ref next);
        if (method == "initialize") session = null; // a new session begins
        using var response = await Post(McpRpc.Request(id, method, parameters), ct).ConfigureAwait(false);
        if (method == "initialize") lost = false;
        if (response.Headers.TryGetValues("Mcp-Session-Id", out var ids))
            foreach (string s in ids) session = s.Length > 0 && s.All(c => c is > ' ' and <= '~') ? s : throw new McpException("The server's session id is not valid.");
        string type = response.Content.Headers.ContentType?.MediaType ?? "";
        if (type == "text/event-stream")
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var lines = new BoundedLineReader(stream, ApiLimits.MaxLine, ApiLimits.MaxBody);
            var sse = new SseParser();
            try
            {
                while (true)
                {
                    string? line = await lines.ReadLineAsync(ct).ConfigureAwait(false);
                    if ((line is null ? sse.End() : sse.Line(line)) is { } e && await Answered(Messages(e.Data), id, ct).ConfigureAwait(false) is { } found) return McpRpc.Outcome(found);
                    if (line is null) throw new McpException("The server's stream ended without an answer.");
                }
            }
            catch (ResponseTooLargeException)
            {
                throw new McpException("The server sent more than Bloom takes, so it was closed.");
            }
        }
        string body;
        try
        {
            body = await BoundedBody.ReadAsync(response.Content, ApiLimits.MaxBody, ct).ConfigureAwait(false);
        }
        catch (ResponseTooLargeException)
        {
            throw new McpException("The server sent more than Bloom takes, so it was closed.");
        }
        return await Answered(Messages(body), id, ct).ConfigureAwait(false) is { } answer ? McpRpc.Outcome(answer) : throw new McpException("The server's answer was not one.");
    }

    public async Task NotifyAsync(string method, JsonObject? parameters, CancellationToken ct)
    {
        using var linked = Linked(ct);
        try
        {
            using var response = await Post(McpRpc.Notification(method, parameters), linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (disposed != 0 && !ct.IsCancellationRequested)
        {
            throw new McpException(Closed);
        }
    }

    /// One message out; a refusal (sign-in needed, the session gone) is the server's failure.
    private async Task<HttpResponseMessage> Post(string body, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        bool inSession = session is not null;
        Mark(message);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new McpException("The server could not be reached.", e);
        }
        if (response.IsSuccessStatusCode) return response;
        int status = (int)response.StatusCode;
        response.Dispose();
        Log.Write($"mcp http: HTTP {status}"); // the address may carry a token: never logged
        if (status == 404 && inSession)
        {
            // the server dropped the session: retired, and the call that found out is not replayed (it may have run)
            session = null;
            lost = true;
            throw new McpException("The server ended the session; the next call starts a new one.");
        }
        throw new McpException($"The server refused the request (HTTP {status}).");
    }

    private void Mark(HttpRequestMessage message)
    {
        foreach (var (k, v) in headers) message.Headers.TryAddWithoutValidation(k, v);
        if (session is { } s) message.Headers.TryAddWithoutValidation("Mcp-Session-Id", s);
        if (Version is { Length: > 0 } v2) message.Headers.TryAddWithoutValidation("MCP-Protocol-Version", v2);
    }

    /// The messages in a body or event: one, or a batch of them; none when it is not JSON.
    private static List<JsonElement> Messages(string json)
    {
        var list = new List<JsonElement>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var m in doc.RootElement.EnumerateArray()) list.Add(m.Clone());
            else list.Add(doc.RootElement.Clone());
        }
        catch (JsonException)
        {
            /* not a message */
        }
        return list;
    }

    /// The answer with `id` among `messages`; a request of the server's own among them is answered (a ping; anything
    /// else is not offered), as the stdio connection does.
    private async Task<JsonElement?> Answered(List<JsonElement> messages, long id, CancellationToken ct)
    {
        JsonElement? found = null;
        foreach (var m in messages)
        {
            if (McpRpc.IsAnswer(m, out long got))
            {
                if (got == id) found = m;
            }
            else if (McpRpc.IsRequest(m))
            {
                try
                {
                    using var _ = await Post(McpRpc.AnswerTo(m), ct).ConfigureAwait(false);
                }
                catch (McpException)
                {
                    /* the server did not take our answer: its request is its own to give up */
                }
            }
        }
        return found;
    }

    /// Ends the session (DELETE) in the background, within DeleteLimit; the second call does nothing.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        life.Cancel(); // a request in flight ends now
        life.Dispose();
        if (session is null) return;
        var end = new HttpRequestMessage(HttpMethod.Delete, url);
        Mark(end);
        _ = Task.Run(async () =>
        {
            using (end)
            {
                using var limit = new CancellationTokenSource(DeleteLimit);
                try
                {
                    using var _ = await http.SendAsync(end, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
                {
                    /* the server is gone already */
                }
            }
        });
    }
}
