using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Bloom.Core;

internal sealed class Settings
{
    public string Theme { get; set; } = "system"; // system | light | dark
    public string Language { get; set; } = "auto"; // auto (Windows' display language) | ko | en — read once per run (Core\Lang.cs)
    public bool? Onboarded { get; set; } // the first-run screens were seen (null: a settings file from before they existed)
    public string IconStyle { get; set; } = "tile"; // favorites' picture icons: soft (rose tint) | tile (Bloom's neutral tile) | card (raised card) | black
    public string Accent { get; set; } = "rose"; // Ui.Accents key: rose | lavender | sky | mint | tangerine | lemon | gray
    public bool WinKey { get; set; } = true;
    public bool AltSpace { get; set; } = true;
    public bool Autostart { get; set; } = true;
    public bool UpdateCheck { get; set; } = true; // one look at GitHub Releases a little after each start (App\Updates.cs)
    public bool FilesInRoot { get; set; } = true;
    public bool WindowsInRoot { get; set; } = true; // open windows whose title matches show under the results
    public bool LearnQueries { get; set; } = true; // what was picked for what was typed ranks first next time (Core\QueryMemory.cs)
    public string WindowsHotkey { get; set; } = "off"; // 창 전환 straight from anywhere
    public string EmojiHotkey { get; set; } = "off"; // 이모지 · 기호
    public string QuickHotkey { get; set; } = "ctrl+alt+a"; // AI 빠른 동작 on the selected text
    public string ChatHotkey { get; set; } = "ctrl+alt+c"; // the AI 채팅 window: open / bring forward / tuck away
    public List<ItemKey> ItemKeys { get; set; } = new(); // shortcuts given to single items (Core\ItemKeys.cs)
    public string WebBrowser { get; set; } = "default"; // default | a Browser.Web.Installed key — where searches and links open
    public string SearchEngine { get; set; } = "google"; // Browser.Web.Engines key
    public string AiProvider { get; set; } = "claude"; // claude | chatgpt: the side new conversations use
    public string AiModel { get; set; } = "sonnet"; // Claude: sonnet | opus | haiku
    public string AiEffort { get; set; } = "fast"; // Claude: fast (thinking off) | low | medium | high | xhigh | max
    public string? GptModel { get; set; } // ChatGPT: a Codex model slug; null = Codex's own default
    public string GptEffort { get; set; } = "low"; // ChatGPT: the model's reasoning levels (low … ultra)
    public bool TabContinuesLastThread { get; set; } = true;
    public bool AiTools { get; set; } = true; // Claude chats get Bloom's MCP tools (actions asked in the chat first)
    public bool AiToolsGpt { get; set; } = true; // ChatGPT (Codex) chats get them too (Bloom asks in the chat itself)
    public List<string> AiToolsOff { get; set; } = new(); // BloomTools groups the AI may not use: look | open | files | clip
    public List<string> McpOn { get; set; } = new(); // other MCP servers the chats may use: "source|name" (Ai\McpCatalog.cs)
    public List<string> McpFiles { get; set; } = new(); // .mcp.json files brought in on 설정 > PC 작업 · MCP
    public List<string> FeaturesOff { get; set; } = new(); // Core\Features.cs keys switched off in 설정 > 기능
    public string? ClaudePath { get; set; }
    public string? CodexPath { get; set; }
    public List<HomeWidget>? Home { get; set; } // the empty view's blocks, in order (Core\Home.cs)
    public int UsageMinutes { get; set; } = 10; // the home's AI usage: asked of Claude Code / Codex every 5 or 10 minutes (App\AiUsage.cs)
    public bool UsageLeft { get; set; } // the usage cards count what's left ("68% 남음", the bar shrinking) instead of what's used
    public bool ClipHistory { get; set; } = true;
    public int ClipKeepMinutes { get; set; } = 30 * 24 * 60; // how long unpinned entries stay; 0 = for good (pinned ones always stay)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ClipKeepDays { get; set; } // a settings file from before minutes: read once into ClipKeepMinutes
    public string ClipHotkey { get; set; } = "ctrl+alt+v"; // off | ctrl+alt+v | ctrl+shift+v
    public bool TimerSound { get; set; } = true; // a due timer / reminder rings (Windows Alarm01) besides the card
    public string ColorHotkey { get; set; } = "off"; // the colour picker straight from anywhere
    public bool ColorCopy { get; set; } = true; // a picked colour's HEX goes to the clipboard at once
    public string LensHotkey { get; set; } = "ctrl+alt+l"; // Bloom Lens: pick an area → what to do with it
    public string AskHotkey { get; set; } = "off"; // Bloom Lens straight to "AI에게 묻기"
    public string LiveHotkey { get; set; } = "off"; // Bloom Lens straight to live translation
    public string OcrHotkey { get; set; } = "ctrl+alt+o"; // Bloom Lens straight to "글자 복사"
    public string TranslateHotkey { get; set; } = "ctrl+alt+t"; // Bloom Lens straight to "번역"
    public string OcrLanguage { get; set; } = "auto"; // auto (the user's languages) | an installed recognizer tag
    public bool OcrCopy { get; set; } = true; // recognized text goes to the clipboard at once
    public string TranslateProvider { get; set; } = "claude"; // claude (Haiku) | chatgpt
    public string? TranslateGptModel { get; set; } // a Codex slug; null = the lightest in the catalog
    public string TranslateClaudeModel { get; set; } = "haiku"; // haiku | sonnet | opus
    public string TranslateClaudeEffort { get; set; } = "fast"; // fast (thinking off) … max; Haiku has none
    public string TranslateGptEffort { get; set; } = "minimal"; // fitted to the model's lowest it takes
    public string TranslateTo { get; set; } = "auto"; // auto (Korean → English, else → Korean) | ko | en | ja
    public double LiveSeconds { get; set; } = 1.5; // realtime translation: how often the area is read
    // 알림: Claude Code / Codex turns and plan limits as cards in a screen corner (App\Notices.cs)
    public bool Notices { get; set; } = true;
    public string NoticeSpot { get; set; } = "bottom-right"; // top-left | top-center | top-right | bottom-left | bottom-center | bottom-right
    public string NoticeSize { get; set; } = "m"; // s | m | l | xl
    public int NoticeSeconds { get; set; } = 6; // how long a card stays (승인 대기 stays while it waits)
    public string NoticeSound { get; set; } = "m"; // off | s | m | l (volume)
    public bool NoticeStart { get; set; } = true; // 진행 중: a turn started (what it works on)
    public bool NoticeDone { get; set; } = true; // 작업 완료
    public bool NoticeWaiting { get; set; } = true; // 승인 대기
    public bool NoticeLimit { get; set; } = true; // 한도 임박 · 소진 (20 % left, used up)
    public bool NoticeRecover { get; set; } = true; // 한도 복구 (a low limit back after its reset)
    // 성능 (Ui\Theme\Motion.cs)
    public string Animation { get; set; } = "system"; // system (Windows' animation effects) | on | reduced (fades only) | off
    public int AnimationFps { get; set; } // 0 = the screen's own rate | 30 | 60
    public string Transparency { get; set; } = "system"; // system (Windows' transparency effects) | off (solid, lighter on the GPU)
    public double Scale { get; set; } = 1; // 설정 > 일반 > 크기: 0.8 … 1.5 (Ui\Theme\Theme.cs Zoom)
    public List<ToolPlace> Tools { get; set; } = new(); // the open tool windows (Ui\Tools\ToolDock.cs)
    public int Width { get; set; } = 760;
    public int Height { get; set; } = 476;

    [JsonIgnore] private static Saver? saver;
    [JsonIgnore] public static Settings Current { get; private set; } = new();

    public static void Load()
    {
        bool existed = File.Exists(Paths.In("settings.json"));
        Current = Json.Load<Settings>(Paths.In("settings.json"));
        Current.Onboarded ??= existed; // someone who already used Bloom doesn't get the first-run screens
        // the clipboard history was kept by days before it was kept by minutes
        if (Current.ClipKeepDays is { } days)
        {
            Current.ClipKeepMinutes = Math.Max(0, days) * 24 * 60;
            Current.ClipKeepDays = null;
        }
        Lang.Init(Current.Language);
        // before Claude and ChatGPT had a model each, "chatgpt" was one of the models
        if (Current.AiModel.StartsWith("chatgpt", StringComparison.Ordinal))
        {
            Current.AiProvider = "chatgpt";
            Current.AiModel = "sonnet";
        }
        saver = new Saver(() => Json.Save(Paths.In("settings.json"), Current));
    }

    public static void Save() => saver?.Touch();

    public static void Flush() => saver?.Flush();
}
