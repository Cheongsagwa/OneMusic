using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// 검색 추천 목록 (pear 35 · 58) — 검색창 밑 8px 에 떨어진 둥근(24) 유리 카드, 뒤가 흐리게 비친다(블러 20 · 진한 유리).
/// 기본 AutoSuggestBox 목록은 따로 떠서(별도 창) 앱 내용을 흐리게 비추지 못해, 앱 화면 맨 위 층에 직접 그린다.
/// 줄: 돋보기 + 검색어 / 아트 + 제목 + '노래 • 가수 …'. 마우스 · ↑↓ 로 고르고 Enter, 줄마다 oneui-drop 으로 내려앉는다
/// </summary>
public sealed class SearchDropdown
{
    private readonly Grid _layer = new() { Visibility = Visibility.Collapsed };
    private readonly Border _card;
    private readonly StackPanel _list = new() { Padding = new Thickness(6, 6, 6, 6), Spacing = 0 };
    private readonly List<(Grid Row, object Tag)> _rows = new();
    private int _sel = -1;
    public bool IsOpen => _layer.Visibility == Visibility.Visible;
    public bool PointerInside { get; private set; }
    public event Action<object>? Chosen;

    public SearchDropdown(Grid root)
    {
        _card = new Border
        {
            Width = 480,
            CornerRadius = new CornerRadius(24),
            Background = Theme.Glass(0.76, Color.FromArgb(255, 46, 46, 48), blur: 40),
            Child = _list,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Shadow = new ThemeShadow(),
            Translation = new Vector3(0, 0, 40),
        };
        _card.PointerEntered += (_, _) => PointerInside = true;
        _card.PointerExited += (_, _) => PointerInside = false;
        _layer.Children.Add(_card);
        // 카드 밖은 클릭이 그대로 아래로 지나가게 (층 자체는 히트 테스트 안 함)
        _layer.Background = null;
        root.Children.Add(_layer);
    }

    /// <summary>검색창(anchor) 바로 밑에 띄운다</summary>
    public void Show(FrameworkElement anchor, IReadOnlyList<string> words, IReadOnlyList<SearchItem> items)
    {
        _list.Children.Clear();
        _rows.Clear();
        _sel = -1;
        if (words.Count == 0 && items.Count == 0) { Hide(); return; }
        var delay = 0;
        foreach (var w in words) AddRow(Row(w, null), w, ref delay);
        if (words.Count > 0 && items.Count > 0)
            _list.Children.Add(new XRect { Height = 1, Fill = Theme.B(Theme.Divider), Margin = new Thickness(12, 6, 12, 6) });
        foreach (var it in items) AddRow(Row(it.Title, it), it, ref delay);
        var p = anchor.TransformToVisual(null).TransformPoint(new Point(0, 0));
        _card.Margin = new Thickness(p.X, p.Y + anchor.ActualHeight + 8, 0, 0);
        if (!IsOpen)
        {
            _layer.Visibility = Visibility.Visible;
            Anim.Pop(_card, 0, new Vector2(0.5f, 0));
        }
    }

    private void AddRow(Grid row, object tag, ref int delay)
    {
        var idx = _rows.Count;
        _rows.Add((row, tag));
        _list.Children.Add(row);
        Anim.Drop(row, delay);
        delay = Math.Min(delay + 25, 200);
        row.PointerEntered += (_, _) => Highlight(idx);
        row.Tapped += (_, _) => { Hide(); Chosen?.Invoke(tag); };
    }

    public void Hide()
    {
        if (!IsOpen) return;
        PointerInside = false;
        _layer.Visibility = Visibility.Collapsed;
    }

    /// <summary>↑ · ↓ 로 고르기</summary>
    public void Move(int d)
    {
        if (_rows.Count == 0) return;
        Highlight(_sel < 0 ? (d > 0 ? 0 : _rows.Count - 1) : Math.Clamp(_sel + d, 0, _rows.Count - 1));
    }

    public object? Selected => _sel >= 0 && _sel < _rows.Count ? _rows[_sel].Tag : null;

    private void Highlight(int idx)
    {
        _sel = idx;
        for (var i = 0; i < _rows.Count; i++)
            _rows[i].Row.Background = i == idx ? Theme.B(Color.FromArgb(15, 255, 255, 255)) : new SolidColorBrush(Colors.Transparent);
    }

    private static Grid Row(string text, SearchItem? item)
    {
        var g = new Grid
        {
            Height = item == null ? 44 : 56,
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Colors.Transparent),
            Padding = new Thickness(12, 0, 12, 0),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        if (item == null)
        {
            var icon = Theme.Icon(18, Theme.Secondary, Icons.SearchSesl);
            icon.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(icon);
            var t = Theme.Text(text, 15, FontWeights.SemiBold, Theme.OnSurface);
            t.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(t);
        }
        else
        {
            row.Children.Add(new Border
            {
                Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(item.Round ? 20 : 6),
                Background = Theme.B(Theme.Chip),
                Child = new Image { Width = 40, Height = 40, Source = Theme.Image(item.Thumb, 72), Stretch = Stretch.UniformToFill },
            });
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1, MaxWidth = 380 };
            col.Children.Add(Theme.Text(item.Title, 15, FontWeights.Bold, Theme.OnSurface));
            col.Children.Add(Theme.Text(item.Subtitle, 13, FontWeights.Normal, Theme.Secondary));
            row.Children.Add(col);
        }
        g.Children.Add(row);
        return g;
    }
}
