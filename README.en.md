<p align="center"><img src="docs/media/mark.png" width="88" alt=""></p>

<h1 align="center">Bloom</h1>

<p align="center"><b>Press Win. Type one line.</b><br>
A light native launcher for Windows: open apps, do maths, find files, chat with AI and translate the screen, all from one search line.</p>

<p align="center">
  <a href="#install--build">Install</a> · <a href="#features">Features</a> · <a href="#shortcuts">Shortcuts</a> · <a href="#about-the-ai-features">AI &amp; privacy</a> · <a href="README.md">한국어</a>
</p>

<p align="center">
  <img src="docs/media/en/loop.webp" alt="Bloom demo: Win opens the search line; it works out 45000*1.1, opens the color picker from a pasted color code, asks AI, attaches notes beside the launcher, and translates a message picked on the screen">
</p>

<p align="center"><sub>WPF · .NET 10 · no WebView · no NuGet packages · about 80 MB with everything off · English / 한국어 · MIT</sub></p>

## Features

### One search line

<img src="docs/media/en/search.webp" alt="The search line reading laundry in 30 min as a reminder, and a list of what one line takes: maths, units, emoji, apps, site searches, Korean initials">

You don't need the whole name. Sums are worked out as you type, and times are written the way you'd say them.

- **Apps · settings · commands**: Start-menu apps, Windows settings pages and Bloom commands. For Korean users it also understands initial consonants (`ㅁㅁㅈ` → 메모장) and words typed with the keyboard left in Korean (`초개ㅡㄷ` → chrome).
- **Maths · units · currencies**: `45000*1.1`, `5km to mi`, `100 usd`
- **Timers · reminders · calendar**: `timer 5m`, `laundry in 30 min`, `remind me at 3pm to call`
- **Web search · site shortcuts**: what you typed goes to your browser as a search (Google · Naver · Bing · DuckDuckGo), or to a site by keyword: `yt cats`
- **Files · recent files · windows**: by name through [Everything](https://www.voidtools.com/) when it is installed, from recent files when it isn't. Open windows are found by name too, and switched to at once.
- **Emoji · symbols**: by English or Korean name, `:heart`, `arrow`, `celsius`
- **System toggles**: dark mode, mute sound or microphone, `volume 30`, output device, Wi-Fi · Bluetooth, hidden files, sleep …
- **Ctrl+K** on any result lists everything you can do with it; on a file: rename, move, copy, Recycle Bin.
- What you pick for what you type comes up first the next time.

### AI with your own Claude Code · Codex login

<img src="docs/media/en/ai.webp" alt="A chat asking Allow, Always or Decline before it moves a PDF into Documents, and Claude Code and Codex work notifications">

No API key. Answers come from the Claude Code or Codex CLI you have installed and signed in to on this PC.

- Ask straight from the search line with **Tab**; the separate **AI chat window** (Ctrl+Alt+C) keeps past chats in groups, continues them and takes attached files.
- **PC actions**: say "move the PDF I got yesterday into Documents and set a 10 minute timer". Anything that changes something is asked about in the chat first, every time.
- **Quick AI actions** (Ctrl+Alt+A): summarize, translate, fix or rewrite the selected text …
- **Usage**: the home shows Claude and ChatGPT's 5-hour and weekly limits, as used or as left.
- **Work notifications**: a card and a sound when a Claude Code or Codex task starts, finishes, waits for approval, or nears or regains its usage limit.

### On the screen — Bloom Lens · color picker

<img src="docs/media/en/lens.webp" alt="A Japanese message on the screen picked with Bloom Lens and translated into English, and the color picker on #FF7A9E">

- **Bloom Lens** (Ctrl+Alt+L): drag over an area of the screen, then `1` ask AI · `2` translate · `3` copy its text (Windows OCR) · `4` translate live.
- **Color picker**: take a color from the screen with the magnifier or paste a color code, and get HEX · RGB · HSL and gradients made from it (copied as CSS).

### Docked tools

<img src="docs/media/en/tools.webp" alt="The pinned launcher with notes attached on its right and the calculator under it">

- **Ctrl+J** opens notes, timers, calculator and clipboard history as small windows. Dropped on the launcher's side or bottom they come and go with it; pulled off, they float on their own.
- **Notes** are kept as you type. Type `note buy milk` in the search line, and notes are searched too.
- **Clipboard history** (Ctrl+Alt+V): text · images · files.
- **Pin · size**: Ctrl+P keeps the launcher on screen; Ctrl+= / Ctrl+- scale Bloom from 80 to 150 %.

### Bloom Space Cleaner

<img src="docs/media/en/space.webp" alt="A map of a folder where videos, downloads, photos and projects are blocks as big as they are, with a list of the biggest files">

A map of where a drive's or folder's space went. Click a block to go inside, pick big files from the list and send them to the Recycle Bin. Drive roots and the Windows folder are never touched.

### Yours to trim

<img src="docs/media/en/yours.webp" alt="The Features page of the settings with a switch per feature, and the Performance page with animation, glass and memory">

- **My commands**: run scripts (.ps1 · .bat · programs · one PowerShell line), snippets and chains (`notepad ; volume 30 ; wait 2`) by keyword; words typed after the keyword fill `{input}`.
- **Favourites**: an icon row (Ctrl+1…9) and folders, with icons drawn by AI if you like.
- **Home**: pick which blocks the empty search line shows, and their order.
- **Features on and off**: a feature you switch off leaves the results, the command list, the home and the shortcuts. Nothing saved is deleted.
- **Performance · look**: animations on, reduced or off, how many per second, glass effect. Color theme presets, light and dark.

<details>
<summary><b>Every feature</b></summary>

- **Apps · commands**: finds Start-menu apps, Windows settings pages and Bloom commands. Understands Korean initial consonants (ㅁㅁㅈ → 메모장) and words typed with the keyboard left in Korean (초개ㅡㄷ → chrome). An app can be renamed inside Bloom only.
- **Favourites**: an icon row (Ctrl+1…9) and folders. Picture icons that match the name, or icons drawn by AI if you like.
- **Calculator · web search**: write a sum and it is worked out at once, or hand the line to your browser as a search (Google · Naver · Bing · DuckDuckGo).
- **Finding and tidying files**: with [Everything](https://www.voidtools.com/) installed, narrow by kind (videos, pictures, documents, folders …) and preview. Ctrl+K on a result renames it, moves or copies it to another folder, or sends it to the Recycle Bin (the way Explorer does, so the progress window and the name-clash questions are the same).
- **My commands**: run what you do often by a keyword. Scripts (.ps1 · .bat · programs, or one PowerShell line — run without a window, the result as a notice card), snippets (saved text pasted into the window in front, `{date}` `{time}` `{clipboard}`), and chains (lines for the search line run in turn, like `notepad ; volume 30 ; wait 2 ; yt lofi`). Words after the keyword fill the `{input}` spot (`bk D:\photos`). Script files are added from Ctrl+K › My commands.
- **Clipboard history** (Ctrl+Alt+V): text · images · files.
- **AI chat**: talk through the installed Claude Code CLI or Codex CLI (pick the model and thinking level). Ask straight from the search line with Tab (Shift+Tab for a new chat); the separate **AI chat window** (Ctrl+Alt+C) has past chats · continue · end session · groups (folders, drag and drop). Attach files and pictures (drag and drop, Ctrl+O, Ctrl+V) and take files the AI made (open · save).
- **Work notifications**: a card in a screen corner and a sound when Claude Code · Codex starts a task, finishes it, waits for approval, or its usage limit nears or comes back. Six positions · size · sound · which events; none during full-screen games or presentations.
- **AI usage**: Claude and ChatGPT's 5-hour and weekly usage on the home. Every 5 or 10 minutes (↻ for now) Bloom asks each CLI without a window and closes it. Shown as used or as left (Settings › Home).
- **Bloom Lens** (Ctrl+Alt+L): pick an area of the screen to ask AI about, translate, copy its text (Windows OCR) or translate live.
- **Bloom Space Cleaner**: a map of a drive's or folder's space. Click a block to go inside; pick from the list of big files and send them to the Recycle Bin (drive roots and the Windows folder are never touched).
- **Timers · reminders · calendar**: write `timer 5m` or `remind me at 3pm to call` in the search line. Reminders also show on the monthly calendar and in the home's "This week".
- **Color picker**: take screen colors with a magnifier, copy HEX/RGB/HSL, and get gradient suggestions made from the color (CSS copy).
- **Browser**: hand searches and links to the browser you picked, site shortcuts (keyword + search words, e.g. `yt cats`).
- **Window switching**: find an open window by name and switch to it, Ctrl+W closes it. A few also show in the root search (can be switched off in the settings).
- **Learning per typed text**: what you pick after typing the same letters comes up first next time. A new habit beats an old one, and the settings can clear it.
- **Emoji · symbols**: find by Korean or English name and paste (`heart`, `arrow`, `celsius`). Starting with `:` works in the root too.
- **System toggles**: dark mode, mute sound/microphone, volume (`volume 30`), output device, Wi-Fi · Bluetooth, hidden files · extensions, sleep, screen off, empty Recycle Bin.
- **Recent files**: Windows' recent items. Without Everything they also show in the root search.
- **Quick AI actions** (Ctrl+Alt+A): turn the selected text (or the clipboard when nothing is selected) into a summary · translation · spelling fix · polish · shorter · longer …, or follow your own instruction, and paste the result.
- **Also**: end processes, lock · restart · shut down.
- **Performance**: animations on · reduced · off, animations per second (30 · 60 · max), glass off. Features that use memory say in the settings how much they add (about 80 MB with all off).
- **Tool windows** (Ctrl+J): notes · timers · calculator · clipboard as small windows. Dragged to the launcher's right, left or bottom they attach and come and go with it; pulled off, they float (always on top on or off). While moving they snap like magnets to other tools, the launcher and the screen edges. Once you start using an attached tool the launcher pins itself.
- **Notes**: several notes, saved as you type, pinning, deleted notes go to a trash. Type `note buy milk` in the search line to keep a note at once, and notes are searched too.
- **Pin · size**: 📌 (Ctrl+P) keeps the launcher on screen. Size in Settings › General or Ctrl+= / Ctrl+- / Ctrl+0 (80–150 %) for the launcher · tools · AI chat · settings together.
- **Features on and off** (Settings › Features): a feature you don't use (AI chat · Bloom Lens · calculator · timers · color picker · emoji · window switching …) leaves the search results, the command list, the home and the favourites row, and its shortcut is freed. Nothing saved is deleted.
- **Home**: pick the blocks the empty search line shows (favourites · timers · recently used · commands …) and their order.
- Color theme presets, dark/light, a settings window by category.
- **한국어 / English**: pick on the welcome screen or in Settings › General; Bloom restarts after a change.

</details>

## Shortcuts

| Keys | What it does |
| --- | --- |
| `Win` · `Alt+Space` | Open Bloom |
| `Tab` / `Shift+Tab` | Ask AI what's in the search line / in a new chat |
| `Ctrl+K` | Everything you can do with the picked result |
| `Ctrl+1` … `Ctrl+9` | Favourites |
| `Ctrl+J` | Tools (notes · timers · calculator · clipboard) |
| `Ctrl+P` | Pin the launcher |
| `Ctrl+=` · `Ctrl+-` · `Ctrl+0` | Bigger · smaller · 100 % |
| `Esc` | Back → clear the text → close |
| `Ctrl+Alt+C` | AI chat window |
| `Ctrl+Alt+A` | Quick AI actions |
| `Ctrl+Alt+L` | Bloom Lens |
| `Ctrl+Alt+V` | Clipboard history |

The global shortcuts can be changed or switched off per feature in the settings.

## About the AI features

Bloom **never takes or stores your sign-in details.** The AI features only run the official CLIs you have already installed and signed in to on this PC.

- Claude: [Claude Code](https://docs.anthropic.com/en/docs/claude-code) CLI (`claude`)
- ChatGPT: [Codex CLI](https://github.com/openai/codex) (`codex`)

Usage and terms are those of your own accounts. Without the CLIs only the AI features are off; everything else works as usual.

**AI acting on the PC** (Claude · ChatGPT, Settings › PC actions · MCP)

- Say "move the PDF I got yesterday into Documents and set a 10 minute timer" in a chat, and Claude does it with Bloom's tool.
- Bloom offers one tool (`pc`) through an MCP server that only opens on this PC (`Bloom.exe --mcp`, a named pipe only this Windows user can open).
- Finding apps · files (Everything) · recent files · windows · timers · my commands runs at once. Opening, running a search-line line (`volume 30`, `timer 10m`), running my commands, renaming/moving/copying/Recycle Bin and the clipboard are asked about every time with **Allow / Always in this chat / Decline buttons in the chat** (Claude Code's `--permission-prompt-tool stdio`). What was done stays under the answer as ✓ / ✕.
- What it may do can be switched on and off in groups: look up · open and run · tidy files · clipboard.
- Other MCP servers already set up on this PC (Claude Code · Claude Desktop · Cursor · VS Code · Windsurf · Codex settings and the `.mcp.json` of projects opened with Claude Code) show on the same page, one switch per server (off by default). Their tools are asked about with the same card each time; for ChatGPT, Bloom puts a relay in front of the server (`Bloom.exe --mcp-proxy`) to ask. Server settings are never copied into Bloom; they are read from their own files each time.
- The tool definition is cut down to one tool, about +400 input tokens per session (against no tools, measured; less with groups switched off). Your other MCP servers are not loaded (`--strict-mcp-config`).
- ChatGPT (Codex) chats use the same tool. Codex can't ask by itself in exec mode, so Bloom's MCP server shows the same card right before acting and waits for the answer (attached per chat run with `-c mcp_servers.bloom…` only). Codex loads MCP tool descriptions only when needed, so a one-line note of the tool's name and job keeps it to about +190 tokens per request.

AI usage comes from `claude -p /usage` (no model call) and the limit lookup of `codex app-server`. Work notifications only read Claude Code's and Codex's own logs on this PC (`~/.claude/sessions`, `~/.codex/sessions`) and send them nowhere.
Only what you send yourself reaches the AI: your question, attached files, the screen area picked with Bloom Lens, the text to translate.
Claude can read and write files only inside that chat's folder (`%LOCALAPPDATA%\Bloom\ai\files\<chat>`). ChatGPT (Codex) doesn't handle files itself, so text attachments go in the message and files it makes are taken out of the answer and saved.

Bloom is an unofficial personal project, not affiliated with Anthropic, OpenAI, voidtools or Raycast.

## Install · build

Needs: Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download) (the .NET 10 Desktop Runtime is enough to run it)

```powershell
dotnet build src\Bloom\Bloom.csproj -c Release
# or make an installed copy
powershell -File tools\package.ps1   # → dist\app
powershell -File tools\install.ps1   # → %LOCALAPPDATA%\Programs\Bloom, Start-menu shortcut
```

Data lives in `%APPDATA%\Bloom` and `%LOCALAPPDATA%\Bloom`.
For development, `Bloom.exe --data <folder>` uses another data folder (`--lang ko|en` fixes the language).

## Code structure

<details>
<summary>Folders · conventions · tests and lint</summary>

Under `src/Bloom`, one folder is one feature. Big classes are split into `partial` files by topic.

| Folder | What's in it |
| --- | --- |
| `App/` | `Host` (wires the whole app: start · shortcuts · commands · Lens · timers/colors · running my commands · file actions), work notifications (`Notices`), the MCP server (`Mcp/` — relay · server · the `pc` tool), run options, tray, the Win key hook |
| `App/Engine/` | Making what the search line shows — root search, home blocks, item conversion, per view (files · clipboard · calendar · timers · colors …) |
| `App/Qa/` | Off-screen capture QA (`--capture <folder> --qa-set v15 --data <folder> --lang en`) |
| `Core/` | What is saved (settings · favourites · usage · learning per typed text · timers · colors) and JSON storage, Korean search, matching, the interface language (`Lang` — `Tr("한국어", "English")`) |
| `Search/` | App list, calculator (sums · units · currencies), Everything, open windows, recent files, emoji · symbols (`Assets/symbols.tsv`), system items, my commands (`UserCommands`), result models |
| `Ai/` | Claude Code / Codex CLI sessions with chats · groups (`Chats`), one-shot asks (`OneShot`), translation, quick AI actions, AI icons, usage (`AiUsage`), reading work logs (`ClaudeWatch` · `CodexWatch`) |
| `Ocr/` | Picking a screen area, Windows OCR, the translation window, picking colors |
| `Clip/`, `Space/`, `Browser/` | Clipboard history, the disk space map, handing off to the browser |
| `Native/` | Win32 calls, WinRT calls (`WinRt`), system toggles (`SystemControl` — sound · wireless · dark mode …), Explorer-style file operations (`ShellFiles`) |
| `Ui/Tools/` | Tool windows: attach · detach · magnetic snapping (`ToolDock`), the window frame (`ToolWindow`), notes · timers · calculator · clipboard |
| `Ui/` | The launcher window (`Launcher/` — every view's chips · hints · results in one place, `Launcher.ViewSpecs.cs`), the AI chat window (`Chat/`), the settings window (`Settings/`), notice cards, shared controls (`Controls/` — continuous-curvature corners `SmoothBorder`, `Dropdown`, `PopupMenu`), theme (`Theme/` — animation settings in `Motion`) |
| `tests/Bloom.Tests` | Automated tests (calculator · time reading · matching · learning · emoji · language · AI prompts · usage reading · work logs · my commands) — a console run without a test framework |

Conventions: a new feature splits into `Engine.<feature>.cs` (results) + one view line in `Launcher.ViewSpecs.cs` + `Core/<feature>.cs` (storage) when needed.
Every piece of text on screen is written as `Tr("한국어", "English")`. Search keywords and parser data keep both languages.
QA and development runs always use `--data <temporary folder>`, apart from the real data.

```powershell
# tests (exit code = failures)
dotnet run --project tests\Bloom.Tests
# lint (analyzers + formatting) — zero warnings
dotnet build src\Bloom\Bloom.csproj -c Debug --no-incremental -p:AnalysisLevel=latest-recommended -p:EnforceCodeStyleInBuild=true
dotnet build tests\Bloom.Tests\Bloom.Tests.csproj -c Debug --no-incremental -p:AnalysisLevel=latest-recommended -p:EnforceCodeStyleInBuild=true
dotnet format whitespace src\Bloom\Bloom.csproj --verify-no-changes
dotnet format whitespace tests\Bloom.Tests\Bloom.Tests.csproj --verify-no-changes
```

</details>

## Optional

Nothing here is required. Without them, the related blocks and settings don't show.

- **Everything**: finding files.
- **Claude Code CLI · Codex CLI**: AI chat · Lens · translation · quick actions · usage · work notifications.

## License

MIT — [LICENSE](LICENSE). The font (Bloom Sans = [Wanted Sans](https://github.com/wanteddev/wanted-sans)) is under the SIL OFL 1.1 — `src/Bloom/Fonts/OFL.txt`.
Exchange rates: [Rates By Exchange Rate API](https://www.exchangerate-api.com).
