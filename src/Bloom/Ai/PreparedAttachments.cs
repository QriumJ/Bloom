using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// Copies belong to one unique turn folder until the UI accepts them. Cancellation/rollback
/// only removes this batch's files; a replacement turn can never reuse their paths.
internal sealed class PreparedAttachments : IDisposable
{
    public readonly List<string> Files = new();
    public string Inline { get; internal set; } = "";
    private readonly string directory;
    private bool accepted;

    private PreparedAttachments(string directory) => this.directory = directory;
    public void Accept() => accepted = true;

    public static async Task<PreparedAttachments> Copy(string threadDirectory, IReadOnlyList<string> sources,
        Func<List<string>, string> inline, CancellationToken cancellation)
    {
        string into = Path.Combine(threadDirectory, "in", Guid.NewGuid().ToString("N"));
        var batch = new PreparedAttachments(into);
        try
        {
            if (sources.Count > 0) Directory.CreateDirectory(into);
            foreach (string source in sources)
            {
                cancellation.ThrowIfCancellationRequested();
                string? target = null;
                try
                {
                    if (!File.Exists(source) || new FileInfo(source).Length > Chats.MaxFileBytes) continue;
                    string name = Path.GetFileNameWithoutExtension(source), ext = Path.GetExtension(source);
                    target = Path.Combine(into, name + ext);
                    for (int n = 2; File.Exists(target); n++) target = Path.Combine(into, $"{name} ({n}){ext}");
                    using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        batch.Files.Add(target);
                        await input.CopyToAsync(output, cancellation).ConfigureAwait(false);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    if (target is not null && batch.Files.Remove(target)) TryDelete(target);
                    Log.Error("attach " + Path.GetFileName(source), e);
                }
            }
            cancellation.ThrowIfCancellationRequested();
            batch.Inline = inline(batch.Files);
            cancellation.ThrowIfCancellationRequested();
            return batch;
        }
        catch
        {
            batch.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (accepted) return;
        foreach (string file in Files) TryDelete(file);
        try { if (Directory.Exists(directory)) Directory.Delete(directory, false); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Error("attachment cleanup", e); }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Error("attachment cleanup", e); }
    }
}
