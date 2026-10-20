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
            var img = new Image { Stretch = Stretch.UniformToFill, Source = Theme.Image(t.Thumb, 44) };
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
            TrackMenu.Show(this, Track, QueueIndex);
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
    public CardView(Card c, double size = 164, bool square = false, bool fixedHeight = false)
    {
        Card = c;
        var w = c.Wide && !square ? size * 16 / 9 : size;
        if (fixedHeight) Height = size + 92;
        Width = w;
        Spacing = 8;
        Background = new SolidColorBrush(Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        var art = new Border { Width = w, Height = size, CornerRadius = new CornerRadius(18), Background = Theme.B(Color.FromArgb(20, 255, 255, 255)) };
        var img = new Image { Stretch = Stretch.UniformToFill, Source = Theme.Image(c.Thumb, (int)w) };
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
    public static void Show(FrameworkElement at, Track t, int? queueIndex = null)
    {
        var p = PlayerService.Instance;
        var f = new MenuFlyout();
        void Item(string text, string icon, Action a)
        {
            // 아이콘은 선 그림이라 PathIcon(채움)으로는 못 그려서 글자만
            var it = new MenuFlyoutItem { Text = text };
            it.Click += (_, _) => a();
            f.Items.Add(it);
        }
        Item("믹스 시작", Icons.Radio, () => _ = p.PlaySong(t));
        Item("다음에 재생", Icons.PlayNextIcon, () => p.PlayNext(t));
        Item("현재 재생목록에 추가", Icons.QueueAdd, () => p.AddToQueue(t));
        if (queueIndex is { } qi && qi != p.Index)
            Item("현재 재생목록에서 삭제", Icons.Close, () => p.RemoveFromQueue(qi));
        if (t.AlbumId != null || t.ArtistId != null) f.Items.Add(new MenuFlyoutSeparator());
        if (t.AlbumId != null) Item("앨범으로 이동", Icons.Album, () => App.MainWin?.OpenBrowse(t.AlbumId));
        if (t.ArtistId != null) Item("아티스트 페이지로 이동", Icons.Person, () => App.MainWin?.OpenBrowse(t.ArtistId));
        f.Items.Add(new MenuFlyoutSeparator());
        Item("공유 (링크 복사)", Icons.Link, () =>
        {
            var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
            pkg.SetText($"https://music.youtube.com/watch?v={t.VideoId}");
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
        });
        f.ShowAt(at);
    }
}
