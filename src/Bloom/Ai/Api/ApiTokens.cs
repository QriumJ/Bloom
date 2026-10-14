using System.Text.Json;

namespace Bloom.Ai;

/// The tokens a model call counted, as the provider's usage fields say: what it read (`In`, cached input included) and
/// what it wrote (`Out`, reasoning included). Kept per answer (ChatMessage.Tokens) and shown under it.
internal readonly record struct ApiTokens(long In, long Out)
{
    public static ApiTokens operator +(ApiTokens a, ApiTokens b) => new(a.In + b.In, a.Out + b.Out);

    /// A later usage event over an earlier one of the same call: each count it has (above 0) replaces the earlier one
    /// (Messages counts the input at the start and the output at the end; the others send one event).
    public ApiTokens Over(ApiTokens earlier) => new(In > 0 ? In : earlier.In, Out > 0 ? Out : earlier.Out);

    /// "1.2K", "300", "3.4M".
    public static string Short(long n) =>
        n >= 1_000_000 ? (n / 1_000_000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M"
        : n >= 1_000 ? (n / 1_000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "K"
        : n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// The counts in a usage object (`usage`, by whichever names the wire uses for input and output); null when it has
    /// neither. Only a number counts: a null (Messages' message_delta may send `input_tokens: null`), a string or an
    /// absent field is 0, which Over leaves to the earlier event's count.
    public static ApiTokens? Of(JsonElement usage, string input, string output, params string[] moreInput)
    {
        if (usage.ValueKind != JsonValueKind.Object) return null;
        long Num(string name) => usage.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) && n > 0 ? n : 0;
        long read = Num(input);
        foreach (string more in moreInput) read += Num(more);
        long wrote = Num(output);
        return read > 0 || wrote > 0 ? new ApiTokens(read, wrote) : null;
    }
}
