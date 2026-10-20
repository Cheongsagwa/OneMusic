using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// 지금 곡의 가사 상태 (재생 바 가사 한 줄 · 재생 화면 가사가 함께 쓴다).
/// 자동: ☆ 출처 → LRCLib → YT Music → Musixmatch 순서로 처음 찾은 것. ‹ › 로 출처를 직접 고르면 그 출처만
/// </summary>
public sealed class LyricsState
{
    public static LyricsState Instance { get; } = new();
    public List<LyricLine>? Lines { get; private set; }
    /// <summary>시간 정보가 있는 가사인지 (없으면 따라 흐르지 않고 글자만)</summary>
    public bool Synced { get; private set; }
    public bool Loading { get; private set; }
    public string? ForVideo { get; private set; }
    /// <summary>지금 보이는(또는 찾는) 출처</summary>
    public LyricsSource Source { get; private set; } = Settings.Current.LyricsSource;
    /// <summary>출처별로 찾았는지 (null = 아직 모름)</summary>
    public Dictionary<LyricsSource, bool> Found { get; } = new();
    public event Action? Changed;
    private CancellationTokenSource? _cts;
    private Track? _track;

    public void Load(Track? t)
    {
        _track = t;
        Found.Clear();
        Run(null);
    }

    /// <summary>‹ › 로 출처를 직접 고름</summary>
    public void Choose(LyricsSource source) => Run(source);

    /// <summary>☆ — 앞으로 이 출처를 먼저 찾는다</summary>
    public void SetPreferred(LyricsSource source)
    {
        Settings.Current.LyricsSource = source;
        Settings.Current.Save();
        Changed?.Invoke();
    }

    private async void Run(LyricsSource? only)
    {
        _cts?.Cancel();
        var t = _track;
        Lines = null;
        Synced = false;
        ForVideo = t?.VideoId;
        Loading = t != null;
        Source = only ?? Settings.Current.LyricsSource;
        Changed?.Invoke();
        if (t == null) return;
        var cts = _cts = new CancellationTokenSource();
        try
        {
            foreach (var src in only is { } o ? new[] { o } : LyricsService.Order(Settings.Current.LyricsSource))
            {
                Source = src;
                Changed?.Invoke();
                var r = await LyricsService.Instance.Get(t, src, cts.Token);
                if (cts.IsCancellationRequested) return;
                Found[src] = r != null;
                if (r == null) continue;
                Lines = r.Lines;
                Synced = r.Synced;
                break;
            }
            if (Lines == null && only == null) Source = Settings.Current.LyricsSource;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { Log.Warn($"가사 실패: {e.Message}"); }
        Loading = false;
        Changed?.Invoke();
    }

    /// <summary>지금 시간에 부르는 줄 번호 (-1 = 아직 시작 전 · 시간 없는 가사)</summary>
    public int IndexAt(double seconds)
    {
        var lines = Lines;
        if (lines == null || !Synced) return -1;
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
        _empty.Text = s.Loading ? "가사를 불러오는 중…" : s.Lines == null ? (s.ForVideo == null ? "" : "이 출처에서 가사를 찾지 못했어요") : "";
        _scroll = s.Synced ? _scroll : 0;
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
        var synced = LyricsState.Instance.Synced;
        // 시간 없는 가사는 휠로 굴린 자리에 그대로 둔다
        if (synced && now - _lastWheel > 3500) _userOffset += (0 - _userOffset) * (1 - MathF.Exp(-dt / 0.25f));
        if (!synced) _userOffset = Math.Clamp(_userOffset, 0, Math.Max(0, (_layout.Count > 0 ? _layout[^1].Top + _layout[^1].Height : 0) - (float)ActualHeight + 80));
        var p = PlayerService.Instance;
        var idx = LyricsState.Instance.IndexAt(p.Position + 0.15);
        if (idx >= 0 && idx < _layout.Count)
        {
            var (top, h) = _layout[idx];
            _scrollTarget = top + h / 2 - (float)ActualHeight / 3;
        }
        else _scrollTarget = synced ? -(float)ActualHeight / 3 + 20 : -8;
        var target = _scrollTarget + _userOffset;
        _scroll += (target - _scroll) * (1 - MathF.Exp(-dt / 0.12f));
        for (var i = 0; i < _layout.Count && i < _glow.Length; i++)
        {
            var want = i == idx || !synced ? 1f : 0f;
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
        if (lines == null || !LyricsState.Instance.Synced) return;
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

/// <summary>
/// 가사 출처 고르기 (pear synced-lyrics 와 같은 모양) — 유리 카드 안에 ‹ [✓ 출처 이름 ☆] ›, 아래 점(지금 몇 번째).
/// ✓ = 이 출처에서 찾음, ☆ 누르면 앞으로 먼저 찾을 출처(★)
/// </summary>
public sealed class LyricsSourceBar : Grid
{
    private static readonly (LyricsSource Src, string Name)[] Sources =
        [(LyricsSource.LRCLib, "LRCLib"), (LyricsSource.YTMusic, "YTMusic"), (LyricsSource.Musixmatch, "MusixMatch"), (LyricsSource.Genius, "Genius")];
    private readonly StackPanel _center = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };

    public LyricsSourceBar()
    {
        Height = 70;
        CornerRadius = new CornerRadius(20);
        Background = Theme.B(Theme.Chip);
        Margin = new Thickness(0, 6, 0, 8);
        var prev = new IconButton(36) { Content = Theme.Icon(Icons.ChevronLeft, 20, Colors.White, stroke: true, strokeWidth: 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(24, 0, 0, 0) };
        var next = new IconButton(36) { Content = Theme.Icon(Icons.ChevronRight, 20, Colors.White, stroke: true, strokeWidth: 2), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 24, 0) };
        prev.Click += () => Step(-1);
        next.Click += () => Step(1);
        Children.Add(prev);
        Children.Add(next);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 8 };
        mid.Children.Add(_center);
        mid.Children.Add(_dots);
        Children.Add(mid);
        LyricsState.Instance.Changed += Update;
        Update();
    }

    private static int IndexOf(LyricsSource s) => Array.FindIndex(Sources, x => x.Src == s);

    private void Step(int d)
    {
        var i = (IndexOf(LyricsState.Instance.Source) + d + Sources.Length) % Sources.Length;
        Anim.Appear(_center, 0, 0);
        LyricsState.Instance.Choose(Sources[i].Src);
    }

    private void Update()
    {
        var s = LyricsState.Instance;
        var i = IndexOf(s.Source);
        _center.Children.Clear();
        if (s.Found.TryGetValue(s.Source, out var found) && found)
        {
            // ✓ 흰 동그라미 안 체크
            var g = new Grid { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(new XEllipse { Fill = Theme.B(Colors.White) });
            g.Children.Add(Theme.Icon(Icons.Check, 14, Colors.Black, stroke: true, strokeWidth: 2.6));
            _center.Children.Add(g);
        }
        var name = Theme.Text(Sources[i].Name, 15, FontWeights.SemiBold, Theme.OnSurface);
        name.VerticalAlignment = VerticalAlignment.Center;
        _center.Children.Add(name);
        var preferred = Settings.Current.LyricsSource == s.Source;
        var star = new IconButton(28) { Content = Theme.Icon(Icons.Star, 20, Colors.White, stroke: !preferred, strokeWidth: 1.6) };
        Tip.Set(star, preferred ? "기본 가사 출처" : "기본 가사 출처로 정하기");
        var src = s.Source;
        star.Click += () => LyricsState.Instance.SetPreferred(src);
        _center.Children.Add(star);
        _dots.Children.Clear();
        for (var k = 0; k < Sources.Length; k++)
            _dots.Children.Add(new XEllipse { Width = 5, Height = 5, Fill = Theme.B(k == i ? Colors.White : Color.FromArgb(90, 255, 255, 255)) });
    }
}
