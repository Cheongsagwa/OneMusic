# OneMusic — 인수인계 문서

YouTube Music 네이티브 윈도우 클라이언트. 원래 쓰던 Electron 앱(pear-desktop + 직접 만든 One UI 테마 플러그인)의
화면 · 동작을 그대로 옮기면서 메모리를 줄이는 게 목표. (측정: OneMusic 약 180MB / pear-desktop 약 2.6GB)
pear 테마 원본은 `reference/pear-oneui-theme/` (style.css 토큰 · bar-icons.ts · scroll-fade.ts …)

## 기술
- C# / .NET 8 / **WinUI 3** (Windows App SDK 1.6, 패키지 없는 실행 파일), Win2D(파도 재생 바 · 가사 · 흘리는 글자)
- UI 는 거의 전부 C# 코드로 만든다 (XAML 은 `App.xaml` 하나). 스타일 토큰은 `src/UI/Theme.cs`
- **윈도우에서만 빌드 · 실행된다.** 리눅스 클라우드 세션에선 코드 수정만 하고, 빌드는 GitHub Actions(`.github/workflows/build.yml`)가 한다
  - Actions 탭 → 최신 실행 → `OneMusic-win-x64` 아티팩트에 exe 가 들어 있다
  - 클라우드에서 확인: `curl -s "https://api.github.com/repos/Cheongsagwa/OneMusic/actions/runs?branch=<브랜치>&per_page=1"` → conclusion,
    실패하면 jobs 의 id 로 `mcp__github__get_job_logs`

## 구조 — Core
- `InnerTube.cs` — YouTube Music 내부 API(WEB_REMIX) 직접 호출. `Post(..., web: true)` 는 www.youtube.com(WEB) — 댓글용
  - 검색(맨 위 카드 버튼 · 갈래 · 칩) · 추천어 · 홈/둘러보기/보관함(칩 · 셸프 · 이어 받기) · 다음 트랙(라디오 · 칩 · automix) · 상세 화면 · 관련 항목
  - ⋮ 메뉴는 YT 가 주는 `menuRenderer` 를 그대로 파싱(`ParseMenu`) → `TrackMenu.RunNode` 가 엔드포인트별로 실행
  - 다음 트랙 한 칸에 음원(ATV)과 뮤비(OMV)가 같이 오면 **음원만 고르고** 뮤비는 `CounterpartId`(노래/동영상 전환)
  - 긴 목록(100곡 넘는 플레이리스트 · 보관함 노래 · 기록): `TrackToken` → `MoreTracks(token)` (새 방식 body continuation, 비면 ctoken)
  - 댓글: commentEntityPayload + toolbarSurface(좋아요/싫어요/답글 동작) + toolbarState(내가 누른 상태). `CommentAction` · `PostReply` · `PostComment`
  - 시간 가사: 가사 탭 browseId 를 iOS 앱(clientName 26) 으로 browse → timedLyricsData
- `TrackPager.cs` — 긴 목록 다음 묶음. 상세 화면(스크롤 끝 `MoreSentinel`)과 재생기(목록 끝 5곡 전)가 같은 것을 같이 쓴다
- `Auth.cs` + `UI/LoginWindow.cs` — **한국은 로그인 없이는 YT Music 응답이 안 온다**. WebView2 로그인 → youtube.com 쿠키를 DPAPI 로 암호화 저장 → SAPISIDHASH
- `StreamResolver.cs` — 스트림 주소는 **yt-dlp**(처음 필요할 때 %LocalAppData%\OneMusic 에 자동 다운로드).
  - 사흘 넘게 묵으면 뒤에서 `yt-dlp -U`, 주소 받기가 실패하면 한 번 업데이트 후 다시 (YouTube 가 바뀌어도 계속 재생되게)
  - 오디오는 2MB 조각 6개씩 동시에 받아 **통째로 메모리에** (32분짜리도 됨) → 복사 없이 `MemoryStream.AsRandomAccessStream()` 으로 재생기에. 최근 3곡만 들고 있음
  - 뮤직비디오는 mp4(소리+화면) 주소를 재생기가 바로 흘려 받는다
- `Mp4Fix.cs` — 일부 곡의 MP4 편집 목록(edts) 때문에 열자마자 끝나는 문제 → `free` 로 바꿔 무시
- `PlayerService.cs` — Windows `MediaPlayer` (미디어 키 · 시스템 미디어 컨트롤 자동)
  - 큐 = 고른 곡들 `Queue[..AutoStart]` + 자동재생 구간 `Queue[AutoStart..]`
  - **자동재생 토글은 끝이 있는 목록(검색에서 튼 곡 · 앨범 · 플레이리스트)일 때만**, 라디오/믹스(`Radio`)는 토글 없이 알고리즘 칩이 맨 위 (YT Music 과 같음)
  - 칩(전체 · 친숙한 곡 · 2020년대 …)을 고르면 칩 엔드포인트 그대로 `next` → **지금 곡 바로 다음부터** 바꾼다 (`SelectChip` → `LoadAuto(fromNext)`)
  - 앨범 · 플레이리스트 뒤는 automix(`RDAMPL`+목록 ID), 비면 마지막 곡 라디오. 라디오에 곡을 직접 넣으면 `LeaveRadio()` 로 토글 있는 목록이 됨
  - 끌어서 순서 바꾸기 `MoveQueue`, 재생 속도 `Speed`, 이어 듣기 `Snapshot/Restore`(session.json), 취침 타이머 `StopAfterCurrent`
- `LyricsService.cs` — pear synced-lyrics 출처 그대로: LRCLib(기본, Jaro-Winkler · 길이 15초 · 한글/가나 원어 확인) → YT Music → Musixmatch → Genius
- `Offline.cs` — 다운로드(음악 폴더\OneMusic, 제목·가수·앨범 정보 기록) · 오프라인 저장(%LocalAppData%\OneMusic\offline)
- `Settings.cs` — settings.json (볼륨 · 반복 · 자동재생 · 가사 출처 · 알림 · 이어 듣기 · 사이드바 · 창 크기)

## 구조 — UI
- `MainWindow.cs` — pear 배치: 32px 창 끌기 / 90px 상단 바(≡ · 로고 · 검색 480×40 · ‹ › · 기록 · 계정) / 72px 레일 / 화면 / 떠 있는 재생 바.
  앨범 색 번짐 배경(`SetWash`), 스크롤하면 상단 바 유리, ≡ 사이드바(내 재생목록), 상세 화면 쌓기(`OpenBrowse` · `Back` · `Forward`), **미니 플레이어**(`ToggleMini`, CompactOverlay)
- **덮개 층**(Popup 대신 Root 맨 위에 직접 그림 — 그래야 앱 안 블러가 됨): `OneMenu`(⋮ 메뉴) · `Tip`(말풍선) · `Toast` · `SearchDropdown`(검색 추천)
- `BlurBrush.cs` — 진한 유리: backdrop → 가우시안 블러(36~40) → 채도 1.4 → 반투명 색. `Theme.Glass` · `Theme.GlassLight` 가 이걸 쓴다 (AcrylicBrush 안 씀)
- `Views.cs` — 셸프 화면(홈 · 둘러보기 · 보관함: 칩 · 큰 버튼 · 빠른 선곡 · 다시 듣기 2줄 · 격자 · 세로 곡 목록), 검색(맨 위 카드 YT 버튼 · 갈래 '모두 보기')
- `DetailView.cs` — 앨범 · 플레이리스트(두 칸) · 아티스트(머리 + 노래 + 셸프) 상세 화면, 끝까지 내리면 곡을 이어 받는다
- `PlayerView.cs` — 재생 화면: 노래/동영상 전환 + 아트(670×648, 16:9 는 안 자름) / 450px 유리 패널 [다음 트랙 | 가사 | 댓글 | 관련 항목]
  - 다음 트랙 줄은 곡마다 한 번 만들어 다시 쓴다(`_rowCache`), 재생 화면이 닫혀 있으면 열 때 그린다
- `TrackMenu.cs` — ⋮ 메뉴 전부: 좋아요|싫어요 · 다운로드 · YT 메뉴 항목 그대로 · (재생 바면) 현재 재생목록 닫기 · 전문 통계
- `CommentsView.cs` · `LyricsView.cs`(Win2D, 가사 탭이 보일 때만 그림) · `FloatingBar.cs` · `WaveSeekBar.cs`(멈추면 쉼) · `Marquee.cs` · `BarIcons.cs`(모양 바뀌는 아이콘) · `ArtView.cs`(번지듯 바뀌는 아트) · `Rows.cs` · `Controls.cs` · `Extras.cs`(단축키 · 재생 속도 · 취침 타이머 · 알림 · 계정 메뉴) · `MiniPlayer.cs`
- `Anim.cs` — pear style.css 의 linear() 곡선(ease · smooth · spring)을 키프레임으로 (220/440/560ms, 누름 0.9). 만든 애니메이션은 캐시해서 다시 씀

## 성능 메모
- `CompositionTarget.Rendering` 을 구독하면 앱 전체가 매 프레임 다시 그려진다 → 필요할 때만 (가사: 탭이 보일 때, 파도 · 이퀄라이저: 재생 중일 때, 흘리는 글자: 넘칠 때)
- 목록 끝 흐림(`ListFade`)은 화면에 걸친 줄만 계산, 흐림 덮개(`WashFade`)는 숨은 화면이면 건너뜀
- 붓(`Theme.B`)은 같은 색이면 하나를 같이 씀 → 붓 색을 직접 바꾸지 말 것

## 개발용 통로 (PC 에서 작업할 때만, Debug 빌드에서만 켜짐)
- `dev-watch.bat` 을 켜 두면 `.dev\cmd.txt` 에 `build` / `run` / `stop` / `mem` / `publish` 를 적을 때 실행하고 `.dev\out.txt` 에 결과를 남긴다
- 앱(Debug)은 `.dev\app-cmd.txt` 의 명령을 받는다: `shot` · `state` · `mem` · `nav:home|player|lyrics|close` · `search:검색어` · `open:browseId` · `back` · `play:first|곡ID` · `next` · `toggle` · `seek:초` · `login` · `auth` · `ytdlp:곡ID|클라이언트` · `dump:곡ID`
- 앱 로그: `.dev\app.log` (배포판은 `%LocalAppData%\OneMusic\log.txt`)

## 사용자 취향 (중요)
- 한국어 반말로 친구처럼 대화
- 디자인 기준은 **삼성 One UI** + 사용자가 만든 버스 앱(Compose) · pear-desktop One UI 테마. 다크 모드만
- **블러 듬뿍**(유리 표면), 앨범 색 배경, 중요한 곳 Bold, iOS 스타일 스프링 애니메이션, 네이티브 느낌(스크롤바 없음 · 글자 선택 없음)
- 파도 재생 바: 직선은 그대로, 재생한 쪽 위로 뾰족한 /\ 언덕이 제각각 진하기 · 속도로 흐르고, 멈추면 그 자리에서 부드럽게 가라앉음
- 아이콘: 꺼짐/음소거는 회색, 켜짐은 흰색. 볼륨 슬라이더는 스피커 아이콘에 올릴 때만 펼쳐짐
- 가로 목록은 마우스 휠로 안 넘어간다 (재생 화면 알고리즘 칩 줄만 예외). 메인 화면 16:9 아트는 가운데 기준 1:1 로 자름
- pear 의 앱 메뉴 막대(확장 · 설정 · 보기 …)는 만들지 않는다 — 기능만

## 단축키 (Shift + ? 로 앱에서도 볼 수 있음)
Space/K 재생 · Shift+N/P 다음/이전 · J/L 10초 · ←/→ 5초 · ↑/↓ 볼륨 · M 음소거 · R 반복 · S 셔플 · +/− 좋아요/싫어요 ·
F 재생 화면 · I 미니 플레이어 · Shift+</> 재생 속도 · / 또는 Ctrl+F 검색 · Alt+←/→ 뒤로/앞으로 · Esc 닫기/뒤로
