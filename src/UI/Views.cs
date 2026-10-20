using Microsoft.UI.Input;
using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>카드 · 곡을 눌렀을 때 무엇을 틀지</summary>
public static class Play
{
    public static async void Card(Card c, Shelf? shelf = null)
    {
        try
        {
            var p = PlayerService.Instance;
            if (DetailView.Opens(c))
            {
                App.MainWin?.OpenBrowse(c.BrowseId!);
                return;
            }
            if (c.VideoId != null && c.PlaylistId == null)
            {
                var t = shelf?.Tracks?.FirstOrDefault(x => x.VideoId == c.VideoId)
                        ?? new Track(c.VideoId, c.Title, InnerTube.StripType(c.Subtitle).Split(" • ")[0], null, c.Thumb, 0);
                await p.PlaySong(t);
                return;
            }
            if (c.PlaylistId != null)
            {
                var q = await InnerTube.Instance.GetQueue(c.VideoId, c.PlaylistId);
                if (q.Tracks.Count > 0) await p.PlayList(q.Tracks, 0, q.Title ?? c.Title, q.RelatedBrowseId, q.AutomixPlaylistId, q.AutomixParams);
                return;
            }
            if (c.BrowseId != null)
            {
                var list = await InnerTube.Instance.BrowseTracks(c.BrowseId);
                if (list.Count > 0) await p.PlayList(list, 0, c.Title);
            }
        }
        catch (Exception e) { Log.Error("카드 재생 실패", e); }
    }
}

/// <summary>세로로 스크롤하는 화면의 공통 틀 — 스크롤바 숨김, 위 · 아래 끝 그라데이션 (pear 테마: 위 44 · 아래 56)</summary>
public class ScrollPage : Grid
{
    protected readonly ScrollViewer Scroller = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalScrollMode = ScrollMode.Disabled,
    };
    protected readonly StackPanel Body = new();
    private readonly XRect _fadeTop, _fadeBottom;
    private bool _topShown, _bottomShown = true;

    /// <summary>내용 왼쪽 여백 — pear 의 셸프 시작점(레일 72 + 106)</summary>
    public const double Left = 106;

    public ScrollPage()
    {
        Scroller.Content = Body;
        Children.Add(Scroller);
        _fadeTop = Fade(true);
        _fadeBottom = Fade(false);
        Children.Add(_fadeTop);
        Children.Add(_fadeBottom);
        Scroller.ViewChanged += (_, _) => UpdateFades();
        Scroller.SizeChanged += (_, _) => UpdateFades();
        Anim.V(_fadeTop).Opacity = 0;
    }

    private static XRect Fade(bool top)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, top ? 0 : 1), EndPoint = new Point(0, top ? 1 : 0) };
        brush.GradientStops.Add(new GradientStop { Color = Theme.Bg, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Theme.Bg.WithAlpha(0), Offset = 1 });
        return new XRect
        {
            Height = top ? 44 : Theme.BarHeight + Theme.FloatGap * 2 + 40,
            VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            Fill = brush,
            Opacity = top ? 1 : 0.85,
            IsHitTestVisible = false,
        };
    }

    private void UpdateFades()
    {
        var top = Scroller.VerticalOffset > 1;
        var bottom = Scroller.VerticalOffset < Scroller.ScrollableHeight - 1;
        if (top != _topShown) { _topShown = top; Anim.Opacity(_fadeTop, top ? 1 : 0, 300); }
        if (bottom != _bottomShown) { _bottomShown = bottom; Anim.Opacity(_fadeBottom, bottom ? 1 : 0, 300); }
    }

    protected static Border BottomSpace() => new() { Height = Theme.BarHeight + Theme.FloatGap * 2 + 24 };
}

/// <summary>셸프 화면 (홈 · 둘러보기 · 보관함) — pear 의 One UI 셸프 모양 그대로</summary>
public sealed class ShelvesView : ScrollPage
{
    private readonly string _browseId;
    private readonly bool _withChips;
    private bool _loaded;
    private string? _chipParams;
    /// <summary>보관함 칩(재생목록 · 노래 · 앨범 …)은 다른 화면(browseId)으로 간다</summary>
    private string? _chipBrowse;
    private readonly TextBlock _status = Theme.Text("불러오는 중…", 15, FontWeights.SemiBold, Theme.Secondary);

    private string? _more;
    private bool _loadingMore;
    private Border? _bottom;
    private int _gen;

    public ShelvesView(string browseId, bool withChips)
    {
        _browseId = browseId;
        _withChips = withChips;
        Body.Padding = new Thickness(0, 0, 0, 0);
        // 끝 가까이 내리면 다음 셸프들을 이어서 (YT Music 홈은 몇 개씩 나눠 준다)
        Scroller.ViewChanged += (_, _) =>
        {
            if (Scroller.VerticalOffset > Scroller.ScrollableHeight - 900) LoadMore();
        };
    }

    private async void LoadMore()
    {
        if (_more == null || _loadingMore || _bottom == null) return;
        _loadingMore = true;
        var gen = _gen;
        var token = _more;
        try
        {
            var data = await InnerTube.Instance.HomeMore(token);
            if (gen != _gen) return;
            _more = data.Continuation;
            var at = Body.Children.IndexOf(_bottom);
            var delay = 0;
            foreach (var s in data.Shelves)
            {
                var shelf = BuildShelf(s);
                Body.Children.Insert(at++, shelf);
                Anim.Appear(shelf, delay);
                delay = Math.Min(delay + 60, 300);
            }
        }
        catch (Exception e)
        {
            Log.Warn($"셸프 이어 받기 실패: {e.Message}");
            _more = null;
        }
        finally { _loadingMore = false; }
        // 화면이 아직 안 찼으면 한 번 더
        if (gen == _gen && Scroller.ScrollableHeight < 900) LoadMore();
    }

    public void Reload()
    {
        _loaded = false;
        LoadOnce();
    }

    private FrameworkElement LoginCard()
    {
        var card = new StackPanel { Spacing = 14, Padding = new Thickness(28) };
        var border = new Border { CornerRadius = new CornerRadius(Theme.RadiusCard), Background = Theme.Glass(0.4), Child = card, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 520, Margin = new Thickness(Left, 32, 0, 0) };
        card.Children.Add(Theme.Text("YouTube Music 로그인", 22, FontWeights.ExtraBold, Theme.OnSurface));
        card.Children.Add(Theme.Text("한국에서는 로그인해야 YouTube Music 을 쓸 수 있어요. 구글 계정으로 로그인하면 홈 추천 · 검색 · 재생이 열려요.", 15, FontWeights.Normal, Theme.Secondary, 3));
        var btn = new Border { CornerRadius = new CornerRadius(22), Background = Theme.B(Colors.White), Padding = new Thickness(24, 12, 24, 12), HorizontalAlignment = HorizontalAlignment.Left };
        btn.Child = Theme.Text("구글 계정으로 로그인", 15, FontWeights.Bold, Colors.Black);
        Anim.Pressable(btn);
        btn.Tapped += async (_, _) =>
        {
            var w = new LoginWindow();
            w.Activate();
            if (await w.Result) App.MainWin?.ReloadAll();
        };
        card.Children.Add(btn);
        return border;
    }

    public async void LoadOnce()
    {
        if (_loaded) return;
        _loaded = true;
        _gen++;
        _more = null;
        _bottom = null;
        Body.Children.Clear();
        if (!Auth.IsLoggedIn)
        {
            var lc = LoginCard();
            Body.Children.Add(lc);
            Anim.Appear(lc);
            _loaded = false;
            return;
        }
        _status.Text = "불러오는 중…";
        _status.Margin = new Thickness(Left, 32, 0, 0);
        Body.Children.Add(_status);
        try
        {
            List<Shelf> shelves;
            List<Chip> chips = new();
            List<NavButton> buttons = new();
            if (_withChips)
            {
                var home = await InnerTube.Instance.Home(_chipBrowse ?? _browseId, _chipParams);
                shelves = home.Shelves;
                chips = home.Chips;
                buttons = home.Buttons;
                _more = home.Continuation;
            }
            else shelves = await InnerTube.Instance.BrowseShelves(_browseId);
            Body.Children.Clear();
            if (chips.Count > 0) Body.Children.Add(BuildChips(chips));
            if (buttons.Count > 0) Body.Children.Add(BuildButtons(buttons));
            // 보관함 맨 위: 오프라인 저장한 곡
            if (_browseId == "FEmusic_library_landing" && _chipBrowse == null && Offline.Instance.Tracks is { Count: > 0 } saved)
                shelves.Insert(0, new Shelf("오프라인 저장", saved.Select(t => new Card(t.Title, t.Artist, t.Thumb, false, t.VideoId, null, null)).ToList(), saved.ToList()));
            if (shelves.Count == 0 && buttons.Count == 0)
            {
                _status.Text = "보여 줄 내용이 없어요.";
                Body.Children.Add(_status);
                _loaded = false;
                return;
            }
            var delay = 0;
            foreach (var s in shelves)
            {
                var shelf = BuildShelf(s);
                Body.Children.Add(shelf);
                Anim.Appear(shelf, delay);
                delay = Math.Min(delay + 60, 360);
            }
            Body.Children.Add(_bottom = BottomSpace());
            DispatcherQueue.TryEnqueue(LoadMore);
        }
        catch (Exception e)
        {
            Log.Error("셸프 불러오기 실패", e);
            _status.Text = "불러오지 못했어요. 인터넷 연결을 확인해 주세요.";
            _loaded = false;
        }
    }

    /// <summary>
    /// 큰 이동 버튼 — 둘러보기 맨 위 3개(최신 음악 · 차트 · 분위기 및 장르)는 한 줄에 같은 폭으로,
    /// 분위기 및 장르 화면의 많은 버튼은 왼쪽 색 띠가 있는 격자로
    /// </summary>
    internal static FrameworkElement BuildButtons(List<NavButton> buttons)
    {
        if (buttons.Count <= 4)
        {
            var g = new Grid { Margin = new Thickness(Left, 24, 40, 8), ColumnSpacing = 24 };
            for (var i = 0; i < buttons.Count; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var b = NavButtonView(buttons[i], big: true);
                Grid.SetColumn(b, i);
                g.Children.Add(b);
            }
            return g;
        }
        var wrap = new WrapPanel { Margin = new Thickness(Left, 24, 40, 8), HSpacing = 16, VSpacing = 16 };
        foreach (var b in buttons) wrap.Children.Add(NavButtonView(b, big: false));
        return wrap;
    }

    private static FrameworkElement NavButtonView(NavButton nb, bool big)
    {
        var g = new Grid { Height = big ? 56 : 48, Width = big ? double.NaN : 196, CornerRadius = new CornerRadius(big ? 12 : 8), Background = Theme.B(Theme.Chip) };
        if (nb.Stripe is { } c)
            g.Children.Add(new XRect { Width = 6, Fill = Theme.B(c), HorizontalAlignment = HorizontalAlignment.Left });
        var t = Theme.Text(nb.Text, big ? 22 : 15, FontWeights.Bold, Theme.OnSurface);
        t.VerticalAlignment = VerticalAlignment.Center;
        t.Margin = new Thickness(big ? 24 : 18, 0, 12, 0);
        g.Children.Add(t);
        g.PointerEntered += (_, _) => g.Background = Theme.B(Theme.ChipHover);
        g.PointerExited += (_, _) => g.Background = Theme.B(Theme.Chip);
        Anim.Pressable(g);
        g.Tapped += (_, _) => App.MainWin?.OpenBrowse(nb.BrowseId, nb.Params);
        return g;
    }

    /// <summary>홈 맨 위 분위기 칩 — 36px 알약, 고르면 흰 칩</summary>
    private FrameworkElement BuildChips(List<Chip> chips)
    {
        var sv = HorizontalRow(out var row);
        row.Spacing = 12;
        row.Padding = new Thickness(Left, 24, 40, 12);
        foreach (var c in chips)
        {
            var selected = c.Selected || (c.Params != null && c.Params == _chipParams)
                           || (_chipBrowse != null && c.BrowseId == _chipBrowse && c.Params == _chipParams);
            var chip = new Border
            {
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(16, 0, 16, 0),
                Background = Theme.B(selected ? Colors.White : Theme.Chip),
                Child = Theme.Text(c.Text, 14, FontWeights.SemiBold, selected ? Colors.Black : Theme.OnSurface),
            };
            ((TextBlock)chip.Child).VerticalAlignment = VerticalAlignment.Center;
            chip.PointerEntered += (_, _) => { if (!selected) chip.Background = Theme.B(Theme.ChipHover); };
            chip.PointerExited += (_, _) => { if (!selected) chip.Background = Theme.B(Theme.Chip); };
            Anim.Pressable(chip);
            chip.Tapped += (_, _) =>
            {
                _chipParams = selected ? null : c.Params;
                _chipBrowse = selected || c.BrowseId == _browseId ? null : c.BrowseId;
                Reload();
            };
            row.Children.Add(chip);
        }
        return sv;
    }

    internal static ScrollViewer HorizontalRow(out StackPanel row)
    {
        var sv = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            // 휠이 가로 목록에 먹히지 않게 사용자 스크롤은 끄고, 넘기기는 ChangeView 로만
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        row = new StackPanel { Orientation = Orientation.Horizontal };
        sv.Content = row;
        // 마우스 휠은 가로로 넘기지 않고 화면을 위아래로 (가로는 ‹ › 버튼으로)
        return sv;
    }

    internal static FrameworkElement BuildShelf(Shelf s)
    {
        var box = new StackPanel { Padding = new Thickness(0, 32, 0, 0) };
        // 머리: (프로필 사진) + 작은 글씨 + 제목 22px ExtraBold
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(Left, 0, 0, 16), Spacing = 16 };
        if (s.Avatar != null)
        {
            var av = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(28), Background = Theme.B(Theme.Chip), Child = new Image { Source = Theme.Image(s.Avatar, 56), Stretch = Stretch.UniformToFill } };
            head.Children.Add(av);
        }
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        if (s.Strapline != null) titles.Children.Add(Theme.Text(s.Strapline, 14, FontWeights.Normal, Theme.Secondary));
        titles.Children.Add(Theme.Text(s.Title, 22, FontWeights.ExtraBold, Theme.OnSurface));
        head.Children.Add(titles);
        if (s.Grid)
        {
            // 보관함: 줄바꿈 격자 (pear 의 보관함 화면)
            if (s.Title.Length > 0) box.Children.Add(head);
            var wrap = new WrapPanel { Margin = new Thickness(Left, 0, 40, 8), HSpacing = 16, VSpacing = 24 };
            foreach (var c in s.Items)
            {
                var card = new CardView(c, 166, square: true, fixedHeight: true);
                card.Click += cc => Play.Card(cc, s);
                wrap.Children.Add(card);
            }
            box.Children.Add(wrap);
            return box;
        }
        var headRow = new Grid();
        headRow.Children.Add(head);
        box.Children.Add(headRow);

        var sv = HorizontalRow(out var row);
        var arrows = (StackPanel)ShelfArrows(sv);
        if (s.MoreBrowseId is { } moreId)
        {
            // '더보기' — 테두리 알약 (pear 셸프 머리)
            var more = new Border
            {
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(16, 0, 16, 0),
                BorderBrush = Theme.B(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Colors.Transparent),
                Child = Theme.Text("더보기", 14, FontWeights.SemiBold, Theme.OnSurface),
            };
            ((TextBlock)more.Child).VerticalAlignment = VerticalAlignment.Center;
            more.PointerEntered += (_, _) => more.Background = Theme.B(Theme.ChipHover);
            more.PointerExited += (_, _) => more.Background = new SolidColorBrush(Colors.Transparent);
            Anim.Pressable(more);
            more.Tapped += (_, _) => App.MainWin?.OpenBrowse(moreId, s.MoreParams);
            arrows.Children.Insert(0, more);
        }
        headRow.Children.Add(arrows);
        row.Padding = new Thickness(Left, 0, 40, 8);
        var twoRows = s.Strapline != null || s.Title is "다시 듣기" or "Listen again";
        if (s.Tracks != null && s.Tracks.Count >= 4 && s.Tracks.Count == s.Items.Count)
        {
            // 빠른 선곡: 4줄 × 여러 칸, 줄 436×48 · 48px 아트
            row.Spacing = 24;
            for (var i = 0; i < s.Tracks.Count; i += 4)
            {
                var col = new StackPanel { Width = 436, Spacing = 16 };
                foreach (var t in s.Tracks.Skip(i).Take(4)) col.Children.Add(QuickRow(t));
                row.Children.Add(col);
            }
        }
        else if (twoRows)
        {
            // 다시 듣기: 164px 정사각 카드 2줄
            row.Spacing = 24;
            for (var i = 0; i < s.Items.Count; i += 2)
            {
                var col = new StackPanel { Spacing = 16 };
                foreach (var c in s.Items.Skip(i).Take(2))
                {
                    var card = new CardView(c, 164, square: true, fixedHeight: true);
                    card.Click += cc => Play.Card(cc, s);
                    col.Children.Add(card);
                }
                row.Children.Add(col);
            }
        }
        else
        {
            row.Spacing = 24;
            foreach (var c in s.Items)
            {
                var card = new CardView(c, 196);
                card.Click += cc => Play.Card(cc, s);
                row.Children.Add(card);
            }
        }
        box.Children.Add(sv);
        return box;
    }

    /// <summary>셸프 머리 오른쪽 ‹ › — 테두리만 있는 36px 동그라미, 끝에 닿으면 흐려진다 (pear 셸프 버튼)</summary>
    internal static FrameworkElement ShelfArrows(ScrollViewer sv)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 40, 16) };
        IconButton Arrow(string icon, int dir)
        {
            var b = new IconButton(36) { Content = Theme.Icon(icon, 20, Colors.White, stroke: true, strokeWidth: 2) };
            b.Children.Insert(0, new XEllipse { Stroke = Theme.B(Color.FromArgb(70, 255, 255, 255)), StrokeThickness = 1 });
            b.Click += () => sv.ChangeView(Math.Clamp(sv.HorizontalOffset + dir * sv.ViewportWidth * 0.8, 0, sv.ScrollableWidth), null, null);
            return b;
        }
        var prev = Arrow(Icons.ChevronLeft, -1);
        var next = Arrow(Icons.ChevronRight, 1);
        panel.Children.Add(prev);
        panel.Children.Add(next);
        void Update()
        {
            var canPrev = sv.HorizontalOffset > 1;
            var canNext = sv.HorizontalOffset < sv.ScrollableWidth - 1;
            prev.Visibility = next.Visibility = sv.ScrollableWidth > 1 ? Visibility.Visible : Visibility.Collapsed;
            prev.Opacity = canPrev ? 1 : 0.35;
            next.Opacity = canNext ? 1 : 0.35;
            prev.IsHitTestVisible = canPrev;
            next.IsHitTestVisible = canNext;
        }
        sv.ViewChanged += (_, _) => Update();
        sv.SizeChanged += (_, _) => Update();
        if (sv.Content is FrameworkElement content) content.SizeChanged += (_, _) => Update();
        return panel;
    }

    /// <summary>빠른 선곡 한 줄 (436×48) — 48px 둥근 아트 · 곡명 · 가수</summary>
    internal static FrameworkElement QuickRow(Track t, double width = 436)
    {
        var g = new Grid { Height = 48, Width = width, Background = new SolidColorBrush(Colors.Transparent) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var art = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(Theme.RadiusThumb), Background = Theme.B(Theme.Chip), HorizontalAlignment = HorizontalAlignment.Left };
        var img = new Image { Width = 48, Height = 48, Stretch = Stretch.UniformToFill, Source = Theme.Image(t.Thumb, 86) };
        Anim.V(img).Opacity = 0;
        img.ImageOpened += (_, _) => Anim.Opacity(img, 1, 300);
        art.Child = img;
        g.Children.Add(art);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Margin = new Thickness(8, 0, 0, 0) };
        text.Children.Add(Theme.Text(t.Title, 14, FontWeights.Bold, Theme.OnSurface));
        text.Children.Add(Theme.Text(t.Artist, 14, FontWeights.Normal, Theme.Secondary));
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        Anim.Pressable(art);
        g.Tapped += (_, _) => _ = PlayerService.Instance.PlaySong(t);
        g.RightTapped += (_, e) => { e.Handled = true; TrackMenu.Show(g, t, null, e.GetPosition(null)); };
        return g;
    }
}

/// <summary>
/// 검색 결과 (pear 의 YT Music 검색과 같은 배치) — 가운데 860px 칸에
/// 필터 칩(노래 · 동영상 · 아티스트 · 앨범 …) / 고친 검색어 / 맨 위 결과 카드(재생 · 저장) / 갈래별 목록
/// </summary>
public sealed class SearchView : ScrollPage
{
    private readonly StackPanel _col = new() { MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 0 };
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    private readonly StackPanel _results = new();
    private CancellationTokenSource? _cts;
    private string _query = "";
    private string? _filter;

    public SearchView()
    {
        Body.Padding = new Thickness(40, 24, 40, 0);
        var chipScroll = ShelvesView.HorizontalRow(out var _);
        chipScroll.Content = _chips;
        chipScroll.Margin = new Thickness(0, 0, 0, 16);
        _col.Children.Add(chipScroll);
        _col.Children.Add(_results);
        Body.Children.Add(_col);
        Body.Children.Add(BottomSpace());
        SizeChanged += (_, _) => _col.Width = Math.Min(860, Math.Max(0, ActualWidth - 80));
    }

    public void Search(string q)
    {
        _query = q;
        _filter = null;
        Run();
    }

    private async void Run()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _results.Children.Clear();
        var status = Theme.Text($"'{_query}' 검색 중…", 15, FontWeights.SemiBold, Theme.Secondary);
        status.Margin = new Thickness(8, 8, 0, 0);
        _results.Children.Add(status);
        try
        {
            var data = await InnerTube.Instance.Search(_query, _filter, cts.Token);
            if (cts.IsCancellationRequested) return;
            _results.Children.Clear();
            BuildChips(data.Chips);
            if (data.Corrected != null)
            {
                var c = Theme.Text($"수정된 검색어에 대한 결과: {data.Corrected}", 14, FontWeights.SemiBold, Theme.Secondary);
                c.Margin = new Thickness(8, 0, 0, 16);
                _results.Children.Add(c);
            }
            if (data.Top == null && data.Sections.Count == 0)
            {
                status.Text = $"'{_query}' 검색 결과가 없어요";
                _results.Children.Add(status);
                return;
            }
            var delay = 0;
            void Add(FrameworkElement el)
            {
                _results.Children.Add(el);
                if (delay < 400) Anim.Appear(el, delay, 8);
                delay += 30;
            }
            if (data.Top != null) Add(TopCard(data.Top));
            foreach (var it in data.TopMore) Add(Row(it, data.TopMore));
            foreach (var sec in data.Sections)
            {
                if (sec.Title.Length > 0 && (_filter == null || data.Sections.Count > 1))
                {
                    var h = Theme.Text(sec.Title, 22, FontWeights.ExtraBold, Theme.OnSurface);
                    h.Margin = new Thickness(8, 32, 0, 12);
                    Add(h);
                }
                foreach (var it in sec.Items) Add(Row(it, sec.Items));
            }
            Scroller.ChangeView(null, 0, null, true);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Log.Error("검색 실패", e);
            status.Text = "검색하지 못했어요. 인터넷 연결을 확인해 주세요.";
        }
    }

    /// <summary>필터 칩 — 고른 칩은 흰 바탕, 다시 누르면 필터 해제</summary>
    private void BuildChips(List<Chip> chips)
    {
        _chips.Children.Clear();
        foreach (var c in chips)
        {
            var selected = c.Selected || (_filter != null && c.Params == _filter);
            var chip = new Border
            {
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(16, 0, 16, 0),
                Background = Theme.B(selected ? Colors.White : Theme.Chip),
                Child = Theme.Text(c.Text, 14, FontWeights.SemiBold, selected ? Colors.Black : Theme.OnSurface),
            };
            ((TextBlock)chip.Child).VerticalAlignment = VerticalAlignment.Center;
            chip.PointerEntered += (_, _) => { if (!selected) chip.Background = Theme.B(Theme.Selected); };
            chip.PointerExited += (_, _) => { if (!selected) chip.Background = Theme.B(Theme.Chip); };
            Anim.Pressable(chip);
            chip.Tapped += (_, _) =>
            {
                _filter = selected ? null : c.Params;
                Run();
            };
            _chips.Children.Add(chip);
        }
    }

    private static void Open(SearchItem it, List<SearchItem> siblings)
    {
        if (it.Track is { } t)
        {
            _ = PlayerService.Instance.PlaySong(t, fromSearch: true);
            return;
        }
        if (it.BrowseId != null) App.MainWin?.OpenBrowse(it.BrowseId);
    }

    /// <summary>맨 위 결과 — 유리 카드, 100px 아트, 큰 제목, 재생(흰 알약) · 저장(테두리 알약)</summary>
    private FrameworkElement TopCard(SearchItem it)
    {
        var g = new Grid { Padding = new Thickness(20), ColumnSpacing = 16 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var art = new Border { Width = 100, Height = 100, CornerRadius = new CornerRadius(it.Round ? 50 : 6), Background = Theme.B(Theme.Chip) };
        var img = new Image { Width = 100, Height = 100, Stretch = Stretch.UniformToFill, Source = Theme.Image(it.Thumb, 178) };
        Anim.V(img).Opacity = 0;
        img.ImageOpened += (_, _) => Anim.Opacity(img, 1, 300);
        art.Child = img;
        g.Children.Add(art);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        text.Children.Add(Theme.Text(it.Title, 24, FontWeights.ExtraBold, Theme.OnSurface));
        text.Children.Add(Theme.Text(it.Subtitle, 15, FontWeights.Normal, Theme.Secondary));
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(Pill(it.Track != null ? "재생" : "열기", true, Icons.Play, () => Open(it, new())));
        var save = Pill("저장", false, null, () => { });
        ToolTipService.SetToolTip(save, "재생목록에 저장은 준비 중이에요");
        buttons.Children.Add(save);
        Grid.SetColumn(buttons, 2);
        g.Children.Add(buttons);
        var card = new Border
        {
            CornerRadius = new CornerRadius(Theme.RadiusCard),
            Background = Theme.B(Color.FromArgb(26, 255, 255, 255)),
            Child = g,
            Margin = new Thickness(0, 0, 0, 24),
        };
        card.Tapped += (_, _) => Open(it, new());
        return card;
    }

    private static FrameworkElement Pill(string text, bool primary, string? icon, Action click)
    {
        var fg = primary ? Colors.Black : Colors.White;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (icon != null) row.Children.Add(Theme.Icon(icon, 18, fg));
        else row.Children.Add(Theme.Icon(20, fg, Icons.PlaylistAdd));
        var label = Theme.Text(text, 14, FontWeights.SemiBold, fg);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        var b = new Border
        {
            Width = 132,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = primary ? Theme.B(Colors.White) : new SolidColorBrush(Colors.Transparent),
            BorderBrush = primary ? null : Theme.B(Color.FromArgb(70, 255, 255, 255)),
            BorderThickness = new Thickness(primary ? 0 : 1),
            Child = row,
        };
        Anim.Pressable(b);
        b.Tapped += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    /// <summary>결과 한 줄 — 56px 아트(아티스트는 동그라미), 제목 Bold, '노래 • MC몽 • 2175만회 재생'</summary>
    private static FrameworkElement Row(SearchItem it, List<SearchItem> siblings)
    {
        var g = new Grid { Height = 80, Background = new SolidColorBrush(Colors.Transparent) };
        var hover = new Border { CornerRadius = new CornerRadius(14), Background = Theme.B(Theme.ChipHover) };
        Anim.V(hover).Opacity = 0;
        g.Children.Add(hover);
        var inner = new Grid { Margin = new Thickness(8, 0, 8, 0), ColumnSpacing = 16 };
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var art = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(it.Round ? 28 : 6), Background = Theme.B(Theme.Chip), VerticalAlignment = VerticalAlignment.Center };
        var img = new Image { Width = 56, Height = 56, Stretch = Stretch.UniformToFill, Source = Theme.Image(it.Thumb, 100) };
        Anim.V(img).Opacity = 0;
        img.ImageOpened += (_, _) => Anim.Opacity(img, 1, 300);
        art.Child = img;
        inner.Children.Add(art);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 3 };
        text.Children.Add(Theme.Text(it.Title, 15, FontWeights.Bold, Theme.OnSurface));
        text.Children.Add(Theme.Text(it.Subtitle, 14, FontWeights.Normal, Theme.Secondary));
        Grid.SetColumn(text, 1);
        inner.Children.Add(text);
        g.Children.Add(inner);
        g.PointerEntered += (_, _) => Anim.Opacity(hover, 1, 160);
        g.PointerExited += (_, _) => Anim.Opacity(hover, 0, 240);
        Anim.Pressable(art);
        g.Tapped += (_, _) => Open(it, siblings);
        g.RightTapped += (_, e) =>
        {
            e.Handled = true;
            if (it.Track is { } t) TrackMenu.Show(g, t, null, e.GetPosition(null));
            else if (it.BrowseId != null) TrackMenu.ShowCard(g, new Card(it.Title, it.Subtitle, it.Thumb, false, null, null, it.BrowseId, it.PageType), e.GetPosition(null));
        };
        return g;
    }
}
