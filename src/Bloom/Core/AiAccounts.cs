using System;
using System.Collections.Generic;
using System.IO;

namespace Bloom.Core;

/// One account whose plan usage the home shows: Claude Code's or Codex's own sign-in in one folder
/// (CLAUDE_CONFIG_DIR / CODEX_HOME). Home null = the CLI's own default folder (~\.claude, ~\.codex).
internal sealed class AiAccount
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "claude"; // claude | chatgpt
    public string? Home { get; set; } // a folder Bloom made for this sign-in (Local\accounts\<id>); null = the default one
    public string? Email { get; set; } // last seen, for the card and the list (never sent anywhere)
    public bool Hidden { get; set; } // a default account the user took off the home

    public bool IsDefault => Home is null;
}

/// The accounts in Settings.AiAccounts: the two defaults always first (kept even when empty), then the added ones.
/// Each added account is a folder of its own; Bloom never reads a Claude login, only Claude Code does.
internal static class AiAccounts
{
    public const string DefaultClaude = "claude", DefaultChatGpt = "chatgpt";

    public static List<AiAccount> All
    {
        get
        {
            var list = Settings.Current.AiAccounts;
            foreach (var (id, kind) in new[] { (DefaultClaude, "claude"), (DefaultChatGpt, "chatgpt") })
                if (!list.Exists(a => a.Id == id)) list.Insert(kind == "claude" ? 0 : Math.Min(1, list.Count), new AiAccount { Id = id, Kind = kind });
            return list;
        }
    }

    public static AiAccount? Find(string id) => All.Find(a => a.Id == id);

    /// The folder of a new sign-in (made empty; the CLI's login fills it).
    public static AiAccount Add(string kind)
    {
        string id = kind + "-" + Guid.NewGuid().ToString("N")[..8];
        string home = Paths.InLocal(Path.Combine("accounts", id, ".keep"));
        File.WriteAllText(home, "");
        var a = new AiAccount { Id = id, Kind = kind, Home = Path.GetDirectoryName(home)! };
        Settings.Current.AiAccounts.Add(a);
        Settings.Save();
        return a;
    }

    /// An added account leaves with its folder (to the Recycle Bin); a default one can only be hidden.
    public static void Remove(AiAccount a)
    {
        if (a.IsDefault) return;
        Settings.Current.AiAccounts.Remove(a);
        Settings.Save();
        if (a.Home is { } home && IsOurs(home) && Directory.Exists(home) && !Native.ShellFiles.Recycle(home, IntPtr.Zero))
            Log.Write("ai account: folder not recycled — " + home);
    }

    /// Only a folder right inside Local\accounts goes to the Recycle Bin (a hand-edited settings file can't point it elsewhere).
    private static bool IsOurs(string home)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(Paths.Local, "accounts")));
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home));
        return string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase);
    }

    /// An added folder holds a sign-in (only that the CLI wrote its file; the file is never opened here). A default
    /// account is taken as signed in: its CLI says otherwise when asked.
    public static bool SignedIn(AiAccount a) => SignInFile(a) is not { } file || File.Exists(file);

    /// The file the CLI writes when an added account signs in (null for a default account).
    public static string? SignInFile(AiAccount a) => a.Home is { } home ? Path.Combine(home, a.Kind == "chatgpt" ? "auth.json" : ".credentials.json") : null;

    /// What a signed-out account needs, in words: an added one signs in again from 설정 > AI 계정, a default one in its
    /// own CLI (Bloom never signs those in). `here`: said on the 설정 page itself.
    public static string NeedsSignIn(AiAccount a, bool here) => a.IsDefault
        ? a.Kind == "chatgpt" ? Tr("로그인 필요 · Codex에서 로그인하세요", "Sign-in needed · sign in from Codex") : Tr("로그인 필요 · Claude Code에서 로그인하세요", "Sign-in needed · sign in from Claude Code")
        : here ? Tr("로그인 필요 · 클릭하여 다시 로그인", "Sign-in needed · click to sign in again") : Tr("로그인 필요 · 설정 > AI 계정", "Sign-in needed · Settings > AI accounts");

    /// The name on a card: the e-mail, or the kind until the first look.
    public static string Label(AiAccount a) => a.Email is { Length: > 0 } e ? e : a.Kind == "chatgpt" ? "ChatGPT" : "Claude";

    /// The account AI chat, Bloom Lens and translation use for `kind` (picked in 설정 > AI 사용량; the default one when
    /// the pick is gone). Only ever the user's pick: nothing switches accounts on its own.
    public static AiAccount Chat(string kind)
    {
        string id = kind == "chatgpt" ? Settings.Current.GptChatAccount : Settings.Current.ClaudeChatAccount;
        return Find(id) is { } a && a.Kind == kind ? a : Find(kind == "chatgpt" ? DefaultChatGpt : DefaultClaude)!;
    }

    /// The folder a CLI for `kind` gets as CLAUDE_CONFIG_DIR / CODEX_HOME; null = its own default.
    public static string? ChatHome(string kind) => Chat(kind).Home;

    /// Points a CLI's process at the chat account's folder (after Cli.CleanEnvironment, which keeps the user's own).
    public static void UseChatAccount(System.Diagnostics.ProcessStartInfo psi, string kind, string? home)
    {
        if (home is not null) psi.Environment[kind == "chatgpt" ? "CODEX_HOME" : "CLAUDE_CONFIG_DIR"] = home;
    }
}
