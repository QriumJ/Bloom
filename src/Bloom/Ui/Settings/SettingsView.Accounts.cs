using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 > AI 계정: the account list and add rows, each account's menu (AI chat pick, hide/show, sign in again, remove),
/// the CLI sign-in, and the ChatGPT 조회 방식 and 조회 간격 rows.
internal sealed partial class SettingsView
{
    private void PageAccounts(Settings s, string filter)
    {
        Page("accounts", Tr("AI 계정", "AI accounts"), G.Account, Tr("계정마다 따로 로그인합니다. 홈에서 계정별 사용량을 보고, AI 채팅에 쓸 계정을 직접 고릅니다. 한도에 닿아도 계정은 자동으로 바뀌지 않습니다.", "Each account signs in on its own. The home shows each one's usage, and AI chat uses the account you pick. Nothing switches accounts when a limit is reached."), filter);
        foreach (var (kind, title) in new[] { ("claude", "Claude"), ("chatgpt", "ChatGPT") })
        {
            Header(title);
            string chatId = AiAccounts.Chat(kind).Id;
            foreach (var a in AiAccounts.All.Where(a => a.Kind == kind).ToList())
            {
                string status = UsageLogin.Running == a.Id ? Tr("로그인 진행 중 · 열린 터미널에서 마치세요", "Signing in · finish in the terminal that opened")
                    : UsageLogin.Open(a) ? Tr("로그인 터미널이 열려 있음 · 닫으면 계정을 바꿀 수 있습니다", "Sign-in terminal still open · close it to change the account")
                    : !AiAccounts.SignedIn(a) || AiUsage.Failed(a.Id) ? AiAccounts.NeedsSignIn(a, true)
                    : a.IsDefault ? (kind == "chatgpt" ? Tr("기본 계정 · Codex의 로그인", "Default account · Codex's own sign-in") : Tr("기본 계정 · Claude Code의 로그인", "Default account · Claude Code's own sign-in"))
                    : Tr("Bloom에서 추가한 계정", "Added in Bloom");
                if (a.Hidden) status += Tr(" · 홈에서 숨김", " · hidden on the home");
                var chip = a.Id == chatId ? Theme.T(Tr("AI 채팅에 사용 중", "Used for AI chat"), 12, Theme.P.Accent, Theme.Medium, false) : null;
                var account = a;
                Add(Demo.On ? title : AiAccounts.Label(a), status, chip, G.Account, () => AccountMenu(account), null, filter);
            }
            Add(Tr($"{title} 계정 추가…", $"Add a {title} account…"), Tr("새 터미널에서 공식 로그인 실행 · 브라우저에서 로그인", "Runs the official sign-in in a new terminal · sign in in the browser"), null, G.Add, () =>
            {
                if (UsageLogin.Blocked(kind) is { } why)
                {
                    ConfirmWindow.Tell(Window.GetWindow(this), Tr($"{title} 계정 추가", $"Add a {title} account"), why);
                    return;
                }
                var a = AiAccounts.Add(kind);
                Build(lastFilter);
                _ = SignIn(a);
            }, null, filter);
        }
        Header(Tr("사용량 조회", "Usage checks"));
        Choice(Tr("ChatGPT 조회 방식", "ChatGPT checks"), s.ChatGptUsage == "light"
                ? Tr("가벼운 방식: Codex 로그인으로 HTTPS 요청 1번 · CLI 실행 없음 · 공식 API가 아니라 바뀔 수 있음", "Lightweight: one HTTPS request with the Codex sign-in · no CLI · not an official API, may change")
                : Tr("공식 CLI: 계정마다 Codex 실행 · 약 110 MB 1초", "Official CLI: runs Codex per account · ~110 MB for 1 s"), G.Sync,
            new[] { ("cli", Tr("공식 CLI", "Official CLI")), ("light", Tr("가벼운 방식", "Lightweight")) }, s.ChatGptUsage == "light" ? "light" : "cli", v =>
            {
                s.ChatGptUsage = v;
                _ = AiUsage.Refresh();
                Dispatcher.BeginInvoke(() => Build(lastFilter));
            }, filter);
        Add(Tr("조회 간격", "How often"), Tr($"기본 계정은 홈 화면 설정의 간격대로 · 추가한 계정은 {AiUsage.ExtraMinutes}분마다 · 홈의 ↻는 모든 계정", $"Default accounts as set on the home page · added ones every {AiUsage.ExtraMinutes} min · the home's ↻ checks all"), null, G.Clock, null, null, filter);
    }

    /// An account's choices: use it for AI chat, sign in again, hide it from the home or take it out.
    private void AccountMenu(AiAccount a)
    {
        var owner = Window.GetWindow(this);
        if (UsageLogin.Open(a))
        {
            ConfirmWindow.Tell(owner, AiAccounts.Label(a), Tr("로그인 터미널이 열려 있습니다. 로그인을 마치거나 터미널을 닫으세요.", "The sign-in terminal is open. Finish signing in or close it."));
            return;
        }
        bool chat = AiAccounts.Chat(a.Kind).Id == a.Id;
        var choices = new List<(string, MessageBoxResult)>();
        if (!chat) choices.Add((Tr("AI 채팅에 사용", "Use for AI chat"), MessageBoxResult.OK));
        choices.Add(a.IsDefault ? (a.Hidden ? Tr("홈에 표시", "Show on the home") : Tr("홈에서 숨기기", "Hide on the home"), MessageBoxResult.Yes) : (Tr("다시 로그인", "Sign in again"), MessageBoxResult.Yes));
        if (!a.IsDefault) choices.Add((Tr("계정 빼기", "Remove"), MessageBoxResult.No));
        choices.Add((Tr("닫기", "Close"), MessageBoxResult.Cancel));
        switch (ConfirmWindow.Ask(owner, AiAccounts.Label(a), a.IsDefault ? Tr("기본 계정", "Default account") : Tr("Bloom에서 추가한 계정", "Added in Bloom"), choices.ToArray()))
        {
            case MessageBoxResult.OK:
                if (a.Kind == "chatgpt" && CodexAuth.Unsaved(a.Home))
                {
                    // Codex would start on the spent sign-in in auth.json
                    ConfirmWindow.Tell(owner, AiAccounts.Label(a), Tr("이 계정의 갱신된 로그인을 아직 저장하지 못했습니다. 잠시 후 다시 시도하세요.", "This account's renewed sign-in isn't saved yet. Try again in a moment."));
                    return;
                }
                AiAccounts.UseForChat(a.Kind, a.Id);
                Settings.Save();
                host.Chats.EndAll(); // the next message starts in the new account
                break;
            case MessageBoxResult.Yes when a.IsDefault:
                a.Hidden = !a.Hidden;
                Settings.Save();
                if (!a.Hidden) _ = AiUsage.Refresh(all: false); // its card comes back with today's numbers (a default account is in every look)
                host.Window.Refresh(true);
                break;
            case MessageBoxResult.Yes:
                _ = SignIn(a);
                break;
            case MessageBoxResult.No:
                if (ConfirmWindow.Ask(owner, Tr("계정 빼기", "Remove account"), Tr("이 계정의 로그인 폴더를 휴지통으로 옮깁니다.", "Moves this account's sign-in folder to the Recycle Bin."), (Tr("빼기", "Remove"), MessageBoxResult.Yes), (Tr("취소", "Cancel"), MessageBoxResult.No)) != MessageBoxResult.Yes) break;
                if (chat)
                {
                    AiAccounts.UseForChat(a.Kind, AiAccounts.DefaultId(a.Kind));
                    host.Chats.EndAll();
                }
                AiAccounts.Remove(a);
                AiUsage.Forget(a.Id);
                break;
            default:
                return;
        }
        Build(lastFilter);
    }

    /// Runs the CLI's sign-in for an added account; the line under it follows, a failure is told.
    private async Task SignIn(AiAccount a)
    {
        string? error = await UsageLogin.Run(a); // the lines follow UsageLogin.Changed (SettingsView)
        Build(lastFilter);
        if (error is not null) ConfirmWindow.Tell(Window.GetWindow(this), AiAccounts.Label(a), error);
    }
}
