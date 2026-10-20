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

/// <summary>노래 / 동영상 전환 알약 (헤드폰 · 영상 아이콘) — 지금은 노래만 (동영상 재생은 아직)</summary>
public sealed class AvToggle : Grid
{
    public AvToggle()
    {
        Height = 44;
        Padding = new Thickness(4);
        CornerRadius = new CornerRadius(22);
        Background = Theme.B(Theme.Chip);
        HorizontalAlignment = HorizontalAlignment.Center;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var song = new Border { Width = 56, Height = 36, CornerRadius = new CornerRadius(18), Background = Theme.B(Theme.Selected), Child = Theme.Icon(20, Colors.White, Icons.Headphones) };
        var video = new Border { Width = 56, Height = 36, CornerRadius = new CornerRadius(18), Child = Theme.Icon(20, Theme.Secondary, Icons.Video), Opacity = 0.5 };
        ToolTipService.SetToolTip(video, "동영상 재생은 준비 중이에요");
        row.Children.Add(song);
        row.Children.Add(video);
        Children.Add(row);
    }
}

/// <summary>왼쪽 72px 레일 — 홈 · 둘러보기 · 보관함 (선택되면 채워진 아이콘 + 반투명 알약)</summary>
public sealed class NavRail : StackPanel
{
    private readonly List<(Border Box, Grid IconHost, (string, bool)[] Outline, (string, bool)[] Filled)> _items = new();
    public int Selected { get; private set; }
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
        var box = new Border { Width = 56, Height = 65, CornerRadius = new CornerRadius(23), Background = new SolidColorBrush(Colors.Transparent) };
        var g = new Grid();
        var iconHost = new Grid { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 0, 0) };
        g.Children.Add(iconHost);
        var t = Theme.Text(label, 10, FontWeights.ExtraBold, Theme.OnSurface);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Top;
        t.Margin = new Thickness(0, 40, 0, 0);
        g.Children.Add(t);
        box.Child = g;
        box.Tapped += (_, _) => Select(idx);
        box.PointerEntered += (_, _) => { if (Selected != idx) box.Background = Theme.B(Color.FromArgb(15, 255, 255, 255)); };
        box.PointerExited += (_, _) => { if (Selected != idx) box.Background = new SolidColorBrush(Colors.Transparent); };
        Anim.Pressable(box);
        _items.Add((box, iconHost, outline, filled));
        Children.Add(box);
    }

    public void Select(int idx, bool notify = true)
    {
        Selected = idx;
        for (var i = 0; i < _items.Count; i++)
        {
            var (box, host, outline, filled) = _items[i];
            box.Background = i == idx ? Theme.B(Color.FromArgb(128, 80, 80, 84)) : new SolidColorBrush(Colors.Transparent);
            host.Children.Clear();
            host.Children.Add(Theme.Icon(24, Colors.White, i == idx ? filled : outline));
        }
        if (notify) Changed?.Invoke(idx);
    }
}
