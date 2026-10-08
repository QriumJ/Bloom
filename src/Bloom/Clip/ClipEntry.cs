using System;
using System.Collections.Generic;

namespace Bloom.Clip;

/// One thing that was copied: text, a picture (kept as a PNG next to the history) or files.
internal sealed class ClipEntry
{
    public string Id { get; set; } = ""; // "clip:" + content hash: the same copy twice is one entry
    public string Kind { get; set; } = "text"; // text | image | files
    public string? Text { get; set; }
    public List<string>? Files { get; set; }
    public string? Image { get; set; } // file name under Local\clipboard
    public int Width { get; set; }
    public int Height { get; set; }
    public string? Source { get; set; } // the app it was copied from
    public long Copied { get; set; }
    public bool Pinned { get; set; }

    public DateTime CopiedAt => DateTimeOffset.FromUnixTimeMilliseconds(Copied).LocalDateTime;
}

internal sealed class ClipFile
{
    public List<ClipEntry> Items { get; set; } = new();
}
