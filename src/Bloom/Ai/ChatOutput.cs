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
    [System.Text.RegularExpressions.GeneratedRegex(@"```file:([^\r\n`]+)\r?\n(.*?)\r?\n```", System.Text.RegularExpressions.RegexOptions.Singleline)]
    private static partial System.Text.RegularExpressions.Regex FencedFile();

    public static ChatOutputResult Process(ChatOutputRequest request, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        string text = request.Text;
        if (request.Fenced && text.Contains("```file:", StringComparison.Ordinal))
            text = FencedFile().Replace(text, match =>
            {
                cancellation.ThrowIfCancellationRequested();
                string name = string.Concat(match.Groups[1].Value.Trim().Split(Path.GetInvalidFileNameChars()));
                if (name.Length == 0 || name.StartsWith('.')) return match.Value;
                try
                {
                    System.IO.Directory.CreateDirectory(request.Directory);
                    File.WriteAllText(Path.Combine(request.Directory, name), match.Groups[2].Value, new UTF8Encoding(false));
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
