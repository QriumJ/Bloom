using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Bloom.Core;

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static T Load<T>(string file) where T : new()
    {
        try
        {
            if (File.Exists(file)) return JsonSerializer.Deserialize<T>(File.ReadAllText(file), Options) ?? new T();
        }
        catch (Exception e)
        {
            Log.Error("json load " + Path.GetFileName(file), e);
            try
            {
                File.Copy(file, file + ".broken", true);
            }
            catch
            {
                /* keep going with defaults */
            }
        }
        return new T();
    }

    /// Write-then-rename so a crash never leaves half a file.
    public static void Save<T>(string file, T value)
    {
        try
        {
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            File.Move(tmp, file, true);
        }
        catch (Exception e)
        {
            Log.Error("json save " + Path.GetFileName(file), e);
        }
    }
}

/// Debounced saver: many changes in a burst become one write.
internal sealed class Saver
{
    private readonly Action save;
    private readonly Timer timer;

    public Saver(Action save, int delayMs = 400)
    {
        this.save = save;
        timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        Delay = delayMs;
    }

    private int Delay { get; }

    public void Touch() => timer.Change(Delay, Timeout.Infinite);

    public void Flush()
    {
        timer.Change(Timeout.Infinite, Timeout.Infinite);
        try
        {
            save();
        }
        catch (Exception e)
        {
            Log.Error("saver", e);
        }
    }
}
