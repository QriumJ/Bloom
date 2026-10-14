using System;
using System.IO;

namespace Bloom.Core;

/// Writes a sibling staging file before replacing the destination. Each attempt owns its staging file.
internal static class AtomicFile
{
    public static void Write(string file, Action<Stream> write)
    {
        string staging = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        bool owned = false;
        try
        {
            using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true;
                write(stream);
            }
            File.Move(staging, file, true);
        }
        finally
        {
            if (owned)
            {
                try { File.Delete(staging); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Log.Error("staging cleanup " + Path.GetFileName(file), e);
                }
            }
        }
    }
}
