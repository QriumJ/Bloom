# Changelog

Each release on GitHub shows its section from this file (the workflow looks for `## <version>`).

## 0.9.3

- Picking a chat up again after its CLI was closed should cost much less AI usage. The clock is no longer part of the chat's instructions (the time now comes with each message), so a CLI started again can reuse what the AI service already cached instead of taking in the whole conversation again.
- An unused chat closes its Claude or Codex CLI after 10 minutes again (0.9.2 made it 5). If 0.9.2 set it to 5 for you, it moves back to 10 once. You can still set your own time in Settings › AI chat.

### 한국어

- CLI가 닫힌 대화를 다시 이어 갈 때 AI 사용량이 훨씬 덜 들어요. 대화 지시문에서 현재 시각을 빼고 메시지마다 시각을 붙이도록 바꿔서, 다시 띄운 CLI가 AI 쪽에 저장된 캐시를 그대로 쓸 수 있어요. 대화 전체를 처음부터 다시 넣지 않아요.
- 안 쓰는 대화의 Claude · Codex CLI를 다시 10분 뒤에 닫아요(0.9.2에서 5분으로 줄였어요). 0.9.2가 5분으로 정해 둔 경우 한 번 10분으로 돌려놔요. 시간은 설정 › AI 채팅에서 직접 정할 수 있어요.

## 0.9.2

- AI chats name themselves per task, like `[◆ Opus • Code ] Fix the login`. The title changes only when you start something new. You can switch it off in Settings › AI chat.
- Pick several chats and groups and delete them at once: the ✓ next to the chat search, Ctrl+click, or ⋯ › Select.
- A chat you leave alone now closes its Claude or Codex CLI after 5 minutes instead of 10, which gives the memory back. Set minutes or hours in Settings › AI chat or at first start. 0 keeps it open.
- New choice: draw with the GPU (smooth) or the CPU (about 30 MB less memory). Pick it at first start or in Settings › Performance.
- Bloom also gives memory back after you close the chat or settings window, and frees more of it than before.
- Long lists open faster. The first rows show right away and the rest follow, and emoji are drawn in the background, so the emoji view no longer pauses.
- Settings are grouped into Basics, Launcher, AI and Tools. Browser and Connections moved into Search.
- After an update, Bloom shows what changed once. See it again in Settings › About.

### 한국어

- AI 대화 제목이 작업마다 `[◆ Opus • Code ] 로그인 수정`처럼 붙어요. 새 작업을 시작할 때만 바뀌어요. 설정 › AI 채팅에서 끌 수 있어요.
- 대화와 그룹을 여러 개 골라 한꺼번에 지울 수 있어요. 대화 찾기 옆 ✓, Ctrl+클릭, ⋯ › 선택.
- 안 쓰는 대화의 Claude · Codex CLI를 10분이 아니라 5분 뒤에 닫아서 메모리를 돌려줘요. 설정 › AI 채팅이나 처음 설정에서 분 · 시간으로 바꿀 수 있고, 0이면 닫지 않아요.
- 새로 고를 수 있어요: GPU로 그리기(부드럽게) 또는 CPU로 그리기(메모리 약 30 MB 덜 씀). 처음 설정이나 설정 › 성능에서 골라요.
- 채팅 창이나 설정 창을 닫은 뒤에도 메모리를 돌려주고, 전보다 더 많이 돌려줘요.
- 긴 목록이 빨리 떠요. 앞쪽 줄이 바로 보이고 나머지가 이어서 붙어요. 이모지도 뒤에서 그려서 이모지 화면이 멈칫하지 않아요.
- 설정을 기본 · 런처 · AI · 도구로 묶었어요. 브라우저와 연결은 검색으로 옮겼어요.
- 업데이트한 뒤 처음 켜면 바뀐 점을 한 번 보여 줘요. 설정 › 정보에서 다시 볼 수 있어요.

## 0.9.1

- AI answers can be copied in pieces. Drag over any part of an answer, your own message or a code block, in the Tab quick chat or the AI chat window, then press Ctrl+C or right-click › Copy.
- The AI chat window keeps up better while a long answer streams in.

### 한국어

- AI 답을 원하는 부분만 복사할 수 있어요. Tab 빠른 AI나 AI 채팅 창에서 답 · 내가 보낸 말 · 코드 블록을 드래그하고 Ctrl+C나 오른쪽 클릭 › 복사를 누르면 돼요.
- 긴 답이 들어오는 동안 AI 채팅 창이 덜 버벅여요.

## 0.9.0

- Type a path like `C:\Users`, `D:/work` or `%APPDATA%` and the folder comes first. Enter opens it, Tab goes into the folder you picked.
- Window layout: `left half`, `right third`, `top-left quarter`, `maximize`, `center` and `next screen` move the window in front.
- Dates and other cities' clocks: `in 100 days`, `days until christmas`, `days since 2026-03-01`, `new york time`, `tokyo 3pm`.
- Give anything its own shortcut: Ctrl+K › Set a shortcut on an app, folder, file, site, command or layout. They're listed in Settings › Tools.
- Bloom Lens has a fifth choice, Pin to the screen. The area stays on top as a picture you can drag, size and copy.
- Install apps with winget: type `install vlc`. Bloom asks first, installs it in the background and tells you how it went.
- Each new one can be switched off in Settings › Features.

### 한국어

- `C:\Users`, `D:/작업`, `%APPDATA%`처럼 경로를 적으면 그 폴더가 맨 위에 떠요. Enter로 열고, Tab으로 고른 폴더에 들어가요.
- 창 배치: `왼쪽 절반`, `오른쪽 1/3`, `왼쪽 위 1/4`, `최대화`, `가운데로`, `다음 화면으로`로 앞에 있는 창을 옮겨요.
- 날짜와 다른 도시 시간: `100일 뒤`, `크리스마스까지`, `2026-03-01부터`, `뉴욕 시간`, `도쿄 오후 3시`.
- 무엇이든 단축키를 붙일 수 있어요. 앱 · 폴더 · 파일 · 사이트 · 명령 · 창 배치에서 Ctrl+K › 단축키 지정. 붙인 단축키는 설정 › 도구에 모여 있어요.
- Bloom Lens에 5번 ‘화면에 고정’이 생겼어요. 고른 영역이 그림으로 맨 위에 떠 있고, 옮기고 크기를 바꾸고 복사할 수 있어요.
- winget으로 앱 설치: `설치 vlc`처럼 적으면 돼요. 먼저 묻고, 뒤에서 설치한 다음 결과를 알려 줘요.
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
