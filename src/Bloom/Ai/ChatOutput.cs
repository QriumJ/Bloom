using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Bloom.Core;

namespace Bloom.Ai;

internal sealed record ChatOutputRequest(string Text, string Directory, DateTime? Since, bool Fenced, string SavedTemplate);
internal sealed record ChatOutputResult(string Text, string[]? Files);

/// Only value snapshots cross this boundary. Files retain their established out/name paths;
/// callers serialize a later turn or explicit deletion behind this operation's completion.
internal static partial class ChatOutput
{
    [System.Text.RegularExpressions.GeneratedRegex(@"```file:[ \t]*([^\r\n`]+)\r?\n(.*?)\r?\n```", System.Text.RegularExpressions.RegexOptions.Singleline)]
    private static partial System.Text.RegularExpressions.Regex FencedFile();

    /// The name a ```file:<name> block is saved under: a leading `out\` or `out/` (as the chat's instructions word the path)
    /// dropped, the characters a file name can't have taken out (any other folder separator with them); null for one with
    /// no name left, a hidden one (.name) or one Windows takes for a device (NUL, COM1…) or a stream (a:b).
    private static string? SafeName(string raw)
    {
        string name = raw.Trim();
        if (name.StartsWith("out/", StringComparison.OrdinalIgnoreCase) || name.StartsWith("out\\", StringComparison.OrdinalIgnoreCase)) name = name[4..];
        name = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
        return name.Length == 0 || name.StartsWith('.') || RealPath.Kind(Path.Combine("C:\\", name)) == PathKind.Device ? null : name;
    }

    /// The files an answer's ```file: blocks would save, in order: (name, content) of each with a name SafeName takes.
    public static List<(string Name, string Content)> FencedFiles(string text)
    {
        var files = new List<(string, string)>();
        if (!text.Contains("```file:", StringComparison.Ordinal)) return files;
        foreach (System.Text.RegularExpressions.Match match in FencedFile().Matches(text))
            if (SafeName(match.Groups[1].Value) is { } name) files.Add((name, match.Groups[2].Value));
        return files;
    }

    public static ChatOutputResult Process(ChatOutputRequest request, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        string text = request.Text;
        if (request.Fenced && text.Contains("```file:", StringComparison.Ordinal))
            text = FencedFile().Replace(text, match =>
            {
                cancellation.ThrowIfCancellationRequested();
                if (SafeName(match.Groups[1].Value) is not { } name) return match.Value;
                try
                {
                    System.IO.Directory.CreateDirectory(request.Directory);
                    // through a handle checked to be inside the output folder (a link made there could lead out of it)
                    if (!RealPath.WriteInside(request.Directory, Path.Combine(request.Directory, name), new UTF8Encoding(false).GetBytes(match.Groups[2].Value))) return match.Value;
                    return request.SavedTemplate.Replace("{0}", name, StringComparison.Ordinal);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Log.Error("fenced file", e);
                    return match.Value;
                }
            });
        cancellation.ThrowIfCancellationRequested();
        if (request.Since is not { } since || !System.IO.Directory.Exists(request.Directory)) return new(text, null);
        // Keep only the same first twenty ordinal filenames while scanning, rather than
        // allocating and sorting every generated file in a large conversation directory.
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in new DirectoryInfo(request.Directory).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            cancellation.ThrowIfCancellationRequested();
            if (file.LastWriteTimeUtc < since) continue;
            files.Add(file.FullName);
            if (files.Count > 20) files.Remove(files.Max!);
        }
        return new(text, files.Count == 0 ? null : files.ToArray());
    }
}
