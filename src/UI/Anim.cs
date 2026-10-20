using Microsoft.UI.Input;

namespace OneMusic.UI;

/// <summary>iOS / One UI 느낌의 스프링 · 감속 애니메이션 (GPU 컴포지션에서 돈다 → 화면 스레드를 막지 않음)</summary>
public static class Anim
{
    public static Visual V(UIElement el) => ElementCompositionPreview.GetElementVisual(el);

    private static CompositionEasingFunction? _smooth;
    public static CompositionEasingFunction Smooth(Compositor c) =>
        _smooth ??= c.CreateCubicBezierEasingFunction(new Vector2(0.22f, 1f), new Vector2(0.36f, 1f));

    private static void Center(UIElement el, Visual v)
    {
        var s = el.ActualSize;
        v.CenterPoint = new Vector3(s.X / 2, s.Y / 2, 0);
    }

    /// <summary>크기를 스프링으로 (누를 때 0.92 → 놓으면 살짝 튕기며 1)</summary>
    public static void Scale(UIElement el, float to, float damping = 0.55f, int periodMs = 45)
    {
        var v = V(el);
        Center(el, v);
        var a = v.Compositor.CreateSpringVector3Animation();
        a.FinalValue = new Vector3(to, to, 1);
        a.DampingRatio = damping;
        a.Period = TimeSpan.FromMilliseconds(periodMs);
        v.StartAnimation("Scale", a);
    }

    public static void Opacity(UIElement el, float to, int ms = 280, int delayMs = 0)
    {
        var v = V(el);
        var a = v.Compositor.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(1f, to, Smooth(v.Compositor));
        a.Duration = TimeSpan.FromMilliseconds(ms);
        if (delayMs > 0) a.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        v.StartAnimation("Opacity", a);
    }

    /// <summary>위치 이동(Translation)을 스프링으로</summary>
    public static void Translate(UIElement el, Vector3 to, float damping = 0.8f, int periodMs = 60)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(el, true);
        var v = V(el);
        var a = v.Compositor.CreateSpringVector3Animation();
        a.FinalValue = to;
        a.DampingRatio = damping;
        a.Period = TimeSpan.FromMilliseconds(periodMs);
        v.StartAnimation("Translation", a);
    }

    public static void SetTranslation(UIElement el, Vector3 value)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(el, true);
        var v = V(el);
        v.StopAnimation("Translation");
        v.Properties.InsertVector3("Translation", value);
    }

    /// <summary>위치 · 크기가 바뀌면(레이아웃 변화) 자동으로 부드럽게 따라가게 한다</summary>
    public static void ImplicitLayout(UIElement el, int ms = 420)
    {
        var v = V(el);
        var c = v.Compositor;
        var offset = c.CreateVector3KeyFrameAnimation();
        offset.Target = "Offset";
        offset.InsertExpressionKeyFrame(1f, "this.FinalValue", Smooth(c));
        offset.Duration = TimeSpan.FromMilliseconds(ms);
        var col = c.CreateImplicitAnimationCollection();
        col["Offset"] = offset;
        v.ImplicitAnimations = col;
    }

    /// <summary>나타날 때 살짝 떠오르며 페이드인</summary>
    public static void Appear(UIElement el, int delayMs = 0, float fromY = 10)
    {
        var v = V(el);
        v.Opacity = 0;
        SetTranslation(el, new Vector3(0, fromY, 0));
        Opacity(el, 1, 360, delayMs);
        var c = v.Compositor;
        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(1f, Vector3.Zero, Smooth(c));
        a.Duration = TimeSpan.FromMilliseconds(460);
        if (delayMs > 0) a.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        v.StartAnimation("Translation", a);
    }

    /// <summary>누르면 줄어들었다가 놓으면 튕기는 느낌</summary>
    public static void Pressable(UIElement el, float pressed = 0.92f)
    {
        el.PointerPressed += (_, _) => Scale(el, pressed, 0.9f, 30);
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
        Anim.Pressable(_content, 0.86f);
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
