using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Bloom.Ai;

/// A picture as the APIs take it: its media type and its bytes in base64.
internal sealed record ApiImage(string MediaType, string Data)
{
    public string DataUrl => $"data:{MediaType};base64,{Data}";

    /// The pictures among `paths` that are still there (a chat's old Lens picture may be gone), read now.
    public static List<ApiImage> Load(IEnumerable<string> paths) => paths
        .Where(File.Exists)
        .Select(p => Chats.ImageType(p) is { } type ? new ApiImage(type, Convert.ToBase64String(File.ReadAllBytes(p))) : null)
        .OfType<ApiImage>()
        .ToList();
}
