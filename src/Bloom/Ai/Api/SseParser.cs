using System.Text;

namespace Bloom.Ai;

/// Server-sent events from a stream's lines, as the providers send them: `event:` and `data:` fields, `:` comments
/// (keep-alives), a blank line ending each event.
internal sealed class SseParser
{
    private string? name;
    private readonly StringBuilder data = new();
    private bool hasData;

    /// One line without its line break → the event it completes, else null.
    public SseEvent? Line(string line)
    {
        if (line.Length == 0) return Flush();
        if (line[0] == ':') return null;
        int colon = line.IndexOf(':');
        string field = colon < 0 ? line : line[..colon];
        string value = colon < 0 ? "" : line[(colon + 1)..];
        if (value.StartsWith(' ')) value = value[1..];
        if (field == "event") name = value;
        else if (field == "data")
        {
            if (hasData) data.Append('\n');
            data.Append(value);
            hasData = true;
        }
        return null;
    }

    /// The end of the stream: an event the server left without its blank line.
    public SseEvent? End() => Flush();

    private SseEvent? Flush()
    {
        var e = hasData ? new SseEvent(name, data.ToString()) : null;
        name = null;
        data.Clear();
        hasData = false;
        return e;
    }
}
