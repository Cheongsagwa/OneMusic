using Microsoft.UI.Input;

namespace OneMusic.UI;

/// <summary>iOS / One UI 느낌의 스프링 · 감속 애니메이션 (GPU 컴포지션에서 돈다 → 화면 스레드를 막지 않음)</summary>
public static class Anim
{
    public static Visual V(UIElement el) => ElementCompositionPreview.GetElementVisual(el);

    // ── pear One UI 테마(style.css 맨 위 토큰)의 곡선 · 시간 그대로 ──
    // CSS linear() 곡선의 점들을 키프레임으로 꽂아서 똑같이 움직인다
    /// <summary>--oneui-ease: 살짝 넘쳤다 돌아오는 기본 곡선</summary>
    public static readonly float[] Ease = [0f, 0.0147f, 0.0531f, 0.1077f, 0.1726f, 0.2434f, 0.3165f, 0.3891f, 0.4594f, 0.5261f, 0.5882f, 0.6453f, 0.697f, 0.7434f, 0.7846f, 0.8208f, 0.8524f, 0.8796f, 0.903f, 0.9228f, 0.9395f, 0.9534f, 0.9649f, 0.9743f, 0.9819f, 0.988f, 0.9928f, 0.9966f, 0.9994f, 1.0015f, 1.0029f, 1.004f, 1.0046f, 1.0049f, 1.005f, 1.005f, 1.0048f, 1.0045f, 1.0042f, 1.0038f, 1f];
    /// <summary>--oneui-smooth: 넘치지 않는 감속</summary>
    public static readonly float[] SmoothCurve = [0f, 0.0109f, 0.0394f, 0.0801f, 0.129f, 0.1829f, 0.2393f, 0.2964f, 0.3528f, 0.4076f, 0.46f, 0.5095f, 0.556f, 0.5993f, 0.6393f, 0.6761f, 0.7098f, 0.7405f, 0.7683f, 0.7936f, 0.8163f, 0.8368f, 0.8552f, 0.8717f, 0.8864f, 0.8996f, 0.9113f, 0.9217f, 0.931f, 0.9392f, 0.9465f, 0.9529f, 0.9586f, 0.9636f, 0.9681f, 0.972f, 0.9755f, 0.9785f, 0.9812f, 0.9835f, 1f];
    /// <summary>--oneui-spring: 눌렀다 뗄 때 튕기는 곡선</summary>
    public static readonly float[] Spring = [0f, 0.0222f, 0.0797f, 0.1606f, 0.2553f, 0.3562f, 0.4572f, 0.5542f, 0.644f, 0.7249f, 0.7957f, 0.8563f, 0.9067f, 0.9476f, 0.9798f, 1.0043f, 1.022f, 1.0341f, 1.0415f, 1.0452f, 1.046f, 1.0446f, 1.0417f, 1.0379f, 1.0334f, 1.0288f, 1.0241f, 1.0197f, 1.0157f, 1.012f, 1.0089f, 1.0062f, 1.0039f, 1.0021f, 1.0007f, 0.9996f, 0.9989f, 0.9984f, 0.9981f, 0.9979f, 1f];
    public const int Fast = 220, Medium = 440, Morph = 560;
    public const float PressedScale = 0.9f;

    private static CompositionEasingFunction? _smooth, _linear;
    public static CompositionEasingFunction Smooth(Compositor c) =>
        _smooth ??= c.CreateCubicBezierEasingFunction(new Vector2(0.22f, 1f), new Vector2(0.36f, 1f));
    private static CompositionEasingFunction Linear(Compositor c) => _linear ??= c.CreateLinearEasingFunction();

    private static string F(float f) => f.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);

    private static ScalarKeyFrameAnimation Scalar(Compositor c, float to, float[] curve, int ms, int delayMs)
    {
        var a = c.CreateScalarKeyFrameAnimation();
        for (var i = 1; i < curve.Length; i++)
            a.InsertExpressionKeyFrame((float)i / (curve.Length - 1), $"this.StartingValue + ({F(to)} - this.StartingValue) * {F(curve[i])}", Linear(c));
        a.Duration = TimeSpan.FromMilliseconds(ms);
        if (delayMs > 0) a.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        return a;
    }

    private static Vector3KeyFrameAnimation Vec(Compositor c, Vector3 to, float[] curve, int ms, int delayMs)
    {
        var a = c.CreateVector3KeyFrameAnimation();
        var target = $"Vector3({F(to.X)}, {F(to.Y)}, {F(to.Z)})";
        for (var i = 1; i < curve.Length; i++)
            a.InsertExpressionKeyFrame((float)i / (curve.Length - 1), $"this.StartingValue + ({target} - this.StartingValue) * {F(curve[i])}", Linear(c));
        a.Duration = TimeSpan.FromMilliseconds(ms);
        if (delayMs > 0) a.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        return a;
    }

    private static void Center(UIElement el, Visual v)
    {
        var s = el.ActualSize;
        v.CenterPoint = new Vector3(s.X / 2, s.Y / 2, 0);
    }

    /// <summary>
    /// 크기 — pear 와 같게: 줄어들 때는 빠른 감속(220ms smooth), 커질 때(떼거나 마우스 올림)는 600ms 스프링 곡선.
    /// (damping · period 는 예전 호출과 맞추려고 남겨 둔 값이라 쓰지 않는다)
    /// </summary>
    public static void Scale(UIElement el, float to, float damping = 0.55f, int periodMs = 45)
    {
        var v = V(el);
        Center(el, v);
        var shrinking = to < v.Scale.X - 0.001f;
        v.StartAnimation("Scale", Vec(v.Compositor, new Vector3(to, to, 1), shrinking ? SmoothCurve : Spring, shrinking ? Fast : 600, 0));
    }

    /// <summary>투명도 — 기본 440ms ease (pear: opacity var(--oneui-medium) var(--oneui-ease))</summary>
    public static void Opacity(UIElement el, float to, int ms = Medium, int delayMs = 0)
    {
        var v = V(el);
        v.StartAnimation("Opacity", Scalar(v.Compositor, to, SmoothCurve, ms, delayMs));
    }

    /// <summary>위치 이동 — 440ms ease (탭 알약 · 스위치 손잡이 · 재생 화면 올라오기)</summary>
    public static void Translate(UIElement el, Vector3 to, float damping = 0.8f, int periodMs = 60)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(el, true);
        var v = V(el);
        v.StartAnimation("Translation", Vec(v.Compositor, to, Ease, periodMs >= 70 ? Morph : Medium, 0));
    }

    public static void SetTranslation(UIElement el, Vector3 value)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(el, true);
        var v = V(el);
        v.StopAnimation("Translation");
        v.Properties.InsertVector3("Translation", value);
    }

    /// <summary>위치 · 크기가 바뀌면(레이아웃 변화) 자동으로 부드럽게 따라가게 한다</summary>
    public static void ImplicitLayout(UIElement el, int ms = Medium)
    {
        var v = V(el);
        var c = v.Compositor;
        var offset = c.CreateVector3KeyFrameAnimation();
        offset.Target = "Offset";
        for (var i = 1; i < Ease.Length; i++)
            offset.InsertExpressionKeyFrame((float)i / (Ease.Length - 1), $"this.StartingValue + (this.FinalValue - this.StartingValue) * {F(Ease[i])}", Linear(c));
        offset.Duration = TimeSpan.FromMilliseconds(ms);
        var col = c.CreateImplicitAnimationCollection();
        col["Offset"] = offset;
        v.ImplicitAnimations = col;
    }

    /// <summary>나타날 때 — pear 의 oneui-rise: 14px 아래에서 떠오르며 페이드인, 440ms ease</summary>
    public static void Appear(UIElement el, int delayMs = 0, float fromY = 14)
    {
        var v = V(el);
        v.Opacity = 0;
        SetTranslation(el, new Vector3(0, fromY, 0));
        v.StartAnimation("Opacity", Scalar(v.Compositor, 1, Ease, Medium, delayMs));
        v.StartAnimation("Translation", Vec(v.Compositor, Vector3.Zero, Ease, Medium, delayMs));
    }

    private static CompositionEasingFunction? _decel;
    /// <summary>cubic-bezier(0.22, 1, 0.36, 1) — 재생 화면 전환 · 아이콘 전환의 '튕김 없는 우아한 감속'</summary>
    public static CompositionEasingFunction Decel(Compositor c) =>
        _decel ??= c.CreateCubicBezierEasingFunction(new Vector2(0.22f, 1f), new Vector2(0.36f, 1f));

    /// <summary>위치를 우아한 감속으로 (재생 화면 올라오기 640ms)</summary>
    public static void Slide(UIElement el, Vector3 to, int ms)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(el, true);
        var v = V(el);
        var a = v.Compositor.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(1f, to, Decel(v.Compositor));
        a.Duration = TimeSpan.FromMilliseconds(ms);
        v.StartAnimation("Translation", a);
    }

    /// <summary>pear oneui-rise 720ms cubic-bezier(.22,1,.36,1): 14px 아래에서 떠오르며 나타남</summary>
    public static void Rise(UIElement el, int delayMs, int ms = 720)
    {
        var v = V(el);
        var c = v.Compositor;
        v.Opacity = 0;
        SetTranslation(el, new Vector3(0, 14, 0));
        var o = c.CreateScalarKeyFrameAnimation();
        o.InsertKeyFrame(1f, 1f, Decel(c));
        o.Duration = TimeSpan.FromMilliseconds(ms);
        o.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        o.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        v.StartAnimation("Opacity", o);
        var t = c.CreateVector3KeyFrameAnimation();
        t.InsertKeyFrame(1f, Vector3.Zero, Decel(c));
        t.Duration = TimeSpan.FromMilliseconds(ms);
        t.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        v.StartAnimation("Translation", t);
    }

    /// <summary>나타날 때 — pear 의 oneui-pop: 0.92 에서 커지며 (메뉴 · 팝업)</summary>
    public static void Pop(UIElement el, int delayMs = 0)
    {
        var v = V(el);
        Center(el, v);
        v.Opacity = 0;
        v.Scale = new Vector3(0.92f, 0.92f, 1);
        v.StartAnimation("Opacity", Scalar(v.Compositor, 1, SmoothCurve, Medium, delayMs));
        v.StartAnimation("Scale", Vec(v.Compositor, Vector3.One, Ease, Medium, delayMs));
    }

    /// <summary>누르면 0.9 로 줄었다가 놓으면 스프링으로 (pear: --oneui-pressed-scale 0.9)</summary>
    public static void Pressable(UIElement el, float pressed = PressedScale)
    {
        el.PointerPressed += (_, _) => Scale(el, pressed);
        el.PointerReleased += (_, _) => Scale(el, 1f);
        el.PointerCanceled += (_, _) => Scale(el, 1f);
        el.PointerCaptureLost += (_, _) => Scale(el, 1f);
        el.PointerExited += (_, _) => Scale(el, 1f);
    }
}

/// <summary>동그란 아이콘 버튼 — 마우스를 올리면 은은한 원, 누르면 스프링</summary>
public class IconButton : Grid
{
    private readonly XEllipse _hover;
    private readonly Grid _content = new();
    public event Action? Click;

    public IconButton(double size = 40)
    {
        Width = Height = size;
        Background = new SolidColorBrush(Colors.Transparent);
        _hover = new XEllipse { Fill = Theme.B(Theme.ChipHover) };
        Anim.V(_hover).Opacity = 0; // XAML Opacity 가 아니라 GPU 쪽 투명도를 움직인다
        Children.Add(_hover);
        Children.Add(_content);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        PointerEntered += (_, _) => Anim.Opacity(_hover, 1, 180);
        PointerExited += (_, _) => Anim.Opacity(_hover, 0, 220);
        Tapped += (_, e) =>
        {
            e.Handled = true;
            Click?.Invoke();
        };
        Anim.Pressable(_content);
    }

    public UIElement? Content
    {
        get => _content.Children.FirstOrDefault();
        set
        {
            _content.Children.Clear();
            if (value != null) _content.Children.Add(value);
        }
    }

    public Brush HoverBrush
    {
        set => _hover.Fill = value;
    }

    public void SetCursorHand() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
}
