using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Bloom.Core;

/// The API keys the user entered in 설정 > AI 계정, one per provider: kept only on this PC, in the data folder, each
/// encrypted for this Windows user (DPAPI, CurrentUser) so the file alone opens nowhere else. A key is read back only
/// to make a request to its own provider; it is never logged, shown, exported or sent anywhere else.
internal static class ApiKeys
{
    /// Mixed into the encryption so another program's DPAPI blob can't be passed off as Bloom's (not a secret).
    private static readonly byte[] Entropy = "Bloom.ApiKeys.v1"u8.ToArray();

    private const string FileName = "api-keys.json";

    private static readonly object Gate = new();
    private static Dictionary<string, string>? blobs; // provider id → base64 DPAPI blob, as in the file

    private static string FilePath => Paths.In(FileName);

    /// A key is stored for the provider (the recording mode shows none).
    public static bool Has(string provider)
    {
        if (Demo.On) return false;
        lock (Gate) return Blobs().ContainsKey(provider);
    }

    /// The provider's key in clear, for its request only; null when none is stored or it no longer opens (another
    /// Windows user, a copied file).
    public static string? Get(string provider)
    {
        if (Demo.On) return null;
        string? blob;
        lock (Gate)
        {
            if (!Blobs().TryGetValue(provider, out blob)) return null;
        }
        return Unprotect(blob);
    }

    /// Encrypts and stores the key. The DPAPI call and the file write happen outside Gate (the UI takes it to ask Has),
    /// the writes one at a time, each with the latest keys.
    public static void Set(string provider, string key)
    {
        string blob = Protect(key.Trim());
        lock (Gate) Blobs()[provider] = blob;
        Persist();
    }

    public static void Remove(string provider)
    {
        bool removed;
        lock (Gate) removed = Blobs().Remove(provider);
        if (removed) Persist();
    }

    /// Set and Remove on a worker thread, for the UI (설정 > AI 계정).
    public static Task SetAsync(string provider, string key) => Task.Run(() => Set(provider, key));

    public static Task RemoveAsync(string provider) => Task.Run(() => Remove(provider));

    private static readonly object WriteGate = new();

    private static void Persist()
    {
        lock (WriteGate)
        {
            Dictionary<string, string> now;
            lock (Gate) now = new(Blobs());
            Write(FilePath, now);
        }
    }

    private static Dictionary<string, string> Blobs() => blobs ??= Read(FilePath);

    internal static string Protect(string key) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser));

    internal static string? Unprotect(string blob)
    {
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(blob), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            Log.Write("api keys: a stored key could not be opened"); // never the blob or the key
            return null;
        }
    }

    internal static Dictionary<string, string> Read(string file)
    {
        try
        {
            if (File.Exists(file)) return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error("api keys read", e);
        }
        return new();
    }

    internal static void Write(string file, Dictionary<string, string> values) => Json.SaveText(file, JsonSerializer.Serialize(values));
}
