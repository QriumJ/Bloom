using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// ChatGPT's plan usage from Codex's own app-server (`codex app-server`, JSON-RPC over stdin/stdout): initialize, then
/// `account/rateLimits/read` and `account/read` (the e-mail). No model call; the login stays Codex's, and Codex keeps
/// its tokens fresh itself. The server is closed as soon as it answers.
internal static class CodexUsage
{
    /// One look at one account (an added one's folder goes in as CODEX_HOME); null when Codex gave no numbers (not
    /// signed in, an old Codex, no answer in 20 s).
    public static async Task<UsageAccount?> Ask(string exe, AiAccount account)
    {
        var psi = OneShot.Psi(exe);
        psi.ArgumentList.Add("app-server");
        AiAccounts.UseHome(psi, "chatgpt", account.Home);
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
            await p.StandardInput.WriteLineAsync("{\"method\":\"account/read\",\"id\":2,\"params\":{}}");
            await p.StandardInput.FlushAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            UsageAccount? usage = null;
            string? email = null;
            bool limitsDone = false, accountDone = false;
            while (!(limitsDone && accountDone) && await p.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                if (!line.StartsWith('{')) continue;
                using var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out int n)) continue; // notifications
                if (n == 1)
                {
                    limitsDone = true;
                    if (doc.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("rateLimits", out var rl) && rl.ValueKind == JsonValueKind.Object)
                        usage = Read(rl);
                    else
                    {
                        Log.Write("ai usage: codex said " + OneShot.Short(line));
                        return null;
                    }
                }
                else if (n == 2)
                {
                    accountDone = true; // an older Codex without account/read answers with an error: no e-mail, nothing else lost
                    if (doc.RootElement.TryGetProperty("result", out var r2) && r2.TryGetProperty("account", out var acc) && acc.ValueKind == JsonValueKind.Object
                        && acc.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String)
                        email = e.GetString();
                }
            }
            return usage is null ? null : usage with { Who = email };
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
            limits.Add(new UsageLimit(UsageLimit.Window((int)(Num(w, "windowDurationMins") ?? 0)), used, resets));
        }
        if (limits.Count == 0) return null;
        string? plan = rl.TryGetProperty("planType", out var pt) ? UsageAccount.ChatGptPlan(pt.GetString()) : null;
        return new UsageAccount("ChatGPT", plan, limits, DateTime.Now);
    }

    private static double? Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
