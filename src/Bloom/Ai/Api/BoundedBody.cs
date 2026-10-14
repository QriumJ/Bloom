using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bloom.Ai;

/// An HTTP body read as UTF-8 text in chunks, ending with ResponseTooLargeException past `max` bytes (its declared
/// length checked first) rather than held whole.
internal static class BoundedBody
{
    public static async Task<string> ReadAsync(HttpContent content, int max, CancellationToken ct)
    {
        if (content.Headers.ContentLength > max) throw new ResponseTooLargeException();
        using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var body = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int n;
        while ((n = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (body.Length + n > max) throw new ResponseTooLargeException();
            body.Write(chunk, 0, n);
        }
        return Encoding.UTF8.GetString(body.GetBuffer(), 0, (int)body.Length);
    }
}
