using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using Bloom.Core;

namespace Bloom.Tests;

/// The AI 계정 list, the light ChatGPT usage answer (wham/usage, the shape seen on 2026-10-11) and refreshed tokens
/// whose save failed, including login markers that keep the terminal's sign-in file protected.
internal static class UsageAccountTests
{
    public static void DefaultsComeFirst()
    {
        var list = Settings.Current.AiAccounts;
        list.Clear();
        list.Add(new AiAccount { Id = "claude-1", Kind = "claude", Home = @"C:\x\accounts\claude-1" });
        try
        {
            var all = AiAccounts.All;
            Check.Eq(string.Join(",", all.ConvertAll(a => a.Id)), "claude,chatgpt,claude-1", "the two defaults first, then the added one");
            Check.Eq(AiAccounts.Chat("claude").Id, "claude", "chat account: the default until one is picked");
            Settings.Current.ClaudeChatAccount = "claude-1";
            Check.Eq(AiAccounts.Chat("claude").Home, @"C:\x\accounts\claude-1", "a picked account's folder");
            Settings.Current.ClaudeChatAccount = "chatgpt";
            Check.Eq(AiAccounts.Chat("claude").Id, "claude", "a pick of the other kind falls back to the default");
        }
        finally
        {
            list.Clear();
            Settings.Current.ClaudeChatAccount = "claude";
        }
    }

    public static void LightAnswer()
    {
        var body = JsonNode.Parse("""
            {"plan_type":"pro","rate_limit":{"allowed":true,"limit_reached":false,
              "primary_window":{"used_percent":12,"limit_window_seconds":18000,"reset_after_seconds":3600,"reset_at":0},
              "secondary_window":{"used_percent":48,"limit_window_seconds":604800,"reset_at":1792295878}}}
            """)!;
        var claims = JsonNode.Parse("""{"email":"someone@example.com"}""");
        var now = new DateTime(2026, 10, 11, 12, 0, 0);
        var a = Ai.ChatGptLight.Parse(body, claims, now)!;
        Check.Eq((a.Name, a.Plan, a.Who), ("ChatGPT", (string?)"Pro", (string?)"someone@example.com"), "name, plan, e-mail");
        Check.Eq(a.Limits.Count, 2, "five hours + week");
        Check.Eq((a.Limits[0].Title, a.Limits[0].Percent, a.Limits[0].Resets), ("5시간", 12.0, (DateTime?)now.AddHours(1)), "five hours, reset counted from now");
        Check.Eq((a.Limits[1].Title, a.Limits[1].Percent), ("주간", 48.0), "week");
        Check.Eq(Ai.ChatGptLight.Parse(JsonNode.Parse("""{"rate_limit":null}""")!, null, now), null, "no windows → nothing");
    }

    /// Only the marked process and start time keep a login open; stale or malformed markers are removed.
    public static void LoginMarkerTracksTheSameProcess()
    {
        string home = Path.Combine(Path.GetTempPath(), "bloom-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        string marker = Path.Combine(home, ".bloom-login");
        using var process = Process.GetCurrentProcess();
        try
        {
            Check.Eq(Ai.LoginMarker.Alive(null), false, "a default account has no marker");
            Check.Eq(Ai.LoginMarker.Alive(home), false, "no terminal marked");
            Ai.LoginMarker.Mark(home, process);
            Check.True(Ai.LoginMarker.Alive(home), "the marked process is still running");
            var account = new AiAccount { Id = "chatgpt-test", Kind = "chatgpt", Home = home };
            Check.True(Ai.UsageLogin.Open(account), "the terminal keeps the account open after the login wait");

            string pid = process.Id.ToString(CultureInfo.InvariantCulture);
            File.WriteAllText(marker, pid + " 0");
            Check.Eq(Ai.LoginMarker.Alive(home), false, "the same PID with another start time is stale");
            Check.Eq(File.Exists(marker), false, "a stale marker is removed");
            File.WriteAllText(marker, int.MaxValue.ToString(CultureInfo.InvariantCulture) + " 0");
            Check.Eq(Ai.LoginMarker.Alive(home), false, "a nonexistent PID is stale without throwing");
            Check.Eq(File.Exists(marker), false, "a nonexistent process's marker is removed");
            File.WriteAllText(marker, "unfinished");
            Check.Eq(Ai.LoginMarker.Alive(home), false, "a malformed marker is stale");
            Check.Eq(File.Exists(marker), false, "a malformed marker is removed");

            Ai.LoginMarker.Mark(home, process);
            Ai.LoginMarker.Unmark(home);
            Check.Eq(Ai.UsageLogin.Open(account), false, "the account is free once unmarked");
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    /// A terminal owns its sign-in file until it closes; refreshed tokens must wait without overwriting it.
    public static void LoginTerminalKeepsTokensWaiting()
    {
        string home = Path.Combine(Path.GetTempPath(), "bloom-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        string file = Path.Combine(home, "auth.json");
        const string original = """{"tokens":{"access_token":"old","refresh_token":"r-old"}}""";
        using var process = Process.GetCurrentProcess();
        try
        {
            File.WriteAllText(file, original);
            var from = Ai.CodexAuth.Read(home)!;
            var fresh = Ai.CodexAuth.From((JsonObject)JsonNode.Parse("""{"tokens":{"access_token":"fresh","refresh_token":"r-fresh"}}""")!)!;
            Ai.LoginMarker.Mark(home, process);
            Ai.CodexAuth.Save(home, from, fresh).GetAwaiter().GetResult();
            Check.Eq(File.ReadAllText(file), original, "a live terminal's auth.json is untouched");
            Check.True(File.Exists(Path.Combine(home, Ai.CodexAuth.PendingName("r-old"))), "waiting tokens survive a Bloom restart while the terminal is open");
            Check.Eq(Ai.CodexAuth.Read(home)?.Access, "fresh", "the light query can still read waiting tokens");
            Check.True(Ai.CodexAuth.Unsaved(home), "CLI fallback waits for the terminal to close");

            Ai.LoginMarker.Unmark(home);
            Check.Eq(Ai.CodexAuth.Unsaved(home), false, "the waiting tokens settle after the terminal closes");
            Check.Eq(Ai.CodexAuth.Read(home)?.Access, "fresh", "the refreshed sign-in is saved");
            Check.Eq(File.Exists(Path.Combine(home, Ai.CodexAuth.PendingName("r-old"))), false, "no pending file remains");
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    /// Refreshed tokens on their way into auth.json (CodexAuth): they wait while the file is held and go in once it is
    /// let go, a second refresh keeps the refresh token they replace, a sign-in since wins, and a pending file from a
    /// Bloom before goes in only while auth.json still holds the refresh token it was made from.
    public static void LightPendingTokens()
    {
        string home = Path.Combine(Path.GetTempPath(), "bloom-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        string file = Path.Combine(home, "auth.json");
        static string Tokens(string access) => $$$"""{"tokens":{"access_token":"{{{access}}}","refresh_token":"r-{{{access}}}"}}""";
        static Ai.CodexAuth.Auth Made(string access) => Ai.CodexAuth.From((JsonObject)JsonNode.Parse(Tokens(access))!)!;
        string Pending(string from) => Path.Combine(home, Ai.CodexAuth.PendingName("r-" + from));
        FileStream Hold() => new(file, FileMode.Open, FileAccess.Read, FileShare.Read); // read, never replaced
        try
        {
            File.WriteAllText(file, Tokens("one"));
            using (Hold())
            {
                Ai.CodexAuth.Save(home, Ai.CodexAuth.Read(home)!, Made("two")).GetAwaiter().GetResult();
                Check.Eq(Ai.CodexAuth.Read(home)?.Access, "two", "auth.json held: the refreshed tokens are read from memory");
                Ai.CodexAuth.Save(home, Ai.CodexAuth.Read(home)!, Made("three")).GetAwaiter().GetResult();
                Check.Eq(File.Exists(Pending("one")), true, "waiting on disk, named for the refresh token auth.json holds");
            }
            Check.Eq(Ai.CodexAuth.Unsaved(home), false, "let go: put in place");
            Check.Eq((Ai.CodexAuth.Read(home)?.Access, File.Exists(Pending("one"))), ((string?)"three", false), "auth.json has the newest tokens");

            using (Hold())
                Ai.CodexAuth.Save(home, Ai.CodexAuth.Read(home)!, Made("four")).GetAwaiter().GetResult();
            File.WriteAllText(file, Tokens("other")); // `codex login` since
            Check.Eq(Ai.CodexAuth.Unsaved(home), false, "a sign-in since wins");
            Check.Eq((Ai.CodexAuth.Read(home)?.Access, File.Exists(Pending("three"))), ((string?)"other", false), "auth.json kept, the waiting tokens dropped");

            File.WriteAllText(Pending("other"), Tokens("five")); // left by a Bloom before
            File.WriteAllText(Pending("gone"), Tokens("six")); // made from a refresh token auth.json no longer holds
            Check.Eq(Ai.CodexAuth.Unsaved(home), false, "a Bloom started again puts the matching file in place");
            Check.Eq((Ai.CodexAuth.Read(home)?.Access, File.Exists(Pending("gone"))), ((string?)"five", false), "the other one dropped");
            File.WriteAllText(Pending("five"), "{\"tokens\":{\"acc");
            Check.Eq((Ai.CodexAuth.Unsaved(home), File.Exists(Pending("five")), Ai.CodexAuth.Read(home)?.Access), (false, false, (string?)"five"), "cut short: dropped");
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }
}
