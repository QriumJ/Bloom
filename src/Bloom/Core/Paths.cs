using System;
using System.IO;

namespace Bloom.Core;

internal static class Paths
{
    /// Settings, favorites, usage, chats and the bridge token. `--data <dir>` moves all of it (dev runs).
    public static string Data { get; private set; } = "";

    /// Throwaway state: icon cache, copied browser databases, the AI working folder.
    public static string Local { get; private set; } = "";

    public static string AppDir => AppContext.BaseDirectory;

    /// A dev copy (own --data folder) never writes the user's Run key or real data.
    public static bool IsDev { get; private set; }

    public static void Init(string? dataOverride)
    {
        if (!string.IsNullOrEmpty(dataOverride))
        {
            Data = Path.GetFullPath(dataOverride);
            Local = Path.Combine(Data, "local");
            IsDev = true;
        }
        else
        {
            Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bloom");
            Local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bloom");
        }
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Local);
    }

    public static string In(string name) => Path.Combine(Data, name);

    public static string InLocal(string name)
    {
        string p = Path.Combine(Local, name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        return p;
    }

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
}

internal static class Log
{
    private static readonly object Gate = new();

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                string file = Paths.In("log.txt");
                var fi = new FileInfo(file);
                if (fi.Exists && fi.Length > 512 * 1024) File.Move(file, Paths.In("log.old.txt"), true);
                File.AppendAllText(file, $"{DateTime.Now:MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
        }
        catch
        {
            /* logging never throws */
        }
    }

    public static void Error(string where, Exception e) => Write($"[{where}] {e.GetType().Name}: {e.Message}");
}
