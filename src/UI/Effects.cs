using Microsoft.Graphics.Canvas.Text;
using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// 설명 말풍선 (pear tooltip.ts) — 윈도우 기본 툴팁 대신 테두리 없는 둥근 유리.
/// 450ms 머무르면 요소 아래(공간이 없으면 위)에 뜨고, 누르거나 휠을 굴리면 사라진다.
/// 나타날 때 4px 비켜 있던 자리에서 0.96 → 1 로 (opacity 220 smooth · transform 440 ease)
/// </summary>
public static class Tip
{
    private const int Delay = 450;
    private static Grid? _layer;
    private static Border? _box;
    private static TextBlock? _text;
    private static FrameworkElement? _current;
    private static int _gen;

    public static void Attach(Grid root)
    {
        _text = Theme.Text("", 13, FontWeights.SemiBold, Theme.OnSurface);
        _box = new Border
        {
            Padding = new Thickness(12, 7, 12, 7),
            CornerRadius = new CornerRadius(12),
            Background = Theme.Glass(0.86, Color.FromArgb(255, 46, 46, 48)),
            Child = _text,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Shadow = new ThemeShadow(),
            Translation = new Vector3(0, 0, 20),
        };
        Anim.V(_box).Opacity = 0;
        _layer = new Grid { IsHitTestVisible = false };
        _layer.Children.Add(_box);
        root.Children.Add(_layer);
        root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Hide()), true);
        root.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, _) => Hide()), true);
    }

    /// <summary>ToolTipService.SetToolTip 대신</summary>
    public static void Set(FrameworkElement el, string text)
    {
        el.PointerEntered += (_, _) => Begin(el, text);
        el.PointerExited += (_, _) => { if (_current == el) Hide(); };
        el.Unloaded += (_, _) => { if (_current == el) Hide(); };
    }

    private static async void Begin(FrameworkElement el, string text)
    {
        Hide();
        _current = el;
        var g = ++_gen;
        await Task.Delay(Delay);
        if (g != _gen || _current != el || el.XamlRoot == null || _box == null || _text == null) return;
        _text.Text = text;
        _box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = _box.DesiredSize.Width;
        var h = _box.DesiredSize.Height;
        var win = el.XamlRoot.Size;
        var r = el.TransformToVisual(null).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
        const double gap = 8;
        var top = r.Bottom + gap;
        var above = top + h > win.Height - 8;
        if (above) top = r.Top - h - gap;
        var left = Math.Clamp(r.Left + r.Width / 2 - w / 2, 8, Math.Max(8, win.Width - w - 8));
        _box.Margin = new Thickness(Math.Round(left), Math.Round(top), 0, 0);
        var v = Anim.V(_box);
        v.CenterPoint = new Vector3((float)w / 2, (float)h / 2, 0);
        v.Scale = new Vector3(0.96f, 0.96f, 1);
        Anim.SetTranslation(_box, new Vector3(0, above ? 4 : -4, 0));
        Anim.Opacity(_box, 1, Anim.Fast);
        Anim.Translate(_box, Vector3.Zero);
        Anim.Scale(_box, 1f);
    }

    public static void Hide()
    {
        _gen++;
        _current = null;
        if (_box != null && Anim.V(_box).Opacity > 0) Anim.Opacity(_box, 0, Anim.Fast);
    }
}

/// <summary>
/// 앨범 색 배경 위에서 쓰는 '흐림 덮개' (pear scroll-fade.ts 의 oneui-page-fade).
/// 배경(바탕색 + 앨범 색 번짐)을 그대로 비춰 그리고, 그 위를 그라데이션으로 흐려서
/// 스크롤되는 내용이 배경 속으로 자연스럽게 사라지게 한다 (단색 덮개를 쓰면 앨범 색 위에서 띠가 보였다).
/// </summary>
public sealed class WashFade : Grid
{
    /// <summary>배경 요소 (MainWindow 가 바탕색 + 앨범 색 번짐을 그리는 층)</summary>
    public static UIElement? Source { get; set; }
    private readonly bool _top;
    private SpriteVisual? _sprite;
    private CompositionVisualSurface? _surface;

    public WashFade(bool top)
    {
        _top = top;
        IsHitTestVisible = false;
        Loaded += (_, _) => { Init(); Sync(); };
        SizeChanged += (_, _) => Sync();
        LayoutUpdated += (_, _) => Sync();
    }

    private void Init()
    {
        if (_sprite != null || Source == null) return;
        var c = Anim.V(this).Compositor;
        _surface = c.CreateVisualSurface();
        _surface.SourceVisual = Anim.V(Source);
        var brush = c.CreateSurfaceBrush(_surface);
        brush.Stretch = CompositionStretch.Fill;
        var grad = c.CreateLinearGradientBrush();
        grad.StartPoint = new Vector2(0, 0);
        grad.EndPoint = new Vector2(0, 1);
        if (_top)
        {
            // 위: 위 끝 불투명 → 아래로 투명 (pear: linear-gradient(to bottom, #000, transparent))
            grad.ColorStops.Add(c.CreateColorGradientStop(0, Color.FromArgb(255, 0, 0, 0)));
            grad.ColorStops.Add(c.CreateColorGradientStop(1, Color.FromArgb(0, 0, 0, 0)));
        }
        else
        {
            // 아래: 0.92 · 45% 지점 0.55 · 위 끝 투명
            grad.ColorStops.Add(c.CreateColorGradientStop(0, Color.FromArgb(0, 0, 0, 0)));
            grad.ColorStops.Add(c.CreateColorGradientStop(0.55f, Color.FromArgb(140, 0, 0, 0)));
            grad.ColorStops.Add(c.CreateColorGradientStop(1, Color.FromArgb(235, 0, 0, 0)));
        }
        var mask = c.CreateMaskBrush();
        mask.Source = brush;
        mask.Mask = grad;
        _sprite = c.CreateSpriteVisual();
        _sprite.Brush = mask;
        ElementCompositionPreview.SetElementChildVisual(this, _sprite);
    }

    private Point _lastPos;
    private Size _lastSize;

    private void Sync()
    {
        if (_sprite == null || _surface == null || Source == null || ActualWidth <= 0) return;
        try
        {
            var pos = TransformToVisual(Source).TransformPoint(new Point(0, 0));
            var size = new Size(ActualWidth, ActualHeight);
            if (pos.Equals(_lastPos) && size.Equals(_lastSize)) return;
            _lastPos = pos;
            _lastSize = size;
            _surface.SourceOffset = new Vector2((float)pos.X, (float)pos.Y);
            _surface.SourceSize = new Vector2((float)size.Width, (float)size.Height);
            _sprite.Size = new Vector2((float)size.Width, (float)size.Height);
        }
        catch { /* 아직 배치 전 */ }
    }
}

/// <summary>
/// 패널 안 목록 · 가로 줄의 끝 흐림 (pear scroll-fade.ts — 위 44 · 아래 56, 가로 줄은 양옆 32 / 셸프 56).
/// 유리 패널 위라서 덮개를 칠할 수 없어, 끝 쪽에 걸친 줄 · 카드의 투명도를 낮춘다. 더 있는 쪽만 흐린다
/// </summary>
public static class ListFade
{
    public static void Attach(ScrollViewer sv, bool horizontal = false, double start = 44, double end = 56)
    {
        void Update()
        {
            if (sv.Content is not FrameworkElement content) return;
            var size = horizontal ? sv.ViewportWidth : sv.ViewportHeight;
            var offset = horizontal ? sv.HorizontalOffset : sv.VerticalOffset;
            var scrollable = horizontal ? sv.ScrollableWidth : sv.ScrollableHeight;
            var fadeStart = offset > 1 ? start : 0;
            var fadeEnd = offset < scrollable - 1 ? end : 0;
            foreach (var item in Items(content))
            {
                double a = 1;
                try
                {
                    var r = item.TransformToVisual(sv).TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
                    var lo = horizontal ? r.Left : r.Top;
                    var hi = horizontal ? r.Right : r.Bottom;
                    var mid = (lo + hi) / 2;
                    if (fadeStart > 0) a = Math.Min(a, Math.Clamp(mid / fadeStart, 0, 1));
                    if (fadeEnd > 0) a = Math.Min(a, Math.Clamp((size - mid) / fadeEnd, 0, 1));
                }
                catch { }
                // 0.15 밑으로는 안 내려서 끝에 걸친 줄도 무엇인지는 보이게
                var op = a >= 1 ? 1 : 0.15 + 0.85 * a;
                if (Math.Abs(item.Opacity - op) > 0.01) item.Opacity = op;
            }
        }
        sv.ViewChanged += (_, _) => Update();
        sv.SizeChanged += (_, _) => Update();
        sv.Loaded += (_, _) =>
        {
            if (sv.Content is FrameworkElement c) c.SizeChanged += (_, _) => Update();
            Update();
        };
    }

    /// <summary>세로 · 가로 StackPanel 은 안으로 들어가고, 나머지는 한 덩어리 줄로 본다</summary>
    private static IEnumerable<FrameworkElement> Items(FrameworkElement el)
    {
        if (el is StackPanel sp)
        {
            foreach (var c in sp.Children.OfType<FrameworkElement>())
                foreach (var x in Items(c)) yield return x;
        }
        else yield return el;
    }
}

/// <summary>Win2D 로 그릴 때 쓸 글꼴 — XAML 과 같은 순서(Pretendard → Segoe UI Variable → 맑은 고딕)로 설치된 첫 글꼴</summary>
public static class Win2DFont
{
    private static string? _family;

    public static string Family
    {
        get
        {
            if (_family != null) return _family;
            try
            {
                var installed = CanvasTextFormat.GetSystemFontFamilies().ToHashSet(StringComparer.OrdinalIgnoreCase);
                _family = new[] { "Pretendard Variable", "Pretendard", "Segoe UI Variable Text", "Malgun Gothic" }
                    .FirstOrDefault(installed.Contains) ?? "Segoe UI";
            }
            catch (Exception e)
            {
                Log.Warn($"글꼴 목록 실패: {e.Message}");
                _family = "Segoe UI";
            }
            return _family;
        }
    }
}
