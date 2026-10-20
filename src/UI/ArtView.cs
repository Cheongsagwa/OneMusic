using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// 재생 바 · 재생 화면 앨범아트 (pear image-fade.ts).
/// 마지막으로 보여 준 아트를 바로 밑 '받침'에 깔아 두고, 새 아트를 다 불러오면
/// 가운데부터 번지듯(radial mask 0 → 180%) 드러낸다 — 700ms cubic-bezier(0.33, 0, 0.2, 1), 투명도 0.2 → 1.
/// 불러오는 동안에도 이전 아트가 그대로 보인다(회색 칸 없음). 둥근 모서리는 아트와 같게 잘라 낸다.
/// </summary>
public sealed class ArtView : Grid
{
    private readonly float _radius;
    private readonly int _decode;
    private ContainerVisual? _root;
    private SpriteVisual? _under, _top;
    private CompositionGeometricClip? _clip;
    private CompositionRoundedRectangleGeometry? _clipGeo;
    private CompositionSurfaceBrush? _shown;
    /// <summary>보이는 아트 · 받침 아트의 그림 (바뀌면 다 쓴 것은 바로 놓아 GPU 메모리를 돌려준다)</summary>
    private LoadedImageSurface? _shownSurface, _underSurface;
    private string? _pendingUrl;
    private int _gen;

    public ArtView(float radius, int decodeSize)
    {
        _radius = radius;
        _decode = decodeSize;
        Background = Theme.B(Color.FromArgb(20, 255, 255, 255));
        CornerRadius = new CornerRadius(radius);
        Loaded += (_, _) => { Init(); Resize(); if (_pendingUrl != null) Show(_pendingUrl); };
        SizeChanged += (_, _) => Resize();
    }

    private Compositor C => Anim.V(this).Compositor;

    private void Init()
    {
        if (_root != null) return;
        var c = C;
        _root = c.CreateContainerVisual();
        _clipGeo = c.CreateRoundedRectangleGeometry();
        _clipGeo.CornerRadius = new Vector2(_radius, _radius);
        _clip = c.CreateGeometricClip(_clipGeo);
        _root.Clip = _clip;
        _under = c.CreateSpriteVisual();
        _top = c.CreateSpriteVisual();
        _root.Children.InsertAtTop(_under);
        _root.Children.InsertAtTop(_top);
        ElementCompositionPreview.SetElementChildVisual(this, _root);
    }

    private void Resize()
    {
        if (_root == null || _under == null || _top == null || _clipGeo == null) return;
        var size = new Vector2((float)ActualWidth, (float)ActualHeight);
        _root.Size = size;
        _under.Size = size;
        _top.Size = size;
        _clipGeo.Size = size;
    }

    /// <summary>새 아트로 바꾼다 (주소가 같으면 그대로)</summary>
    public void Show(string? url)
    {
        _pendingUrl = url;
        if (_root == null) return;
        var gen = ++_gen;
        var (u, fallback) = Theme.ImageUrl(url, _decode);
        if (u == null) return;
        Load(u, fallback, gen);
    }

    private void Load(string url, string? fallback, int gen)
    {
        LoadedImageSurface surface;
        try { surface = LoadedImageSurface.StartLoadFromUri(new Uri(url), new Size(_decode * 2, _decode * 2)); }
        catch (Exception e) { Log.Warn($"아트 불러오기 실패: {e.Message}"); return; }
        surface.LoadCompleted += (s, e) =>
        {
            if (gen != _gen) { s.Dispose(); return; }
            if (e.Status != LoadedImageSourceLoadStatus.Success)
            {
                s.Dispose();
                if (fallback != null && fallback != url) Load(fallback, null, gen);
                return;
            }
            Reveal(s);
        };
    }

    private void Reveal(LoadedImageSurface surface)
    {
        if (_under == null || _top == null) return;
        var c = C;
        var brush = c.CreateSurfaceBrush(surface);
        brush.Stretch = CompositionStretch.UniformToFill;
        brush.HorizontalAlignmentRatio = 0.5f;
        brush.VerticalAlignmentRatio = 0.5f;
        // 지금 보이던 아트는 받침으로, 그 전 받침은 버린다
        var oldUnder = _under.Brush;
        var oldUnderSurface = _underSurface;
        if (_shown != null)
        {
            _under.Brush = _shown;
            _underSurface = _shownSurface;
            oldUnder?.Dispose();
            oldUnderSurface?.Dispose();
        }
        _shown = brush;
        _shownSurface = surface;

        // 가운데에서 번지는 원형 마스크 (안쪽 45% 까지 불투명 → 가장자리 투명)
        var radial = c.CreateRadialGradientBrush();
        radial.EllipseCenter = new Vector2(0.5f, 0.5f);
        radial.EllipseRadius = new Vector2(0, 0);
        radial.ColorStops.Add(c.CreateColorGradientStop(0, Colors.Black));
        radial.ColorStops.Add(c.CreateColorGradientStop(0.45f, Colors.Black));
        radial.ColorStops.Add(c.CreateColorGradientStop(1, Color.FromArgb(0, 0, 0, 0)));
        var mask = c.CreateMaskBrush();
        mask.Source = brush;
        mask.Mask = radial;
        var oldMask = _top.Brush as CompositionMaskBrush;
        _top.Brush = mask;
        oldMask?.Mask?.Dispose();
        oldMask?.Dispose();

        var ease = c.CreateCubicBezierEasingFunction(new Vector2(0.33f, 0), new Vector2(0.2f, 1));
        var grow = c.CreateVector2KeyFrameAnimation();
        // 180% 의 farthest-corner ≈ 반지름 1.3 (가장자리 0.5 기준)
        grow.InsertKeyFrame(1f, new Vector2(1.3f, 1.3f), ease);
        grow.Duration = TimeSpan.FromMilliseconds(700);
        radial.StartAnimation("EllipseRadius", grow);
        _top.Opacity = 0.2f;
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 1f, ease);
        fade.Duration = TimeSpan.FromMilliseconds(700);
        _top.StartAnimation("Opacity", fade);
    }
}
