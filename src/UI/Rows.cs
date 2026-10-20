using Microsoft.UI.Input;
using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>곡 한 줄 (검색 결과 · 다음 트랙) — 44px 둥근 아트, 곡명, 가수 • 길이</summary>
public sealed class TrackRow : Grid
{
    private readonly Border _hover;
    private readonly Grid _artBox;
    private Equalizer? _eq;
    public Track Track { get; }
    public event Action<TrackRow>? Click;

    /// <param name="subtitle">둘째 줄 (기본: 가수). 플레이리스트에선 '가수 • 앨범'</param>
    /// <param name="number">앨범 수록곡처럼 앨범아트 대신 번호를 보일 때</param>
    public TrackRow(Track t, bool divider = true, string? subtitle = null, int? number = null)
    {
        Track = t;
        Height = 68;
        Background = new SolidColorBrush(Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        _hover = new Border { CornerRadius = new CornerRadius(14), Background = Theme.B(Theme.ChipHover) };
        Anim.V(_hover).Opacity = 0;
        Children.Add(_hover);
        if (divider)
            Children.Add(new XRect { Height = 1, Fill = Theme.B(Theme.Divider), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 8, 0) });

        var g = new Grid { Margin = new Thickness(8, 0, 8, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _artBox = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center };
        if (number is { } n)
        {
            var num = Theme.Text(n.ToString(), 15, FontWeights.SemiBold, Theme.Secondary);
            num.HorizontalAlignment = HorizontalAlignment.Center;
            num.VerticalAlignment = VerticalAlignment.Center;
            _artBox.Children.Add(num);
        }
        else
        {
            var art = new Border { CornerRadius = new CornerRadius(Theme.RadiusThumb), Background = Theme.B(Color.FromArgb(20, 255, 255, 255)) };
            var img = new Image { Width = 44, Height = 44, Stretch = Stretch.UniformToFill, Source = Theme.Image(t.Thumb, 78) };
            Anim.V(img).Opacity = 0;
            img.ImageOpened += (_, _) => Anim.Opacity(img, 1, 300);
            art.Child = img;
            _artBox.Children.Add(art);
        }
        g.Children.Add(_artBox);
        var text = new StackPanel { Margin = new Thickness(16, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(Theme.Text(t.Title, 14, FontWeights.Bold, Theme.OnSurface));
        text.Children.Add(Theme.Text(subtitle ?? t.Artist, 14, FontWeights.Normal, Theme.Secondary));
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        var dur = Theme.Text(t.DurationText, 14, FontWeights.Normal, Theme.Secondary);
        dur.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(dur, 2);
        g.Children.Add(dur);
        Children.Add(g);

        PointerEntered += (_, _) => Anim.Opacity(_hover, 1, 160);
        PointerExited += (_, _) => Anim.Opacity(_hover, 0, 240);
        Tapped += (_, _) => Click?.Invoke(this);
        RightTapped += (_, e) =>
        {
            e.Handled = true;
            TrackMenu.Show(this, Track, QueueIndex, e.GetPosition(null));
        };
    }

    /// <summary>다음 트랙 목록 안의 자리 (있으면 우클릭 메뉴에 '현재 재생목록에서 삭제')</summary>
    public int? QueueIndex { get; set; }

    /// <summary>지금 재생 중인 곡 표시 — 아트 오른쪽 아래 이퀄라이저</summary>
    public void SetCurrent(bool current)
    {
        if (current && _eq == null)
        {
            _eq = new Equalizer { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 4, 4) };
            _artBox.Children.Add(_eq);
            _hover.Background = Theme.B(Theme.Selected);
            Anim.V(_hover).Opacity = 1;
        }
        else if (!current && _eq != null)
        {
            _artBox.Children.Remove(_eq);
            _eq.Stop();
            _eq = null;
            _hover.Background = Theme.B(Theme.ChipHover);
            Anim.V(_hover).Opacity = 0;
        }
    }
}

/// <summary>막대 세 개 이퀄라이저 — 재생 중엔 불규칙하게 오르내리고, 멈추면 점으로 부드럽게 내려앉는다</summary>
public sealed class Equalizer : StackPanel
{
    private readonly XRect[] _bars = new XRect[3];
    private readonly (float F, float P, float A)[][] _waves;
    private float _amp, _t;
    private long _last;
    private bool _running;

    public Equalizer()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 2;
        Height = 16;
        IsHitTestVisible = false;
        var r = Random.Shared;
        _waves = Enumerable.Range(0, 3).Select(_ => Enumerable.Range(0, 3)
            .Select(_ => ((float)(0.6 + r.NextDouble() * 2.2), (float)(r.NextDouble() * Math.PI * 2), (float)(0.25 + r.NextDouble() * 0.5))).ToArray()).ToArray();
        for (var i = 0; i < 3; i++)
        {
            _bars[i] = new XRect { Width = 3, Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = Theme.B(Colors.White), VerticalAlignment = VerticalAlignment.Bottom };
            Children.Add(_bars[i]);
        }
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    private void Start()
    {
        if (_running) return;
        _running = true;
        CompositionTarget.Rendering += OnFrame;
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? s, object e)
    {
        var now = Environment.TickCount64;
        if (_last != 0 && now - _last < 30) return; // 30fps 면 충분
        var dt = _last == 0 ? 0.016f : Math.Min((now - _last) / 1000f, 0.1f);
        _last = now;
        var playing = PlayerService.Instance.IsPlaying;
        _amp += ((playing ? 1 : 0) - _amp) * (1 - MathF.Exp(-dt / (playing ? 0.12f : 0.16f)));
        if (!playing && _amp < 0.005f) _amp = 0;
        _t += dt * (0.4f + 0.6f * _amp);
        for (var i = 0; i < 3; i++)
        {
            float v = 0, n = 0;
            foreach (var w in _waves[i])
            {
                v += w.A * (0.5f + 0.5f * MathF.Sin(_t * w.F * MathF.PI * 2 + w.P));
                n += w.A;
            }
            _bars[i].Height = 3 + 13 * _amp * (v / n);
        }
    }
}

/// <summary>메인 화면 카드 — 둥근 아트(정사각 · 16:9), 제목, 부제목. 마우스를 올리면 아트가 살짝 커진다</summary>
public sealed class CardView : StackPanel
{
    public Card Card { get; }
    public event Action<Card>? Click;

    /// <param name="square">16:9 아트도 가운데를 잘라 정사각으로 (격자가 흐트러지지 않게)</param>
    /// <param name="fixedHeight">제목 · 부제목 두 줄씩 자리를 늘 비워 둬서 아래 줄 카드 위치가 같게</param>
    public CardView(Card c, double size = 164, bool square = false, bool fixedHeight = false, bool round = false)
    {
        Card = c;
        var w = c.Wide && !square ? size * 16 / 9 : size;
        if (fixedHeight) Height = size + 92;
        Width = w;
        Spacing = 8;
        Background = new SolidColorBrush(Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        var art = new Border { Width = w, Height = size, CornerRadius = new CornerRadius(round ? size / 2 : 18), Background = Theme.B(Color.FromArgb(20, 255, 255, 255)) };
        // 16:9 를 정사각으로 자를 땐 가운데 기준 — Image 크기를 칸에 고정해야 UniformToFill 이 가운데를 남긴다
        // (크기를 안 주면 넘친 그림이 왼쪽부터 놓여 오른쪽이 잘렸다). 자를 땐 높이에 맞게 더 크게 받아 흐리지 않게
        var cropWide = c.Wide && square;
        var img = new Image
        {
            Width = w,
            Height = size,
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Source = Theme.Image(c.Thumb, (int)(cropWide ? size * 16 / 9 : w)),
        };
        Anim.V(img).Opacity = 0;
        img.ImageOpened += (_, _) => Anim.Opacity(img, 1, 360);
        art.Child = img;
        Children.Add(art);
        var title = Theme.Text(c.Title, 14, FontWeights.Bold, Theme.OnSurface, 2);
        Children.Add(title);
        if (!string.IsNullOrEmpty(c.Subtitle))
        {
            var sub = Theme.Text(c.Subtitle, 14, FontWeights.Normal, Theme.Secondary, 2);
            sub.Margin = new Thickness(0, -6, 0, 0);
            Children.Add(sub);
        }
        PointerEntered += (_, _) => Anim.Scale(img, 1.05f, 0.8f, 60);
        PointerExited += (_, _) => Anim.Scale(img, 1f, 0.8f, 60);
        img.SizeChanged += (_, _) => Anim.V(img).CenterPoint = new Vector3((float)img.ActualWidth / 2, (float)img.ActualHeight / 2, 0);
        Anim.Pressable(art);
        Tapped += (_, _) => Click?.Invoke(c);
    }
}

/// <summary>재생 바 위에 잠깐 떴다 사라지는 알림 (pear 의 토스트)</summary>
public static class Toast
{
    private static Border? _box;
    private static TextBlock? _text;
    private static int _gen;

    public static void Attach(Grid root)
    {
        _text = Theme.Text("", 14, FontWeights.SemiBold, Colors.White);
        _box = new Border
        {
            CornerRadius = new CornerRadius(20),
            Background = Theme.Glass(0.75),
            Padding = new Thickness(20, 10, 20, 10),
            Child = _text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, Theme.BarHeight + Theme.FloatGap * 2 + 12),
            IsHitTestVisible = false,
        };
        Anim.V(_box).Opacity = 0;
        root.Children.Add(_box);
    }

    public static async void Show(string message)
    {
        if (_box == null || _text == null) return;
        var g = ++_gen;
        _text.Text = message;
        Anim.Pop(_box);
        await Task.Delay(2600);
        if (g == _gen) Anim.Opacity(_box, 0, Anim.Fast);
    }
}

/// <summary>가로로 채우다 넘치면 다음 줄로 (보관함 격자)</summary>
public sealed class WrapPanel : Panel
{
    public double HSpacing { get; set; } = 16;
    public double VSpacing { get; set; } = 24;

    protected override Size MeasureOverride(Size available)
    {
        double x = 0, y = 0, rowH = 0, maxW = 0;
        foreach (var c in Children)
        {
            c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var d = c.DesiredSize;
            if (x > 0 && x + d.Width > available.Width)
            {
                y += rowH + VSpacing;
                x = 0;
                rowH = 0;
            }
            x += d.Width + HSpacing;
            maxW = Math.Max(maxW, x - HSpacing);
            rowH = Math.Max(rowH, d.Height);
        }
        return new Size(double.IsInfinity(available.Width) ? maxW : available.Width, y + rowH);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0, y = 0, rowH = 0;
        foreach (var c in Children)
        {
            var d = c.DesiredSize;
            if (x > 0 && x + d.Width > final.Width)
            {
                y += rowH + VSpacing;
                x = 0;
                rowH = 0;
            }
            c.Arrange(new Rect(x, y, d.Width, d.Height));
            x += d.Width + HSpacing;
            rowH = Math.Max(rowH, d.Height);
        }
        return final;
    }
}

/// <summary>곡 우클릭 메뉴 (pear 의 ⋮ 메뉴에서 실제로 되는 것들)</summary>
public static class TrackMenu
{
    /// <param name="pos">우클릭이면 누른 자리 (창 기준). 없으면 at 아래</param>
    public static void Show(FrameworkElement at, Track t, int? queueIndex = null, Point? pos = null)
    {
        var p = PlayerService.Instance;
        var m = new OneMenu();
        // 맨 위: 좋아요 · 싫어요 한 줄 (pear ⋮ 메뉴)
        m.Pair(("좋아요", Icons.ThumbUp, () => Rate(t, true)), ("싫어요", Icons.ThumbDown, () => Rate(t, false)));
        m.Separator();
        m.Item("다운로드", Icons.Download, () => DownloadAll(new[] { t }));
        m.Item("믹스 시작", Icons.Radio, () => _ = p.PlaySong(t));
        m.Item("다음에 재생", Icons.PlayNextIcon, () => p.PlayNext(t));
        m.Item("현재 재생목록에 추가", Icons.QueueAdd, () => p.AddToQueue(t));
        var off = Offline.Instance;
        if (off.Has(t.VideoId)) m.Item("오프라인 저장 삭제", Icons.Download, () => { off.Remove(t.VideoId); Toast.Show("오프라인 저장에서 뺐어요"); });
        else m.Item("오프라인 저장", Icons.Download, () => SaveOffline(new[] { t }));
        m.Item("재생목록에 저장", Icons.QueueAdd, () => SaveToPlaylist(at, t, pos), keepOpen: true);
        if (queueIndex is { } qi && qi != p.Index)
            m.Item("현재 재생목록에서 삭제", Icons.MinusCircle, () => p.RemoveFromQueue(qi));
        if (t.AlbumId != null) m.Item("앨범으로 이동", Icons.Album, () => App.MainWin?.OpenBrowse(t.AlbumId));
        if (t.ArtistId != null) m.Item("아티스트 페이지로 이동", Icons.Person, () => App.MainWin?.OpenBrowse(t.ArtistId));
        m.Item("공유", Icons.Share, () =>
        {
            var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
            pkg.SetText($"https://music.youtube.com/watch?v={t.VideoId}");
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
            Toast.Show("링크를 복사했어요");
        });
        m.Show(at, pos);
    }

    private static async void Rate(Track t, bool like)
    {
        try
        {
            var ok = await InnerTube.Instance.Rate(t.VideoId, like);
            Toast.Show(ok ? (like ? "좋아요 표시한 노래에 추가했어요" : "싫어요를 표시했어요") : "로그인이 필요해요");
        }
        catch (Exception e) { Log.Warn($"좋아요 실패: {e.Message}"); }
    }

    /// <summary>재생목록에 저장 — 같은 메뉴 모양으로 내 재생목록을 보여 주고 고르면 넣는다</summary>
    private static async void SaveToPlaylist(FrameworkElement at, Track t, Point? pos)
    {
        try
        {
            var lists = await InnerTube.Instance.MyPlaylists(t.VideoId);
            OneMenu.CloseAll();
            if (lists.Count == 0) { Toast.Show("재생목록이 없어요"); return; }
            var m = new OneMenu();
            m.Header("재생목록에 저장");
            foreach (var (title, id, contains) in lists.Take(14))
                m.Item(contains ? $"{title}  ✓" : title, Icons.QueueAdd, async () =>
                {
                    var ok = !contains && await InnerTube.Instance.AddToPlaylist(id, t.VideoId);
                    Toast.Show(contains ? $"이미 '{title}'에 있어요" : ok ? $"'{title}'에 저장했어요" : "저장하지 못했어요");
                });
            m.Show(at, pos);
        }
        catch (Exception e)
        {
            Log.Warn($"재생목록 목록 실패: {e.Message}");
            OneMenu.CloseAll();
            Toast.Show("재생목록을 불러오지 못했어요");
        }
    }

    /// <summary>음악 폴더\OneMusic 에 m4a 로 (여러 곡이면 차례로)</summary>
    public static async void DownloadAll(IEnumerable<Track> tracks)
    {
        var list = tracks.ToList();
        var ok = 0;
        Toast.Show(list.Count == 1 ? $"'{list[0].Title}' 다운로드 중…" : $"{list.Count}곡 다운로드 중…");
        foreach (var t in list)
        {
            try { await Offline.Download(t); ok++; }
            catch (Exception e) { Log.Error($"다운로드 실패: {t.Title}", e); }
        }
        Toast.Show(ok == list.Count ? $"다운로드 완료 — {Offline.DownloadDir}" : $"{ok}/{list.Count}곡 다운로드 (나머지 실패)");
    }

    public static async void SaveOffline(IEnumerable<Track> tracks)
    {
        var list = tracks.ToList();
        Toast.Show(list.Count == 1 ? $"'{list[0].Title}' 오프라인 저장 중…" : $"{list.Count}곡 오프라인 저장 중…");
        var ok = 0;
        foreach (var t in list) if (await Offline.Instance.Save(t)) ok++;
        Toast.Show(ok == list.Count ? "오프라인 저장 완료 — 보관함에서 볼 수 있어요" : $"{ok}/{list.Count}곡 저장 (나머지 실패)");
    }
}
