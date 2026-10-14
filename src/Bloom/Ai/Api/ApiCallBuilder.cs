using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bloom.Ai;

/// The tool calls of one streamed answer, put together from their pieces (ApiCallPiece). A piece goes to the call with
/// its id; else to the call its wire index points at — unless it names a new id or a name while that call already has
/// one, which starts the next call: Gemini sends parallel calls each whole in its own chunk, all at index 0 (or none),
/// and they must not run together. Argument text joins only the call it belongs to.
internal sealed class ApiCallBuilder
{
    private sealed class Call
    {
        public string Id = "", Name = "";
        public string? Extra;
        public readonly StringBuilder Arguments = new();
    }

    private readonly List<Call> calls = new();
    private readonly Dictionary<int, Call> atIndex = new(); // the call each wire index is on now

    /// Calls begun so far (where a reasoning item stands among them, ApiThought.At).
    public int Count => calls.Count;

    public void Add(ApiCallPiece piece)
    {
        var call = piece.Id is { Length: > 0 } byId ? calls.FirstOrDefault(c => c.Id == byId) : null;
        if (call is null)
        {
            atIndex.TryGetValue(piece.Index, out call);
            bool newId = piece.Id is { Length: > 0 } && call is { Id.Length: > 0 };
            bool newName = piece.Name is { Length: > 0 } && call is { Name.Length: > 0 };
            if (call is null || newId || newName)
            {
                call = new Call();
                calls.Add(call);
            }
            atIndex[piece.Index] = call;
        }
        if (piece.Id is { Length: > 0 } id) call.Id = id;
        if (piece.Name is { Length: > 0 } name) call.Name = name;
        if (piece.Extra is { Length: > 0 } extra) call.Extra = extra;
        if (piece.Whole) call.Arguments.Clear();
        call.Arguments.Append(piece.Arguments);
    }

    /// The named calls in the order they came; one the provider gave no id (Gemini may not) gets one, so its result can
    /// name it.
    public List<ApiToolCall> Calls() => calls
        .Where(c => c.Name.Length > 0)
        .Select(c => new ApiToolCall(c.Id.Length > 0 ? c.Id : "call_" + Guid.NewGuid().ToString("N")[..12], c.Name, c.Arguments.ToString()) { Extra = c.Extra })
        .ToList();
}
