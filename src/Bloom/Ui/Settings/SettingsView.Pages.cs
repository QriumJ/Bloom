using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Bloom.Browser;
using Bloom.Ai;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// The settings pages, one method each (their order here is the sidebar's).
internal sealed partial class SettingsView
{
    private void PageGeneral(Settings s, string filter)
    {
        Page("general", Tr("일반", "General"), G.Settings, Tr("Bloom을 여는 방법과 모양", "How Bloom opens and looks"), filter);
        Header(Tr("언어", "Language"));
        Choice("언어 · Language", Tr("바꾸면 Bloom을 다시 시작해요 · 자동은 Windows 표시 언어를 따라요", "Bloom restarts to switch · Auto follows Windows' display language"), G.Globe,
            Lang.Choices, s.Language, v =>
            {
                if (v == s.Language) return;
                s.Language = v;
                Settings.Save();
                if (!Lang.Pending(v)) return; // "auto" that lands on the language already shown
                Dispatcher.BeginInvoke(() =>
                {
                    var answer = MessageBox.Show(Window.GetWindow(this), Tr("Bloom을 다시 시작해서 언어를 바꿀까요?", "Restart Bloom now to switch the language?"), "Bloom", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (answer == MessageBoxResult.Yes) host.Restart("--settings");
                });
            }, filter);
        Header(Tr("열기", "Open"));
        var winSw = Toggle(s.WinKey, v => { s.WinKey = v; host.ApplyHotkeys(); }, out var winT);
        string winSub = Tr("Win 키만 눌렀다 떼면 열려요 (Win+E 같은 조합은 그대로)", "Tap Win alone to open (combinations like Win+E still work)");
        Add(Tr("Win 키로 열기", "Open with the Win key"), winSub, winSw, G.Keyboard, winT, null, filter);
        var altSw = Toggle(s.AltSpace, v => { s.AltSpace = v; host.ApplyHotkeys(); }, out var altT);
        string altSub = host.AltSpaceStatus switch { "taken" => Tr("다른 앱이 쓰고 있어요", "Another app uses it"), "ok" => Tr("켜짐", "On"), _ => Tr("꺼짐", "Off") };
        Add(Tr("Alt + Space로 열기", "Open with Alt + Space"), altSub, altSw, G.Keyboard, altT, null, filter);
        var autoSw = Toggle(s.Autostart, v => { s.Autostart = v; Autostart.Set(v); }, out var autoT);
        Add(Tr("윈도우 시작 시 실행", "Start with Windows"), Paths.IsDev ? Tr("개발용 실행에서는 등록하지 않아요", "Not registered for a dev run") : Autostart.IsOn() ? Tr("로그인하면 트레이에서 대기", "Waits in the tray after sign-in") : Tr("등록되어 있지 않아요", "Not registered"), autoSw, G.Power, autoT, null, filter);
        Header(Tr("모양", "Look"));
        Choice(Tr("테마", "Theme"), Theme.Acrylic ? Tr("유리 효과 켜짐 (Windows 투명 효과)", "Glass on (Windows transparency effects)") : Tr("Windows 투명 효과가 꺼져 있어 단색으로 보여요", "Windows transparency effects are off, so it is solid"), G.Palette,
            new[] { ("system", Tr("시스템", "System")), ("light", Tr("라이트", "Light")), ("dark", Tr("다크", "Dark")) }, s.Theme, v =>
            {
                s.Theme = v;
                Theme.Apply(v);
            }, filter);
        var accent = Accents.Find(s.Accent) ?? Accents.All[0];
        var sw = new Swatches(Accents.All.Select(a => (a.Key, a.Name, Accents.Swatch(a, Theme.P.Dark))), accent.Key);
        void PickAccent(string v)
        {
            s.Accent = v;
            Settings.Save();
            Theme.Apply(s.Theme); // every window repaints; this list is rebuilt by its window
        }
        sw.Picked += PickAccent;
        Add(Tr("색", "Colour"), Tr($"{accent.Name} · 강조색과 전체 톤이 함께 바뀌어요", $"{accent.Name} · the accent and the overall tone change together"), sw, G.Palette, () => PickAccent(sw.Cycle()), d =>
        {
            PickAccent(sw.Step(d));
            return true;
        }, filter);

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Choice(Tr("크기", "Size"), Tr("런처 · 도구 창 · AI 채팅 · 설정 창을 함께 · 런처에서 Ctrl+= / Ctrl+- / Ctrl+0", "The launcher, tool windows, chat and settings together · Ctrl+= / Ctrl+- / Ctrl+0 in the launcher"), G.Screen,
            Theme.Scales.Select(x => (x.ToString(inv), $"{x * 100:0}%")).ToArray(), Theme.Scale.ToString(inv), v => Theme.SetScale(double.Parse(v, inv)), filter);

        Header(Tr("즐겨찾기 아이콘", "Favorite icons"));
        Choice(Tr("아이콘 스타일", "Icon style"), Tr("그림 아이콘의 바탕 — Bloom 화면과 같은 재료로", "The tile behind picture icons — the same material as Bloom"), G.Palette,
            new[] { ("soft", Tr("소프트 로즈", "Soft rose")), ("tile", Tr("뉴트럴", "Neutral")), ("card", Tr("카드", "Card")), ("black", Tr("검정", "Black")) }, s.IconStyle, v =>
            {
                s.IconStyle = v;
                host.Window.Refresh(true);
            }, filter);
        int favs = Favorites.All().Count(), monos = Favorites.MonoCount;
        Add(Tr("그림 아이콘 만들기", "Make picture icons"), Tr($"지금 즐겨찾기 {favs}개(폴더 안 포함)를 이름에 맞는 그림 + 검정 타일 + 강조색으로 · 하나씩은 Ctrl+K → 아이콘 그림 고르기", $"Gives your {favs} favorites (folders included) a picture that fits the name, on a black tile in the accent · one at a time: Ctrl+K → pick an icon picture"), null, G.Star, favs == 0 ? null : () =>
        {
            host.Engine.MakePictureIcons();
            host.Window.Refresh(true);
            Build(lastFilter);
        }, null, filter);
        Add(Tr("AI로 전부 그리기", "Draw them all with AI"), drawing ?? Tr($"즐겨찾기 {favs}개를 Claude가 하나씩 그려요 — 사용량 {favs}회 · 하나만은 Ctrl+K → AI로 아이콘 그리기", $"Claude draws your {favs} favorites one by one — {favs} uses · one only: Ctrl+K → draw an icon with AI"), null, G.Sparkle, favs == 0 || drawing is not null ? null : async () =>
        {
            drawing = Tr("그리는 중…", "Drawing…");
            Build(lastFilter);
            await host.Engine.DrawAllIcons((d, n, bad) =>
            {
                drawing = d < n ? Tr($"그리는 중… {d}/{n}", $"Drawing… {d}/{n}") + (bad > 0 ? Tr($" · 실패 {bad}", $" · {bad} failed") : "") : null;
                Build(lastFilter);
                host.Window.Refresh(true);
            });
            drawing = null;
            Build(lastFilter);
        }, null, filter);
        if (monos > 0)
            Add(Tr("원래 아이콘으로", "Back to the original icon"), Tr($"그림 아이콘 {monos}개를 앱 아이콘으로 되돌려요", $"Puts the app icons back on {monos} picture icons"), null, G.Refresh, () =>
            {
                Favorites.ClearMono();
                host.Window.Refresh(true);
                Build(lastFilter);
            }, null, filter);
    }

    private void PageHome(Settings s, string filter)
    {
        Page("home", Tr("홈 화면", "Home"), G.Star, Tr("검색줄이 비어 있을 때 보이는 블록 — 켜고 끄기, 끌어서 · ▲▼ · Ctrl+↑↓로 순서 (런처의 ‘홈 편집’에서도)", "The blocks under an empty search line — switch them, drag or ▲▼ / Ctrl+↑↓ to order (also ‘Edit home’ in the launcher)"), filter);
        Header(Tr("블록", "Blocks"));
        foreach (var w in Home.Order().ToList())
        {
            var kind = Home.Kinds.First(k => k.key == w.Key);
            string key = w.Key;
            var hsw = Toggle(w.On, v => Home.Set(key, v), out var hToggle);
            var ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var grip = Theme.Glyph('\uE700', 12, Theme.P.TextFaint);
            grip.VerticalAlignment = VerticalAlignment.Center;
            grip.Margin = new Thickness(0, 0, 8, 0);
            grip.ToolTip = Tr("끌어서 순서 바꾸기", "Drag to reorder");
            ctl.Children.Add(grip);
            ctl.Children.Add(Arrow('\uE70E', Tr("위로 (Ctrl+↑)", "Up (Ctrl+↑)"), () => MoveHome(key, -1)));
            ctl.Children.Add(Arrow('\uE70D', Tr("아래로 (Ctrl+↓)", "Down (Ctrl+↓)"), () => MoveHome(key, 1)));
            hsw.Margin = new Thickness(10, 0, 0, 0);
            ctl.Children.Add(hsw);
            string about = key != "usage" ? kind.about
                : AiUsage.Available ? Tr("5시간 · 주간 사용량 · 켜면 메모리 약 +2 MB", "5-hour and weekly usage · about +2 MB of memory when on")
                : kind.about + Tr(" — Claude Code · Codex가 없어요", " — neither Claude Code nor Codex found");
            // only a row that shows gets its key and the drag (other categories add nothing)
            if (Add(kind.name, about, ctl, kind.glyph, () =>
            {
                if (justDragged) justDragged = false; // the release that ends a drag is not a click
                else hToggle();
            }, null, filter) is not { } homeRow) continue;
            homeKeys[rows.Count - 1] = key;
            if (filter.Length == 0) WireDrag(homeRow, key);
        }
        Header(Tr("AI 사용량", "AI usage"));
        Choice(Tr("새로 받는 간격", "Refresh every"), Tr("받을 때만 창 없이: Claude 약 470 MB 4초 · Codex 약 110 MB 1초", "Hidden, only while asked: Claude ~470 MB for 4 s · Codex ~110 MB for 1 s"), G.Refresh,
            new[] { ("5", Tr("5분", "5 min")), ("10", Tr("10분", "10 min")) }, s.UsageMinutes == 5 ? "5" : "10", v =>
            {
                s.UsageMinutes = v == "5" ? 5 : 10;
                AiUsage.Schedule();
            }, filter);
        Choice(Tr("보여 줄 값", "Show"), Tr("쓴 만큼(32%) 또는 남은 만큼(68% 남음) · 막대도 같이 바뀌어요", "What's used (32%) or what's left (68% left) · the bars follow"), G.Model,
            new[] { ("used", Tr("쓴 만큼", "Used")), ("left", Tr("남은 만큼", "Left")) }, s.UsageLeft ? "left" : "used", v =>
            {
                s.UsageLeft = v == "left";
                host.Window.Refresh(true);
            }, filter);
    }

    private void PageSearch(Settings s, string filter)
    {
        Page("search", Tr("검색", "Search"), G.Search, Tr("빈 검색줄에 입력할 때 앱 말고 무엇을 함께 찾을지", "What else to search besides apps as you type"), filter);
        Header(Tr("함께 찾기", "Also search"));
        var filesSw = Toggle(s.FilesInRoot, v => s.FilesInRoot = v, out var filesT);
        Add(Tr("파일도 함께 찾기", "Files too"), host.Everything.Running ? Tr("Everything 결과를 위에서 5개까지", "Up to 5 Everything results") : Tr("Everything이 꺼져 있어요 — 대신 최근 파일에서 찾아요", "Everything is not running — recent files are searched instead"), filesSw, G.Search, filesT, null, filter);
        var winsSw = Toggle(s.WindowsInRoot, v => s.WindowsInRoot = v, out var winsT);
        Add(Tr("열린 창도 함께 찾기", "Open windows too"), Tr("제목이 맞는 창을 결과 아래에 3개까지 · Enter로 그 창으로", "Up to 3 windows whose title matches, under the results · Enter switches to it"), winsSw, G.NewWindow, winsT, null, filter);
        Header(Tr("학습", "Learning"));
        var learnSw = Toggle(s.LearnQueries, v => s.LearnQueries = v, out var learnT);
        Add(Tr("고른 결과 기억하기", "Remember what I pick"), Tr("같은 말을 다시 적으면 지난번에 고른 걸 맨 위로 (“c” → Chrome) · 이 PC에만 저장", "Typing the same again puts your last pick first (“c” → Chrome) · kept on this PC only"), learnSw, G.Bulb, learnT, null, filter);
        int learned = QueryMemory.Count;
        Add(Tr("학습 기록 지우기", "Forget what was learned"), learned == 0 ? Tr("아직 없어요", "Nothing yet") : Tr($"입력 {learned}개 · Enter로 지우기", $"{learned} typed lines · Enter clears them"), null, G.Delete, learned == 0 ? null : () =>
        {
            QueryMemory.Clear();
            Build(lastFilter);
        }, null, filter);
        Header(Tr("웹 검색", "Web search"));
        Menu(Tr("검색 · 링크를 열 브라우저", "Browser for searches and links"), Tr("이 PC에 설치된 브라우저 중에서 · 웹 검색과 링크를 이 브라우저로 바로 넘겨요", "From the browsers on this PC · web searches and links open in it"), G.Globe,
            Web.Choices.Select(c => (c.value, c.label, (string?)null)).ToArray(), Web.Picked?.Key ?? "default", v => s.WebBrowser = v, filter);
        Choice(Tr("검색 엔진", "Search engine"), null, G.Search, Web.Engines.Select(e => (e.key, e.name)).ToArray(), s.SearchEngine, v => s.SearchEngine = v, filter);
        Header(Tr("이름", "Names"));
        int renamed = AppNames.Count;
        Add(Tr("Bloom에서 바꾼 앱 이름", "App names changed in Bloom"), renamed == 0 ? Tr("앱에서 Ctrl+K → 이름 바꾸기 (Bloom 안에서만, 원래 이름으로도 찾아져요)", "On an app: Ctrl+K → Rename (in Bloom only; the original name still finds it)") : Tr($"{renamed}개 · Enter로 모두 원래 이름으로", $"{renamed} · Enter puts all the original names back"), null, G.Tag, renamed == 0 ? null : () =>
        {
            AppNames.ClearAll();
            host.AppsChanged();
            Build(lastFilter);
        }, null, filter);
    }

    private void PageAi(Settings s, string filter)
    {
        Page("ai", Tr("AI 채팅", "AI chat"), G.Chat, Tr("Tab으로 여는 채팅 · Claude와 ChatGPT는 각자의 CLI 로그인으로 답해요", "The chat Tab opens · Claude and ChatGPT answer with their own CLI logins"), filter);
        Header(Tr("기본", "Default"));
        Choice(Tr("새 대화는", "New chats use"), Tr("대화 중에는 Ctrl+Shift+M으로 바꿔요 · 모델은 Ctrl+M", "Ctrl+Shift+M switches during a chat · Ctrl+M picks the model"), G.Sparkle,
            new[] { ("claude", "Claude"), ("chatgpt", "ChatGPT") }, s.AiProvider == "chatgpt" ? "chatgpt" : "claude", v => s.AiProvider = v, filter);
        Shortcut("chat", Tr("AI 채팅 창 단축키", "AI chat window shortcut"), Tr("어디서든 대화 창을 열어요 · 앞에 있으면 내려가요", "Opens the chat window anywhere · down again when it's in front"), G.Chat, new[] { "ctrl+alt+c", "ctrl+alt+i" }, s.ChatHotkey, v => s.ChatHotkey = v, filter);
        var tabSw = Toggle(s.TabContinuesLastThread, v => s.TabContinuesLastThread = v, out var tabT);
        Add(Tr("Tab으로 지난 대화 이어가기", "Tab continues the last chat"), Tr("30분 안에 쓴 대화를 이어가요 · Shift+Tab은 언제나 새 대화", "Picks up a chat used in the last 30 min · Shift+Tab always starts a new one"), tabSw, G.Chat, tabT, null, filter);

        Header("Claude");
        Choice(Tr("Claude 모델", "Claude model"), Tr("새 대화의 기본값 · 대화 중에는 Ctrl+M", "For new chats · Ctrl+M during a chat"), G.Model,
            AiModels.Claude.Select(m => (m, AiModels.Name(m))).ToArray(), s.AiModel, v => s.AiModel = v, filter);
        Choice(Tr("Claude 생각 수준", "Claude thinking level"), Tr("빠름 = 생각 끔 · Haiku는 없어요 · 대화 중 Ctrl+E", "Fast = thinking off · not for Haiku · Ctrl+E during a chat"), G.Bulb,
            AiModels.ClaudeEfforts.Select(e => (e, AiModels.EffortName(e))).ToArray(), s.AiEffort, v => s.AiEffort = v, filter);
        string? cli = ClaudeSession.FindCli(s.ClaudePath);
        if (cli is not null && Demo.On) cli = Tr("설치됨", "Installed"); // recording mode: no path with the account name
        Add("Claude CLI", cli ?? Tr("찾지 못했어요 — Claude Code를 설치하고 로그인하세요", "Not found — install Claude Code and sign in"), null, G.Info, null, null, filter);

        Header("ChatGPT");
        var gpts = GptCatalog.All;
        string? gptSlug = s.GptModel is { Length: > 0 } g && gpts.Any(m => m.Slug == g) ? g : GptCatalog.DefaultSlug();
        if (gpts.Count > 0)
            Menu(Tr("ChatGPT 모델", "ChatGPT model"), (GptCatalog.Find(gptSlug) is { } cur ? GptCatalog.About(cur) : Tr("Codex 기본 모델", "Codex's default model")) + Tr(" · 대화 중에는 Ctrl+M", " · Ctrl+M during a chat"), G.Model,
                gpts.Select(m => (m.Slug, m.Name, (string?)GptCatalog.About(m))).ToArray(), gptSlug ?? gpts[0].Slug, v =>
                {
                    s.GptModel = v;
                    Dispatcher.BeginInvoke(() => Build(lastFilter)); // the levels and the line under it follow the model
                }, filter);
        else
            Add(Tr("ChatGPT 모델", "ChatGPT model"), Tr("Codex의 모델 목록이 아직 없어요 — Codex를 한 번 실행하면 생겨요", "No Codex model list yet — run Codex once to get it"), null, G.Model, null, null, filter);
        string gptId = gptSlug is null ? "chatgpt" : AiModels.Gpt(gptSlug);
        Choice(Tr("ChatGPT 생각 수준", "ChatGPT thinking level"), Tr("모델마다 단계가 달라요 · 대화 중 Ctrl+E", "The levels depend on the model · Ctrl+E during a chat"), G.Bulb,
            AiModels.Efforts(gptId).Select(e => (e, AiModels.EffortName(e))).ToArray(), AiModels.Fit(gptId, s.GptEffort), v => s.GptEffort = v, filter);
        string? codex = CodexSession.FindCli(s.CodexPath);
        if (codex is not null && Demo.On) codex = Tr("설치됨", "Installed");
        Add("Codex CLI", codex is null ? Tr("찾지 못했어요 — npm i -g @openai/codex 후 codex login", "Not found — npm i -g @openai/codex, then codex login") : codex + Tr(" · ChatGPT 로그인으로 답해요", " · answers with your ChatGPT login"), null, G.Info, null, null, filter);
    }

    private void PageMcp(Settings s, string filter)
    {
        Page("mcp", Tr("PC 작업 · MCP", "PC actions · MCP"), G.Bolt, Tr("대화 중에 AI가 이 PC에서 앱을 열고, 파일을 찾아 옮기고, 타이머를 맞춰요 — Bloom MCP 도구 하나로, 이 PC 안에서만 이어져요", "While you chat, the AI opens apps, finds and moves files and sets timers on this PC — through one Bloom MCP tool, connected inside this PC only"), filter);
        Header(Tr("기본", "Default"));
        var pcSw = Toggle(s.AiTools, v =>
        {
            s.AiTools = v;
            host.Chats.EndAll(); // the next message starts its session with (or without) the tools
        }, out var pcT);
        Add(Tr("Claude 대화에서", "In Claude chats"), Tr("Claude Code가 먼저 묻고, Bloom이 채팅에 허락 카드를 띄워요 · 다음 메시지부터", "Claude Code asks first, Bloom shows the card in the chat · from the next message"), pcSw, G.Bolt, pcT, null, filter);
        var gptSw = Toggle(s.AiToolsGpt, v => s.AiToolsGpt = v, out var gptT);
        Add(Tr("ChatGPT 대화에서", "In ChatGPT chats"), Tr("Codex는 스스로 묻지 않아서, Bloom이 실행 직전에 채팅에서 물어봐요 · 다음 메시지부터", "Codex doesn't ask on its own, so Bloom asks in the chat right before acting · from the next message"), gptSw, G.Bolt, gptT, null, filter);
        Header(Tr("할 수 있는 일", "What it may do"));
        void Group(string key, string title, string sub, char glyph)
        {
            var sw = Toggle(App.Mcp.BloomTools.GroupOn(key), v =>
            {
                if (v) s.AiToolsOff.Remove(key);
                else if (!s.AiToolsOff.Contains(key)) s.AiToolsOff.Add(key);
                host.Chats.EndAll();
            }, out var t);
            Add(title, sub, sw, glyph, t, null, filter);
        }
        Group("look", Tr("찾아보기", "Look up"), Tr("앱 · 파일 · 최근 파일 · 열린 창 · 타이머 · 내 명령 — 묻지 않고 바로 찾아요", "Apps, files, recent files, open windows, timers, your commands — looked up without asking"), G.Search);
        Group("open", Tr("열기 · 실행", "Open · run"), Tr("앱 · 파일 · 사이트 열기, ‘10분 타이머’ ‘볼륨 30’ 같은 Bloom 명령, 내 명령 — 하기 전에 물어봐요", "Open apps, files and sites; Bloom lines like ‘10 min timer’ or ‘volume 30’; your commands — asks first"), G.App);
        Group("bloom", Tr("메모 · 타이머 · 일정", "Notes · timers · calendar"), Tr("Bloom 메모에 적기 · 타이머 · 캘린더 일정 — 하기 전에 물어봐요 · 적은 메모는 채팅 옆에 떠요", "Write in Bloom's notes, start its timers, put events on its calendar — asks first · the note shows beside the chat"), G.Document);
        Group("files", Tr("파일 정리", "Files"), Tr("이름 바꾸기 · 옮기기 · 복사 · 휴지통 — 하기 전에 물어봐요 · 휴지통에서 되살릴 수 있어요", "Rename, move, copy, recycle — asks first · the Recycle Bin brings things back"), G.Folder);
        Group("clip", Tr("클립보드", "Clipboard"), Tr("클립보드 읽기 · 넣기 — 하기 전에 물어봐요", "Read and write the clipboard — asks first"), G.Paste);
        Header(Tr("이 PC의 다른 MCP 서버", "Other MCP servers on this PC"));
        // read where each program keeps them (Claude Code, Claude Desktop, Cursor, VS Code, Windsurf, Codex, files
        // brought in), once per opening of this page
        mcpFound ??= McpCatalog.Found(s.McpFiles);
        if (mcpFound.Count == 0)
            Add(Tr("찾은 서버가 없어요", "No servers found"), Tr("Claude Code · Claude Desktop · Cursor · VS Code · Windsurf · Codex에 등록한 MCP가 여기 나와요", "MCP servers set up in Claude Code, Claude Desktop, Cursor, VS Code, Windsurf or Codex show here"), null, G.Plug, null, null, filter);
        foreach (var d in mcpFound)
        {
            string key = d.Key;
            var sw = Toggle(s.McpOn.Contains(key), v =>
            {
                if (v) s.McpOn.Add(key);
                else s.McpOn.Remove(key);
                host.Chats.EndAll(); // the next message starts with (or without) it
            }, out var t);
            string where = McpCatalog.Label(d.Source) + (d.Stdio ? Tr(" · 이 PC에서 실행", " · runs on this PC") : Tr(" · 웹 주소 · ChatGPT에서는 못 써요", " · a web address · not in ChatGPT"));
            Add(d.Name, where, sw, G.Plug, t, null, filter);
        }
        Add(Tr("MCP 설정 파일 가져오기…", "Bring in an MCP settings file…"), Tr("mcpServers가 적힌 .json (예: 프로젝트의 .mcp.json) — 위에서 못 찾은 서버가 있을 때", "A .json with mcpServers (like a project's .mcp.json) — for servers not found above"), null, G.Import, () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = Tr("MCP 설정 (*.json)|*.json", "MCP settings (*.json)|*.json") };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            if (!s.McpFiles.Contains(dlg.FileName, StringComparer.OrdinalIgnoreCase)) s.McpFiles.Add(dlg.FileName);
            Settings.Save();
            mcpFound = null;
            Build(lastFilter);
        }, null, filter);
        Add(Tr("켜기 전에", "Before you switch one on"), Tr("대화를 시작할 때 함께 켜져서 첫 답이 조금 늦어지고, 도구 설명만큼 사용량이 늘어요 · 도구를 쓸 때마다 채팅에서 물어봐요", "It starts with each chat (a slower first answer) and its tool list adds usage · every tool call asks in the chat"), null, G.Info, () =>
        {
            mcpFound = null; // Enter reads the list again
            Build(lastFilter);
        }, null, filter);

        Header(Tr("연결", "Connection"));
        Add(Tr("연결 방식", "How it connects"), Tr("Claude Code에 등록하지 않아요 · Bloom 채팅에만 붙고 이 PC 안에서만 이어져요", "Not registered in Claude Code · only Bloom's chats get it, inside this PC only"), null, G.Shield, null, null, filter);
        Add(Tr("쓰는 양", "What it costs"), Tr("요청마다 Claude 약 +400 · ChatGPT 약 +190 토큰 (켠 일이 적을수록 줄어요) · 쓰는 동안 메모리 약 10 MB", "Per request: Claude about +400, ChatGPT about +190 tokens (less with fewer kinds on) · about 10 MB of memory while in use"), null, G.Speed, null, null, filter);
    }

    private void PageFeatures(Settings s, string filter)
    {
        Page("features", Tr("기능", "Features"), G.Sliders, Tr("안 쓰는 기능은 꺼 두세요 — 검색 결과 · 명령 목록 · 홈 · 즐겨찾기 줄에서 빠지고 단축키도 풀려요. 저장된 것은 지우지 않아요", "Switch off what you don't use — it leaves the results, the command list, the home and the favorites row, and lets go of its shortcut. Nothing saved is deleted"), filter);
        foreach (var (group, title) in new[] { ("ai", "AI"), ("search", Tr("검색줄에서", "In the search line")), ("tools", Tr("창 · 도구", "Windows · tools")) })
        {
            Header(title);
            foreach (var f in Features.All.Where(f => f.group == group))
            {
                string key = f.key;
                var sw = Toggle(Features.On(key), v =>
                {
                    Features.Set(key, v);
                    host.ApplyHotkeys();
                }, out var t);
                Add(f.name, f.about, sw, f.glyph, t, null, filter);
            }
        }
        Header(Tr("백그라운드에서 도는 기능", "Running in the background"));
        var clSw = Toggle(s.ClipHistory, v => { s.ClipHistory = v; host.ApplyHotkeys(); }, out var clT);
        Add(Tr("클립보드 기록", "Clipboard history"), Tr("복사한 글 · 그림 · 파일을 모아 둬요 · 메모리 약 +2 MB부터", "Keeps what you copy · from about +2 MB of memory"), clSw, G.Paste, clT, null, filter);
        var noSw = Toggle(s.Notices, v =>
        {
            s.Notices = v;
            Notices.Apply();
        }, out var noT);
        Add(Tr("작업 알림", "Work notifications"), Tr("Claude Code · Codex 작업과 AI 한도를 카드로 알려요 · 메모리 약 +1 MB", "Claude Code and Codex work and AI limits as cards · about +1 MB of memory"), noSw, G.Bell, noT, null, filter);
    }

    private void PageClipboard(Settings s, string filter)
    {
        Page("clipboard", Tr("클립보드", "Clipboard"), G.Paste, Tr("복사한 글·그림·파일을 모아 두고 다시 붙여 넣어요", "Keeps the text, pictures and files you copy, to paste again"), filter);
        Header(Tr("기록", "History"));
        var clip = host.Clipboard;
        var clSw = Toggle(s.ClipHistory, v => { s.ClipHistory = v; host.ApplyHotkeys(); }, out var clT);
        Add(Tr("클립보드 기록", "Clipboard history"), s.ClipHistory ? Tr($"켜짐 · {clip.Items.Count}개 · 메모리 약 +2 MB부터, 그림이 많을수록 늘어요 · 비밀번호 관리자가 숨긴 복사는 남기지 않아요", $"On · {clip.Items.Count} · from about +2 MB of memory, more with pictures · copies that password managers hide are never kept") : Tr("꺼짐 — 복사한 것을 모으지 않아요 · 켜면 메모리 약 +2 MB부터", "Off — nothing you copy is kept · on: from about +2 MB"), clSw, G.Paste, clT, null, filter);
        Choice(Tr("보관 기간", "Keep for"), Tr("고정한 항목은 기간과 상관없이 남아요 · 최대 500개", "Pinned entries always stay · 500 at most"), G.History,
            new[] { ("1", Tr("하루", "1 day")), ("7", Tr("7일", "7 days")), ("30", Tr("30일", "30 days")), ("90", Tr("90일", "90 days")) }, s.ClipKeepDays.ToString(), v => s.ClipKeepDays = int.Parse(v), filter);
        Shortcut("clipboard", Tr("단축키", "Shortcut"), Tr("누르면 클립보드 기록이 바로 열려요", "Opens the clipboard history right away"), G.Keyboard, new[] { "ctrl+alt+v", "ctrl+shift+v" }, s.ClipHotkey, v => s.ClipHotkey = v, filter);
        Header(Tr("정리", "Clean up"));
        Add(Tr("기록 모두 지우기", "Clear the history"), Tr("고정한 항목은 남겨요", "Pinned entries stay"), null, G.Delete, () =>
        {
            clip.Clear();
            Build(lastFilter);
        }, null, filter);
    }

    private void PageLens(Settings s, string filter)
    {
        Page("screen", "Bloom Lens", G.Page, Tr("화면 영역을 고른 뒤 할 일을 골라요 — AI에게 묻기 · 번역 · 글자 복사 · 실시간 번역 (1~4)", "Pick a screen area, then what to do — ask AI, translate, copy text, live translation (1–4)"), filter);
        Header(Tr("단축키", "Shortcut"));
        Shortcut("lens", "Bloom Lens", Tr("영역을 고르면 할 일 메뉴가 떠요", "After the area: a menu of what to do"), G.Page, new[] { "ctrl+alt+l", "ctrl+shift+l" }, s.LensHotkey, v => s.LensHotkey = v, filter);
        Shortcut("ask", Tr("바로 AI에게 묻기", "Ask AI right away"), Tr("영역을 고르면 그 화면을 붙인 채팅이 열려요 (묻기마다 사용량 1회)", "After the area: a chat with that picture attached (1 use per question)"), G.Sparkle, new[] { "ctrl+alt+q", "ctrl+shift+q" }, s.AskHotkey, v => s.AskHotkey = v, filter);
        Shortcut("ocr", Tr("바로 글자 복사", "Copy text right away"), Tr("영역을 고르면 글자를 읽어 복사해요 (PC 안에서만, 사용량 없음)", "After the area: reads and copies the text (on this PC, no usage)"), G.Copy, new[] { "ctrl+alt+o", "ctrl+shift+o" }, s.OcrHotkey, v => s.OcrHotkey = v, filter);
        Shortcut("translate", Tr("바로 번역", "Translate right away"), Tr("영역을 고르면 읽어서 번역해요 (번역마다 사용량 1회)", "After the area: reads and translates it (1 use each)"), G.Globe, new[] { "ctrl+alt+t", "ctrl+shift+t" }, s.TranslateHotkey, v => s.TranslateHotkey = v, filter);
        Shortcut("live", Tr("바로 실시간 번역", "Live translation right away"), Tr("영역을 계속 읽어 바뀔 때만 번역해요 (사용량 계속)", "Keeps reading the area and translates when it changes (ongoing usage)"), G.Refresh, new[] { "ctrl+alt+r", "ctrl+shift+r" }, s.LiveHotkey, v => s.LiveHotkey = v, filter);
        Header(Tr("글자 읽기", "Reading text"));
        var ocCopy = Toggle(s.OcrCopy, v => s.OcrCopy = v, out var ocT);
        Add(Tr("읽은 글자 바로 복사", "Copy the text at once"), Tr("끄면 런처에서 고른 뒤에 복사해요", "Off: copy it from the launcher"), ocCopy, G.Copy, ocT, null, filter);
        var langs = Ocr.WinOcr.Languages();
        Choice(Tr("인식 언어", "Recognition language"), langs.Count == 0 ? Tr("이 PC에 OCR 언어가 없어요 — Windows 설정 > 언어에서 추가", "No OCR languages on this PC — add one in Windows Settings > Language") : Tr($"설치된 인식기: {string.Join(", ", langs)}", $"Installed: {string.Join(", ", langs)}"), G.Keyboard,
            new[] { ("auto", Tr("자동", "Auto")) }.Concat(langs.Select(l => (l, LangName(l)))).ToArray(), s.OcrLanguage, v => s.OcrLanguage = v, filter);
        Header(Tr("번역 · AI 빠른 동작", "Translation · AI actions"));
        string tp = s.TranslateProvider == "chatgpt" ? "chatgpt" : "claude";
        Choice(Tr("번역 엔진", "Translation engine"), Tr("AI 빠른 동작도 이 엔진을 써요 · 번역 창에서도 바꿀 수 있어요", "AI actions use this engine too · also switchable in the translation window"), G.Sparkle,
            new[] { ("claude", "Claude"), ("chatgpt", "ChatGPT") }, tp, v => { s.TranslateProvider = v; Dispatcher.BeginInvoke(() => Build(lastFilter)); }, filter);
        if (tp == "claude")
            Choice(Tr("번역용 Claude 모델", "Claude model for translation"), Tr("Haiku가 가장 빠르고 가벼워요 (Haiku는 생각 수준이 없어요)", "Haiku is the fastest and lightest (it has no thinking levels)"), G.Model,
                AiModels.Claude.Select(m => (m, AiModels.Name(m))).ToArray(), Translator.Model("claude"), v => { s.TranslateClaudeModel = v; Dispatcher.BeginInvoke(() => Build(lastFilter)); }, filter);
        else if (GptCatalog.All.Count > 0)
            Menu(Tr("번역용 ChatGPT 모델", "ChatGPT model for translation"), Tr("가벼운 모델일수록 빠르고 사용량이 적어요", "Lighter models are faster and use less"), G.Model,
                GptCatalog.All.Select(m => (m.Slug, m.Name, (string?)GptCatalog.About(m))).ToArray(), AiModels.GptSlug(Translator.Model("chatgpt")) ?? GptCatalog.All[0].Slug, v => { s.TranslateGptModel = v; Dispatcher.BeginInvoke(() => Build(lastFilter)); }, filter);
        var tLevels = Translator.Levels(tp);
        if (tLevels.Length > 0)
            Choice(Tr("번역 생각 수준", "Thinking level for translation"), Tr("낮을수록 빠르고 사용량이 적어요 · 번역 창의 칩으로도 바꿔요", "Lower is faster and uses less · also a chip in the translation window"), G.Bulb,
                tLevels.Select(e => (e, AiModels.EffortName(e))).ToArray(), Translator.Effort(tp)!, v => Translator.SetEffort(tp, v), filter);
        Choice(Tr("번역할 언어", "Translate into"), Tr("자동: 한국어 글은 영어로, 나머지는 한국어로", "Auto: English text into Korean, everything else into English"), G.Globe, Translator.Targets, s.TranslateTo, v => s.TranslateTo = v, filter);
        Choice(Tr("실시간 번역 간격", "Live translation interval"), Tr("화면을 다시 읽는 간격 (읽기는 사용량 없음 · 글이 바뀌고 멈췄을 때만 번역)", "How often the screen is read again (reading uses nothing · it translates only when the text changed and settled)"), G.History,
            new[] { ("1", Tr("1초", "1 s")), ("1.5", Tr("1.5초", "1.5 s")), ("3", Tr("3초", "3 s")), ("5", Tr("5초", "5 s")) }, s.LiveSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), v => s.LiveSeconds = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture), filter);
    }

    private void PageTools(Settings s, string filter)
    {
        Page("tools", Tr("도구", "Tools"), G.Stopwatch, Tr("창 전환 · 이모지 · AI 빠른 동작 · 타이머 · 컬러 피커의 단축키와 설정", "Shortcuts and options for switching windows, emoji, AI actions, timers and the colour picker"), filter);
        Header(Tr("창 · 이모지 · AI 빠른 동작", "Windows · emoji · AI actions"));
        Shortcut("windows", Tr("창 전환 단축키", "Switch windows shortcut"), Tr("열린 창 목록이 바로 떠요", "Opens the list of open windows"), G.NewWindow, new[] { "ctrl+alt+w", "ctrl+alt+s" }, s.WindowsHotkey, v => s.WindowsHotkey = v, filter);
        Shortcut("emoji", Tr("이모지 · 기호 단축키", "Emoji & symbols shortcut"), Tr("찾아서 Enter면 앞의 창에 붙여 넣어요", "Find one, Enter pastes it into the window in front"), '\uE76E', new[] { "ctrl+alt+e", "ctrl+shift+e" }, s.EmojiHotkey, v => s.EmojiHotkey = v, filter);
        Shortcut("quick", Tr("AI 빠른 동작 단축키", "AI actions shortcut"), Tr("아무 앱에서 글을 선택하고 누르면 요약 · 번역 · 다듬기 (동작마다 사용량 1회 · 엔진은 Bloom Lens > 번역)", "Select text in any app and press it: summarize, translate, polish (1 use per action · engine: Bloom Lens > Translation)"), G.Sparkle, new[] { "ctrl+alt+a", "ctrl+shift+a" }, s.QuickHotkey, v => s.QuickHotkey = v, filter);
        Header(Tr("도구 창", "Tool windows"));
        foreach (var (key, name, glyph) in ToolDock.Kinds)
        {
            string k = key;
            Add(name, Tr("런처 옆에 붙어서 같이 보였다 숨어요 · 떼어 내면 따로 떠 있어요 · Enter로 열기", "Sits beside the launcher and comes and goes with it · detached, it stays on its own · Enter opens it"), null, glyph, () => host.Tools.Open(k), null, filter);
        }
        Header(Tr("타이머 · 알림", "Timers · reminders"));
        var tSound = Toggle(s.TimerSound, v => s.TimerSound = v, out var tSoundT);
        Add(Tr("소리로도 알리기", "Play a sound too"), Tr("끝나면 오른쪽 아래 카드와 함께 Windows 알람 소리 (30초까지)", "With the card at the bottom right, Windows' alarm sound (up to 30 s)"), tSound, G.Stopwatch, tSoundT, null, filter);
        Add(Tr("타이머 · 알림 열기", "Open timers and reminders"), Timers.Current.Items.Count > 0 ? Tr($"진행 중 {Timers.Current.Items.Count}개", $"{Timers.Current.Items.Count} running") : Tr("진행 중인 것 없음", "Nothing running"), null, G.Stopwatch, () => host.OpenView(App.Mode.Timers), null, filter);
        Header(Tr("컬러 피커", "Color picker"));
        Shortcut("color", Tr("컬러 피커 단축키", "Color picker shortcut"), Tr("어디서든 화면 색 찍기", "Pick a colour anywhere"), G.Eyedrop, new[] { "ctrl+alt+p", "ctrl+shift+p" }, s.ColorHotkey, v => s.ColorHotkey = v, filter);
        var cCopy = Toggle(s.ColorCopy, v => s.ColorCopy = v, out var cCopyT);
        Add(Tr("찍은 색 바로 복사", "Copy the colour at once"), Tr("HEX(#ff5a8a)로 복사해요", "Copies it as HEX (#ff5a8a)"), cCopy, G.Copy, cCopyT, null, filter);
        Add(Tr("최근 색 지우기", "Clear recent colours"), Tr($"{ColorBook.Current.Recent.Count}개", $"{ColorBook.Current.Recent.Count}"), null, G.Delete, () =>
        {
            ColorBook.Clear();
            Build(lastFilter);
        }, null, filter);
    }

    private void PageQuicklinks(Settings s, string filter)
    {
        Page("quicklinks", Tr("브라우저", "Browser"), G.Link, Tr("사이트 바로 가기 — 단축어 + 스페이스 + 검색어로 바로 검색해요 (예: yt 고양이)", "Site shortcuts — keyword + space + words searches right away (e.g. yt cats)"), filter);
        Header("");
        Add(Tr("바로 가기 관리", "Manage shortcuts"), Tr($"{Quicklinks.Current.Items.Count}개 · Enter로 런처에서 열기", $"{Quicklinks.Current.Items.Count} · Enter opens them in the launcher"), null, G.Link, () => host.OpenView(App.Mode.Quicklinks), null, filter);
    }

    private void PageConnections(Settings s, string filter)
    {
        Page("connections", Tr("연결", "Connections"), G.Plug, Tr("Bloom이 함께 쓰는 프로그램", "Programs Bloom works with"), filter);
        Header("");
        bool ev = host.Everything.Running;
        Add("Everything", ev ? Tr("실행 중", "Running") : Tr("꺼져 있어요 — Enter로 실행", "Not running — Enter starts it"), Status(ev), G.Search, ev ? null : () =>
        {
            if (EverythingClient.ExePath() is { } exe) Launch.Open(exe, "-startup");
        }, null, filter);
    }

    private void PageNotices(Settings s, string filter)
    {
        Page("notices", Tr("알림", "Notifications"), G.Bell, Tr("Claude Code · Codex 작업과 AI 한도를 화면 구석에 카드로 알려요 — 세션 기록만 읽고, 전체 화면 게임 · 발표 중에는 띄우지 않아요", "Claude Code and Codex work and AI limits as cards in a screen corner — it only reads their session logs, and stays quiet over full-screen games and presentations"), filter);
        Header(Tr("알림", "Notifications"));
        var on = Toggle(s.Notices, v =>
        {
            s.Notices = v;
            Notices.Apply();
        }, out var onT);
        Add(Tr("작업 알림", "Work notifications"), s.Notices ? Tr("켜짐 · 메모리 약 +1 MB · 알릴 일은 아래에서 골라요", "On · about +1 MB of memory · pick what to tell below") : Tr("꺼짐 — 알림도 소리도 없어요 · 켜면 메모리 약 +1 MB", "Off — no cards, no sounds · on: about +1 MB"), on, G.Bell, onT, null, filter);
        Menu(Tr("위치", "Position"), Tr("고르면 미리 보기 카드가 그 자리에 떠요", "A preview card shows there when you pick"), G.Screen, new (string, string, string?)[]
        {
            ("top-left", Tr("왼쪽 위", "Top left"), null), ("top-center", Tr("가운데 위", "Top center"), null), ("top-right", Tr("오른쪽 위", "Top right"), null),
            ("bottom-left", Tr("왼쪽 아래", "Bottom left"), null), ("bottom-center", Tr("가운데 아래", "Bottom center"), null), ("bottom-right", Tr("오른쪽 아래", "Bottom right"), null),
        }, s.NoticeSpot, v =>
        {
            s.NoticeSpot = v;
            Notices.Preview(one: true);
        }, filter);
        Choice(Tr("크기", "Size"), null, G.Sliders, new[] { ("s", Tr("작게", "Small")), ("m", Tr("보통", "Medium")), ("l", Tr("크게", "Large")), ("xl", Tr("아주 크게", "X-large")) }, s.NoticeSize, v =>
        {
            s.NoticeSize = v;
            Notices.Preview(one: true);
        }, filter);
        Choice(Tr("보이는 시간", "Shown for"), Tr("마우스를 올리면 기다려요 · 승인 대기는 끝날 때까지", "Waits while the mouse is on it · 승인 대기 stays until it ends"), G.Stopwatch,
            new[] { ("4", Tr("4초", "4 s")), ("6", Tr("6초", "6 s")), ("10", Tr("10초", "10 s")), ("20", Tr("20초", "20 s")) }, s.NoticeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), v => s.NoticeSeconds = int.Parse(v, System.Globalization.CultureInfo.InvariantCulture), filter);
        Choice(Tr("효과음", "Sound"), Tr("알림마다 다른 짧은 소리", "A short sound of its own for each kind"), G.Volume, new[] { ("off", Tr("끔", "Off")), ("s", Tr("작게", "Low")), ("m", Tr("보통", "Medium")), ("l", Tr("크게", "Loud")) }, s.NoticeSound, v =>
        {
            s.NoticeSound = v;
            Chimes.Play("done");
        }, filter);
        Add(Tr("미리 보기", "Preview"), Tr("켜 둔 알림을 지금 띄워 봐요", "Shows the kinds that are on, now"), null, G.Bell, () => Notices.Preview(), null, filter);
        Header(Tr("알릴 일", "What to tell"));
        void Kind(string title, string sub, char glyph, bool value, Action<bool> set)
        {
            var sw = Toggle(value, set, out var t);
            Add(title, sub, sw, glyph, t, null, filter);
        }
        Kind(Tr("진행 중", "Working"), Tr("작업이 시작되면 무엇을 하는지 (4초 넘게 걸릴 때)", "What a turn works on once it runs past 4 s"), G.Gears, s.NoticeStart, v => s.NoticeStart = v);
        Kind(Tr("작업 완료", "Done"), Tr("끝나면 걸린 시간과 함께 (10초 넘게 걸린 작업)", "When it ends, with how long it took (past 10 s)"), G.Done, s.NoticeDone, v => s.NoticeDone = v);
        Kind(Tr("승인 대기", "Needs approval"), Tr("허락이나 답을 기다리면, 끝날 때까지 떠 있어요", "When it waits for you; stays until it goes on"), G.Clock, s.NoticeWaiting, v => s.NoticeWaiting = v);
        Kind(Tr("한도 임박", "Limit near"), Tr("5시간 · 주간 한도가 20% 남았을 때와 다 썼을 때", "At 20% left of the 5-hour or weekly limit, and when used up"), G.Warning, s.NoticeLimit, v => s.NoticeLimit = v);
        Kind(Tr("한도 복구", "Limit back"), Tr("모자랐던 한도가 초기화되어 다시 찼을 때", "When a low limit is full again after its reset"), G.Sync, s.NoticeRecover, v => s.NoticeRecover = v);
    }

    private void PagePerformance(Settings s, string filter)
    {
        Page("performance", Tr("성능", "Performance"), G.Speed, Tr("움직임과 유리 효과를 줄여 가볍게 써요", "Less motion and glass for a lighter Bloom"), filter);
        Header(Tr("애니메이션", "Animation"));
        Choice(Tr("애니메이션", "Animation"), Tr("줄이기: 나타나고 사라지기만 · 끄기: 움직임 없음", "Reduced: fades only · Off: nothing moves"), G.Bolt,
            new[] { ("system", Tr("시스템", "System")), ("on", Tr("켜기", "On")), ("reduced", Tr("줄이기", "Reduced")), ("off", Tr("끄기", "Off")) }, s.Animation, v => s.Animation = v, filter);
        Choice(Tr("초당 애니메이션 횟수", "Animation frames per second"), Tr("낮출수록 GPU · CPU를 덜 써요", "Lower uses less GPU and CPU"), G.Speed,
            new[] { ("30", Tr("30회", "30")), ("60", Tr("60회", "60")), ("0", Tr("최대", "Max")) }, s.AnimationFps is 30 or 60 ? s.AnimationFps.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0", v => s.AnimationFps = int.Parse(v, System.Globalization.CultureInfo.InvariantCulture), filter);
        Header(Tr("화면", "Display"));
        Choice(Tr("유리 효과", "Glass effect"), Tr("끄면 단색 배경으로 GPU를 덜 써요", "Off: a solid background, lighter on the GPU"), G.Palette,
            new[] { ("system", Tr("시스템", "System")), ("off", Tr("끄기", "Off")) }, s.Transparency, v =>
            {
                s.Transparency = v;
                Theme.Apply(s.Theme);
            }, filter);
        Header(Tr("메모리", "Memory"));
        using var me = System.Diagnostics.Process.GetCurrentProcess();
        Add(Tr("지금 Bloom이 쓰는 메모리", "Memory Bloom uses now"), Tr($"{me.PrivateMemorySize64 / 1048576.0:0} MB · 모두 끈 기준 약 80 MB, 켤 때 늘어나는 양은 각 설정에 적혀 있어요", $"{me.PrivateMemorySize64 / 1048576.0:0} MB · about 80 MB with everything off; each setting says what it adds"), null, G.Speed, () => Build(lastFilter), null, filter);
    }

    private void PageAbout(Settings s, string filter)
    {
        Page("about", Tr("정보", "About"), G.Info, Tr("데이터와 버전", "Data and version"), filter);
        Header(Tr("데이터", "Data"));
        Add(Tr("데이터 폴더 열기", "Open the data folder"), Demo.On ? Tr("녹화용 데이터 폴더", "The recording's data folder") : Paths.Data, null, G.Folder, () => Launch.Open(Paths.Data), null, filter);
        Add(Tr("설정 내보내기", "Export settings"), Tr("설정 · 즐겨찾기 · 앱 이름 · 브라우저 바로 가기 · 최근 색을 파일 하나로", "Settings, favorites, app names, browser shortcuts and recent colours in one file"), null, G.Send, () => host.ExportSettings(Window.GetWindow(this)), null, filter);
        Add(Tr("Bloom 초기화", "Reset Bloom"), Tr("모든 설정과 기록을 지우고 처음 설정 화면부터 다시 · 지우기 전에 내보낼지 물어봐요", "Deletes all settings and history and starts from the first-run screens · asks to export first"), null, G.Delete, () => host.ResetAll(Window.GetWindow(this)), null, filter);
        Add(Tr("설정 불러오기", "Import settings"), Tr("내보낸 파일에서 · 지금 설정은 데이터 폴더의 backups에 남겨요", "From an exported file · your current settings stay in the data folder's backups"), null, G.Import, () => host.ImportSettings(Window.GetWindow(this)), null, filter);
        Header(Tr("버전", "Version"));
        string ver = typeof(SettingsView).Assembly.GetName().Version?.ToString(3) ?? "";
        Add($"Bloom {ver}", Tr("가벼운 네이티브(WPF) 런처", "A light native (WPF) launcher"), null, G.Info, null, null, filter);
        Add(Tr("Bloom 둘러보기", "Bloom tour"), Tr("처음 설정과 사용법 안내를 다시 보기", "See the first-run setup and the guide again"), null, G.Info, () => host.ShowOnboarding(), null, filter);
    }
}
