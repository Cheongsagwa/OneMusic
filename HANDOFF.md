# OneMusic — 인수인계 문서

YouTube Music 네이티브 윈도우 클라이언트. 원래 쓰던 Electron 앱(pear-desktop + 직접 만든 One UI 테마 플러그인)의
화면 · 동작을 그대로 옮기면서 메모리를 줄이는 게 목표. (측정: OneMusic 약 180MB / pear-desktop 약 2.6GB)

## 기술
- C# / .NET 8 / **WinUI 3** (Windows App SDK 1.6, 패키지 없는 실행 파일), Win2D(파도 재생 바 · 가사)
- UI 는 거의 전부 C# 코드로 만든다 (XAML 은 `App.xaml` 하나). 스타일 토큰은 `src/UI/Theme.cs`
- **윈도우에서만 빌드 · 실행된다.** 리눅스 클라우드 세션에선 코드 수정만 하고, 빌드는 GitHub Actions(`.github/workflows/build.yml`)가 한다
  - Actions 탭 → 최신 실행 → `OneMusic-win-x64` 아티팩트에 exe 가 들어 있다

## 구조
- `src/Core/InnerTube.cs` — YouTube Music 내부 API(WEB_REMIX) 직접 호출: 검색 · 추천어 · 홈(칩 · 셸프) · 다음 트랙(라디오) · 앨범/플레이리스트 · 관련 항목
  - 다음 트랙 목록의 한 칸에 음원(ATV)과 뮤비(OMV)가 같이 오므로 **음원만 고른다** (`GetQueue`)
- `src/Core/Auth.cs` + `src/UI/LoginWindow.cs` — **한국은 로그인 없이는 YT Music 응답이 안 온다**(프리미엄 전용 지역).
  WebView2 로그인 창에서 사용자가 직접 로그인 → youtube.com 쿠키만 받아 DPAPI(윈도우 사용자 계정)로 암호화 저장 → 요청마다 SAPISIDHASH 헤더
- `src/Core/StreamResolver.cs` — 스트림 주소는 **yt-dlp**(처음 필요할 때 %LocalAppData%\OneMusic 에 자동 다운로드, `www.youtube.com/watch` 주소로 호출).
  오디오를 **통째로 메모리에 받아서** 재생기에 넘긴다(4MB ≈ 0.1초). 다음 곡은 미리 받아 둔다.
  - YoutubeExplode 는 주소는 주지만 재생이 바로 끊겨서 뺐다
- `src/Core/Mp4Fix.cs` — 일부 곡의 MP4 '편집 목록(edts)' 때문에 Windows 재생기가 열자마자 끝나는 문제 → 상자 이름을 `free` 로 바꿔 무시하게
- `src/Core/PlayerService.cs` — Windows `MediaPlayer` (미디어 키 · 시스템 미디어 컨트롤 자동). 큐 · 반복 · 셔플(한 번 섞기) · 자동재생
  - 큐 = 고른 곡들 `Queue[..AutoStart]` + 자동재생 구간 `Queue[AutoStart..]`. 자동재생 끄면 자동재생 구간은 안 틈
  - 자동재생 칩(전체 · 친숙한 곡 · 인기 · 처음 듣는 곡 · 숨은 명곡 · 2020년대 …)은 `next` 응답의 `musicQueueRenderer.subHeaderChipCloud`. 누르면 칩의 playlistId+params 로 `next` 를 다시 불러 자동재생 구간만 바꾼다 (`SelectChip`)
  - 앨범 · 플레이리스트 뒤 자동재생은 `automixPreviewVideoRenderer` 의 주소, 없으면 마지막 곡 라디오
- `src/Core/LyricsService.cs` — LRCLib (`get-cached` → `search`). 요청 제한에 걸린 적이 있어 재시도는 1번만
- `src/UI/MainWindow.cs` — pear 와 같은 배치: 32px 창 끌기 / 90px 상단 바(≡ · 로고 · 검색 480×40 at x=172) / 72px 레일 / 화면 / 떠 있는 재생 바. 앨범 색 번짐 배경
- `src/UI/Views.cs` — 셸프 화면(홈 · 둘러보기 · 보관함), 검색 결과
- `src/UI/DetailView.cs` — 앨범 · 플레이리스트 · 아티스트 상세 화면 (`InnerTube.BrowsePage`). 앨범/아티스트/플레이리스트 카드를 누르면 바로 재생 대신 이 화면이 열린다
  - `MainWindow.OpenBrowse(browseId)` 로 지금 화면 위에 쌓이고 `Back()`(← 버튼 · Esc · 마우스 뒤로 버튼)으로 돌아간다. 탭 · 검색으로 가면 쌓인 것 전부 닫힘, 최대 8장
  - 곡(`Track`)에 `ArtistId` · `AlbumId` 를 같이 파싱한다 → 재생 바의 가수 이름을 누르면 아티스트 화면
- `src/UI/PlayerView.cs` — 재생 화면: 노래/동영상 전환 + 앨범아트(670×648) / 450px 유리 패널 [다음 트랙 | 가사 | 댓글 | 관련 항목]
- `src/UI/FloatingBar.cs` · `WaveSeekBar.cs` · `Marquee.cs` · `LyricsView.cs` · `Rows.cs` · `Controls.cs` · `Anim.cs`

## 개발용 통로 (PC 에서 작업할 때만, Debug 빌드에서만 켜짐)
- `dev-watch.bat` 을 켜 두면 `.dev\cmd.txt` 에 `build` / `run` / `stop` / `mem` / `publish` 를 적을 때 실행하고 `.dev\out.txt` 에 결과를 남긴다
- 앱(Debug)은 `.dev\app-cmd.txt` 의 명령을 받는다: `shot`(화면 → `.dev\shot.png`) · `state` · `mem` · `nav:home|player|lyrics|close` · `search:검색어` · `open:browseId` · `back` · `play:first|곡ID` · `next` · `toggle` · `seek:초` · `login` · `auth` · `ytdlp:곡ID|클라이언트` · `dump:곡ID`
- 앱 로그: `.dev\app.log` (배포판은 `%LocalAppData%\OneMusic\log.txt`)

## 사용자 취향 (중요)
- 한국어 반말로 친구처럼 대화
- 디자인 기준은 **삼성 One UI** + 사용자가 만든 버스 앱(Compose) · pear-desktop One UI 테마. 다크 모드만
- 유리(블러) 표면, 앨범 색 배경, 중요한 곳 Bold, iOS 스타일 스프링 애니메이션, 네이티브 느낌(스크롤바 없음 · 글자 선택 없음)
- 파도 재생 바: 직선은 그대로, 재생한 쪽 위로 뾰족한 /\ 언덕이 제각각 진하기 · 속도로 흐르고, 멈추면 그 자리에서 부드럽게 가라앉음
- 아이콘: 꺼짐/음소거는 회색, 켜짐은 흰색. 볼륨 슬라이더는 스피커 아이콘에 올릴 때만 펼쳐짐

## 남은 일
- pear 맞추기 남은 것: 차트 인기곡 순위(▲▼) 줄, 보관함 '최근 활동' 정렬, '저장'(재생목록에 담기) 버튼, 메뉴 아이콘, 흘리는 글자 양 끝 흐림
- 애니메이션은 `Anim.cs` 가 pear style.css 의 linear() 곡선(ease · smooth · spring)을 키프레임으로 그대로 쓴다 (220/440/560ms, 누름 0.9)
- 다운로드 · 오프라인 저장: `Core/Offline.cs` (음악 폴더 OneMusic · LocalAppData OneMusic offline)
- 댓글은 YouTube(WEB) next 로 받는다 (`InnerTube.Comments`, `Post(..., web: true)`)
- 가로 목록은 마우스 휠로 안 넘어간다 (사용자 요청, 재생 화면 알고리즘 칩 줄만 예외)
