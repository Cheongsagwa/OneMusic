using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using OneMusic.Core;
using Launcher = Windows.System.Launcher;
using VirtualKey = Windows.System.VirtualKey;

namespace OneMusic.UI;

/// <summary>
/// 키보드 단축키 (YouTube · YT Music 웹과 같은 키 위주). 입력칸에 글을 쓰는 중이면 동작하지 않는다.
/// Shift + ? 로 목록을 볼 수 있다
/// </summary>
public static class Shortcuts
{
    public static readonly (string Keys, string What)[] List =
    [
        ("Space · K", "재생 / 일시정지"),
        ("Shift + N", "다음 곡"),
        ("Shift + P", "이전 곡"),
        ("J · L", "10초 뒤로 · 앞으로"),
        ("← · →", "5초 뒤로 · 앞으로"),
        ("↑ · ↓", "볼륨 5% 올리기 · 내리기"),
        ("M", "음소거"),
        ("R", "반복 모드 바꾸기"),
        ("S", "셔플"),
        ("+ · −", "좋아요 · 싫어요"),
        ("F", "재생 화면 열기 / 닫기"),
        ("I", "미니 플레이어"),
        ("< · >", "재생 속도 느리게 · 빠르게"),
        ("/ · Ctrl + F", "검색"),
        ("Alt + ← · →", "뒤로 · 앞으로"),
        ("Esc", "메뉴 · 재생 화면 닫기, 뒤로"),
        ("Shift + ?", "단축키 목록"),
    ];

    /// <summary>처리했으면 true</summary>
    public static bool Handle(MainWindow w, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox or AutoSuggestBox or PasswordBox) return false;
        var p = w.Player;
        var shift = Down(VirtualKey.Shift);
        var ctrl = Down(VirtualKey.Control);
        var alt = Down(VirtualKey.Menu);
        switch (e.Key)
        {
            case VirtualKey.Space:
            case VirtualKey.K when !ctrl:
                p.TogglePlay();
                return true;
            case VirtualKey.N when shift:
                _ = p.Next();
                return true;
            case VirtualKey.P when shift:
                _ = p.Previous();
                return true;
            case VirtualKey.J:
                p.Seek(p.Position - 10);
                return true;
            case VirtualKey.L when !ctrl:
                p.Seek(p.Position + 10);
                return true;
            case VirtualKey.Left when alt:
                w.Back();
                return true;
            case VirtualKey.Right when alt:
                w.Forward();
                return true;
            case VirtualKey.Left:
                p.Seek(p.Position - 5);
                return true;
            case VirtualKey.Right:
                p.Seek(p.Position + 5);
                return true;
            case VirtualKey.Up:
                p.Volume = Math.Min(1, p.Volume + 0.05);
                Toast.Show($"볼륨 {Math.Round(p.Volume * 100)}%");
                return true;
            case VirtualKey.Down:
                p.Volume = Math.Max(0, p.Volume - 0.05);
                Toast.Show($"볼륨 {Math.Round(p.Volume * 100)}%");
                return true;
            case VirtualKey.M:
                p.Muted = !p.Muted;
                return true;
            case VirtualKey.R:
                p.CycleRepeat();
                Toast.Show(p.Repeat switch { RepeatMode.All => "전체 반복", RepeatMode.One => "한 곡 반복", _ => "반복 안 함" });
                return true;
            case VirtualKey.S when !ctrl:
                p.Shuffle();
                Toast.Show("목록을 섞었어요");
                return true;
            case VirtualKey.F when ctrl:
            case (VirtualKey)191 when !shift: // '/'
                w.FocusSearch();
                return true;
            case (VirtualKey)191 when shift: // '?'
                ShowHelp(w);
                return true;
            case VirtualKey.F:
                w.TogglePlayer();
                return true;
            case VirtualKey.I when !ctrl:
                w.ToggleMini();
                return true;
            case (VirtualKey)188 when shift: // '<'
            case (VirtualKey)190 when shift: // '>'
            {
                var steps = SpeedMenu.Steps;
                var i = Array.FindIndex(steps, s => Math.Abs(s - p.Speed) < 0.01);
                if (i < 0) i = Array.IndexOf(steps, 1.0);
                i = Math.Clamp(i + (e.Key == (VirtualKey)190 ? 1 : -1), 0, steps.Length - 1);
                p.Speed = steps[i];
                Toast.Show($"재생 속도 {SpeedMenu.Label(p.Speed)}");
                return true;
            }
            case (VirtualKey)187: // '=' / '+'
            case VirtualKey.Add:
                if (p.Current is { } liked) _ = Rate(liked, true);
                return true;
            case (VirtualKey)189: // '-'
            case VirtualKey.Subtract:
                if (p.Current is { } disliked) _ = Rate(disliked, false);
                return true;
        }
        return false;
    }

    private static async Task Rate(Track t, bool like)
    {
        var ok = await InnerTube.Instance.Rate(t.VideoId, like);
        Toast.Show(!ok ? "로그인이 필요해요" : like ? "좋아요 표시한 노래에 추가했어요" : "싫어요를 표시했어요");
        if (ok && !like && PlayerService.Instance.Current?.VideoId == t.VideoId) _ = PlayerService.Instance.Next();
    }

    private static bool Down(VirtualKey k) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    public static void ShowHelp(MainWindow w)
    {
        var m = new OneMenu(width: 380);
        m.Header("단축키");
        foreach (var (k, what) in List) m.Info(k, what);
        var size = w.Root.XamlRoot?.Size ?? new Size(1200, 800);
        m.Show(w.Root, new Point(size.Width / 2 - 190, 110));
    }
}

/// <summary>출력 장치 고르기 (pear custom-output-device) — 스피커 · 헤드폰 · 블루투스 …</summary>
public static class OutputMenu
{
    public static string Label => PlayerService.Instance.OutputName ?? "기본 장치";

    public static async void Show(FrameworkElement at)
    {
        var p = PlayerService.Instance;
        List<Windows.Devices.Enumeration.DeviceInformation> devices;
        try
        {
            var found = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Media.Devices.MediaDevice.GetAudioRenderSelector());
            devices = found.Where(d => d.IsEnabled).ToList();
        }
        catch (Exception e)
        {
            Log.Warn($"출력 장치 목록 실패: {e.Message}");
            devices = new();
        }
        var current = Settings.Current.AudioDevice;
        var m = new OneMenu(width: 320);
        m.Header("출력 장치");
        m.Item(current == null ? "기본 장치 (윈도우 설정)  ✓" : "기본 장치 (윈도우 설정)", Icons.Speaker, async () =>
        {
            await p.SetOutput(null);
            Toast.Show("윈도우 기본 장치로 소리를 내요");
        });
        foreach (var d in devices)
        {
            var dev = d;
            m.Item(dev.Id == current ? $"{dev.Name}  ✓" : dev.Name, Icons.Speaker, async () =>
            {
                await p.SetOutput(dev.Id);
                Toast.Show($"'{dev.Name}'(으)로 소리를 내요");
            });
        }
        m.Show(at);
    }
}

/// <summary>재생 속도 (pear playback-speed) — 0.5 ~ 2배</summary>
public static class SpeedMenu
{
    public static readonly double[] Steps = [0.5, 0.75, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0];

    public static string Label(double s) => Math.Abs(s - 1) < 0.001 ? "보통" : $"{s:0.##}배";

    public static void Show(FrameworkElement at)
    {
        var p = PlayerService.Instance;
        var m = new OneMenu();
        m.Header($"재생 속도 · {Label(p.Speed)}");
        foreach (var s in Steps)
        {
            var v = s;
            m.Item(Math.Abs(s - p.Speed) < 0.01 ? $"{Label(s)}  ✓" : Label(s), Icons.Speed, () =>
            {
                p.Speed = v;
                Toast.Show($"재생 속도 {Label(v)}");
            });
        }
        m.Show(at);
    }
}

/// <summary>취침 타이머 — 정한 시간이 지나면(또는 지금 곡이 끝나면) 멈춘다</summary>
public static class SleepTimer
{
    private static DispatcherQueueTimer? _timer;
    public static DateTime? Until { get; private set; }

    public static string Label => PlayerService.Instance.StopAfterCurrent ? "지금 곡이 끝나면"
        : Until is { } u ? $"{Math.Max(1, (int)Math.Ceiling((u - DateTime.Now).TotalMinutes))}분 뒤" : "꺼짐";

    public static void Set(int minutes)
    {
        _timer?.Stop();
        Until = null;
        PlayerService.Instance.StopAfterCurrent = false;
        if (minutes == 0) { Toast.Show("취침 타이머를 껐어요"); return; }
        if (minutes < 0)
        {
            PlayerService.Instance.StopAfterCurrent = true;
            Toast.Show("지금 곡이 끝나면 멈춰요");
            return;
        }
        Until = DateTime.Now.AddMinutes(minutes);
        _timer ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMinutes(minutes);
        _timer.IsRepeating = false;
        _timer.Tick -= Fire;
        _timer.Tick += Fire;
        _timer.Start();
        Toast.Show($"{minutes}분 뒤에 멈춰요");
    }

    private static void Fire(DispatcherQueueTimer t, object o)
    {
        Until = null;
        PlayerService.Instance.Pause();
        Toast.Show("취침 타이머로 멈췄어요");
    }

    public static void ShowMenu(FrameworkElement at)
    {
        var m = new OneMenu();
        m.Header($"취침 타이머 · {Label}");
        foreach (var (text, min) in new[] { ("끄기", 0), ("15분", 15), ("30분", 30), ("45분", 45), ("1시간", 60), ("2시간", 120), ("지금 곡이 끝나면", -1) })
            m.Item(text, Icons.Moon, () => Set(min));
        m.Show(at);
    }
}

/// <summary>곡이 바뀌면 윈도우 알림 (pear notifications) — 창이 앞에 있을 때는 띄우지 않는다</summary>
public static class Notifier
{
    private static bool _registered;

    public static void Register()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, _) => { };
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception e) { Log.Warn($"알림 등록 실패: {e.Message}"); }
    }

    public static void Unregister()
    {
        if (!_registered) return;
        try { AppNotificationManager.Default.Unregister(); } catch { }
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static async void TrackChanged(Track t, bool windowActive)
    {
        if (!_registered || windowActive || !Settings.Current.Notifications) return;
        try
        {
            var b = new AppNotificationBuilder()
                .AddText(t.Title)
                .AddText(t.Artist)
                .MuteAudio();
            // 패키지 없는 앱은 인터넷 그림을 바로 못 쓰니 앨범아트를 파일로 받아 둔다
            try
            {
                if (Fmt.Thumb(t.Thumb, 200) is { } thumb)
                {
                    var file = IOPath.Combine(Log.AppDataDir, "notify-art.jpg");
                    await File.WriteAllBytesAsync(file, await Http.GetByteArrayAsync(thumb));
                    b.SetAppLogoOverride(new Uri(file), AppNotificationImageCrop.Default);
                }
            }
            catch { /* 그림 없이 */ }
            if (PlayerService.Instance.Current?.VideoId != t.VideoId) return;
            var n = b.BuildNotification();
            n.Tag = "track";
            n.Group = "onemusic";
            n.ExpiresOnReboot = true;
            AppNotificationManager.Default.Show(n);
        }
        catch (Exception e) { Log.Warn($"알림 실패: {e.Message}"); }
    }
}

/// <summary>상단 바 프로필 사진(계정) 메뉴 — 취침 타이머 · 알림 · 이어 듣기 · 단축키 · 다운로드 폴더 · 로그인/로그아웃</summary>
public static class AccountMenu
{
    public static void Show(MainWindow w, FrameworkElement at)
    {
        var s = Settings.Current;
        var m = new OneMenu(width: 300);
        m.Item("미니 플레이어", Icons.Pip, () => w.ToggleMini());
        m.Item($"재생 속도 · {SpeedMenu.Label(PlayerService.Instance.Speed)}", Icons.Speed, () => SpeedMenu.Show(at), keepOpen: true);
        m.Item($"출력 장치 · {OutputMenu.Label}", Icons.Speaker, () => OutputMenu.Show(at), keepOpen: true);
        m.Item($"취침 타이머 · {SleepTimer.Label}", Icons.Moon, () => SleepTimer.ShowMenu(at), keepOpen: true);
        m.Item($"곡 바뀜 알림 · {(s.Notifications ? "켜짐" : "꺼짐")}", Icons.Bell, () =>
        {
            s.Notifications = !s.Notifications;
            s.Save();
            Toast.Show(s.Notifications ? "곡이 바뀌면 알려 줄게요" : "알림을 껐어요");
        });
        m.Item($"이어 듣기 · {(s.ResumeSession ? "켜짐" : "꺼짐")}", Icons.PlayNextIcon, () =>
        {
            s.ResumeSession = !s.ResumeSession;
            s.Save();
            Toast.Show(s.ResumeSession ? "다시 켜면 듣던 곡부터 이어요" : "이어 듣기를 껐어요");
        });
        m.Item("단축키", Icons.Keyboard, () => Shortcuts.ShowHelp(w));
        m.Item("다운로드 폴더 열기", Icons.Download, async () =>
        {
            Directory.CreateDirectory(Offline.DownloadDir);
            await Launcher.LaunchFolderPathAsync(Offline.DownloadDir);
        });
        m.Separator();
        if (Auth.IsLoggedIn) m.Item("로그아웃", Icons.Person, () => { Auth.Logout(); w.ReloadAll(); });
        else m.Item("구글 계정으로 로그인", Icons.Person, async () =>
        {
            var lw = new LoginWindow();
            lw.Activate();
            if (await lw.Result) w.ReloadAll();
        });
        m.Show(at);
    }
}
