<p align="center"><img src="docs/media/mark.png" width="88" alt=""></p>

<h1 align="center">Bloom</h1>

<p align="center"><b>Win 키 한 번, 한 줄이면 끝.</b><br>
Windows용 가벼운 네이티브 런처예요. 앱 실행부터 계산, 파일, AI 채팅, 화면 번역까지 검색창 하나에서 해요.</p>

<p align="center">
  <a href="#설치--빌드">설치</a> · <a href="#기능">기능</a> · <a href="#단축키">단축키</a> · <a href="#ai-기능에-대해">AI와 개인정보</a> · <a href="README.md">English</a>
</p>

<p align="center">
  <img src="docs/media/loop.webp" alt="Bloom 데모: 한/영 오타로 Chrome 찾기, 계산, 평 단위 변환, 알림, 이모지, 클립보드 기록, 컬러 피커, AI에게 묻기, 승인 카드로 PC 작업 맡기기, 런처 옆에 메모 붙이기, Claude Code 작업 완료 알림, 화면 번역, Space Cleaner">
</p>

<p align="center"><sub>WPF · .NET 10 · WebView 없음 · NuGet 패키지 없음 · 모두 끈 상태 약 80 MB · 한국어 / English · MIT</sub></p>

- **한 줄로 다 해요.** 앱 · 파일 · 계산 · 알림 · 이모지 · 웹 검색이 검색창 하나에 있어요. 한/영을 안 바꿔도, 초성만 쳐도 찾아요.
- **쓰던 AI를 그대로 써요.** API 키 없이, 이 PC에 로그인해 둔 Claude Code · Codex로 묻고 파일 정리 · 메모 · 타이머 · 일정까지 맡겨요.
- **가볍고 내 마음대로예요.** WebView 없는 네이티브 앱이에요. 안 쓰는 기능은 꺼 두면 메뉴 · 단축키 · 메모리에서 빠져요.

## 기능

### 검색창 하나로

<img src="docs/media/search.webp" alt="ㅁㅁㅈ를 치자 메모장이 맨 위에 뜬 검색창, 그리고 한 줄에 적을 수 있는 것들: 초성, 한/영 오타, 계산, 단위, 이모지, 타이머, 사이트 검색">

이름을 다 치지 않아도, 키보드가 한글에 놓여 있어도 찾아요. 식은 적는 동안 계산되고, 시간은 말하듯이 적으면 돼요.

- **앱 · 설정 · 명령**: 시작 메뉴 앱, Windows 설정 페이지, Bloom 명령을 찾아요.
  - 초성(`ㅁㅁㅈ` → 메모장), 한/영을 잘못 두고 친 글자(`초개ㅡㄷ` → Chrome), 영어 이름의 첫 글자(`gc` → Google Chrome)도 알아들어요.
  - 앱 이름은 Bloom 안에서만 바꿔 부를 수 있어요.
- **즐겨찾기**: 홈의 아이콘 줄에 앱 · 파일 · 사이트 · 명령을 두고 Ctrl+1…9로 열어요.
  - 폴더로 묶을 수 있어요.
  - 아이콘은 이름에 맞춘 그림으로 바꾸거나 AI에게 그려 달라고 할 수 있어요.
- **계산 · 단위 · 환율**: `45000*1.1`, `10 더하기 5 곱하기 2`, `3억 5천만 × 2`, `5!`, `84m2 to 평`, `5km to mi`, `100 usd`
  - 결과에서 Enter를 누르면 값이 복사돼요.
- **타이머 · 알림 · 캘린더**: `10분 타이머`, `30분 뒤 빨래`, `오후 3시 반에 회의 알림`, `내일 9시 업로드 리마인더`
  - 알림은 월간 달력과 홈의 '이번 주'에도 보여요.
  - 때가 되면 소리와 함께 카드가 떠요(5분 뒤 다시 · 끄기).
- **웹 검색 · 단축 검색**: 적은 그대로 Google · 네이버 · Bing · DuckDuckGo 검색으로 넘겨요.
  - 단축 검색은 단축어 뒤에 검색어를 붙여요: `yt 고양이`, `n 날씨`, `gh bloom`, `map 성수역`.
  - 단축 검색은 직접 만들고 고칠 수 있어요. 검색과 링크를 열 브라우저도 골라요.
- **파일 · 최근 파일**
  - [Everything](https://www.voidtools.com/)이 있으면 파일 이름으로 바로 찾아요. 영상 · 사진 · 문서 · 폴더처럼 종류로 좁히고 미리 봐요.
  - Everything이 없으면 Windows의 최근 파일에서 찾아요.
- **창 전환**: 열린 창을 이름으로 찾아 바로 넘어가고, Ctrl+W로 닫아요.
- **이모지 · 특수문자**: `:하트`, `arrow`, `섭씨`처럼 한국어나 영어 이름으로 찾아 붙여 넣어요.
- **시스템**
  - 다크 모드, 소리 · 마이크 끄기, `볼륨 30`, 출력 장치 바꾸기, Wi-Fi · 블루투스, 숨김 파일 · 확장자 보기
  - 절전, 화면 끄기, 휴지통 비우기, 프로세스 종료, 화면 잠금 · 다시 시작 · 종료
- 어떤 결과에서든 **Ctrl+K**를 누르면 그 항목으로 할 수 있는 일이 다 나와요. 파일이면 이름 바꾸기 · 다른 폴더로 옮기기 · 복사 · 휴지통이 있고, 탐색기와 같은 방식이라 진행 창과 이름 충돌 질문도 그대로예요.
- 같은 글자를 치고 고른 항목은 다음부터 위로 올라와요. 새 습관이 예전 것을 이기고, 설정에서 지울 수 있어요.

### AI — 내 Claude Code · Codex 로그인 그대로

<img src="docs/media/ai.webp" alt="채팅에서 견적서 PDF를 문서 폴더로 옮기기 전에 허용 · 항상 허용 · 거절을 묻는 카드, 그리고 Claude Code · Codex의 작업 알림 카드들">

API 키는 필요 없어요. 이 PC에 설치하고 로그인해 둔 Claude Code나 Codex CLI가 답해요.

- **바로 묻기**: 검색창에 적고 **Tab**을 누르면 그 자리에서 물어요. Shift+Tab은 새 대화예요. 모델과 생각 수준을 고를 수 있어요.
- **AI 채팅 창**(Ctrl+Alt+C)
  - 지난 대화를 이어 하고, 그룹(폴더)으로 정리해요.
  - 파일과 사진을 첨부해요(끌어다 놓기 · Ctrl+O · Ctrl+V).
  - AI가 만든 파일은 바로 열거나 저장해요.
- **PC 작업 맡기기**: "어제 받은 견적서 PDF 문서 폴더로 옮겨 줘", "볼륨 30으로", "다운로드에서 지난주 영상 찾아 줘"처럼 말하면 해요.
  - 무언가를 바꾸는 일은 매번 채팅 안의 카드로 물어봐요(허용 · 이 대화에서 항상 허용 · 거절).
  - 한 일은 답 아래에 ✓ / ✕로 남아요.
- **메모 · 타이머 · 일정은 Bloom 안으로**
  - "메모에 장보기 목록 적어 줘"라고 하면 채팅 옆에 Bloom 메모 창이 붙고 거기에 적혀요.
  - "25분 타이머", "내일 오후 3시 회의 잡아 줘"는 Bloom 타이머와 캘린더에 들어가요.
  - 파일을 만들거나 다른 앱을 열지 않아요.
- **AI 빠른 동작**(Ctrl+Alt+A): 아무 앱에서 고른 글자(없으면 클립보드)를 바꿔서 그 자리에 붙여 넣어요.
  - 요약 · 번역 · 맞춤법 · 다듬기 · 줄이기 · 늘리기 · 정중하게 · 편하게 · 핵심만 목록으로 · 쉽게 설명 · 답장 초안
  - 직접 적은 지시도 돼요.
- **사용량**: 홈에 Claude · ChatGPT의 5시간 · 주간 한도를 쓴 만큼 또는 남은 만큼으로 보여 줘요. 5분이나 10분마다 갱신돼요.
- **작업 알림**: Claude Code · Codex가 일을 시작했을 때, 끝났을 때, 승인을 기다릴 때, 한도가 가까워지거나 다시 찼을 때 화면 구석에 카드와 소리로 알려요.
  - 위치 6곳과 크기, 소리, 알릴 일을 고를 수 있어요.
  - 전체 화면 게임이나 발표 중에는 띄우지 않아요.

### 화면에서 — Bloom Lens · 컬러 피커

<img src="docs/media/lens.webp" alt="화면의 영어 메시지를 Bloom Lens로 골라 번역 카드에 한국어로 띄운 모습과, #FF7A9E를 연 컬러 피커">

- **Bloom Lens**(Ctrl+Alt+L): 화면 영역을 끌어서 고른 뒤 할 일을 숫자로 골라요.
  - `1` AI에게 묻기: 비우고 Enter를 누르면 그 화면을 설명해 줘요.
  - `2` 번역
  - `3` 글자 복사(Windows OCR)
  - `4` 실시간 번역: 그 영역을 계속 읽어 바뀔 때마다 번역해요.
  - 번역(Ctrl+Alt+T)과 글자 복사(Ctrl+Alt+O)는 바로 가는 단축키도 있어요.
- **컬러 피커**: 돋보기로 화면의 색을 찍거나 색 코드를 붙여 넣어요.
  - HEX · RGB · HSL을 보여 주고, 흰 글자 · 검은 글자와의 대비도 알려 줘요.
  - 그 색에서 만든 그라데이션을 추천해요(CSS 복사).
  - 최근 색을 모아 둬요.

### 옆에 붙는 도구

<img src="docs/media/tools.webp" alt="고정한 런처의 오른쪽에 메모, 아래에 계산기가 붙어 있는 모습">

- **Ctrl+J**(또는 위쪽 막대의 도구 버튼)로 메모 · 타이머 · 계산기 · 클립보드를 작은 창으로 열어요. 한 번 더 누르면 메뉴가 닫혀요.
- **어디든 자유롭게 놓아요.**
  - 머리 부분을 잡고 끌어 옮기고, 가장자리를 잡고 크기를 바꿔요.
  - 런처나 다른 도구, 화면 가장자리에 가까워지면 착 붙어요.
  - 런처 옆에 놓으면 그 자리와 크기 그대로 붙어서, 런처와 함께 움직이고 함께 보였다 숨어요.
  - 떼어 놓으면 따로 떠 있어요(항상 위 켜고 끄기).
  - 붙은 도구를 쓰기 시작하면 런처가 저절로 고정돼요.
- **메모**: 여러 메모가 적는 대로 저장돼요. 고정할 수 있고, 지우면 휴지통으로 가요.
  - 검색창에 `메모 우유 사기`라고 치면 바로 메모가 돼요.
  - 메모 내용도 검색돼요.
- **타이머 · 알림**: `10분`, `25분 집중`, `오후 3시 회의`처럼 적고 Enter(또는 ↵)를 누르면 시작돼요. 남은 시간이 보이고 5분 더 · 취소도 돼요.
- **계산기**: 런처의 계산과 같은 식을 적어요. Enter로 답을 복사하고 기록으로 남겨요.
- **클립보드 기록**(Ctrl+Alt+V): 복사한 텍스트 · 이미지 · 파일을 모아 둬요. 누르면 다시 복사되고, 고정할 수 있어요.
- **런처 고정 · 이동 · 크기**
  - 📌(Ctrl+P)로 런처를 화면에 남겨 둬요.
  - 아래쪽 바를 끌면 원하는 곳으로 옮겨지고, 두 번 누르면 가운데로 돌아가요.
  - 크기는 Ctrl+= / Ctrl+- / Ctrl+0(80~150%)으로 바꿔요. 런처 · 도구 · AI 채팅 · 설정 창이 함께 바뀌어요.

### Bloom Space Cleaner

<img src="docs/media/space.webp" alt="폴더 안의 영상 · 다운로드 · 사진 · 프로젝트가 크기만큼의 블록으로 나뉜 지도와 큰 파일 목록">

드라이브나 폴더의 용량을 크기만큼의 블록으로 나눈 지도로 보여 줘요.

- 블록을 눌러 안으로 들어가고, 옆의 큰 파일 목록에서 골라 휴지통으로 보내요.
- 드라이브를 바꾸거나 폴더를 골라 볼 수 있어요.
- 드라이브 루트와 Windows 폴더는 건드리지 않아요. 휴지통으로 보낸 것은 되살릴 수 있어요.

### 내 방식대로

<img src="docs/media/yours.webp" alt="설정의 기능 페이지(기능마다 켜고 끄는 스위치)와 성능 페이지(애니메이션 · 유리 효과 · 메모리)">

- **내 명령**: 자주 하는 일을 단축어 하나로 실행해요.
  - 스크립트: .ps1 · .bat · 프로그램, 또는 PowerShell 한 줄. 창 없이 돌리고 결과는 알림 카드로 알려 줘요.
  - 스니펫: 자주 쓰는 글을 앞에 열린 창에 붙여 넣어요(`{date}` `{time}` `{clipboard}`).
  - 묶음: 검색창에 적을 줄을 차례로 실행해요(`메모장 ; 볼륨 30 ; 기다리기 2초 ; yt lofi`).
  - 단축어 뒤에 적은 말은 `{input}` 자리에 들어가요(`bk D:\사진`).
  - 스크립트 파일은 Ctrl+K > 내 명령으로 바로 등록해요.
- **홈 화면**(Ctrl+E): 빈 검색창에 보일 블록(즐겨찾기 · AI 사용량 · 이번 주 · 타이머 · 최근 사용 · 명령 …)을 고르고 순서를 바꿔요.
- **기능 켜고 끄기**: 안 쓰는 기능(AI 채팅 · Bloom Lens · 계산기 · 타이머 · 컬러 피커 · 이모지 · 창 전환 …)을 끄면 검색 결과 · 명령 목록 · 홈 · 즐겨찾기 줄에서 빠지고 단축키도 풀려요. 저장된 것은 지우지 않아요.
- **모양 · 성능**
  - 다크 · 라이트 · 시스템 테마, 강조색 프리셋
  - 애니메이션 켜기 · 줄이기 · 끄기, 초당 횟수(30 · 60 · 최대), 유리 효과 끄기
  - 메모리를 쓰는 기능은 설정에 늘어나는 양을 적어 뒀어요.
- **언어 · 처음 설정 · 옮기기**
  - 화면 언어는 한국어 · English(자동은 Windows 언어를 따라요)예요.
  - 처음 켜면 여는 방법 · 홈 · AI를 고르는 안내가 나와요.
  - 설정 · 즐겨찾기 · 앱 이름 · 브라우저 바로 가기를 파일 하나로 내보내고 가져와요.

## 단축키

| 키 | 하는 일 |
| --- | --- |
| `Win` · `Alt+Space` | Bloom 열기 |
| `Tab` / `Shift+Tab` | 검색창의 글자로 AI에게 묻기 / 새 대화로 묻기 |
| `Ctrl+K` | 고른 결과로 할 수 있는 일 |
| `Ctrl+1` … `Ctrl+9` | 즐겨찾기 |
| `Ctrl+J` | 도구 (메모 · 타이머 · 계산기 · 클립보드) |
| `Ctrl+P` | 런처 고정 |
| `Ctrl+E` | 홈 편집 |
| `Ctrl+=` · `Ctrl+-` · `Ctrl+0` | 크게 · 작게 · 100% |
| `Esc` | 뒤로 → 글자 지우기 → 닫기 |
| `Ctrl+Alt+C` | AI 채팅 창 |
| `Ctrl+Alt+A` | AI 빠른 동작 |
| `Ctrl+Alt+L` | Bloom Lens |
| `Ctrl+Alt+T` · `Ctrl+Alt+O` | 화면 번역 · 화면 글자 복사 |
| `Ctrl+Alt+V` | 클립보드 기록 |

전역 단축키는 설정에서 기능마다 바꾸거나 끌 수 있어요. 창 전환 · 이모지 · 컬러 피커 · 바로 AI에게 묻기 · 실시간 번역에도 단축키를 줄 수 있어요(처음엔 꺼져 있어요).

## AI 기능에 대해

Bloom은 **로그인 정보를 받거나 저장하지 않습니다.** AI 기능은 이 PC에 이미 설치하고 로그인해 둔 공식 CLI를 실행할 뿐입니다.

- Claude: [Claude Code](https://docs.anthropic.com/en/docs/claude-code) CLI (`claude`)
- ChatGPT: [Codex CLI](https://github.com/openai/codex) (`codex`)

사용량과 이용 약관은 각자의 계정 기준입니다. CLI가 없으면 AI 기능만 꺼진 채로 나머지는 그대로 동작해요.

**AI가 PC에서 작업하기** (Claude · ChatGPT, 설정 > PC 작업 · MCP)

- Bloom은 이 PC 안에서만 열리는 MCP 서버(`Bloom.exe --mcp`, 이 Windows 사용자만 여는 named pipe)로 도구 하나(`pc`)를 내놓아요.
- 할 수 있는 일은 다섯 묶음으로 나뉘고, 설정에서 묶음마다 켜고 꺼요.
  - **찾아보기**: 앱 · 파일(Everything) · 최근 파일 · 창 · 타이머와 일정 · 메모 · 내 명령. 묻지 않고 바로 찾아요.
  - **열기 · 실행**: 앱 · 파일 · 사이트 열기, 검색창 한 줄 실행(`볼륨 30`, `yt lofi`), 내 명령 실행.
  - **메모 · 타이머 · 일정**: Bloom 메모에 적기(채팅 옆에 메모 창이 떠요), 타이머, 캘린더 일정.
  - **파일 정리**: 이름 바꾸기 · 옮기기 · 복사 · 휴지통.
  - **클립보드**: 읽기 · 넣기.
- 찾아보기 말고는 **채팅 안의 허용 / 이 대화에서 항상 허용 / 거절 버튼**으로 매번 물어봐요(Claude Code의 `--permission-prompt-tool stdio`). 한 일은 답변 아래에 ✓ / ✕로 남아요.
- 이 PC에 이미 등록된 다른 MCP 서버도 같은 화면에 서버마다 스위치로 나와요(기본 꺼짐).
  - Claude Code · Claude 데스크톱 · Cursor · VS Code · Windsurf · Codex 설정과, Claude Code로 연 프로젝트의 `.mcp.json`에서 찾아요.
  - 켠 서버의 도구도 쓸 때마다 같은 허락 카드로 물어요.
  - ChatGPT는 Bloom이 서버 앞에 중계(`Bloom.exe --mcp-proxy`)를 두고 물어요.
  - 서버 설정은 Bloom에 복사하지 않고 그때그때 원래 파일에서 읽어요.
- 도구 정의는 한 개로 줄여서, 요청마다 입력이 도구 없음 대비 약 +500 토큰 늘어요. 메모 · 타이머 · 일정이 들어가기 전에 +400을 측정했고, 늘어난 설명 길이로 어림한 값이에요. 끈 묶음만큼 더 줄어요. 사용자의 다른 MCP 서버는 불러오지 않아요(`--strict-mcp-config`).
- ChatGPT(Codex) 대화에서도 같은 도구를 써요.
  - Codex는 실행 모드에서 스스로 묻지 못해서, Bloom의 MCP 서버가 동작 직전에 같은 허락 카드를 띄우고 답을 기다려요.
  - 대화마다 실행할 때 `-c mcp_servers.bloom…`으로만 붙여요.
  - Codex는 MCP 도구 설명을 필요할 때만 불러오므로, 도구 이름과 동작을 한 줄로 알려 줘요(요청마다 약 +250 토큰, 같은 방식으로 어림).

AI 사용량은 `claude -p /usage`(모델 호출 없음)와 `codex app-server`의 한도 조회로 받아요. 작업 알림은 이 PC에 있는 Claude Code · Codex의 기록(`~/.claude/sessions`, `~/.codex/sessions`)을 읽기만 하고 어디로도 보내지 않아요.
AI에게는 사용자가 직접 보낸 내용(질문, 첨부한 파일, Bloom Lens로 고른 화면, 번역할 글자)과 PC 작업 도구가 돌려준 결과만 전달됩니다.
Claude는 그 대화의 폴더(`%LOCALAPPDATA%\Bloom\ai\files\<대화>`) 안에서만 파일을 읽고 쓸 수 있어요. ChatGPT(Codex)는 파일을 직접 다루지 않아서, 텍스트 첨부는 메시지에 담아 보내고 만든 파일은 답변에서 꺼내 저장합니다.

Bloom은 Anthropic · OpenAI · voidtools · Raycast와 관련 없는 비공식 개인 프로젝트입니다.

## 설치 · 빌드

필요한 것: Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download) (실행만 할 때는 .NET 10 Desktop Runtime)

```powershell
dotnet build src\Bloom\Bloom.csproj -c Release
# 또는 설치본 만들기
powershell -File tools\package.ps1   # → dist\app
powershell -File tools\install.ps1   # → %LOCALAPPDATA%\Programs\Bloom, 시작 메뉴 바로 가기
```

데이터는 `%APPDATA%\Bloom`과 `%LOCALAPPDATA%\Bloom`에 저장돼요.
개발용 실행은 `Bloom.exe --data <폴더>`로 다른 데이터 폴더를 쓸 수 있어요(`--lang ko|en`으로 언어 고정).

## 코드 구조

<details>
<summary>폴더 구성 · 개발 규칙 · 테스트와 Lint</summary>

`src/Bloom` 아래, 폴더 하나가 기능 하나입니다. 큰 클래스는 `partial`로 주제별 파일에 나눠 두었어요.

| 폴더 | 내용 |
| --- | --- |
| `App/` | `Host`(앱 전체의 연결: 시작 · 단축키 · 명령 · Lens · 타이머/컬러 · 내 명령 실행 · 파일 동작), 작업 알림(`Notices`), MCP 서버(`Mcp/` — 중계 · 서버 · `pc` 도구), 실행 옵션, 트레이, Win 키 훅 |
| `App/Engine/` | 검색창에 보일 결과 만들기 — 루트 검색, 홈 블록, 항목 변환, 보기별(파일 · 클립보드 · 캘린더 · 타이머 · 색 …) |
| `App/Qa/` | 화면 밖 캡처 QA (`--capture <폴더> --qa-set v15 --data <폴더> --lang en`) |
| `Core/` | 저장되는 것(설정 · 즐겨찾기 · 사용 기록 · 입력어 학습 · 타이머 · 메모 · 색)과 JSON 저장, 한글 검색, 매칭, 화면 언어(`Lang` — `Tr("한국어", "English")`) |
| `Search/` | 앱 목록, 계산기(식 · 단위 · 환율), Everything, 열린 창, 최근 파일, 이모지 · 기호(`Assets/symbols.tsv`), 시스템 항목, 내 명령(`UserCommands`), 결과 모델 |
| `Ai/` | Claude Code / Codex CLI 세션과 대화 · 그룹(`Chats`), 한 번 묻기(`OneShot`), 번역, AI 빠른 동작, AI 아이콘, 사용량(`AiUsage`), 작업 기록 읽기(`ClaudeWatch` · `CodexWatch`) |
| `Ocr/` | 화면 영역 고르기, Windows OCR, 번역 창, 색 찍기 |
| `Clip/`, `Space/`, `Browser/` | 클립보드 기록, 디스크 공간 지도, 브라우저로 넘기기 |
| `Native/` | Win32 호출(System32의 DLL만), WinRT 호출(`WinRt`), 시스템 토글(`SystemControl` — 소리 · 무선 · 다크 모드 …), 탐색기 방식 파일 작업(`ShellFiles`) |
| `Ui/Tools/` | 도구 창: 자유 배치 · 붙이기 · 자석 정렬 · 크기 맞춤(`ToolDock`), 창 틀(`ToolWindow`), 메모 · 타이머 · 계산기 · 클립보드 |
| `Ui/` | 런처 창(`Launcher/` — 보기마다 칩 · 안내 · 결과를 `Launcher.ViewSpecs.cs` 한 곳에), AI 채팅 창(`Chat/`), 설정 창(`Settings/`), 알림 카드, 공용 컨트롤(`Controls/` — 연속 곡률 모서리 `SmoothBorder`, `Dropdown`, `PopupMenu`), 테마(`Theme/` — 애니메이션 설정은 `Motion`) |
| `tests/Bloom.Tests` | 자동 테스트(계산기 · 시간 읽기 · 매칭 · 학습 · 이모지 · 언어 · AI 프롬프트 · MCP 도구 · 사용량 읽기 · 작업 기록 · 내 명령) — 테스트 프레임워크 없이 콘솔 실행 |

규칙: 새 기능은 `Engine.<기능>.cs`(결과) + `Launcher.ViewSpecs.cs`에 보기 한 줄 + 필요하면 `Core/<기능>.cs`(저장)로 나눠요.
화면에 보이는 글자는 모두 `Tr("한국어", "English")`로 적어요. 검색 키워드와 파서 데이터는 두 언어를 함께 둬요.
QA나 개발 실행은 항상 `--data <임시 폴더>`로 실제 데이터와 분리해요.

```powershell
# 테스트 (종료 코드 = 실패 수)
dotnet run --project tests\Bloom.Tests
# Lint (분석기 + 서식) — 경고 0
dotnet build src\Bloom\Bloom.csproj -c Debug --no-incremental -p:AnalysisLevel=latest-recommended -p:EnforceCodeStyleInBuild=true
dotnet build tests\Bloom.Tests\Bloom.Tests.csproj -c Debug --no-incremental -p:AnalysisLevel=latest-recommended -p:EnforceCodeStyleInBuild=true
dotnet format whitespace src\Bloom\Bloom.csproj --verify-no-changes
dotnet format whitespace tests\Bloom.Tests\Bloom.Tests.csproj --verify-no-changes
```

</details>

## 선택 연동

없어도 되는 것들입니다. 없으면 관련 블록과 설정이 보이지 않아요.

- **Everything**: 파일 찾기.
- **Claude Code CLI · Codex CLI**: AI 채팅 · Lens · 번역 · 빠른 동작 · 사용량 · 작업 알림.

## 라이선스

MIT — [LICENSE](LICENSE). 글꼴(Bloom Sans = [Wanted Sans](https://github.com/wanteddev/wanted-sans))은 SIL OFL 1.1 — `src/Bloom/Fonts/OFL.txt`.
환율은 [Rates By Exchange Rate API](https://www.exchangerate-api.com).
