using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bloom.Tests;

/// A provider on this PC for the API tests (dev only): plain HTTP/1.1 on a free localhost port, each request answered by
/// `respond` with the raw response text (status line, headers, body; the connection closes after it, which ends the
/// body). Every request is kept: method, path, headers and body.
internal sealed class FakeApiServer : IDisposable
{
    public sealed record Request(string Method, string Path, Dictionary<string, string> Headers, string Body);

    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly Func<Request, Task<string>> respond;
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;

    public readonly ConcurrentQueue<Request> Requests = new();

    public FakeApiServer(Func<Request, Task<string>> respond)
    {
        this.respond = respond;
        listener.Start();
        loop = Task.Run(Serve);
    }

    public FakeApiServer(Func<Request, string> respond) : this(r => Task.FromResult(respond(r))) { }

    public string BaseUrl => $"http://localhost:{((IPEndPoint)listener.LocalEndpoint).Port}/v1";

    /// A 200 that streams `events` as server-sent events (each already "event: …\ndata: …" or "data: …").
    public static string Sse(params string[] events) =>
        "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n" + string.Concat(Array.ConvertAll(events, e => e + "\n\n"));

    public static string Status(int code, string reason, string headers = "", string body = "") =>
        $"HTTP/1.1 {code} {reason}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n{headers}\r\n{body}";

    private async Task Serve()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }
            _ = Task.Run(() => Answer(client));
        }
    }

    private async Task Answer(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                // the head byte by byte up to its blank line, then Content-Length bytes of body (UTF-8)
                var head = new List<byte>();
                var one = new byte[1];
                while (!(head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n')
                    && await stream.ReadAsync(one).ConfigureAwait(false) == 1) head.Add(one[0]);
                string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split("\r\n");
                string[] start = lines[0].Split(' ');
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in lines[1..])
                    if (line.IndexOf(':') is var colon and > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                var body = new byte[headers.TryGetValue("Content-Length", out var length) ? int.Parse(length) : 0];
                int read = 0;
                while (read < body.Length && await stream.ReadAsync(body.AsMemory(read)).ConfigureAwait(false) is var n and > 0) read += n;
                var request = new Request(start[0], start.Length > 1 ? start[1] : "", headers, Encoding.UTF8.GetString(body, 0, read));
                Requests.Enqueue(request);
                byte[] response = Encoding.UTF8.GetBytes(await respond(request).ConfigureAwait(false));
                await stream.WriteAsync(response).ConfigureAwait(false);
            }
            catch (IOException)
            {
                /* the client went away */
            }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
        try
        {
            loop.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            /* the loop ended with the listener */
        }
        stop.Dispose();
    }
}
