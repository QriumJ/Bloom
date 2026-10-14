using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bloom.Ai;

/// Lines of UTF-8 text from a stream, read in chunks: a line longer than `maxLine` bytes, or a stream longer than
/// `maxTotal` bytes (0: no bound), ends with ResponseTooLargeException before it is held whole. A line ends at \n (a
/// \r before it goes too); a last line with no end is still a line.
internal sealed class BoundedLineReader
{
    private readonly Stream stream;
    private readonly int maxLine;
    private readonly long maxTotal;
    private readonly byte[] buffer = new byte[16 * 1024];
    private readonly MemoryStream line = new();
    private int start, end;
    private long total;
    private bool first = true;

    public BoundedLineReader(Stream stream, int maxLine, long maxTotal = 0)
    {
        this.stream = stream;
        this.maxLine = maxLine;
        this.maxTotal = maxTotal;
    }

    /// The next line, null at the end of the stream.
    public string? ReadLine()
    {
        while (true)
        {
            if (Take(out string? text)) return text;
            int n = stream.Read(buffer, 0, buffer.Length);
            if (Filled(n, out text)) return text;
        }
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken ct)
    {
        while (true)
        {
            if (Take(out string? text)) return text;
            int n = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (Filled(n, out text)) return text;
        }
    }

    /// Takes from the buffer up to a line end; false when it holds none.
    private bool Take(out string? text)
    {
        text = null;
        if (start == end) return false;
        int at = Array.IndexOf(buffer, (byte)'\n', start, end - start);
        int count = at < 0 ? end - start : at - start;
        if (line.Length + count > maxLine) throw new ResponseTooLargeException();
        line.Write(buffer, start, count);
        if (at < 0)
        {
            start = end;
            return false;
        }
        start = at + 1;
        text = Flush();
        return true;
    }

    /// n bytes came (0: the stream ended); true with the last line (or null) when it ended.
    private bool Filled(int n, out string? text)
    {
        text = null;
        if (n == 0)
        {
            text = line.Length > 0 ? Flush() : null;
            return true;
        }
        total += n;
        if (maxTotal > 0 && total > maxTotal) throw new ResponseTooLargeException();
        start = 0;
        end = n;
        return false;
    }

    private string Flush()
    {
        int length = (int)line.Length;
        if (length > 0 && line.GetBuffer()[length - 1] == '\r') length--;
        string text = Encoding.UTF8.GetString(line.GetBuffer(), 0, length);
        line.SetLength(0);
        if (!first) return text;
        first = false;
        return text.TrimStart('\uFEFF'); // a byte-order mark at the start of the stream
    }
}
