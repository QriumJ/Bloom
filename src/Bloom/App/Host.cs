using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.App;

internal sealed partial class Host
{
    public readonly Options Opt;
    public AppIndex Apps = null!;
    public EverythingClient Everything = null!;
    public Clip.ClipboardHistory Clipboard = null!;
    public readonly Processes Processes = new();
    public Hotkeys Shortcuts = null!;
    public Ocr.OcrOutput? LastOcr;
    private bool ocrBusy;
    private DispatcherTimer? procTick;
    public Chats Chats = null!;
    public App.Mcp.McpServer Mcp = null!;
    public Engine Engine = null!;
    public Launcher Window = null!;
    public ToolDock Tools = null!;
    private SettingsWindow? settingsWindow;
    private ChatWindow? chatWindow;

    /// The AI 채팅 window while it shows (else zero): what a tool the AI brings up floats beside.
    public IntPtr ChatWindowHandle => chatWindow is { IsVisible: true } c ? new System.Windows.Interop.WindowInteropHelper(c).Handle : IntPtr.Zero;
    private WinKeyHook? winKey;
    private Tray? tray;
    private Dispatcher ui = null!;
    private DispatcherTimer? dataTick;
    private DispatcherTimer? alarmTick;
    private bool colorBusy;
    private bool dataDirty;
    private int dataQueued;

    public Host(Options opt) => Opt = opt;

    public void Start()
    {
        ui = Application.Current.Dispatcher;
        Demo.On = Opt.Demo; // before anything reads the PC
        if (Opt.Lang is { } lang) Lang.Init(lang); // before the settings, which would fix it otherwise
        Settings.Load();
        Usage.Load();
        QueryMemory.Load();
        Favorites.Load();
        AppNames.Load();
        Quicklinks.Load();
        UserCommands.Load();
        Rates.Load();
        Timers.Load();
        Notes.Load();
        ColorBook.Load();
        Theme.InitScale();
        Theme.Apply(Settings.Current.Theme);

        Apps = new AppIndex();
        Everything = new EverythingClient();
        Chats = new Chats(ui);
        var tools = new App.Mcp.BloomTools(this, ui);
        Mcp = new App.Mcp.McpServer(tools);
        // Bloom's MCP server for the chats (설정 > PC 작업 · MCP): Claude Code asks the user itself; for Codex runs the
        // server asks in the chat (Approve)
        ClaudeSession.PcTools = () =>
        {
            if (!App.Mcp.BloomTools.OfferedTo("claude")) return null;
            Mcp.Start();
            return (Mcp.Server, App.Mcp.BloomTools.Doing);
        };
        ClaudeSession.ExtraMcp = McpCatalog.Picked;
        CodexSession.ExtraMcp = McpCatalog.Picked;
        CodexSession.Relay = () =>
        {
            Mcp.Start();
            return (App.Mcp.McpServer.Command, Mcp.Pipe);
        };
        CodexSession.PcTools = () =>
        {
            if (!App.Mcp.BloomTools.OfferedTo("chatgpt")) return null;
            Mcp.Start();
            return (App.Mcp.McpServer.Command, Mcp.Pipe, tools.Hint);
        };
        tools.Approve = (conversation, tool, input) => ui.InvokeAsync(() => Chats.AskInConversation(conversation, tool, input)).Task.Unwrap();
        Chats.Tools = tools;
        Engine = new Engine(this);
        Clipboard = new Clip.ClipboardHistory(ui);
        Icons.Start(ui);
        Window = new Launcher(this) { Capturing = Opt.CaptureDir is not null };
        Tools = new ToolDock(this);
        if (Opt.ChatFile is not null)
        {
            _ = Captures.Chat(this, Opt.ChatFile, Opt.QaModel ?? "haiku", Opt.QaEffort, Opt.QaAsk?.Split('|'));
            return;
        }
        if (Opt.SpaceDir is not null)
        {
            _ = Captures.Space(this, Opt.SpaceDir, Opt.SpacePath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            return;
        }
        if (Opt.OcrFile is not null)
        {
            _ = Captures.OcrTest(this, Opt.OcrFile, Opt.QaTranslate);
            return;
        }
        if (Opt.PasteFile is not null)
        {
            _ = Captures.Paste(this, Opt.PasteFile, Opt.Display ?? 2);
            return;
        }
        if (Opt.GlassFile is not null)
        {
            Apps.Start();
            Window.Capturing = false;
            _ = Captures.Glass(this, Opt.GlassFile, Opt.Display ?? 2);
            return;
        }

        if (Opt.Demo) BeginDemo();
        Chats.Updated += t => Window.ChatChanged(t);
        Chats.SessionsChanged += () => Window.SessionsChanged();
        // a tool call waiting while no Bloom window is in front: a card says so
        Chats.AskWaiting += (_, ask) =>
        {
            if (!Window.IsActive && chatWindow?.IsActive != true) Notices.Ask(ask.Says);
        };
        Rates.Updated += () => Later();
        Clipboard.Changed += () => Later();
        Quicklinks.Changed += () => Later();
        UserCommands.Changed += () => Later();
        Apps.Changed += () => ui.BeginInvoke(Later);
        Timers.Changed += Later;
        Timers.Changed += SyncAlarm;
        AiUsage.Updated += Later;
        // bursts of change events become one refresh, 300 ms after the first; the timer sleeps in between
        dataTick = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(300) };
        dataTick.Tick += (_, _) =>
        {
            dataTick.Stop();
            Interlocked.Exchange(ref dataQueued, 0);
            if (!dataDirty) return;
            dataDirty = false;
            Window.DataChanged();
        };

        Apps.Start();
        if (Opt.CaptureDir is null)
        {
            // timers and reminders: due ones ring (also those that came due while Bloom was off), the countdowns tick
            alarmTick = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromSeconds(1) };
            alarmTick.Tick += (_, _) =>
            {
                foreach (var e in Timers.TakeDue()) Ring(e);
                Window.TickLive();
            };
            SyncAlarm();
            AiUsage.Start(ui);
            Notices.Quiet = Opt.MeasureSeconds is not null;
            if (!Opt.Demo) Notices.Start(ui); // recording mode: no cards from the real Claude Code and Codex
            if (Opt.MeasureSeconds is { } measure) MeasureLater(measure);
            if (!Opt.Demo) Clipboard.Start(); // recording mode: the PC's clipboard stays out, the samples stay in
            Shortcuts = new Hotkeys();
            ApplyHotkeys();
            if (!Opt.NoTray)
            {
                tray = new Tray();
                tray.Toggle += () => Window.Summon();
                tray.OpenSettings += OpenSettings;
                tray.Quit += () => Quit();
            }
            if (Settings.Current.Autostart && !Paths.IsDev && !Autostart.IsCurrent()) Autostart.Set(true);
            Window.Prewarm();
            Tools.Restore(); // floating tool windows come back now, attached ones with the launcher
            if (Settings.Current.Onboarded != true) ShowOnboarding();
            else if (Opt.Settings) OpenSettings();
            else if (Opt.Show) Summon();
        }
        else _ = Captures.Run(this, Opt.CaptureDir);
        Log.Write($"started {(Paths.IsDev ? "dev " + Paths.Data : "")} pid {Environment.ProcessId}");
    }

    /// QA (`--qa-measure <s>`): how much memory this run holds after `seconds` of ordinary running → memory.txt in the
    /// data folder, then quit. Private bytes is the number to compare (the working set is trimmed while hidden).
    private void MeasureLater(int seconds)
    {
        var wait = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(seconds) };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            using var me = System.Diagnostics.Process.GetCurrentProcess();
            System.IO.File.WriteAllText(Paths.In("memory.txt"), $"private {me.PrivateMemorySize64 / 1048576.0:0.0} MB · working set {me.WorkingSet64 / 1048576.0:0.0} MB · managed {GC.GetTotalMemory(false) / 1048576.0:0.0} MB\n");
            Quit();
        };
        wait.Start();
    }

    /// Something the lists show changed (any thread): one refresh soon.
    private void Later()
    {
        dataDirty = true;
        if (Interlocked.Exchange(ref dataQueued, 1) == 0) ui.BeginInvoke(() => dataTick?.Start());
    }

    /// The once-a-second tick (due timers ring, countdowns move) runs only while a timer or reminder waits.
    private void SyncAlarm()
    {
        if (alarmTick is not null) alarmTick.IsEnabled = Timers.Current.Items.Count > 0;
    }

    /// A Bloom-only app name changed: names and order again, the list follows.
    public void AppsChanged() => Apps.ApplyNames();

    public void Summon()
    {
        if (Opt.Display is { } d && !Window.IsVisible)
        {
            Window.Place(d);
            Window.Show();
            Window.Activate();
            Window.FocusBox();
            return;
        }
        Window.Summon();
    }

    // ── hotkeys ─────────────────────────────────────────────────────────────────────────────────
    public string AltSpaceStatus => Shortcuts?.Status("altspace") ?? "off";

    public void ApplyHotkeys()
    {
        if (Opt.CaptureDir is not null) return;
        var s = Settings.Current;
        static string F(string feature, string keys) => Features.On(feature) ? keys : "off"; // 설정 > 기능 lets go of an off feature's keys
        Clipboard.Apply();
        Shortcuts?.Set("altspace", s.AltSpace ? "alt+space" : "off", () => ui.BeginInvoke(Summon));
        Shortcuts?.Set("clipboard", s.ClipHistory ? s.ClipHotkey : "off", () => ToggleView(Mode.Clipboard));
        Shortcuts?.Set("color", F("color", s.ColorHotkey), StartColor);
        Shortcuts?.Set("lens", F("lens", s.LensHotkey), () => StartLens("menu"));
        Shortcuts?.Set("ask", F("lens", s.AskHotkey), () => StartLens("ask"));
        Shortcuts?.Set("ocr", F("lens", s.OcrHotkey), () => StartLens("text"));
        Shortcuts?.Set("translate", F("lens", s.TranslateHotkey), () => StartLens("translate"));
        Shortcuts?.Set("live", F("lens", s.LiveHotkey), () => StartLens("live"));
        Shortcuts?.Set("windows", F("windows", s.WindowsHotkey), () =>
        {
            OpenWindows.Invalidate();
            ToggleView(Mode.Windows);
        });
        Shortcuts?.Set("emoji", F("emoji", s.EmojiHotkey), () => ToggleView(Mode.Emoji));
        Shortcuts?.Set("quick", F("quick", s.QuickHotkey), () => StartQuick(true));
        Shortcuts?.Set("chat", F("chat", s.ChatHotkey), () => ui.BeginInvoke(ToggleChatWindow));
        bool wantHook = Settings.Current.WinKey && !Opt.NoHook;
        if (wantHook && winKey is null)
        {
            winKey = new WinKeyHook(() => ui.BeginInvoke(Summon));
            winKey.Start();
        }
        else if (!wantHook && winKey is not null)
        {
            winKey.Dispose();
            winKey = null;
        }
    }

    public void OpenView(Mode mode, string text = "")
    {
        if (!Window.IsVisible) Summon();
        Window.Go(mode, text);
    }

    /// Bloom Space Cleaner (one window; a folder given starts a scan of it there).
    public void OpenSpace(string? path)
    {
        Window.Dismiss();
        if (spaceWindow is null)
        {
            spaceWindow = new Space.SpaceWindow(path);
            spaceWindow.Closed += (_, _) =>
            {
                spaceWindow = null;
                // a drive's map is hundreds of MB: give it back now, not at the next idle trim
                // (after a moment: WPF lets go of a closed window's visuals on its next passes; Aggressive also
                // hands the emptied GC segments back to Windows, not only the working set)
                _ = Task.Delay(2000).ContinueWith(_ =>
                {
                    GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                    Win32.SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1));
                }, TaskScheduler.Default);
            };
            spaceWindow.Show();
        }
        else if (path is not null) spaceWindow.Scan(path);
        if (spaceWindow.WindowState == WindowState.Minimized) spaceWindow.WindowState = WindowState.Normal;
        spaceWindow.Activate();
        Win32.ForceForeground(new System.Windows.Interop.WindowInteropHelper(spaceWindow).Handle);
    }

    /// Kill Process: a snapshot now, then one every 1.5 s while the view is in front.
    private void OpenProcesses()
    {
        Processes.Reset();
        Processes.Sample();
        OpenView(Mode.Processes);
        if (procTick is null)
        {
            procTick = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(1500) };
            procTick.Tick += (_, _) =>
            {
                if (!Window.IsVisible || Window.Mode != Mode.Processes)
                {
                    procTick.Stop();
                    return;
                }
                Task.Run(Processes.Sample).ContinueWith(_ => Later(), TaskScheduler.Default);
            };
        }
        procTick.Start();
    }

    public void EndProcess(ProcGroup g, bool force)
    {
        var (ended, refused) = Processes.End(g, force);
        string msg = refused > 0 && ended == 0 ? Tr($"‘{g.Name}’ 종료 실패 — 관리자 권한으로 실행 중일 수 있어요", $"Couldn't end ‘{g.Name}’ — it may run as administrator")
            : refused > 0 ? Tr($"{ended}개는 종료, {refused}개는 거부됐어요", $"{ended} ended, {refused} refused")
            : force ? Tr($"‘{g.Name}’ 강제 종료했어요", $"Force-quit ‘{g.Name}’") : Tr($"‘{g.Name}’에 창 닫기를 보냈어요", $"Asked ‘{g.Name}’ to close");
        Window.ShowToast(msg);
        _ = Task.Delay(400).ContinueWith(_ =>
        {
            Processes.Sample();
            Later();
        }, TaskScheduler.Default);
    }

    /// Windows starts the shell again by itself; if it has not after 2.5 s, it is started here.
    public async void RestartExplorer(ProcGroup g)
    {
        Processes.End(g, true);
        Window.ShowToast(Tr("탐색기를 다시 시작하는 중…", "Restarting Explorer…"));
        await Task.Delay(2500);
        if (System.Diagnostics.Process.GetProcessesByName("explorer").Length == 0) Launch.Open("explorer.exe");
        Processes.Sample();
        Later();
    }

    /// A view's own hotkey: opens it, or hides the launcher when that view is already in front.
    public void ToggleView(Mode mode)
    {
        if (Window.IsVisible && Window.IsActive && Window.Mode == mode)
        {
            Window.Dismiss();
            return;
        }
        OpenView(mode);
    }

    /// Settings live in their own window; a second call brings it back to the front.
    /// The first-run screens (also "Bloom 둘러보기").
    public void ShowOnboarding()
    {
        var w = new OnboardingWindow(this);
        w.Show();
        w.Activate();
        Win32.ForceForeground(new System.Windows.Interop.WindowInteropHelper(w).Handle);
    }

    public void OpenSettings() => OpenSettings(null);

    public void OpenSettings(string? page)
    {
        Window.Dismiss();
        if (settingsWindow is null)
        {
            settingsWindow = new SettingsWindow(this);
            settingsWindow.Closed += (_, _) => settingsWindow = null;
            settingsWindow.Activated += (_, _) => settingsWindow?.Refresh();
            settingsWindow.Show();
        }
        if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal;
        if (page is not null) settingsWindow.Page(page);
        settingsWindow.Activate();
        Win32.ForceForeground(new System.Windows.Interop.WindowInteropHelper(settingsWindow).Handle);
    }

    public void OpenChat(ChatThread? t)
    {
        if (!Window.IsVisible) Summon();
        Window.ShowChat(Recent(t) ?? Chats.New());
    }

    /// The conversation Tab picks up: the last one, while 이어가기 is on and it was used in the last 30 minutes.
    public ChatThread? TabThread => Settings.Current.TabContinuesLastThread && Chats.Latest is { } last
        && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - last.Updated < 30 * 60 * 1000 && !last.Busy ? last : null;

    /// Ask in a conversation: the one given, else the one Tab picks up, else a new one (`fresh`: always a new one).
    public void Ask(string text, ChatThread? t, bool fresh = false)
    {
        if (t is null && !fresh) t = TabThread;
        t ??= Chats.New();
        if (!Window.IsVisible) Summon();
        Window.ShowChat(t);
        Chats.Send(t, text);
    }

    public void DeleteThread(ChatThread t)
    {
        Chats.Delete(t);
        Window.Forget(t);
        chatWindow?.Removed(t);
        if (Window.Mode == Mode.Threads) Window.Refresh(false);
    }

    /// AI 채팅 in its own window (cmd:ai, Ctrl+Shift+O in the quick chat): the conversation given, else the last one
    /// while 이어가기 is on and it was used in the last 30 minutes, else a new one. An open window comes forward.
    public void OpenChatWindow(ChatThread? t = null, string? draft = null, IReadOnlyList<string>? files = null)
    {
        Window.Dismiss();
        if (chatWindow is null)
        {
            var last = Settings.Current.TabContinuesLastThread ? Recent(Chats.Latest) : null;
            chatWindow = new ChatWindow(this, t ?? last ?? Chats.New());
            chatWindow.Closed += (_, _) => chatWindow = null;
            chatWindow.Show();
        }
        else if (t is not null) chatWindow.Open(t);
        if (draft is { Length: > 0 } || files is { Count: > 0 }) chatWindow.Draft(draft, files);
        if (chatWindow.WindowState == WindowState.Minimized) chatWindow.WindowState = WindowState.Normal;
        chatWindow.Activate();
        Win32.ForceForeground(new System.Windows.Interop.WindowInteropHelper(chatWindow).Handle);
    }

    /// The AI 채팅 shortcut: the window comes forward, or goes down when it is already in front.
    public void ToggleChatWindow()
    {
        if (chatWindow is { IsActive: true, WindowState: not WindowState.Minimized }) chatWindow.WindowState = WindowState.Minimized;
        else OpenChatWindow();
    }

    /// The conversation, if it was used in the last 30 minutes (an older one isn't "the last one" any more).
    private static ChatThread? Recent(ChatThread? t) => t is not null && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t.Updated <= 30 * 60 * 1000 ? t : null;

    // ── import ──────────────────────────────────────────────────────────────────────────────────
    /// Pending debounced writes go to disk now (quit, restart, Windows signing out).
    public void SaveAll()
    {
        Settings.Flush();
        Usage.Flush();
        QueryMemory.Flush();
        Favorites.Flush();
        AppNames.Flush();
        Quicklinks.Flush();
        UserCommands.Flush();
        Timers.Flush();
        Notes.Flush();
        ColorBook.Flush();
        Clipboard?.Flush();
    }

    /// `save: false` is the reset: nothing in memory goes back to the data that is about to be deleted.
    public void Quit(bool save = true)
    {
        Log.Write("quit");
        if (save)
        {
            Tools.Save(); // where the tool windows are, before the settings are written
            SaveAll();
        }
        Chats.Shutdown();
        winKey?.Dispose();
        Clipboard.Dispose();
        Shortcuts?.Dispose();
        tray?.Dispose();
        Everything.Dispose();
        Apps.Dispose();
        Window.AllowClose = true;
        Application.Current.Shutdown();
    }
}
