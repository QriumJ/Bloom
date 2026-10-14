using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// A stdio MCP server as an API chat runs it: its command (McpLaunch) with no window, its own env values over Bloom's,
/// created suspended and put into a job of its own (ChildProcess, CommandJob) so it and everything it starts end with
/// it; one JSON-RPC message per line each way, answers matched to requests by id on a reader thread (a line past
/// ApiLimits.MaxLine closes the server). Messages go out through a queue to a writer thread, so a server that stops
/// reading blocks neither the caller (RequestAsync is cancellable at any moment) nor Dispose, which never waits for the
/// writer. The queue is capped (messages and bytes: ApiLimits): a server that stops reading is closed once it fills, with
/// a clear error, and no more is waited for. What a thread meets that it did not expect — a malformed message from the
/// server — fails this connection only. Its stderr is read in pieces and dropped (it may log secrets).
internal sealed class McpStdioConnection : IMcpConnection
{
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(1);
    private static readonly UTF8Encoding Utf8 = new(false);

    /// A message waiting for the writer; `Id` is its request's (null: a notification or an answer), so a request
    /// cancelled before it is written is not.
    private sealed record Outgoing(byte[] Data, long? Id, TaskCompletionSource Done);

    private readonly ChildProcess process;
    private readonly CommandJob job;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly BlockingCollection<Outgoing> outgoing = new(ApiLimits.MaxQueuedMessages);
    private long next;
    private long queuedBytes; // what waits in `outgoing` and what the writer holds: bounded by ApiLimits.MaxQueuedBytes
    private volatile bool closed;
    private volatile McpException? ended;
    private int disposed;

    private McpStdioConnection(ChildProcess process, CommandJob job)
    {
        this.process = process;
        this.job = job;
        Run("mcp stdio write", Write);
        Run("mcp stdio read", Read);
        Run("mcp stdio stderr", Drain);
    }

    /// A thread of the connection. Whatever escapes its body — a message Bloom did not expect from the server — ends this
    /// connection only (Contain), never the app: an unhandled exception on a thread would.
    private void Run(string name, ThreadStart body) => new Thread(() =>
    {
        try
        {
            body();
        }
        catch (Exception e)
        {
            Contain(e);
        }
    })
    { IsBackground = true, Name = name }.Start();

    /// The connection fails with a clear error, its requests with it; the server ends. Nothing is logged but the kind.
    private void Contain(Exception e)
    {
        Log.Write($"mcp stdio: {e.GetType().Name}");
        Break(new McpException("The server sent something Bloom could not follow, so it was closed."));
    }

    /// The connection ends for `why`: requests waiting fail with it, and the server is closed on a worker thread (the
    /// caller may be a cancellation callback or a thread Dispose would wait on).
    private void Break(McpException why)
    {
        ended ??= why;
        closed = true;
        Fail();
        _ = Task.Run(Dispose);
    }

    /// Starts the server; McpException when it can't.
    public static McpStdioConnection Start(McpServerDef def)
    {
        var (file, arguments) = McpLaunch.Resolve(def.Command ?? "", def.Args);
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
            if (e.Key is string name && e.Value is string value) env[name] = value;
        foreach (var (k, v) in def.Env) env[k] = v;
        var job = CommandJob.Create() ?? throw new McpException("The server could not get a job of its own.");
        try
        {
            return new McpStdioConnection(ChildProcess.Start(McpLaunch.CommandLine(file, arguments), null, env, job), job);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            job.Dispose();
            throw new McpException("The server's command could not start.", e);
        }
    }

    public async Task<JsonElement> RequestAsync(string method, JsonObject? parameters, CancellationToken ct)
    {
        long id = Interlocked.Increment(ref next);
        var answer = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = answer;
        // registered before anything is written, so a cancel at any moment is seen; the callback never waits
        using var cancel = ct.Register(() => Cancel(id, ct));
        if (closed) Fail(); // the reader ended while this was added
        try
        {
            await Send(McpRpc.Request(id, method, parameters), id).WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            pending.TryRemove(id, out _);
            throw;
        }
        return McpRpc.Outcome(await answer.Task.ConfigureAwait(false));
    }

    public async Task NotifyAsync(string method, JsonObject? parameters, CancellationToken ct) =>
        await Send(McpRpc.Notification(method, parameters), null).WaitAsync(ct).ConfigureAwait(false);

    /// A request cancelled: off the list, the server told it may stop its work (a late answer finds no one waiting).
    private void Cancel(long id, CancellationToken ct)
    {
        if (!pending.TryRemove(id, out var waiting)) return;
        _ = Send(McpRpc.Notification("notifications/cancelled", new JsonObject { ["requestId"] = id }), null);
        waiting.TrySetCanceled(ct);
    }

    /// Queues a message for the writer → completes when it is written, or fails when the server is gone.
    private Task Send(string line, long? id)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var data = Utf8.GetBytes(line + "\n");
        try
        {
            if (closed) done.TrySetException(Gone());
            else if (Interlocked.Add(ref queuedBytes, data.Length) > ApiLimits.MaxQueuedBytes || !outgoing.TryAdd(new Outgoing(data, id, done))) // TryAdd: never waits
            {
                // the server has stopped reading and too much waits for it: it is closed, not waited for
                Interlocked.Add(ref queuedBytes, -data.Length);
                Break(new McpException("The server stopped reading what Bloom sends, so it was closed."));
                done.TrySetException(Gone());
            }
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
        {
            Interlocked.Add(ref queuedBytes, -data.Length);
            done.TrySetException(Gone()); // the queue is closed: Dispose began
        }
        return done.Task;
    }

    private McpException Gone() => ended ?? new McpException("The server is not running.");

    /// The writer thread: one message at a time to the server's input; at the end of the queue it closes it (a server
    /// ends on that). A write that fails (the server ended) fails what is queued.
    private void Write()
    {
        var stream = process.Input;
        bool broken = false;
        try
        {
            foreach (var o in outgoing.GetConsumingEnumerable())
            {
                try
                {
                    if (broken)
                    {
                        o.Done.TrySetException(Gone());
                    }
                    else if (o.Id is { } id && !pending.ContainsKey(id))
                    {
                        o.Done.TrySetCanceled(); // cancelled while it waited
                    }
                    else
                    {
                        stream.Write(o.Data);
                        o.Done.TrySetResult();
                    }
                }
                catch (Exception e) when (e is IOException or ObjectDisposedException)
                {
                    broken = true;
                    o.Done.TrySetException(Gone());
                }
                finally
                {
                    Interlocked.Add(ref queuedBytes, -o.Data.Length);
                }
            }
        }
        finally
        {
            stream.Dispose();
        }
    }

    /// The server's lines: answers go to their requests, its own requests get ours, anything else is passed over.
    private void Read()
    {
        var stream = process.Output;
        try
        {
            var lines = new BoundedLineReader(stream, ApiLimits.MaxLine);
            string? line;
            while ((line = lines.ReadLine()) is not null)
            {
                JsonElement message;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    message = doc.RootElement.Clone();
                }
                catch (JsonException)
                {
                    continue; // a server printing something else on stdout
                }
                if (McpRpc.IsAnswer(message, out long id))
                {
                    if (pending.TryRemove(id, out var waiting)) waiting.TrySetResult(message);
                }
                else if (McpRpc.IsRequest(message))
                    _ = Send(McpRpc.AnswerTo(message), null);
            }
        }
        catch (ResponseTooLargeException)
        {
            ended = new McpException("The server sent a message that was too large, so it was closed.");
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            /* the server ended */
        }
        catch (Exception e)
        {
            // a message that broke a rule of the protocol: this connection fails with a clear error, Bloom goes on
            Log.Write($"mcp stdio: {e.GetType().Name}");
            ended = new McpException("The server sent something Bloom could not follow, so it was closed.");
        }
        closed = true;
        Fail();
        stream.Dispose();
        if (ended is not null) Dispose(); // closed for what it sent: ended now, not at the chat's next close
    }

    /// stderr read in small pieces, kept nowhere, so a server that logs endlessly never fills a pipe or the memory.
    private void Drain()
    {
        var stream = process.Error;
        var chunk = new byte[ApiLimits.DrainChunk];
        try
        {
            while (stream.Read(chunk, 0, chunk.Length) > 0)
            {
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            /* the server ended */
        }
        finally
        {
            stream.Dispose();
        }
    }

    /// Every request still waiting fails: the server is gone (or closed for what it sent).
    private void Fail()
    {
        foreach (long id in pending.Keys)
            if (pending.TryRemove(id, out var waiting)) waiting.TrySetException(Gone());
    }

    /// The queue closes (the writer then closes the server's input), a moment to go, then the job ends the server and
    /// all it started. Once; no lock is waited for. On a worker thread: this waits.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        closed = true;
        outgoing.CompleteAdding();
        process.WaitForExit((int)ExitWait.TotalMilliseconds);
        job.Kill();
        job.Dispose();
        process.Dispose();
        Fail();
    }
}
