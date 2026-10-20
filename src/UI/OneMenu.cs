using Microsoft.UI.Input;

namespace OneMusic.UI;

/// <summary>
/// pear ⋮ 메뉴와 같은 팝업 메뉴 — 뒤가 흐리게 비치는 유리(26px 둥근 모서리), 아이콘 + 글자 줄(48px),
/// 맨 위 좋아요 · 싫어요 두 칸 줄. 열릴 때 0.92 에서 커지고(oneui-pop), 닫힐 때 200ms 줄어들며 사라진다(oneui-pop-out)
/// </summary>
public sealed class OneMenu
{
    private static readonly List<OneMenu> Open = new();
    /// <summary>메뉴를 띄우는 맨 위 층 (Popup 대신 — 재생 바에서 Popup · MenuFlyout 이 안 뜨던 문제)</summary>
    private static Grid? _layer;
    private Grid? _dismiss;
    private readonly StackPanel _items = new() { Padding = new Thickness(8) };
    private readonly Border _box;
    private bool _closing;

    public OneMenu()
    {
        _box = new Border
        {
            Width = 264,
            CornerRadius = new CornerRadius(Theme.RadiusCard),
            Background = Theme.Glass(0.55, Color.FromArgb(255, 40, 40, 44)),
            BorderBrush = Theme.B(Color.FromArgb(24, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = _items,
            Shadow = new ThemeShadow(),
            Translation = new Vector3(0, 0, 32),
        };
    }

    public static bool IsOpen => Open.Count > 0;

    public static void Attach(Grid root)
    {
        _layer = new Grid { Visibility = Visibility.Collapsed };
        root.Children.Add(_layer);
    }

    public static void CloseAll()
    {
        foreach (var m in Open.ToList()) m.Close();
    }

    public void Header(string text)
    {
        var t = Theme.Text(text, 15, FontWeights.ExtraBold, Theme.OnSurface);
        t.Margin = new Thickness(16, 10, 16, 8);
        _items.Children.Add(t);
    }

    public void Separator() =>
        _items.Children.Add(new XRect { Height = 1, Fill = Theme.B(Theme.Divider), Margin = new Thickness(16, 6, 16, 6) });

    public void Item(string text, string icon, Action click, bool keepOpen = false) =>
        _items.Children.Add(Row(text, icon, click, keepOpen));

    /// <summary>한 줄 두 칸 (좋아요 · 싫어요)</summary>
    public void Pair((string Text, string Icon, Action Click) a, (string Text, string Icon, Action Click) b)
    {
        var g = new Grid { ColumnSpacing = 4 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var ra = Row(a.Text, a.Icon, a.Click, false, center: true);
        var rb = Row(b.Text, b.Icon, b.Click, false, center: true);
        Grid.SetColumn(rb, 1);
        g.Children.Add(ra);
        g.Children.Add(rb);
        _items.Children.Add(g);
    }

    private FrameworkElement Row(string text, string icon, Action click, bool keepOpen, bool center = false)
    {
        var g = new Grid { Height = 48, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Colors.Transparent) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(center ? 0 : 16, 0, 12, 0), HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left };
        var ic = Theme.Icon(icon, 20, Colors.White, stroke: true, strokeWidth: 1.7);
        ic.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(ic);
        var t = Theme.Text(text, 15, FontWeights.SemiBold, Theme.OnSurface);
        t.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(t);
        g.Children.Add(row);
        g.PointerEntered += (_, _) => g.Background = Theme.B(Theme.ChipHover);
        g.PointerExited += (_, _) => g.Background = new SolidColorBrush(Colors.Transparent);
        Anim.Pressable(g, 0.96f);
        g.Tapped += (_, e) =>
        {
            e.Handled = true;
            if (!keepOpen) Close();
            try { click(); }
            catch (Exception ex) { Core.Log.Error("메뉴 동작 실패", ex); }
        };
        return g;
    }

    /// <summary>at 옆에 연다. pos(창 기준 좌표)가 있으면 그 자리에</summary>
    public void Show(FrameworkElement at, Point? pos = null)
    {
        try { ShowCore(at, pos); }
        catch (Exception e)
        {
            Core.Log.Error("메뉴 열기 실패", e);
            Toast.Show("메뉴를 열지 못했어요: " + e.Message);
        }
    }

    private void ShowCore(FrameworkElement at, Point? pos)
    {
        CloseAll();
        var layer = _layer ?? throw new InvalidOperationException("메뉴 층이 없음");
        _box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = _box.DesiredSize.Width;
        var h = _box.DesiredSize.Height;
        var winW = layer.ActualWidth;
        var winH = layer.ActualHeight;
        double x, y;
        if (pos is { } p)
        {
            x = p.X;
            y = p.Y;
        }
        else
        {
            var r = at.TransformToVisual(layer).TransformBounds(new Rect(0, 0, at.ActualWidth, at.ActualHeight));
            x = r.X;
            y = r.Bottom + 6;
            // 아래 공간이 없으면 위로 (재생 바의 ⋮)
            if (y + h > winH - 8) y = r.Y - h - 6;
        }
        x = Math.Clamp(x, 8, Math.Max(8, winW - w - 8));
        y = Math.Clamp(y, 8, Math.Max(8, winH - h - 8));
        // 바깥을 누르면 닫히는 투명 판
        _dismiss = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        _dismiss.PointerPressed += (_, e) => { e.Handled = true; Close(); };
        _box.HorizontalAlignment = HorizontalAlignment.Left;
        _box.VerticalAlignment = VerticalAlignment.Top;
        _box.Margin = new Thickness(x, y, 0, 0);
        layer.Children.Add(_dismiss);
        layer.Children.Add(_box);
        layer.Visibility = Visibility.Visible;
        Open.Add(this);
        Anim.Pop(_box);
    }

    public async void Close()
    {
        if (_layer == null || _closing || !Open.Contains(this)) return;
        _closing = true;
        Open.Remove(this);
        if (_dismiss != null) _layer.Children.Remove(_dismiss);
        // oneui-pop-out: 200ms 동안 0.94 로 줄며 사라짐
        var v = Anim.V(_box);
        v.CenterPoint = new Vector3((float)_box.ActualWidth / 2, (float)_box.ActualHeight / 2, 0);
        Anim.Opacity(_box, 0, 200);
        var c = v.Compositor;
        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(1f, new Vector3(0.94f, 0.94f, 1), Anim.Smooth(c));
        a.Duration = TimeSpan.FromMilliseconds(200);
        v.StartAnimation("Scale", a);
        _box.IsHitTestVisible = false;
        await Task.Delay(200);
        _layer.Children.Remove(_box);
        if (Open.Count == 0) _layer.Visibility = Visibility.Collapsed;
    }
}
