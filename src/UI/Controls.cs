using Microsoft.UI.Input;

namespace OneMusic.UI;

/// <summary>One UI 스위치 (48×28) — 켜지면 흰 트랙에 검은 손잡이, 손잡이는 스프링으로 미끄러진다</summary>
public sealed class OneToggle : Grid
{
    private readonly Border _track = new() { Width = 48, Height = 28, CornerRadius = new CornerRadius(14) };
    private readonly XEllipse _knob = new() { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 0) };
    private bool _on;
    public event Action<bool>? Toggled;

    public OneToggle(bool on)
    {
        Width = 48;
        Height = 28;
        Children.Add(_track);
        Children.Add(_knob);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        Tapped += (_, e) => { e.Handled = true; Set(!_on, true); Toggled?.Invoke(_on); };
        Set(on, false);
    }

    public void Set(bool on, bool animate)
    {
        _on = on;
        _track.Background = Theme.B(on ? Colors.White : Color.FromArgb(56, 255, 255, 255));
        _knob.Fill = Theme.B(on ? Colors.Black : Colors.White);
        var to = new Vector3(on ? 20 : 0, 0, 0);
        if (animate) Anim.Translate(_knob, to, 0.7f, 45);
        else Anim.SetTranslation(_knob, to);
    }
}

/// <summary>
/// 노래 / 동영상 전환 알약 (pear 29 · 59 · 88) — 높이 44 칩 트랙 안에 56×36 버튼 둘(헤드폰 · 영상, 아이콘 20).
/// 고른 쪽은 진한 알약(흰 18%)이 미끄러져 오고 아이콘이 흰색, 아닌 쪽은 회색.
/// 뮤직비디오가 없는 곡은 YT 처럼 전환을 막고 흐리게(0.4)
/// </summary>
public sealed class AvToggle : Grid
{
    private readonly Border _pill = new() { Width = 56, Height = 36, CornerRadius = new CornerRadius(18), Background = Theme.B(Color.FromArgb(46, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Grid _song = new() { Width = 56, Height = 36, Background = new SolidColorBrush(Colors.Transparent) };
    private readonly Grid _video = new() { Width = 56, Height = 36, Background = new SolidColorBrush(Colors.Transparent) };
    private readonly Core.PlayerService _p = Core.PlayerService.Instance;

    public AvToggle()
    {
        Height = 44;
        Padding = new Thickness(4);
        CornerRadius = new CornerRadius(22);
        Background = Theme.B(Theme.Chip);
        HorizontalAlignment = HorizontalAlignment.Center;
        _pill.Shadow = new ThemeShadow();
        _pill.Translation = new Vector3(0, 0, 8);
        Children.Add(_pill);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        row.Children.Add(_song);
        row.Children.Add(_video);
        Children.Add(row);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        Tip.Set(_song, "노래");
        Tip.Set(_video, "동영상");
        _song.Tapped += (_, _) => _p.SetVideoMode(false);
        _video.Tapped += (_, _) => _p.SetVideoMode(true);
        Anim.Pressable(_song);
        Anim.Pressable(_video);
        _p.VideoChanged += Update;
        _p.TrackChanged += Update;
        Update();
    }

    private void Update()
    {
        var video = _p.VideoMode;
        _song.Children.Clear();
        _video.Children.Clear();
        _song.Children.Add(Theme.Icon(20, video ? Theme.Secondary : Colors.White, Icons.Headphones));
        _video.Children.Add(Theme.Icon(20, video ? Colors.White : Theme.Secondary, Icons.Video));
        Anim.Translate(_pill, new Vector3(video ? 58 : 0, 0, 0));
        // 뮤직비디오가 없으면 전환을 막고 흐리게
        var enabled = _p.CanVideo || video;
        Opacity = enabled ? 1 : 0.4;
        IsHitTestVisible = enabled;
    }
}

/// <summary>왼쪽 72px 레일 — 홈 · 둘러보기 · 보관함 (선택되면 채워진 아이콘 + 반투명 알약)</summary>
public sealed class NavRail : StackPanel
{
    private readonly List<(Border Pill, FrameworkElement Outline, FrameworkElement Filled, TextBlock Label)> _items = new();
    public int Selected { get; private set; } = -1;
    public event Action<int>? Changed;

    public NavRail()
    {
        Width = 72;
        Spacing = 0;
        Padding = new Thickness(8, 16, 8, 0);
        Add("홈", Icons.HomeOutline, Icons.HomeFilled);
        Add("둘러보기", Icons.ExploreOutline, Icons.ExploreFilled);
        Add("보관함", Icons.LibraryOutline, Icons.LibraryFilled);
        Select(0, false);
    }

    private void Add(string label, (string, bool)[] outline, (string, bool)[] filled)
    {
        var idx = _items.Count;
        var box = new Grid { Width = 56, Height = 65, Background = new SolidColorBrush(Colors.Transparent) };
        // 선택 알약 · hover 는 투명도로 부드럽게 (pear 7: background medium ease)
        var hover = new Border { CornerRadius = new CornerRadius(23), Background = Theme.B(Color.FromArgb(15, 255, 255, 255)) };
        Anim.V(hover).Opacity = 0;
        var pill = new Border { CornerRadius = new CornerRadius(23), Background = Theme.B(Color.FromArgb(128, 80, 80, 84)) };
        Anim.V(pill).Opacity = 0;
        box.Children.Add(hover);
        box.Children.Add(pill);
        var iconHost = new Grid { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 0, 0) };
        var o = Theme.Icon(24, Colors.White, outline);
        var f = Theme.Icon(24, Colors.White, filled);
        Anim.V(f).Opacity = 0;
        iconHost.Children.Add(o);
        iconHost.Children.Add(f);
        box.Children.Add(iconHost);
        var t = Theme.Text(label, 10, FontWeights.SemiBold, Theme.OnSurface);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Top;
        t.Margin = new Thickness(0, 40, 0, 0);
        box.Children.Add(t);
        box.Tapped += (_, _) => Select(idx);
        box.PointerEntered += (_, _) => Anim.Opacity(hover, Selected != idx ? 1 : 0, Anim.Medium);
        box.PointerExited += (_, _) => Anim.Opacity(hover, 0, Anim.Medium);
        Anim.Pressable(box);
        _items.Add((pill, o, f, t));
        Children.Add(box);
    }

    public void Select(int idx, bool notify = true)
    {
        var changed = idx != Selected;
        Selected = idx;
        for (var i = 0; i < _items.Count; i++)
        {
            var (pill, o, f, t) = _items[i];
            var on = i == idx;
            Anim.Opacity(pill, on ? 1 : 0, Anim.Medium);
            // 아이콘: 선 → 채움 으로 바뀌며 살짝 튕긴다
            Anim.Opacity(o, on ? 0 : 1, Anim.Fast);
            Anim.Opacity(f, on ? 1 : 0, Anim.Fast);
            if (on && changed) { Anim.V(f).Scale = new Vector3(0.8f, 0.8f, 1); Anim.Scale(f, 1f); }
            t.FontWeight = on ? FontWeights.ExtraBold : FontWeights.SemiBold;
        }
        if (notify) Changed?.Invoke(idx);
    }
}
