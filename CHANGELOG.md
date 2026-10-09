# Changelog

Each release on GitHub shows its section from this file (the workflow looks for `## <version>`).

## 0.11.3

- Uninstall and Reset check the folders before deleting them: only Bloom's own data folders (Bloom under %APPDATA% and %LOCALAPPDATA%) can go, never another folder by mistake.

### 한국어

- Bloom 삭제와 초기화가 지우기 전에 폴더를 한 번 더 확인해요. Bloom의 데이터 폴더(%APPDATA% · %LOCALAPPDATA% 안의 Bloom)만 지우고, 다른 폴더는 실수로라도 지우지 않아요.

## 0.11.2

- Uninstall Bloom from Bloom itself: Settings › About › Uninstall Bloom. It asks twice: are you sure, and should your chats, settings and favorites go too. A copy run from the portable zip stops starting with Windows and opens its folder for you to delete.
- Install from PowerShell in one line: `irm https://raw.githubusercontent.com/hyeon1035298/Bloom/main/install.ps1 | iex`. It downloads the same setup from the latest release and runs it.

### 한국어

- Bloom 안에서 Bloom을 지울 수 있어요. 설정 › 정보 › Bloom 삭제를 누르면 정말 지울지, 대화 · 설정 · 즐겨찾기까지 지울지 두 번 물어봐요. 압축본으로 쓰던 Bloom은 Windows 시작 시 자동 실행을 풀고, 직접 지울 수 있게 폴더를 열어 줘요.
- PowerShell 한 줄로 설치할 수 있어요: `irm https://raw.githubusercontent.com/hyeon1035298/Bloom/main/install.ps1 | iex`. 최신 릴리즈의 같은 설치 파일을 받아 실행해요.

## 0.11.0

- Bloom speaks Japanese and Simplified Chinese: 设置 › 常规 › 语言 / 設定 › 一般 › 言語, or Auto with a Japanese or Chinese Windows. Searches, times and units are still typed in English or Korean.
- Text now uses the Windows system font (Segoe UI, Malgun Gothic) instead of Bloom Sans, drawn on whole pixels: sharper on Full HD and other 100% screens. Bloom takes 6 MB less on disk.
- Drag the launcher by its search line, not only the bottom bar. Past the text the line drags; on the text it still places the caret and selects.
- A ⚙ at the end of the search line opens the settings.
- Notes, timers and the other tool windows can be dragged onto another monitor. They used to keep snapping back to the edge of the screen they were on.
- Shortcuts take any key: PrintScreen, Pause, F13–F24, the number pad, punctuation, media keys. Keys that type nothing work on their own; the rest need Ctrl, Alt, Shift or Win.
- The color picker always starts on the screen: the magnifier and the color code follow the mouse, and the color view opens after you click.
- Starting with Codex notices on reads far less: Bloom skips the logs of its own chats (half the start-up allocations).

### 한국어

- 일본어와 간체 중국어를 지원해요. 설정 › 일반 › 언어에서 고르거나, 자동이면 Windows가 일본어 · 중국어일 때 그 언어로 나와요. 검색어 · 시간 · 단위는 지금처럼 한국어나 영어로 적어요.
- 글꼴이 Bloom Sans에서 Windows 시스템 글꼴(맑은 고딕 · Segoe UI)로 바뀌었어요. 글자를 픽셀에 맞춰 그려서 FHD처럼 배율 100%인 화면에서도 또렷해요. 설치 크기도 6 MB 줄었어요.
- 아래쪽 바뿐 아니라 검색줄을 끌어서도 런처를 옮길 수 있어요. 글자가 없는 곳을 끌면 옮겨지고, 글자 위에서는 그대로 커서를 두거나 선택해요.
- 검색줄 끝의 ⚙로 설정을 바로 열어요.
- 메모 · 타이머 같은 도구 창을 다른 모니터로 옮길 수 있어요. 전에는 화면 가장자리에 계속 다시 붙었어요.
- 단축키에 어떤 키든 쓸 수 있어요. PrintScreen, Pause, F13~F24, 숫자 패드, 기호, 미디어 키까지요. 글자를 치지 않는 키는 혼자서도 되고, 나머지는 Ctrl · Alt · Shift · Win과 함께 눌러요.
- 컬러 피커는 항상 화면에서 시작해요. 마우스를 따라 돋보기와 색 코드가 보이고, 클릭하면 색 화면이 열려요.
- Codex 알림을 켜 둔 채 시작할 때 읽는 양이 크게 줄었어요. Bloom이 연 채팅의 기록은 건너뛰어요(시작할 때 쓰는 메모리 할당이 절반).

## 0.10.0

Everything since 0.8.3 in one place: what's new in this update first, then what came in 0.9.0 to 0.9.3.

#### New in this update

- Bloom Lens can capture. Pick an area and it goes to the clipboard as a picture, ready for Ctrl+V. It's `6` in the Lens menu, or search for "screen capture". You can give it its own shortcut in Settings › Bloom Lens (off at first).
- Uninstall apps from Bloom. Right-click an app (or Ctrl+K) › Uninstall…. Bloom asks once more, then opens that program's own uninstaller. Store apps are removed for your account.
- Set a shortcut by pressing it. Click a shortcut in Settings, or Ctrl+K › Set a shortcut, and press the keys you want. Backspace turns it off.
- The quick AI keeps going when you leave. Esc takes you home and the answer carries on. The search line shows it until you look, Tab takes you back, and a card tells you when it's done if Bloom is hidden. ■ Stop or Ctrl+. stops it.
- A tidier quick AI bar, so what you type has room. One chip for the model and thinking level, Stop and End session side by side, and a 📎 button for files. The pin and tools buttons left the bar; Ctrl+P and Ctrl+J still work.

#### Search and windows

- Type a path like `C:\Users`, `D:/work` or `%APPDATA%` and the folder comes first. Enter opens it, Tab goes into the folder you picked.
- Window layout: `left half`, `right third`, `top-left quarter`, `maximize`, `center` and `next screen` move the window in front.
- Dates and other cities' clocks: `in 100 days`, `days until christmas`, `days since 2026-03-01`, `new york time`, `tokyo 3pm`.
- Give anything its own shortcut: Ctrl+K › Set a shortcut on an app, folder, file, site, command or layout. They're listed in Settings › Tools.
- Bloom Lens can pin an area to the screen. It stays on top as a picture you can drag, size and copy.
- Install apps with winget: type `install vlc`. Bloom asks first, installs it in the background and tells you how it went.

#### AI chat

- Copy any part of an answer. Drag over an answer, your own message or a code block, then Ctrl+C or right-click › Copy.
- Chats name themselves per task, like `[◆ Opus • Code ] Fix the login`. You can switch it off in Settings › AI chat.
- Pick several chats and groups and delete them at once: the ✓ next to the chat search, Ctrl+click, or ⋯ › Select.
- A chat you leave alone closes its CLI after 10 minutes. Set your own time in Settings › AI chat; 0 keeps it open.
- Picking a chat up again after its CLI closed costs much less AI usage, because the AI service can reuse what it already cached.

#### Speed, memory and settings

- Draw with the GPU (smooth) or the CPU (about 30 MB less memory), at first start or in Settings › Performance.
- Bloom gives memory back after you close the chat or settings window.
- Long lists open faster, and the emoji view no longer pauses.
- Settings are grouped into Basics, Launcher, AI and Tools.
- After an update, Bloom shows what changed once. See it again in Settings › About.
- Each new feature can be switched off in Settings › Features.

### 한국어

0.8.3 이후 바뀐 점을 한곳에 모았어요. 이번 업데이트에서 새로 생긴 것부터, 그다음 0.9.0~0.9.3에서 생긴 것이에요.

#### 이번 업데이트에서 새로

- Bloom Lens로 캡처할 수 있어요. 영역을 고르면 그림으로 클립보드에 복사돼서 바로 Ctrl+V로 붙여 넣으면 돼요. Lens 메뉴의 `6`번이고, "화면 캡처"로 찾아도 돼요. 설정 › Bloom Lens에서 단축키를 따로 줄 수 있어요(처음엔 꺼져 있어요).
- Bloom에서 앱을 지울 수 있어요. 앱을 오른쪽 클릭(또는 Ctrl+K) › 제거…. 한 번 더 묻고 그 프로그램의 제거 창을 열어요. Store 앱은 내 계정에서 지워요.
- 단축키는 키를 눌러서 정해요. 설정의 단축키 칸이나 Ctrl+K › 단축키 지정에서 쓸 키를 그대로 누르면 돼요. Backspace는 끄기예요.
- 빠른 AI가 자리를 비워도 계속 일해요. Esc를 누르면 홈으로 가고 답은 계속 써요. 확인하기 전까지 검색창에 보이고, Tab으로 돌아가요. Bloom이 숨어 있으면 끝났을 때 카드로 알려 줘요. ■ 정지나 Ctrl+.로 멈춰요.
- 빠른 AI 위쪽 바를 정리해서 적는 글이 잘 보여요. 모델과 생각 수준은 칩 하나로, 정지와 세션 종료는 나란히, 파일은 📎 버튼으로. 고정 · 도구 버튼은 바에서 뺐어요. Ctrl+P · Ctrl+J는 그대로 돼요.

#### 검색 · 창

- `C:\Users`, `D:/작업`, `%APPDATA%`처럼 경로를 적으면 그 폴더가 맨 위에 떠요. Enter로 열고, Tab으로 고른 폴더에 들어가요.
- 창 배치: `왼쪽 절반`, `오른쪽 1/3`, `왼쪽 위 1/4`, `최대화`, `가운데로`, `다음 화면으로`로 앞에 있는 창을 옮겨요.
- 날짜와 다른 도시 시간: `100일 뒤`, `크리스마스까지`, `2026-03-01부터`, `뉴욕 시간`, `도쿄 오후 3시`.
- 무엇이든 단축키를 붙일 수 있어요. 앱 · 폴더 · 파일 · 사이트 · 명령 · 창 배치에서 Ctrl+K › 단축키 지정. 붙인 단축키는 설정 › 도구에 모여 있어요.
- Bloom Lens로 고른 영역을 화면에 고정할 수 있어요. 그림으로 맨 위에 떠 있고, 옮기고 크기를 바꾸고 복사할 수 있어요.
- winget으로 앱 설치: `설치 vlc`처럼 적으면 돼요. 먼저 묻고, 뒤에서 설치한 다음 결과를 알려 줘요.

#### AI 채팅

- AI 답을 원하는 부분만 복사할 수 있어요. 답 · 내가 보낸 말 · 코드 블록을 드래그하고 Ctrl+C나 오른쪽 클릭 › 복사.
- 대화 제목이 작업마다 `[◆ Opus • Code ] 로그인 수정`처럼 붙어요. 설정 › AI 채팅에서 끌 수 있어요.
- 대화와 그룹을 여러 개 골라 한꺼번에 지울 수 있어요. 대화 찾기 옆 ✓, Ctrl+클릭, ⋯ › 선택.
- 안 쓰는 대화의 CLI는 10분 뒤에 닫아요. 설정 › AI 채팅에서 시간을 직접 정할 수 있고, 0이면 닫지 않아요.
- CLI가 닫힌 대화를 다시 이어 갈 때 AI 사용량이 훨씬 덜 들어요. AI 쪽에 저장된 캐시를 그대로 다시 써요.

#### 속도 · 메모리 · 설정

- GPU로 그리기(부드럽게)와 CPU로 그리기(메모리 약 30 MB 덜 씀) 중에 고를 수 있어요. 처음 설정이나 설정 › 성능에서요.
- 채팅 창이나 설정 창을 닫으면 메모리를 돌려줘요.
- 긴 목록이 빨리 뜨고, 이모지 화면이 멈칫하지 않아요.
- 설정을 기본 · 런처 · AI · 도구로 묶었어요.
- 업데이트한 뒤 처음 켜면 바뀐 점을 한 번 보여 줘요. 설정 › 정보에서 다시 볼 수 있어요.
- 새 기능은 모두 설정 › 기능에서 끌 수 있어요.

## 0.8.3

- AI chat with ChatGPT (Codex): MCP servers you switched on no longer flash terminal windows while a chat runs.

### 한국어

- ChatGPT(Codex)로 AI 채팅할 때, 켜 둔 MCP 서버 때문에 터미널 창이 잔뜩 떴다 사라지던 문제를 고쳤어요.

## 0.8.2

- Clipboard history: set how long entries stay in minutes (Settings › Clipboard › Keep for). 0 keeps them. Your old setting carries over, so a day becomes 1440 minutes.
- Short times now hold even when you copy nothing new. Entries go when they come due.

### 한국어

- 클립보드 기록 보관 기간을 분 단위로 직접 적을 수 있어요(설정 › 클립보드 › 보관 기간). 0이면 계속 남겨요. 예전 설정은 그대로 옮겨져서, 하루였다면 1440분이 돼요.
- 짧게 잡아 두면 새로 복사하지 않아도 시간이 되는 대로 지워져요.

## 0.8.1

The first public release.

Bloom is a launcher for Windows. Press Win, type a line, and it opens apps, does sums, finds files, sets reminders and asks the AI you already use.

What's in it
- One search line for apps, files, maths, units and currencies, emoji, reminders and web searches
- AI chat through your own Claude Code or Codex login, no API key
- The AI can move and rename files, write notes and add timers or events, and it asks before each one
- Bloom Lens: pick part of the screen to ask about it, translate it or copy its text
- Notes, timers, a calculator and clipboard history that sit beside the launcher
- Space Cleaner shows where your disk space went
- Bloom updates itself from this page

Install
- Download `BloomLauncher-win-Setup.exe` below and run it. Windows 10 or 11, 64-bit.
- Bloom isn't code-signed yet, so Windows may show "Windows protected your PC". Click More info, then Run anyway.

### 한국어

처음으로 공개하는 버전이에요.

Bloom은 Windows용 런처예요. Win 키를 누르고 한 줄 치면 앱 실행, 계산, 파일 찾기, 알림, AI 질문까지 돼요.

들어 있는 것
- 검색창 하나로 앱, 파일, 계산, 단위·환율, 이모지, 알림, 웹 검색
- 쓰던 Claude Code·Codex 로그인으로 AI 채팅 (API 키 필요 없음)
- AI에게 파일 옮기기·이름 바꾸기, 메모, 타이머·일정 추가를 맡길 수 있고, 할 때마다 먼저 물어봐요
- Bloom Lens: 화면 일부를 골라 묻거나, 번역하거나, 글자를 복사해요
- 런처 옆에 붙여 쓰는 메모, 타이머, 계산기, 클립보드 기록
- Space Cleaner로 디스크 용량이 어디에 쓰였는지 볼 수 있어요
- 이 페이지에서 알아서 업데이트돼요

설치
- 아래 `BloomLauncher-win-Setup.exe`를 받아서 실행하세요. Windows 10, 11 (64비트).
- 아직 코드 서명이 없어서 "Windows의 PC 보호" 창이 뜰 수 있어요. 추가 정보 → 실행을 누르면 돼요.
