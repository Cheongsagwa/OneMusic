using Microsoft.UI.Xaml.Media.Animation;
using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// 화면 아래 떠 있는 유리 재생 바 (pear-desktop One UI 테마의 플로팅 바).
/// 왼쪽: 이전 · 재생 · 다음 · 시간 / 가운데: 앨범아트 · 곡명 · 가수 · 가사 한 줄 / 오른쪽: 볼륨 · 반복 · 셔플 · 재생 화면 열기
/// 위 가장자리를 따라 파도 재생 바.
/// </summary>
public sealed class FloatingBar : Grid
{
    private readonly PlayerService _p = PlayerService.Instance;
    private readonly WaveSeekBar _wave = new();
    private readonly TextBlock _time = Theme.Text("0:00 / 0:00", 15, FontWeights.SemiBold, Theme.Secondary);
    private readonly Border _art = new() { Width = 52, Height = 52, CornerRadius = new CornerRadius(12), Background = Theme.B(Color.FromArgb(20, 255, 255, 255)) };
    private readonly Image _artImg = new() { Stretch = Stretch.UniformToFill };
    private readonly Image _artOld = new() { Stretch = Stretch.UniformToFill };
    private readonly Marquee _title = new(18, FontWeights.Bold, Theme.OnSurface);
    private readonly Marquee _artist = new(15, FontWeights.Normal, Theme.Secondary);
    private readonly Border _lyric = new() { Height = 36, Width = 0, CornerRadius = new CornerRadius(18), Background = Theme.B(Theme.Chip), Margin = new Thickness(0) };
    private readonly Marquee _lyricText = new(14, FontWeights.Bold, Theme.OnSurface);
    private readonly Grid _playIcon = new();
    private FrameworkElement _playGlyph, _pauseGlyph;
    private readonly IconButton _volume = new(36), _repeat = new(36), _shuffle = new(36);
    private readonly Grid _volumeIcon = new();
    private FrameworkElement _volWaves = null!, _volX = null!, _speaker = null!;
    private readonly Grid _repeatIcon = new();
    private FrameworkElement _repeatOne = null!;
    private readonly MiniSlider _volSlider = new() { Width = 0, Opacity = 0 };
    private bool _lyricOpen;
    private bool _volHover;
    private DispatcherQueueTimer? _volCloseTimer;
    private string? _shownLine;
    private bool _wasPlaying;
    public event Action? OpenPlayer;
    private readonly DispatcherQueueTimer _tick; // 필드로 붙잡아 둬야 GC 에 수거되지 않는다

    public FloatingBar()
    {
        Height = Theme.BarHeight;
        CornerRadius = new CornerRadius(28);
        Background = Theme.Glass(0.62);
        BorderBrush = Theme.B(Color.FromArgb(20, 255, 255, 255));
        BorderThickness = new Thickness(1);
        Shadow = new ThemeShadow();
        Translation = new Vector3(0, 0, 24);

        // 파도 재생 바 — 위 가장자리 (좌우 28px 안쪽)
        _wave.VerticalAlignment = VerticalAlignment.Top;
        _wave.Margin = new Thickness(28 - 17, -9, 28 - 17, 0);
        _wave.Progress = () => _p.Duration > 0 ? _p.Position / _p.Duration : 0;
        _wave.IsPlaying = () => _p.IsPlaying;
        _wave.Seek += f => _p.Seek(f * _p.Duration);
        Children.Add(_wave);

        var row = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Height = 64, Margin = new Thickness(14, 0, 22, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Children.Add(row);

        // ── 왼쪽 ──
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        var prev = new IconButton(44) { Content = Theme.Icon(Icons.Previous, 24, Colors.White) };
        prev.Click += () => { Bump(prev.Content, -6); _ = _p.Previous(); };
        var play = new IconButton(52);
        play.HoverBrush = Theme.B(Colors.Transparent);
        var circle = new XEllipse { Fill = Theme.B(Colors.White), Width = 52, Height = 52 };
        _playGlyph = Theme.Icon(Icons.Play, 26, Colors.Black);
        _pauseGlyph = Theme.Icon(Icons.Pause, 26, Colors.Black);
        _playIcon.Children.Add(circle);
        _playIcon.Children.Add(_playGlyph);
        _playIcon.Children.Add(_pauseGlyph);
        Anim.V(_pauseGlyph).Opacity = 0;
        play.Content = _playIcon;
        play.Margin = new Thickness(4, 0, 4, 0);
        play.Click += () => _p.TogglePlay();
        var next = new IconButton(44) { Content = Theme.Icon(Icons.Next, 24, Colors.White) };
        next.Click += () => { Bump(next.Content, 6); _ = _p.Next(); };
        _time.Width = 108;
        _time.Margin = new Thickness(12, 0, 0, 0);
        _time.VerticalAlignment = VerticalAlignment.Center;
        left.Children.Add(prev);
        left.Children.Add(play);
        left.Children.Add(next);
        left.Children.Add(_time);
        Grid.SetColumn(left, 0);
        row.Children.Add(left);

        // ── 가운데 ──
        var mid = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var artGrid = new Grid();
        artGrid.Children.Add(_artOld);
        artGrid.Children.Add(_artImg);
        _art.Child = artGrid;
        _art.Tapped += (_, _) => OpenPlayer?.Invoke();
        Anim.Pressable(_art, 0.94f);
        var info = new StackPanel { Width = 240, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        info.Children.Add(_title);
        info.Children.Add(_artist);
        info.Tapped += (_, _) => OpenPlayer?.Invoke();
        // 가수 이름을 누르면 아티스트 화면
        _artist.Tapped += (_, e) =>
        {
            if (_p.Current?.ArtistId is not { } id) return;
            e.Handled = true;
            App.MainWin?.OpenBrowse(id);
        };
        info.Background = new SolidColorBrush(Colors.Transparent);
        _lyricText.Margin = new Thickness(16, 0, 16, 0);
        _lyricText.VerticalAlignment = VerticalAlignment.Center;
        _lyric.Child = _lyricText;
        _lyric.VerticalAlignment = VerticalAlignment.Center;
        Anim.V(_lyric).Opacity = 0;
        mid.Children.Add(_art);
        mid.Children.Add(info);
        mid.Children.Add(_lyric);
        Grid.SetColumn(mid, 1);
        row.Children.Add(mid);

        // ── 오른쪽 ──
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 12 };
        _volSlider.VerticalAlignment = VerticalAlignment.Center;
        _volSlider.Value = _p.Volume;
        _volSlider.ValueChanged += v => _p.Volume = v;
        _speaker = Theme.Icon(Icons.Speaker, 24, Colors.White);
        _volWaves = Theme.Icon(Icons.Waves, 24, Colors.White, stroke: true, strokeWidth: 2);
        _volX = Theme.Icon(Icons.MuteX, 24, Colors.White, stroke: true, strokeWidth: 2);
        _volumeIcon.Children.Add(_speaker);
        _volumeIcon.Children.Add(_volWaves);
        _volumeIcon.Children.Add(_volX);
        _volume.Content = _volumeIcon;
        _volume.Click += () => _p.Muted = !_p.Muted;
        // 볼륨: 스피커 아이콘이나 슬라이더 위에 있을 때만 펼친다 (사이 틈에서 닫히지 않게 조금 기다림)
        foreach (var el in new FrameworkElement[] { _volume, _volSlider })
        {
            el.PointerEntered += (_, _) => SetVolumeOpen(true);
            el.PointerExited += (_, _) => SetVolumeOpen(false);
        }
        _repeatIcon.Children.Add(Theme.Icon(Icons.Repeat, 24, Colors.White, stroke: true, strokeWidth: 2));
        _repeatOne = Theme.Icon(Icons.RepeatOne, 24, Colors.White, stroke: true, strokeWidth: 2);
        _repeatIcon.Children.Add(_repeatOne);
        _repeat.Content = _repeatIcon;
        _repeat.Click += () =>
        {
            _p.CycleRepeat();
            Spin(_repeatIcon);
        };
        _shuffle.Content = Theme.Icon(Icons.Shuffle, 24, Colors.White, stroke: true, strokeWidth: 2);
        Anim.V(_shuffle.Content!).Opacity = 0.55f;
        _shuffle.Click += () =>
        {
            _p.Shuffle();
            Anim.Opacity(_shuffle.Content!, 1, 120);
            Anim.Opacity(_shuffle.Content!, 0.55f, 500, 700);
            Bump(_shuffle.Content, 5);
        };
        var open = new IconButton(36);
        open.HoverBrush = Theme.B(Colors.Transparent);
        var openGrid = new Grid();
        openGrid.Children.Add(new XEllipse { Fill = Theme.B(Colors.White), Width = 36, Height = 36 });
        openGrid.Children.Add(Theme.Icon(Icons.ChevronUp, 20, Colors.Black, stroke: true, strokeWidth: 2.2));
        open.Content = openGrid;
        open.Click += () => OpenPlayer?.Invoke();
        right.Children.Add(_volSlider);
        right.Children.Add(_volume);
        right.Children.Add(_repeat);
        right.Children.Add(_shuffle);
        right.Children.Add(open);
        Grid.SetColumn(right, 2);
        row.Children.Add(right);

        _p.TrackChanged += OnTrack;
        _p.StateChanged += OnState;
        _p.VolumeChanged += OnVolume;
        _p.RepeatChanged += OnRepeat;
        LyricsState.Instance.Changed += () => _shownLine = null;
        OnVolume();
        OnRepeat();
        OnTrack();

        _tick = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _tick.Interval = TimeSpan.FromMilliseconds(150);
        _tick.Tick += (_, _) => Tick();
        _tick.Start();
    }

    public void SetAccent(Color c) => _wave.Color = c;

    private void OnTrack()
    {
        var t = _p.Current;
        _title.Text = t?.Title ?? "재생 중인 곡 없음";
        _artist.Text = t?.Artist ?? "";
        // 이전 아트를 아래에 남겨 두고, 새 아트를 다 불러오면 가운데부터 번지듯 페이드인
        _artOld.Source = _artImg.Source;
        var bmp = Theme.Image(t?.Thumb, 104);
        Anim.V(_artImg).Opacity = 0;
        bmp.ImageOpened += (_, _) =>
        {
            Anim.V(_artImg).Scale = new Vector3(1.06f, 1.06f, 1);
            Anim.Scale(_artImg, 1f, 0.9f, 90);
            Anim.Opacity(_artImg, 1, 520);
        };
        _artImg.Source = bmp;
        _shownLine = null;
    }

    private void OnState()
    {
        var playing = _p.IsPlaying || (_p.IsBuffering && _wasPlaying);
        if (playing == _wasPlaying) return;
        _wasPlaying = playing;
        // 재생 ↔ 일시정지: 아이콘이 돌며 바뀐다
        var show = playing ? _pauseGlyph : _playGlyph;
        var hide = playing ? _playGlyph : _pauseGlyph;
        Anim.Opacity(hide, 0, 160);
        Anim.Scale(hide, 0.6f, 0.8f, 40);
        Anim.Opacity(show, 1, 220);
        Anim.V(show).Scale = new Vector3(0.6f, 0.6f, 1);
        Anim.Scale(show, 1f, 0.55f, 45);
    }

    private void OnVolume()
    {
        var muted = _p.Muted;
        Anim.Opacity(_volWaves, muted ? 0 : 1, 220);
        Anim.Opacity(_volX, muted ? 1 : 0, 220);
        Anim.Opacity(_volumeIcon, muted ? 0.55f : 1f, 240);
        if (!_volSlider.Dragging) _volSlider.Value = _p.Volume;
    }

    private void OnRepeat()
    {
        var mode = _p.Repeat;
        Anim.Opacity(_repeatIcon, mode == RepeatMode.Off ? 0.55f : 1f, 240);
        Anim.Opacity(_repeatOne, mode == RepeatMode.One ? 1 : 0, 220);
    }

    private void SetVolumeOpen(bool open)
    {
        _volHover = open;
        _volCloseTimer ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
        _volCloseTimer.Stop();
        if (open) AnimateWidth(_volSlider, 112, 1);
        else
        {
            _volCloseTimer.Interval = TimeSpan.FromMilliseconds(260);
            _volCloseTimer.IsRepeating = false;
            _volCloseTimer.Tick += CloseVolume;
            _volCloseTimer.Start();
        }
    }

    private void CloseVolume(DispatcherQueueTimer t, object o)
    {
        t.Tick -= CloseVolume;
        if (!_volHover && !_volSlider.Dragging) AnimateWidth(_volSlider, 0, 0);
    }

    /// <summary>폭을 부드럽게 바꾼다 (레이아웃이 함께 움직여서 옆 요소들도 자연스럽게 비켜 감)</summary>
    private static void AnimateWidth(FrameworkElement el, double to, double opacity)
    {
        var sb = new Storyboard();
        var a = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(420),
            EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(a, el);
        Storyboard.SetTargetProperty(a, "Width");
        sb.Children.Add(a);
        var o = new DoubleAnimation { To = opacity, Duration = TimeSpan.FromMilliseconds(260) };
        Storyboard.SetTarget(o, el);
        Storyboard.SetTargetProperty(o, "Opacity");
        sb.Children.Add(o);
        sb.Begin();
    }

    private static void Bump(UIElement? el, float dx)
    {
        if (el == null) return;
        Anim.SetTranslation(el, new Vector3(dx, 0, 0));
        Anim.V(el).Scale = new Vector3(0.7f, 0.7f, 1);
        Anim.Translate(el, Vector3.Zero, 0.6f, 50);
        Anim.Scale(el, 1f, 0.55f, 45);
    }

    private static void Spin(UIElement el)
    {
        var v = Anim.V(el);
        var s = el.ActualSize;
        v.CenterPoint = new Vector3(s.X / 2, s.Y / 2, 0);
        var a = v.Compositor.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(0f, 0f);
        a.InsertKeyFrame(1f, 360f, Anim.Smooth(v.Compositor));
        a.Duration = TimeSpan.FromMilliseconds(620);
        v.StartAnimation("RotationAngleInDegrees", a);
    }

    private void Tick()
    {
        var dur = _p.Duration;
        _time.Text = $"{Fmt.Time(_p.Position)} / {Fmt.Time(dur)}";
        if (_p.IsPlaying != _wasPlaying && !_p.IsBuffering) OnState();

        // 가사 한 줄
        var lines = LyricsState.Instance.Lines;
        var has = lines != null && lines.Count > 0;
        if (has != _lyricOpen)
        {
            _lyricOpen = has;
            AnimateWidth(_lyric, has ? 300 : 0, 1);
            _lyric.Margin = new Thickness(has ? 12 : 0, 0, 0, 0);
            Anim.Opacity(_lyric, has ? 1 : 0, 300);
        }
        if (has)
        {
            var idx = LyricsState.Instance.IndexAt(_p.Position + 0.15);
            var line = idx >= 0 ? lines![idx].Text : "";
            if (string.IsNullOrWhiteSpace(line)) line = "♪";
            if (line != _shownLine)
            {
                _shownLine = line;
                _lyricText.Text = line;
                Anim.Appear(_lyricText, 0, 8);
            }
        }
    }
}
