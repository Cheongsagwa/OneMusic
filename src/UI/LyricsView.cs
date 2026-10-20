using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>지금 곡의 가사 상태 (재생 바 가사 한 줄 · 재생 화면 가사가 함께 쓴다)</summary>
public sealed class LyricsState
{
    public static LyricsState Instance { get; } = new();
    public List<LyricLine>? Lines { get; private set; }
    public bool Loading { get; private set; }
    public string? ForVideo { get; private set; }
    public event Action? Changed;
    private CancellationTokenSource? _cts;

    public async void Load(Track? t)
    {
        _cts?.Cancel();
        Lines = null;
        ForVideo = t?.VideoId;
        Loading = t != null;
        Changed?.Invoke();
        if (t == null) return;
        var cts = _cts = new CancellationTokenSource();
        try
        {
            var lines = await LyricsService.Instance.Get(t, cts.Token);
            if (cts.IsCancellationRequested) return;
            Lines = lines;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { Log.Warn($"가사 실패: {e.Message}"); }
        Loading = false;
        Changed?.Invoke();
    }

    /// <summary>지금 시간에 부르는 줄 번호 (-1 = 아직 시작 전)</summary>
    public int IndexAt(double seconds)
    {
        var lines = Lines;
        if (lines == null) return -1;
        var ms = seconds * 1000;
        for (var i = lines.Count - 1; i >= 0; i--)
            if (lines[i].TimeMs <= ms) return i;
        return -1;
    }
}

/// <summary>
/// 재생 화면의 실시간 가사 — Win2D 로 직접 그린다.
/// 지금 줄이 위에서 1/3 지점에 오도록 부드럽게 스크롤, 위 · 아래 끝은 그라데이션으로 흐려지고, 줄을 누르면 그 위치로 이동.
/// </summary>
public sealed class LyricsView : Grid
{
    private const float FontSize = 22;
    private const float LineGap = 18;
    private const float FadeTop = 44, FadeBottom = 56;

    private readonly CanvasControl _canvas = new();
    private readonly TextBlock _empty;
    private float _scroll, _scrollTarget;
    private float _userOffset; // 휠로 직접 굴린 양 (몇 초 뒤 제자리로)
    private long _lastWheel;
    private readonly List<(float Top, float Height)> _layout = new();
    private float _layoutWidth = -1;
    private List<LyricLine>? _layoutLines;
    private readonly float[] _glow = new float[2048];
    private long _last;
    private bool _active;

    public LyricsView()
    {
        Background = new SolidColorBrush(Colors.Transparent);
        _canvas.Draw += OnDraw;
        Children.Add(_canvas);
        _empty = Theme.Text("", 16, FontWeights.SemiBold, Theme.Secondary);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_empty);
        LyricsState.Instance.Changed += OnLyricsChanged;
        Loaded += (_, _) => { CompositionTarget.Rendering += OnFrame; OnLyricsChanged(); };
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnFrame;
        Tapped += OnTapped;
        PointerWheelChanged += (_, e) =>
        {
            _userOffset -= e.GetCurrentPoint(this).Properties.MouseWheelDelta * 0.6f;
            _lastWheel = Environment.TickCount64;
            e.Handled = true;
        };
        ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
    }

    /// <summary>탭이 보일 때만 그린다</summary>
    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            if (value) _canvas.Invalidate();
        }
    }

    private void OnLyricsChanged()
    {
        var s = LyricsState.Instance;
        _empty.Text = s.Loading ? "가사를 불러오는 중…" : s.Lines == null ? (s.ForVideo == null ? "" : "이 곡의 실시간 가사를 찾지 못했어요") : "";
        _layoutLines = null;
        _userOffset = 0;
        Array.Clear(_glow);
        _canvas.Invalidate();
    }

    private void EnsureLayout(CanvasControl sender, float width)
    {
        var lines = LyricsState.Instance.Lines;
        if (lines == _layoutLines && Math.Abs(width - _layoutWidth) < 0.5f) return;
        _layoutLines = lines;
        _layoutWidth = width;
        _layout.Clear();
        if (lines == null) return;
        var y = 0f;
        using var fmt = Format();
        foreach (var l in lines)
        {
            using var tl = new CanvasTextLayout(sender, string.IsNullOrEmpty(l.Text) ? "♪" : l.Text, fmt, width, 9999);
            var h = (float)tl.LayoutBounds.Height;
            _layout.Add((y, h));
            y += h + LineGap;
        }
    }

    private static CanvasTextFormat Format() => new()
    {
        FontFamily = "Pretendard Variable",
        FontSize = FontSize,
        FontWeight = FontWeights.Bold,
        WordWrapping = CanvasWordWrapping.WholeWord,
    };

    private void OnFrame(object? sender, object e)
    {
        if (!_active || LyricsState.Instance.Lines == null) return;
        var now = Environment.TickCount64;
        if (now - _last < 15) return;
        var dt = _last == 0 ? 0.016f : Math.Min((now - _last) / 1000f, 0.1f);
        _last = now;
        if (now - _lastWheel > 3500) _userOffset += (0 - _userOffset) * (1 - MathF.Exp(-dt / 0.25f));
        var p = PlayerService.Instance;
        var idx = LyricsState.Instance.IndexAt(p.Position + 0.15);
        if (idx >= 0 && idx < _layout.Count)
        {
            var (top, h) = _layout[idx];
            _scrollTarget = top + h / 2 - (float)ActualHeight / 3;
        }
        else _scrollTarget = -(float)ActualHeight / 3 + 20;
        var target = _scrollTarget + _userOffset;
        _scroll += (target - _scroll) * (1 - MathF.Exp(-dt / 0.12f));
        for (var i = 0; i < _layout.Count && i < _glow.Length; i++)
        {
            var want = i == idx ? 1f : 0f;
            _glow[i] += (want - _glow[i]) * (1 - MathF.Exp(-dt / 0.1f));
        }
        _canvas.Invalidate();
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var lines = LyricsState.Instance.Lines;
        if (lines == null) return;
        var w = (float)ActualWidth - 48;
        var h = (float)ActualHeight;
        if (w <= 0) return;
        EnsureLayout(sender, w);
        var ds = args.DrawingSession;
        using var fmt = Format();
        for (var i = 0; i < _layout.Count; i++)
        {
            var (top, lh) = _layout[i];
            var y = top - _scroll;
            if (y + lh < -10 || y > h + 10) continue;
            // 위 · 아래 끝에서는 흐려진다
            var edge = Math.Min(1f, Math.Min((y + lh) / FadeTop, (h - y) / FadeBottom));
            edge = Math.Clamp(edge, 0, 1);
            var g = _glow[i];
            var alpha = (0.32f + 0.68f * g) * edge;
            if (alpha <= 0.01f) continue;
            var text = string.IsNullOrEmpty(lines[i].Text) ? "♪" : lines[i].Text;
            using var tl = new CanvasTextLayout(sender, text, fmt, w, 9999);
            ds.DrawTextLayout(tl, 24, y, Colors.White.WithAlpha(alpha));
        }
    }

    private void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        var lines = LyricsState.Instance.Lines;
        if (lines == null) return;
        var y = (float)e.GetPosition(this).Y + _scroll;
        for (var i = 0; i < _layout.Count; i++)
        {
            var (top, lh) = _layout[i];
            if (y >= top - LineGap / 2 && y <= top + lh + LineGap / 2)
            {
                PlayerService.Instance.Seek(lines[i].TimeMs / 1000.0 + 0.01);
                _userOffset = 0;
                return;
            }
        }
    }
}
