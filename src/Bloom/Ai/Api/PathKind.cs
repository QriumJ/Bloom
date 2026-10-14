namespace Bloom.Ai;

/// What a full path is, told from its text alone (RealPath.Kind) — no file system call, which on a network path could
/// itself reach out. `Local`: a drive on this PC; `Network`: a UNC path or a mapped network drive; `Device`: a device
/// namespace (\\?\, \\.\), a reserved device name (NUL, COM1…), an alternate data stream, or anything else not a plain path.
internal enum PathKind
{
    Local,
    Network,
    Device,
}
