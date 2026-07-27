# ClaudeWidget

Windows 11용 초소형 always-on-top Claude 사용량 위젯. .NET 10 / WPF, 외부 NuGet 의존성 없음.

화면 점유 최소화가 최우선 설계 목표입니다. 실측값 (96 DPI 기준):

| 설정 | 크기 |
| --- | --- |
| 100%, 전체 표시 | **129 × 81 px** |
| 75%, 라벨 끔 | **77 × 61 px** |
| 50%, 전체 표시 | **64 × 40 px** |
| 50%, 라벨·리셋시각 끔 | **52 × 40 px** |

50%에서는 숫자는 읽히지만 라벨(`5H`/`7D`/`Fbl`)이 뭉개집니다. 그 크기로 쓸 거면
메뉴 → 표시 → 라벨을 끄는 편이 낫습니다. 폭이 64px에서 52px로 줄고 뭉갠 글자도 사라집니다.
읽기 편한 최소치는 **75% + 라벨 끔** 정도입니다.

```
╭──────────────────────────╮
│ ▬▬▬▬▭▭▭▭  59% 5H         │   5시간 세션 사용량
│ ▬▭▭▭▭▭▭▭  18% 7D         │   주간 전체 사용량
│ ▬▬▭▭▭▭▭▭  30% Fbl        │   Fable 전용 주간 사용량
│ ─────────────────────────│
│ 19:09  (1:23 남음)       │   5시간 창 리셋 시각 · 남은 시간
╰──────────────────────────╯
```

투명도는 **배경에만** 적용됩니다. 숫자와 게이지는 항상 불투명하게 유지됩니다
(`Window.Opacity` 대신 배경/테두리 브러시의 알파를 조절 — 작은 글자가 흐려지는 걸 막기 위함).

## 사용법

```powershell
dotnet run --project src\ClaudeWidget
```

| 조작 | 동작 |
| --- | --- |
| 드래그 | 위치 이동 (자동 저장) |
| `Ctrl` + 휠 | 크기 조절 (50%~200%) |
| 우클릭 / 우상단 `⋯` | 메뉴 |
| 트레이 아이콘 좌클릭 | 화면 밖으로 놓친 위젯 회수 |

메뉴: 새로고침 주기(1~5분) · 크기 · 투명도 · 표시 항목 토글 · **언어(한국어/English)** · 항상 위 ·
위치 잠금 · 시작 프로그램 등록

언어는 재시작 없이 즉시 바뀝니다. 메뉴 텍스트와 `(1:04 남음)` / `(1:04 left)`, 상태 툴팁이 대상입니다.
서비스 계층은 완성된 문장 대신 상태 코드(`UsageStatus`)를 반환하고 표시 시점에 번역하므로,
언어를 바꿔도 직전 조회 때 만들어진 메시지가 옛 언어로 남지 않습니다.

### 진단 · 미리보기

```powershell
ClaudeWidget.exe --probe   # 토큰 상태 + 로컬 기록 신선도 + 파싱된 버킷 값을 콘솔로 출력
ClaudeWidget.exe --demo    # API 호출 없이 예시 수치로 렌더링
```

`--probe`는 화면에 값이 안 나올 때 UI 문제인지 데이터 문제인지 가르는 용도입니다.
`--demo`는 크기·투명도를 맞춰볼 때 씁니다. 레이트 리밋에 걸린 상태에서도 동작하고
쿼터를 쓰지 않습니다. 별도 뮤텍스를 쓰므로 실제 위젯과 동시에 띄울 수 있습니다.

## 데이터 소스

두 곳에서 가져오고, **로컬 파일이 우선**입니다.

### 1순위 — `%USERPROFILE%\.claude\usage-history.jsonl`

**별도의 사용량 로거가 있을 때만 존재하는 파일입니다.** Claude Code 자체는 이 파일을
쓰지 않습니다 — 주기적으로 usage API를 폴링해 이 경로에 남기는 외부 도구(예: Python 기반
사용량 모니터)를 쓰고 있다면 위젯이 그 기록을 재활용합니다. 파일이 없거나 오래됐으면
그냥 2순위(API)로 내려가므로, 로거가 없어도 위젯은 정상 동작합니다.
필요한 세 버킷이 그대로 들어있습니다:

```json
{"ts":1784705573.5,"session":0.45,"weekly":0.17,"session_reset":1784714999,
 "weekly_reset":1785239999,"scoped":0.29,"scoped_reset":1785239999,"scoped_label":"Fable"}
```

값은 **분수(0.45)**라서 API의 백분율(45.0)과 다릅니다. 100을 곱해서 씁니다.

3분 이내의 기록이면 이걸 그대로 쓰고 네트워크를 건드리지 않습니다.

### 2순위 — OAuth API (로컬 기록이 3분 넘게 끊겼을 때만)

```
GET https://api.anthropic.com/api/oauth/usage
    Authorization: Bearer <accessToken>
    anthropic-beta: oauth-2025-04-20
```

토큰 조회 순서: `CLAUDE_CODE_OAUTH_TOKEN` 환경변수 → `%USERPROFILE%\.claude\.credentials.json`.

둘 다 실패하면 오래된 로컬 기록이라도 흐리게 표시합니다. 빈 위젯보다 낫습니다.

### 왜 로컬 파일이 우선인가

**이 엔드포인트는 토큰 단위로 레이트 리밋이 걸리고, 로거가 돌고 있다면 그 쿼터를
이미 로거가 쓰고 있습니다.** 위젯이 별도로 폴링을 하나 더 붙이면 한도를 넘깁니다.

실제로 관측된 내용입니다 — 수동 호출 몇 번을 얹자마자 429가 시작됐고,
`usage-history.jsonl`의 기록이 **429가 시작된 바로 그 시각에 멈췄습니다.**
기록기 자신도 같이 차단된 것입니다. 이후 45분 넘게 429가 유지됐고,
`Retry-After`는 `0`으로 내려와 그대로 믿으면 안 됩니다.

로컬 파일을 읽는 건 공짜이고, 최대 1분 이상 뒤처지지 않으며, 쿼터를 전혀 쓰지 않습니다.

### 응답 파싱 시 주의점

**모델별 사용량은 `limits[]` 배열에만 있습니다.** 최상위 `seven_day_opus`,
`seven_day_sonnet` 같은 필드는 실제로는 항상 `null`로 내려오므로 읽지 않습니다.

| `kind` | 의미 | 비고 |
| --- | --- | --- |
| `session` | 5시간 창 | |
| `weekly_all` | 주간 전체 | |
| `weekly_scoped` | 모델 한정 주간 | `scope.model.display_name`으로 구분 (예: `"Fable"`) |

최상위 `five_hour` / `seven_day`는 `limits[]`가 없는 응답에 대한 폴백으로만 사용합니다.
표시되는 숫자는 **사용한 양**(API의 `percent` 원본)입니다.

### API 폴백 경로의 방어 장치

2순위 경로로 내려갔을 때만 적용됩니다:

- 실제 네트워크 호출은 폴링 주기와 무관하게 최소 20초 간격 (수동 새로고침 연타는 캐시로 응답)
- 429를 받으면 `Retry-After`, 없거나 0이면 1분 동안 호출 중단
- 연속 실패 시 폴링 간격을 1x → 2x → 4x → 8x로 백오프
- 실패해도 마지막 값을 흐리게 계속 표시 (`Opacity` 0.45)

위젯 전체에 마우스를 올리면 데이터 기준 시각과 출처가 툴팁으로 나옵니다.

## 토큰 — 위젯은 읽기만 합니다

액세스 토큰은 약 8시간마다 만료됩니다. Claude Code를 쓰는 동안은 CLI가
`.credentials.json`을 갱신하므로 위젯은 매 폴링마다 파일을 다시 읽는 것만으로 유효한 토큰을 얻습니다.

**위젯은 토큰을 직접 재발급하지 않습니다. refresh token은 읽지도 않습니다.**
OAuth refresh token은 1회용(회전식)인데 CLI와 파일 하나로 공유됩니다. 초기 버전은
만료 시 직접 재발급 후 파일에 되써주는 방식이었지만, CLI가 메모리에 들고 있던 예전
refresh token으로 재발급을 시도하는 순간 서버가 이를 토큰 탈취로 간주해 **토큰 계열
전체를 무효화했고, 위젯과 CLI 양쪽 로그인이 함께 풀리는 사고**가 실제로 발생했습니다.
재발급 권한이 두 프로그램에 있는 한 이 경합은 타이밍으로 피할 수 없어서, 재발급은
CLI의 전유물로 두고 위젯은 순수 소비자로 남습니다.

토큰이 만료되면 위젯은 네트워크 호출 없이 리셋 시각 자리에 **"Claude Code 로그인 필요"**
안내를 띄웁니다. Claude Code를 한 번 열면 CLI가 파일을 갱신하고, 위젯은 다음 폴링에서
자동으로 복구됩니다.

## 알려진 제약

`AllowsTransparency="True"`인 WPF 창은 ClearType 서브픽셀 안티에일리어싱이 꺼집니다.
투명도를 자유롭게 조절하는 대가로 아주 작은 글자가 약간 부드럽게 보입니다.
Win11의 작은 크기 전용 폰트(`Segoe UI Variable Small`)와 tabular figures로 보완했습니다.

## 빌드

두 가지 릴리즈를 만듭니다. 둘 다 단일 exe이고 기능은 동일합니다.

```powershell
# 런타임 의존 — .NET 10 Desktop Runtime 필요
dotnet publish src\ClaudeWidget\ClaudeWidget.csproj -c Release -r win-x64 `
  -p:SelfContained=false -p:PublishSingleFile=true -p:DebugType=none `
  -o publish\framework-dependent

# 독립 실행 — 런타임 없는 PC에서도 실행
dotnet publish src\ClaudeWidget\ClaudeWidget.csproj -c Release -r win-x64 `
  -p:SelfContained=true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
  -o publish\self-contained
```

| | 크기 | 첫 실행 | 요구사항 |
| --- | --- | --- | --- |
| `framework-dependent` | **0.24 MB** | 0.4초 | .NET 10 Desktop Runtime |
| `self-contained` | **66 MB** | 2.7초+α (압축 해제) | 없음 |

`--self-contained false` 형태는 무시되는 경우가 있습니다. 반드시 `-p:SelfContained=false`로 주세요 —
그렇지 않으면 런타임이 통째로 번들된 "런타임 의존" 빌드가 나옵니다.

독립 실행 버전은 csproj의 `EnableCompressionInSingleFile`(SelfContained일 때만 적용)로
관리 어셈블리를 압축해 165 MB → 66 MB로 줄였습니다. 대신 시작할 때마다 메모리에서
압축을 푸는 비용이 붙습니다. .NET 프레임워크 현지화 리소스도 `SatelliteResourceLanguages`로
영어/한국어만 남겼습니다 (−6 MB, 앱 자체 UI 문자열에는 영향 없음).

트리밍(`PublishTrimmed`)과 NativeAOT는 WPF에서 지원되지 않으므로 켜지 마세요.

## 구조

```
src/ClaudeWidget/
├─ Models/
│  ├─ OAuthUsageResponse.cs   API DTO
│  └─ UsageSnapshot.cs        정규화된 3버킷 + 위젯 상태
├─ Services/
│  ├─ UsageProvider.cs        출처 결정 (로컬 우선 → API 폴백)
│  ├─ UsageHistoryReader.cs   usage-history.jsonl tail 읽기
│  ├─ UsageClient.cs          OAuth API + 스로틀 · 429 처리
│  ├─ CredentialStore.cs      토큰 읽기 (읽기 전용 — 재발급 없음)
│  └─ SettingsStore.cs        설정 · 시작 프로그램 등록
│  └─ Strings.cs             한국어/영어 UI 텍스트
├─ Controls/BarGauge.cs       알약형 막대 게이지 (OnRender 직접 그리기)
├─ ViewModels/MainViewModel.cs
└─ MainWindow.xaml            투명 창 · 드래그 · 스케일 · 메뉴
```

설정 저장 위치: `%APPDATA%\ClaudeWidget\settings.json`
