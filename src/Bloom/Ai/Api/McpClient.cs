using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Bloom.Ai;

/// MCP as an API chat's client speaks it to one server: initialize (and its initialized notification), tools/list page
/// by page, tools/call with the result's content as text. The server itself is the connection's (IMcpConnection).
internal sealed class McpClient : IDisposable
{
    /// The protocol version Bloom asks for; the server answers with the one it speaks.
    public const string Protocol = "2025-06-18";

    /// The protocol versions Bloom knows; a server that answers initialize with another is not used.
    internal static readonly string[] KnownVersions = ["2024-11-05", "2025-03-26", Protocol, "2025-11-25"];

    /// tools/list pages read at most (a server that keeps giving cursors).
    private const int MaxPages = 20;

    private readonly IMcpConnection link;
    private readonly SemaphoreSlim renewing = new(1, 1);

    private McpClient(IMcpConnection link) => this.link = link;

    /// The server started (or reached) and initialized. Throws McpException, OperationCanceledException.
    public static async Task<McpClient> StartAsync(McpServerDef def, CancellationToken ct)
    {
        IMcpConnection link = def.Stdio ? McpStdioConnection.Start(def) : new McpHttpConnection(def);
        var client = new McpClient(link);
        try
        {
            await InitializeAsync(link, ct).ConfigureAwait(false);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// initialize and its initialized notification; the server must name, as a string, a version Bloom knows.
    private static async Task InitializeAsync(IMcpConnection link, CancellationToken ct)
    {
        var init = await link.RequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = Protocol,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "Bloom", ["version"] = typeof(McpClient).Assembly.GetName().Version?.ToString(3) ?? "1.0" },
        }, ct).ConfigureAwait(false);
        // an explicit string from the list: a missing one, or one of another type (ApiJson.Str gives ""), fails the server
        string version = ApiJson.Str(init, "protocolVersion");
        if (Array.IndexOf(KnownVersions, version) < 0) throw new McpException("The server speaks a protocol version Bloom doesn't know.");
        if (link is McpHttpConnection http) http.Version = version;
        await link.NotifyAsync("notifications/initialized", null, ct).ConfigureAwait(false);
    }

    /// An HTTP server that dropped the session (404): before the next call, initialize again and list its tools again; the
    /// tool about to be called must still be there. The call that met the 404 was not replayed.
    private async Task RenewAsync(string? tool, CancellationToken ct)
    {
        if (!link.SessionLost) return;
        await renewing.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!link.SessionLost) return; // another call renewed it meanwhile
            await InitializeAsync(link, ct).ConfigureAwait(false);
            var tools = await ToolsAsync(ct).ConfigureAwait(false);
            if (tool is not null && tools.TrueForAll(t => t.Name != tool)) throw new McpException("The server no longer has this tool.");
        }
        finally
        {
            renewing.Release();
        }
    }

    /// The server's tools, a page at a time, at most ApiLimits.MaxServerTools; a tool with no usable name, or a schema
    /// past ApiLimits.MaxToolSchema, is left out and a description is cut to ApiLimits.MaxToolDescription.
    public async Task<List<McpTool>> ToolsAsync(CancellationToken ct)
    {
        var tools = new List<McpTool>();
        string? cursor = null;
        for (int page = 0; page < MaxPages && tools.Count < ApiLimits.MaxServerTools; page++)
        {
            var result = await link.RequestAsync("tools/list", cursor is null ? null : new JsonObject { ["cursor"] = cursor }, ct).ConfigureAwait(false);
            if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("tools", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var t in list.EnumerateArray())
                {
                    if (tools.Count >= ApiLimits.MaxServerTools) break;
                    string name = ApiJson.Str(t, "name"), description = ApiJson.Str(t, "description");
                    string schema = t.TryGetProperty("inputSchema", out var s) && s.ValueKind == JsonValueKind.Object ? s.GetRawText() : "";
                    if (name.Length is 0 or > ApiLimits.MaxToolName || schema.Length > ApiLimits.MaxToolSchema) continue;
                    tools.Add(new McpTool(name, description.Length > ApiLimits.MaxToolDescription ? description[..ApiLimits.MaxToolDescription] : description, schema));
                }
            cursor = ApiJson.Str(result, "nextCursor");
            if (cursor.Length == 0) break;
        }
        return tools;
    }

    /// One call → its content as text (pictures and other non-text parts named, not inlined; `secrets` taken out of the
    /// decoded values), and whether the server said it failed. Throws McpException (an error answer, the server gone),
    /// OperationCanceledException.
    public async Task<(string Text, bool Error)> CallAsync(string tool, JsonElement arguments, IReadOnlyList<string> secrets, CancellationToken ct)
    {
        await RenewAsync(tool, ct).ConfigureAwait(false);
        var result = await link.RequestAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = JsonNode.Parse(arguments.ValueKind == JsonValueKind.Object ? arguments.GetRawText() : "{}"),
        }, ct).ConfigureAwait(false);
        bool error = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("isError", out var e) && e.ValueKind == JsonValueKind.True;
        return (Text(result, secrets), error);
    }

    /// A tools/call result as text: each content part in order; else its structured content as JSON. `secrets` are taken
    /// out of the decoded values: a part Bloom has no reading for, and the structured content, are walked as JSON and
    /// written out again after (their property names too), never passed on as the server's raw text, where a secret
    /// can hide in an escaped form ("\u0074oken").
    internal static string Text(JsonElement result, IReadOnlyList<string> secrets)
    {
        var sb = new StringBuilder();
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            foreach (var part in content.EnumerateArray())
            {
                string piece = McpSecrets.Redact(ApiJson.Str(part, "type") switch
                {
                    "text" => ApiJson.Str(part, "text"),
                    "image" or "audio" => $"[{ApiJson.Str(part, "type")} {ApiJson.Str(part, "mimeType")}, not shown]",
                    "resource" when part.TryGetProperty("resource", out var r) => ApiJson.Str(r, "text") is { Length: > 0 } text ? text : ApiJson.Str(r, "uri"),
                    "resource_link" => ApiJson.Str(part, "uri"),
                    _ => McpSecrets.RedactedJson(part, secrets),
                }, secrets);
                if (piece.Length > 0) sb.Append(sb.Length > 0 ? "\n" : "").Append(piece);
                if (sb.Length > ApiLimits.MaxToolResult)
                {
                    sb.Length = ApiLimits.MaxToolResult;
                    sb.Append("\n… (cut)");
                    break;
                }
            }
        if (sb.Length == 0 && result.ValueKind == JsonValueKind.Object && result.TryGetProperty("structuredContent", out var structured)) sb.Append(McpSecrets.RedactedJson(structured, secrets));
        return sb.ToString();
    }

    public void Dispose() => link.Dispose();
}
