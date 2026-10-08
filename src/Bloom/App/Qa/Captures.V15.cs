using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// v15: switch windows, learned ranking, emoji · symbols, system toggles, recent files, AI actions, and the
/// settings rows for them — in the language of the run (`--lang ko|en`). Pictures are named with the language.
internal static partial class Captures
{
    /// `--qa-set chat`: a local example conversation (never sent), dark and light — the answer's 복사 button.
    private static async Task ChatScreens(Host host, Launcher w, string dir)
    {
        string l = En ? "en" : "ko";
        var t = new ChatThread { Id = "capture", Title = "Shortcuts", Model = "sonnet" };
        t.Messages.Add(new ChatMessage { Role = "user", Text = Tr("이 런처 단축키 정리해줘", "Sum up this launcher's shortcuts") });
        t.Messages.Add(new ChatMessage { Role = "assistant", Text = Tr("자주 쓰는 것만 추렸어요.\n\n- `Tab` 입력한 내용을 **AI에게** 묻기\n- `Ctrl` + `K` 선택한 항목의 동작", "The ones you'll use most:\n\n- `Tab` asks **the AI** about what you typed\n- `Ctrl` + `K` the selected item's actions") });
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            w.ShowChat(t);
            await Snap(w, dir, $"{l}-{theme}-chat-copy");
        }
        // a chat used just now: typing offers to continue it (Tab) or to start a new one (Shift+Tab)
        t.Updated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        host.Chats.Threads.Insert(0, t);
        Theme.Apply("dark");
        await Shot(w, dir, $"{l}-dark-chat-tab-rows", Mode.Root, Tr("다음 주 일정 정리해 줘", "Plan next week"), 700);
        host.Chats.Threads.Remove(t);
    }

    /// `--qa-set chatwin`: the AI chat window with local example conversations (never sent) in groups and by day, one
    /// with an open session, two files waiting; dark and light, a new chat, the filter, the smallest size, a
    /// conversation's menu and a new group being named.
    private static async Task ChatWindowScreens(Host host, Launcher w, string dir)
    {
        string l = En ? "en" : "ko";
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), min = 60_000;
        string csv = Path.Combine(dir, "sales.csv"), png = Path.Combine(dir, Tr("매장 사진.png", "store photo.png"));
        await File.WriteAllTextAsync(csv, "month,sales\n7,120\n8,135\n9,151\n");
        using (var fs = File.Create(png))
        {
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(Shots.AppIcon()));
            enc.Save(fs);
        }
        ChatThread T(string id, string ko, string en, string model, long ago) => new() { Id = "qa-" + id, Title = Tr(ko, en), Model = model, Created = now - ago, Updated = now - ago };
        var open = T("open", "3분기 매출 정리", "Q3 sales summary", "sonnet", 4 * min);
        open.Messages.Add(new ChatMessage { Role = "user", Text = Tr("첨부한 매출 자료로 3분기 흐름을 짧게 정리해줘", "Sum up Q3 from the attached sales data, briefly"), Files = new() { csv } });
        open.Messages.Add(new ChatMessage { Role = "assistant", Text = Tr("3분기는 석 달 내내 올랐어요.\n\n- **7월** 120 → **9월** 151, 약 26% 증가\n- 달마다 늘어난 폭도 커졌어요 (15 → 16)\n- 이 흐름이면 10월은 165 안팎이에요\n\n```text\n7월  120  ████████████\n8월  135  █████████████▌\n9월  151  ███████████████\n```", "Q3 went up every month.\n\n- **July** 120 → **September** 151, about 26% up\n- Each month grew a little more (15 → 16)\n- At this pace October lands near 165\n\n```text\nJul  120  ████████████\nAug  135  █████████████▌\nSep  151  ███████████████\n```") });
        var threads = new[]
        {
            open,
            T("ps", "폴더 정리 PowerShell 스크립트", "PowerShell script to tidy folders", "chatgpt", 2 * 60 * min),
            T("trip", "제주 2박 3일 일정", "Three days in Lisbon", "opus", 26 * 60 * min),
            T("react", "상태 관리 라이브러리 비교", "Comparing state libraries", "sonnet", 3 * 24 * 60 * min),
            T("excel", "엑셀 XLOOKUP 질문", "An Excel XLOOKUP question", "haiku", 12 * 24 * 60 * min),
            T("cv", "자기소개서 문장 다듬기", "Polishing a cover letter", "sonnet", 40 * 24 * 60 * min),
        };
        foreach (var t in threads.Skip(1)) t.Messages.Add(new ChatMessage { Role = "user", Text = t.Title });
        host.Chats.Threads.InsertRange(0, threads);
        host.Chats.SetLiveForQa(open, true);
        // groups: 업무 (open, two chats), 여행 (folded, one), 공부 (open, one)
        ChatGroup Made(string ko, string en, bool folded, params ChatThread[] inside)
        {
            var g = host.Chats.NewGroup();
            host.Chats.Rename(g, Tr(ko, en));
            g.Folded = folded;
            foreach (var t in inside) t.Group = g.Id;
            return g;
        }
        Made("업무", "Work", false, open, threads[1]);
        Made("여행", "Trips", true, threads[2]);
        Made("공부", "Study", false, threads[3]);
        var cw = new ChatWindow(host, open) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        try
        {
            cw.Show();
            cw.Draft(Tr("이 자료로 4분기 예상도 해줘", "Now estimate Q4 from this"), new[] { csv, png });
            async Task Cap(string name)
            {
                await Task.Delay(350);
                await cw.Dispatcher.InvokeAsync(() => cw.Capture(Path.Combine(dir, $"{l}-{name}.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            foreach (string theme in new[] { "dark", "light" })
            {
                Theme.Apply(theme);
                await Cap($"{theme}-cw1-window");
            }
            Theme.Apply("dark");
            cw.FindForQa(Tr("정리", "sum"));
            await Cap("dark-cw2-filter");
            cw.FindForQa("");
            cw.Open(host.Chats.New());
            await Cap("dark-cw3-new");
            cw.Width = cw.MinWidth;
            cw.Height = cw.MinHeight;
            cw.Open(open);
            await Cap("dark-cw4-small");
            cw.Width = 1060;
            cw.Height = 720;
            await Task.Delay(200);
            cw.CaptureMenuForQa(open, Path.Combine(dir, $"{l}-dark-cw5-menu.png"));
            var fresh = host.Chats.NewGroup();
            cw.RenameForQa(fresh, true);
            await Cap("dark-cw6-new-group");
            cw.RenameForQa(fresh, false);
        }
        finally
        {
            cw.Close();
            host.Chats.SetLiveForQa(open, false);
            foreach (var g in host.Chats.Groups.ToList()) host.Chats.DeleteGroup(g);
            foreach (var t in threads) host.Chats.Threads.Remove(t);
        }
    }

    /// `--qa-set usage`: the AI usage block asked of the real CLIs (no model call) with its ↻, dark and light, and the
    /// 새로 받는 간격 row; what came back (and how long it took) in usage.txt.
    private static async Task UsageScreens(Host host, Launcher w, string dir)
    {
        string l = En ? "en" : "ko";
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await AiUsage.Refresh();
        var log = new System.Text.StringBuilder($"refresh {clock.Elapsed.TotalSeconds:0.0} s · CLIs found: {AiUsage.Available}\n");
        foreach (var a in AiUsage.Accounts)
            log.AppendLine($"{a.Name} ({a.Plan}): " + string.Join(" · ", a.Limits.ConvertAll(x => $"{x.Title} {x.Percent}% → {x.Resets:M/d HH:mm}")));
        await File.WriteAllTextAsync(Path.Combine(dir, "usage.txt"), log.ToString());
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            await Shot(w, dir, $"{l}-{theme}-u1-home", Mode.Root, "", 700);
        }
        Theme.Apply("dark");
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        sw.Filter(Tr("간격", "Refresh every"));
        await Task.Delay(350);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, $"{l}-dark-u2-settings-interval.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        sw.Close();
    }

    /// `--qa-set notices`: one card of each kind (made-up tasks), dark and light, then the large size.
    private static async Task NoticeScreens(Host host, Launcher w, string dir)
    {
        string l = En ? "en" : "ko";
        var samples = new[]
        {
            new Notice("start", Tr("Claude Code · 진행 중", "Claude Code · Working"), Tr("로그인 화면 리팩터링과 세션 만료 처리, 오류 문구까지 한 번에 정리", "Refactor the sign-in screen, session expiry and the error wording in one go"), Tr("“세션이 끊기면 다시 로그인하는 흐름도 같이 고쳐 줘, 테스트도 추가하고”", "“Also fix signing in again after the session drops, and add tests”")),
            new Notice("done", Tr("Codex · 작업 완료", "Codex · Done"), Tr("결제 모듈 테스트 추가", "Add payment module tests"), Tr("12분 걸렸어요", "Took 12 min")),
            new Notice("waiting", Tr("Claude Code · 승인 대기", "Claude Code · Needs approval"), Tr("빌드 스크립트 정리", "Tidy the build script"), Tr("명령 실행을 허락해 주세요", "Allow the command to run"), "qa"),
            new Notice("limit", Tr("Claude Max 20x · 한도 임박", "Claude Max 20x · Limit near"), Tr("주간 한도 18% 남았어요", "18% of the weekly limit left"), Tr("2일 4시간 뒤 초기화", "Resets in 2 d 4 h")),
            new Notice("recover", Tr("ChatGPT Plus · 한도 복구", "ChatGPT Plus · Limit back"), Tr("5시간 한도가 다시 찼어요", "The 5-hour limit is full again"), null),
        };
        var nw = new NoticeWindow { Offscreen = true };
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            nw.Clear();
            Settings.Current.NoticeSpot = "bottom-right";
            Settings.Current.NoticeSize = "m";
            foreach (var n in samples.AsSpan(0, 4)) nw.Add(n);
            await Task.Delay(400);
            await nw.Dispatcher.InvokeAsync(() => nw.Capture(Path.Combine(dir, $"{l}-{theme}-n1-cards.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            nw.Clear();
            nw.Add(samples[4]);
            nw.Add(samples[0]);
            await Task.Delay(400);
            await nw.Dispatcher.InvokeAsync(() => nw.Capture(Path.Combine(dir, $"{l}-{theme}-n2-recover.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        Theme.Apply("dark");
        nw.Clear();
        Settings.Current.NoticeSize = "l";
        nw.Add(samples[1]);
        await Task.Delay(400);
        await nw.Dispatcher.InvokeAsync(() => nw.Capture(Path.Combine(dir, $"{l}-dark-n3-large.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        nw.Close();
        Settings.Current.NoticeSize = "m";

        // the watchers on this PC's real session logs (read only): what runs now, twice a few seconds apart
        var claude = new ClaudeWatch();
        var codex = new CodexWatch();
        var log = new System.Text.StringBuilder();
        claude.Finished += (t, took) => log.AppendLine($"finished {t.Key} after {took.TotalSeconds:0} s");
        codex.Finished += (t, took) => log.AppendLine($"finished {t.Key} after {took.TotalSeconds:0} s");
        for (int round = 1; round <= 2; round++)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var tasks = claude.Poll();
            tasks.AddRange(codex.Poll());
            log.AppendLine($"round {round}: {tasks.Count} active ({clock.ElapsedMilliseconds} ms)");
            foreach (var t in tasks) log.AppendLine($"  {t.Tool} | {(t.Waiting ? "waiting" : "working")} | since {t.Since:HH:mm:ss} | {t.Title} | {t.Prompt}");
            await Task.Delay(3000);
        }
        await File.WriteAllTextAsync(Path.Combine(dir, "watch.txt"), log.ToString());

        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        foreach (string page in new[] { "notices", "performance" })
        {
            sw.Page(page);
            await Task.Delay(350);
            await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, $"{l}-dark-n4-settings-{page}.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        sw.Filter(Tr("한도", "limit"));
        await Task.Delay(350);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, $"{l}-dark-n5-settings-kinds.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        sw.Close();
    }

    /// `--qa-set promo` (fresh data): pictures for the promo images at 2× — the home with Bloom's tools, a reminder on
    /// the week (the timers block off), made-up usage numbers and a few well-known apps as recent, a calculation, a local
    /// example chat, two notice cards — in the language of the run.
    private static async Task PromoScreens(Host host, Launcher w, string dir)
    {
        string l = En ? "en" : "ko";
        Shots.Scale = 2;
        foreach (string id in new[] { "cmd:quicklinks", "cmd:files", "cmd:clipboard", "cmd:timer", "cmd:calendar", "cmd:lens", "cmd:space" })
            if (Search.SystemItems.Find(id) is { } e) Favorites.Add(new Favorites.Entry { Id = e.Id, Kind = e.Kind, Title = e.Title, Subtitle = e.Subtitle, Target = e.Target });
        Timers.Add("reminder", Tr("디자인 리뷰", "Design review"), DateTime.Today.AddDays(1).AddHours(15));
        Home.Set("timers", false);
        for (int i = 0; i < 60 && !host.Apps.Ready; i++) await Task.Delay(100);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), step = 0;
        foreach (string name in new[] { "Google Chrome", "Adobe Photoshop 2026", "Spotify", "Discord", "OBS Studio" }) // whichever of them this PC has
            if (host.Apps.Apps.FirstOrDefault(a => a.Name == name) is { } app) Usage.Current.Items[app.Id] = new Usage.Entry { Count = 3, Last = now - 60_000 * step++ };
        static UsageAccount Plan(string name, string plan, double? fiveHour, double week)
        {
            var limits = new System.Collections.Generic.List<UsageLimit>();
            if (fiveHour is { } h) limits.Add(new UsageLimit(Tr("5시간", "5h"), h, DateTime.Now.AddHours(3).AddMinutes(12)));
            limits.Add(new UsageLimit(Tr("주간", "Weekly"), week, DateTime.Now.AddDays(4)));
            return new(name, plan, limits, DateTime.Now);
        }
        AiUsage.SetForQa(Plan("Claude", "Max 20x", 32, 47), Plan("ChatGPT", "Pro 20x", null, 21)); // Pro has a weekly limit only
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            await Shot(w, dir, $"{l}-{theme}-p1-home", Mode.Root, "", 900);
        }
        Theme.Apply("dark");
        await Shot(w, dir, $"{l}-dark-p2-calc", Mode.Root, "(1200+800)*1.1", 700);
        await Shot(w, dir, $"{l}-dark-p3-timer", Mode.Root, Tr("10분 타이머", "10 min timer"), 700);
        var t = new ChatThread { Id = "promo", Title = "Bloom", Model = "sonnet" };
        t.Messages.Add(new ChatMessage { Role = "user", Text = Tr("이번 주 일정 요약해 줘", "Sum up my week") });
        t.Messages.Add(new ChatMessage { Role = "assistant", Text = Tr("이번 주는 일정이 하나예요.\n\n- **내일 15:00** 디자인 리뷰\n\n그 전에 끝낼 일을 정리해 드릴까요?", "One thing this week:\n\n- **Tomorrow 3 PM** design review\n\nWant a list of what to finish before it?") });
        w.ShowChat(t);
        await Snap(w, dir, $"{l}-dark-p4-chat");
        var nw = new NoticeWindow { Offscreen = true };
        nw.Add(new Notice("waiting", Tr("Claude Code · 승인 대기", "Claude Code · Needs approval"), Tr("빌드 스크립트 정리", "Tidy the build script"), Tr("확인을 기다리고 있어요", "Waiting for you"), "promo"));
        nw.Add(new Notice("done", Tr("Codex · 작업 완료", "Codex · Done"), Tr("결제 모듈 테스트 추가", "Add payment module tests"), Tr("12분 걸렸어요", "Took 12 min")));
        await Task.Delay(400);
        await nw.Dispatcher.InvokeAsync(() => nw.Capture(Path.Combine(dir, $"{l}-dark-p5-notices.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        nw.Close();
        Shots.Scale = 1;
        foreach (bool tile in new[] { true, false }) // the mark, on its tile and bare
        {
            var mark = new System.Windows.Media.Imaging.RenderTargetBitmap(512, 512, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            mark.Render(Theme.MarkVisual(512, tile));
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(mark));
            using var fs = File.Create(Path.Combine(dir, tile ? "mark-tile.png" : "mark.png"));
            png.Save(fs);
        }
    }

    private static async Task V15Screens(Host host, Launcher w, string dir)
    {
        string l = En ? "en" : "ko";
        Theme.Apply("dark");
        await Shot(w, dir, $"{l}-x01-home", Mode.Root, "", 700);
        await Shot(w, dir, $"{l}-x02-emoji", Mode.Emoji, "", 900);
        await Shot(w, dir, $"{l}-x03-emoji-search", Mode.Emoji, En ? "heart" : "하트", 700);
        await Shot(w, dir, $"{l}-x04-root-emoji", Mode.Root, En ? ":fire" : ":불", 700);
        await Shot(w, dir, $"{l}-x05-symbols", Mode.Emoji, En ? "arrow" : "화살표", 700);
        await Shot(w, dir, $"{l}-x06-system", Mode.System, "", 1500);
        await Shot(w, dir, $"{l}-x07-root-toggle", Mode.Root, En ? "dark" : "다크", 700);
        await Shot(w, dir, $"{l}-x08-root-volume", Mode.Root, En ? "volume 30" : "볼륨 30", 700);
        await Shot(w, dir, $"{l}-x09-windows", Mode.Windows, "", 900);
        await Shot(w, dir, $"{l}-x10-recent", Mode.Recent, "", 900);

        // learned ranking: the same line, before and after three picks of the second result
        const string line = "s";
        w.Go(Mode.Root, line);
        await Task.Delay(400);
        await Snap(w, dir, $"{l}-x11-learn-before");
        string? second = w.ResultIdsForQa().Count > 1 ? w.ResultIdsForQa()[1] : null;
        if (second is not null)
        {
            for (int i = 0; i < 3; i++) QueryMemory.Current.Add(QueryMemory.Key(line), second, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await Shot(w, dir, $"{l}-x12-learn-after", Mode.Root, line, 500);
            await File.WriteAllTextAsync(Path.Combine(dir, $"{l}-learn.txt"), $"picked: {second}\nnow first: {(w.ResultIdsForQa().Count > 0 ? w.ResultIdsForQa()[0] : "-")}\n");
        }

        // AI actions: a sample text as if copied, then a sample answer (no AI call)
        host.Quick.Source = En
            ? "hey team, i think we should move the launch to next friday because the build isnt ready and qa found three bugs"
            : "팀 여러분 빌드가 아직 준비가 안됬고 QA에서 버그 세개가 나와서 출시를 다음주 금요일로 미루는게 좋을것 같습니다";
        host.Quick.FromSelection = true;
        host.Quick.Action = host.Quick.Result = host.Quick.Error = null;
        host.Quick.Busy = false;
        await Shot(w, dir, $"{l}-x13-quick", Mode.Quick, "", 700);
        await Shot(w, dir, $"{l}-x14-quick-filter", Mode.Quick, En ? "short" : "짧게", 500);
        host.Quick.Action = "fix";
        host.Quick.Result = En
            ? "Hey team, I think we should move the launch to next Friday because the build isn't ready and QA found three bugs."
            : "팀 여러분, 빌드가 아직 준비되지 않았고 QA에서 버그가 세 개 나와서 출시를 다음 주 금요일로 미루는 게 좋을 것 같습니다.";
        await Shot(w, dir, $"{l}-x15-quick-result", Mode.Quick, "", 700);

        // settings: language (general), search learning + windows, tools shortcuts
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        foreach (string page in new[] { "general", "search", "tools" })
        {
            sw.Page(page);
            await Task.Delay(350);
            await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, $"{l}-x16-settings-{page}.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        sw.Close();

        // first run: the welcome screen with the language row
        var ob = new OnboardingWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        ob.Show();
        ob.Go(0);
        await Task.Delay(300);
        await ob.Dispatcher.InvokeAsync(() => ob.Capture(Path.Combine(dir, $"{l}-x17-welcome.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        ob.Close();
    }
}
