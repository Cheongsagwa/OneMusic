using Windows.UI.Text;

namespace OneMusic.UI;

/// <summary>
/// 칸보다 긴 글자를 버스 LED 처럼 흘려서 끝까지 보여 주는 한 줄 글자.
/// 2.2초 멈춤 → 초당 38px 로 끝까지 → 1.4초 멈춤 → 살짝 사라졌다 처음으로 → 반복.
/// </summary>
public sealed class Marquee : Grid
{
    private readonly TextBlock _text;
    private readonly Canvas _host = new();
    private readonly RectangleGeometry _clip = new();
    private string _key = "";

    public Marquee(double size, FontWeight weight, Color color)
    {
        _text = Theme.Text("", size, weight, color);
        _text.TextTrimming = TextTrimming.None;
        _text.HorizontalAlignment = HorizontalAlignment.Left;
        // Grid 에 바로 넣으면 칸 폭으로 잘린 채 배치돼서 흘려도 뒷부분이 안 보인다 → Canvas 위에 제 길이 그대로
        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _host.Height = Math.Max(1, _text.DesiredSize.Height);
        _host.Children.Add(_text);
        Children.Add(_host);
        Clip = _clip;
        SizeChanged += (_, _) =>
        {
            _clip.Rect = new Rect(0, -4, ActualWidth, ActualHeight + 8);
            Restart();
        };
        _text.SizeChanged += (_, _) => Restart();
    }

    public string Text
    {
        get => _text.Text;
        set
        {
            if (_text.Text == value) return;
            _text.Text = value;
            Restart();
        }
    }

    public Brush Foreground
    {
        set => _text.Foreground = value;
    }

    private void Restart()
    {
        // 글자의 실제 폭 (칸에 잘리기 전)
        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var need = _text.DesiredSize.Width;
        _host.Height = Math.Max(1, _text.DesiredSize.Height);
        var avail = ActualWidth;
        var key = $"{_text.Text}|{need:0}|{avail:0}";
        if (key == _key) return;
        _key = key;

        var v = Anim.V(_text);
        ElementCompositionPreview.SetIsTranslationEnabled(_text, true);
        v.StopAnimation("Translation");
        v.StopAnimation("Opacity");
        v.Properties.InsertVector3("Translation", Vector3.Zero);
        v.Opacity = 1;
        var over = (float)(need - avail);
        if (over <= 1 || avail <= 0) return;

        const float hold = 2.2f, holdEnd = 1.4f, fadeT = 0.28f, speed = 38f;
        var move = MathF.Max(0.9f, over / speed);
        var total = hold + move + holdEnd + fadeT * 2;
        float At(float s) => s / total;
        var c = v.Compositor;
        var ease = c.CreateCubicBezierEasingFunction(new Vector2(0.45f, 0), new Vector2(0.55f, 1));
        var linear = c.CreateLinearEasingFunction();

        var x = c.CreateVector3KeyFrameAnimation();
        x.InsertKeyFrame(0f, Vector3.Zero);
        x.InsertKeyFrame(At(hold), Vector3.Zero, linear);
        x.InsertKeyFrame(At(hold + move), new Vector3(-over, 0, 0), ease);
        x.InsertKeyFrame(At(hold + move + holdEnd + fadeT), new Vector3(-over, 0, 0), linear);
        x.InsertKeyFrame(At(hold + move + holdEnd + fadeT) + 0.0001f, Vector3.Zero, linear);
        x.InsertKeyFrame(1f, Vector3.Zero, linear);
        x.Duration = TimeSpan.FromSeconds(total);
        x.IterationBehavior = AnimationIterationBehavior.Forever;
        v.StartAnimation("Translation", x);

        var o = c.CreateScalarKeyFrameAnimation();
        o.InsertKeyFrame(0f, 1f);
        o.InsertKeyFrame(At(hold + move + holdEnd), 1f, linear);
        o.InsertKeyFrame(At(hold + move + holdEnd + fadeT), 0f, linear);
        o.InsertKeyFrame(1f, 1f, linear);
        o.Duration = TimeSpan.FromSeconds(total);
        o.IterationBehavior = AnimationIterationBehavior.Forever;
        v.StartAnimation("Opacity", o);
    }
}
