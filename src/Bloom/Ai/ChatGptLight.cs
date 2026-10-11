using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Bloom.Core;
using Auth = Bloom.Ai.CodexAuth.Auth;

namespace Bloom.Ai;

/// ChatGPT's plan usage the light way (설정 > AI 계정 › ChatGPT 조회 방식 = 가벼운 방식): the account's Codex sign-in
/// (auth.json in its CODEX_HOME, read and saved by CodexAuth) and one HTTPS request to the endpoint Codex's own /status
/// reads — no CLI process. Not an official API, so it may change. Tokens: a folder Codex itself uses — the default ~\.codex, or the account AI chat
/// runs Codex in — is only read, never refreshed (a refresh rotates the refresh token and would sign that Codex out;
/// Codex keeps it fresh, and AiUsage falls back to Codex's app-server when it ran out). Only an added folder no Codex
/// runs in is refreshed here. Nothing is sent anywhere but auth.openai.com and chatgpt.com.
internal static class ChatGptLight
{
    private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann"; // Codex CLI's public OAuth client
    private const string TokenUrl = "https://auth.openai.com/oauth/token";
    private const string UsageUrl = "https://chatgpt.com/backend-api/wham/usage";

    /// Refresh this long before the access token runs out.
    private static readonly TimeSpan RefreshAhead = TimeSpan.FromHours(6);

    private static readonly HttpClient Http = MakeClient();

    /// One look; null when there is no sign-in, it ran out, or the server said no (the reason goes to the log).
    public static async Task<UsageAccount?> Ask(AiAccount account)
    {
        string home = account.Home ?? CodexAuth.DefaultHome();
        // refreshed here: an added folder no Codex runs in, and no login terminal is open on
        bool ours = account.Home is not null && AiAccounts.Chat("chatgpt").Id != account.Id && !UsageLogin.Open(account);
        try
        {
            if (CodexAuth.Read(home) is not { } auth) return Fail("no sign-in in " + (ours ? "its folder" : "~\\.codex"));
            if (ours && auth.Refresh is not null && Expires(auth.Access) is { } exp && exp - DateTime.UtcNow < RefreshAhead)
                auth = await Refresh(home) ?? auth;
            var (status, body) = await Usage(auth);
            if (status == HttpStatusCode.Unauthorized && ours && auth.Refresh is not null && await Refresh(home) is { } fresh)
                (status, body) = await Usage(auth = fresh);
            if (status != HttpStatusCode.OK || body is null) return Fail($"usage answered {(int)status}");
            return Parse(body, Claims(auth.IdToken));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or JsonException or UnauthorizedAccessException)
        {
            return Fail(e.GetType().Name + ": " + e.Message);
        }
    }

    /// The wham answer → the card: rate_limit.primary_window / secondary_window {used_percent, limit_window_seconds,
    /// reset_at | reset_after_seconds}, plan_type.
    public static UsageAccount? Parse(JsonNode body, JsonNode? claims, DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        var limits = new List<UsageLimit>(2);
        foreach (string key in new[] { "primary_window", "secondary_window" })
        {
            if (body["rate_limit"]?[key] is not JsonObject w) continue;
            double used = Num(w, "used_percent") ?? 0;
            DateTime? resets = Num(w, "reset_at") is double resetAt && resetAt > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)resetAt).LocalDateTime
                : Num(w, "reset_after_seconds") is double after ? at.AddSeconds(after) : null;
            limits.Add(new UsageLimit(UsageLimit.Window((int)((Num(w, "limit_window_seconds") ?? 0) / 60)), used, resets));
        }
        if (limits.Count == 0) return null;
        string? plan = Str(body, "plan_type") ?? Str(claims?["https://api.openai.com/auth"], "chatgpt_plan_type");
        return new UsageAccount("ChatGPT", UsageAccount.ChatGptPlan(plan), limits, DateTime.Now) { Who = Str(claims, "email") };
    }

    private static UsageAccount? Fail(string why)
    {
        Log.Write("ai usage: chatgpt (light) — " + why);
        return null;
    }

    private static async Task<(HttpStatusCode status, JsonNode? body)> Usage(Auth auth)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.Access);
        if ((auth.AccountId ?? Str(Claims(auth.IdToken)?["https://api.openai.com/auth"], "chatgpt_account_id")) is { } id) req.Headers.Add("ChatGPT-Account-Id", id);
        using var res = await Http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return (res.StatusCode, null);
        return (res.StatusCode, JsonNode.Parse(await res.Content.ReadAsStringAsync()));
    }

    /// New tokens for the folder (CodexAuth saves them, the rest of auth.json kept); null when the sign-in is over.
    private static async Task<Auth?> Refresh(string home)
    {
        // the sign-in as it is now: a refresh token the CLI's login just rotated must not be sent spent
        if (CodexAuth.Read(home) is not { } from || from.Refresh is null) return null;
        CodexAuth.Refreshing(home, true); // AI chat can't take the folder meanwhile (CodexAuth.Unsaved)
        try
        {
            return await Refresh(home, from);
        }
        finally
        {
            CodexAuth.Refreshing(home, false);
        }
    }

    /// The token request for `from` and its answer, handed to CodexAuth.
    private static async Task<Auth?> Refresh(string home, Auth from)
    {
        var body = new JsonObject { ["client_id"] = ClientId, ["grant_type"] = "refresh_token", ["refresh_token"] = from.Refresh, ["scope"] = "openid profile email" };
        using var res = await Http.PostAsync(TokenUrl, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        if (!res.IsSuccessStatusCode || JsonNode.Parse(await res.Content.ReadAsStringAsync()) is not JsonObject answer || Str(answer, "access_token") is not { } access)
        {
            Log.Write($"ai usage: chatgpt (light) refresh answered {(int)res.StatusCode}");
            return null;
        }
        var raw = (JsonObject)from.Raw.DeepClone(); // `from` may be the waiting tokens CodexAuth still holds
        var tokens = raw["tokens"]!.AsObject();
        tokens["access_token"] = access;
        if (Str(answer, "id_token") is { } id) tokens["id_token"] = id;
        if (Str(answer, "refresh_token") is { } rt) tokens["refresh_token"] = rt;
        raw["last_refresh"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var fresh = CodexAuth.From(raw)!;
        await CodexAuth.Save(home, from, fresh);
        return fresh;
    }

    /// A JWT's claims (the middle part); only read, never checked — the server checks them.
    private static JsonNode? Claims(string? jwt)
    {
        if (jwt?.Split('.') is not { Length: >= 2 } parts) return null;
        try
        {
            string b64 = parts[1].Replace('-', '+').Replace('_', '/');
            return JsonNode.Parse(Convert.FromBase64String(b64.PadRight(b64.Length + ((4 - (b64.Length % 4)) % 4), '=')));
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }

    private static DateTime? Expires(string jwt) => Num(Claims(jwt), "exp") is { } exp ? DateTimeOffset.FromUnixTimeSeconds((long)exp).UtcDateTime : null;

    private static HttpClient MakeClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Bloom");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return http;
    }

    private static string? Str(JsonNode? n, string key) => n?[key] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static double? Num(JsonNode? n, string key) => n?[key] is JsonValue v && v.TryGetValue(out double d) ? d : null;
}
