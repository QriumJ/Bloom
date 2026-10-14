using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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
            SaveText(file, JsonSerializer.Serialize(value, Options));
        }
        catch (Exception e)
        {
            Log.Error("json save " + Path.GetFileName(file), e);
        }
    }

    /// Commit an already captured snapshot; callers may write it away from the owning UI thread.
    internal static void SaveText(string file, string text)
    {
        try
        {
            AtomicFile.Write(file, stream =>
            {
                using var writer = new StreamWriter(stream);
                writer.Write(text);
            });
        }
        catch (Exception e)
        {
            Log.Error("json save " + Path.GetFileName(file), e);
        }
    }
}
