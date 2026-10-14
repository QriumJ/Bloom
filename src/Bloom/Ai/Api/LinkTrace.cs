namespace Bloom.Ai;

/// What looking down a path for links found (RealPath.Trace), without following any: the first link on the way (null:
/// none), where it leads (null: not told — a reparse point that is no symbolic link or junction, or one that could not
/// be read), and whether that is somewhere that is not a local drive (a network share, a device), which a file system
/// call must not reach before the user has said yes.
///
/// A link is also pinned for the approval of a path through it: its Identity (volume and file id of the link itself),
/// the whole path it leads to for the file asked about (Expected: its target plus what lay below the link), and the
/// links found on that path in turn (Then). What the user is shown is this value; after the answer the path is traced
/// again and goes on only if the new value is equal (nothing was replaced) and the file opened is at Expected.
internal sealed record LinkTrace(string? Link, string? Target, bool Remote, string? Identity = null, string? Expected = null, LinkTrace? Then = null)
{
    public static readonly LinkTrace None = new(null, null, false);

    public bool Any => Link is not null;

    /// The last link of the chain (a method: a property would be printed by the record, and loop): where the path ends up (this, when no link follows it).
    public LinkTrace End() => Then is { Any: true } next ? next.End() : this;
}
