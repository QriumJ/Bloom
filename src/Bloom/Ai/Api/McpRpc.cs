using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bloom.Ai;

/// JSON-RPC 2.0 as MCP uses it: requests and notifications as one line of JSON each, and what an answer holds.
internal static class McpRpc
{
    public static string Request(long id, string method, JsonObject? parameters)
    {
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (parameters is not null) message["params"] = parameters;
        return message.ToJsonString();
    }

    public static string Notification(string method, JsonObject? parameters)
    {
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
        if (parameters is not null) message["params"] = parameters;
        return message.ToJsonString();
    }

    /// An answer to one of ours (a numeric id, no method), with its id; a server's own request or notification is not
    /// one, nor is an error reply with a null id (a server that could not read what it was sent) or an id of any other
    /// kind: the id's kind is checked first, as TryGetInt64 throws on a value that is not a number.
    public static bool IsAnswer(JsonElement message, out long id)
    {
        id = 0;
        return message.ValueKind == JsonValueKind.Object && !message.TryGetProperty("method", out _)
            && message.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number && i.TryGetInt64(out id);
    }

    /// A request of the server's own (a method and an id that is a string or a number, as JSON-RPC allows).
    public static bool IsRequest(JsonElement message) =>
        message.ValueKind == JsonValueKind.Object && message.TryGetProperty("method", out _)
        && message.TryGetProperty("id", out var i) && i.ValueKind is JsonValueKind.String or JsonValueKind.Number;

    /// The answer's `result`; an `error` answer throws McpException with its message (decoded; the raw JSON is never
    /// used as text, as an escaped form of a secret would get past redaction).
    public static JsonElement Outcome(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object) throw new McpException("The server's answer was not one.");
        if (answer.TryGetProperty("error", out var error))
            throw new McpException(error.ValueKind == JsonValueKind.String ? error.GetString() ?? "" : ApiJson.Str(error, "message") is { Length: > 0 } m ? m : "The server answered with an error.");
        return answer.TryGetProperty("result", out var result) ? result.Clone() : default;
    }

    /// Our answer to a request the server made of us (IsRequest): a ping is answered, anything else (sampling, roots) is
    /// not offered.
    public static string AnswerTo(JsonElement request)
    {
        var id = JsonNode.Parse(request.GetProperty("id").GetRawText());
        return ApiJson.Str(request, "method") == "ping"
            ? new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = new JsonObject() }.ToJsonString()
            : new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Method not found" } }.ToJsonString();
    }
}
