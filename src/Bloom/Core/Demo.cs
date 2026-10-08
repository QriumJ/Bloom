namespace Bloom.Core;

/// Recording mode (`--demo`, always with `--data`): a run for promo videos that shows nothing of this PC. Apps are
/// limited to a few every PC has; files, recent items, open windows, the live clipboard, MCP lists and the limit
/// notices stay out; the usage numbers are made up and the AI answers are canned (nothing is asked). Set once by Host
/// before anything reads it.
internal static class Demo
{
    public static bool On;
}
