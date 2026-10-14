namespace Bloom.Search;

/// Whether an Everything query is only plain text to match against names: no colon anywhere. A `name:` is a function
/// or a macro — content:, ansicontent:, a hash and a property read into files, and Everything 1.5 lets its settings
/// define any name as a macro (`_name:`, even a single letter like `c:`), one that may read files — so no colon is
/// trusted, drive paths included. Such a query waits for the same approval as reading a file outside the chat's folder.
internal static class EverythingQuery
{
    public static bool IsPlainName(string query) => !query.Contains(':');
}
