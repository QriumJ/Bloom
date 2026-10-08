using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// ChatGPT's plan usage from Codex's own app-server (`codex app-server`, JSON-RPC over stdin/stdout): initialize, then
/// `account/rateLimits/read`. No model call; the login stays Codex's. The server is closed as soon as it answers.
internal static class CodexUsage
{
    /// One look; null when Codex gave no numbers (not signed in, an old Codex, no answer in 20 s).
    public static async Task<UsageAccount?> Ask(string exe)
    {
        var psi = OneShot.Psi(exe);
        psi.ArgumentList.Add("app-server");
        psi.RedirectStandardError = false; // its log is not read: left unredirected, it can never fill a pipe and block
        psi.StandardErrorEncoding = null;
        Process? p = null;
        try
        {
            p = Process.Start(psi);
            if (p is null) return null;
            string version = typeof(CodexUsage).Assembly.GetName().Version?.ToString(3) ?? "0";
            await p.StandardInput.WriteLineAsync("{\"method\":\"initialize\",\"id\":0,\"params\":{\"clientInfo\":{\"name\":\"bloom\",\"title\":\"Bloom\",\"version\":\"" + version + "\"}}}");
            await p.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}");
            await p.StandardInput.WriteLineAsync("{\"method\":\"account/rateLimits/read\",\"id\":1}");
            await p.StandardInput.FlushAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (await p.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                if (!line.StartsWith('{')) continue;
                using var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out int n) || n != 1) continue; // the initialize answer, notifications
                if (doc.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("rateLimits", out var rl) && rl.ValueKind == JsonValueKind.Object)
                    return Read(rl);
                Log.Write("ai usage: codex said " + OneShot.Short(line));
                return null;
            }
            return null;
        }
        catch (Exception e)
        {
            if (e is not OperationCanceledException) Log.Error("ai usage codex", e);
            return null;
        }
        finally
        {
            try
            {
                p?.Kill(true);
            }
            catch
            {
                /* gone */
            }
            p?.Dispose();
        }
    }

    /// { primary, secondary: { usedPercent, windowDurationMins, resetsAt (unix s) }, planType }.
    private static UsageAccount? Read(JsonElement rl)
    {
        var limits = new List<UsageLimit>(2);
        foreach (string key in new[] { "primary", "secondary" })
        {
            if (!rl.TryGetProperty(key, out var w) || w.ValueKind != JsonValueKind.Object) continue;
            double used = Num(w, "usedPercent") ?? 0;
            DateTime? resets = Num(w, "resetsAt") is { } at ? DateTimeOffset.FromUnixTimeSeconds((long)at).LocalDateTime : null;
            limits.Add(new UsageLimit(Window((int)(Num(w, "windowDurationMins") ?? 0)), used, resets));
        }
        if (limits.Count == 0) return null;
        string? plan = rl.TryGetProperty("planType", out var pt) && pt.GetString() is { Length: > 0 } s ? char.ToUpperInvariant(s[0]) + s[1..] : null;
        return new UsageAccount("ChatGPT", plan, limits, DateTime.Now);
    }

    /// 300 → "5시간", 10080 → "주간".
    private static string Window(int mins) => mins switch
    {
        10080 => Tr("주간", "Weekly"),
        >= 1440 when mins % 1440 == 0 => Tr($"{mins / 1440}일", $"{mins / 1440}d"),
        >= 60 => Tr($"{mins / 60}시간", $"{mins / 60}h"),
        > 0 => Tr($"{mins}분", $"{mins} min"),
        _ => Tr("한도", "Limit"),
    };

    private static double? Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
