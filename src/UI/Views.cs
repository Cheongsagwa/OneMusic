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
                        ?? new Track(c.VideoId, c.Title, c.Subtitle.Split(" • ")[0], null, c.Thumb, 0);
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
    private readonly TextBlock _status = Theme.Text("불러오는 중…", 15, FontWeights.SemiBold, Theme.Secondary);

    public ShelvesView(string browseId, bool withChips)
    {
        _browseId = browseId;
        _withChips = withChips;
        Body.Padding = new Thickness(0, 0, 0, 0);
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
        Anim.Pressable(btn, 0.95f);
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
            if (_withChips)
            {
                var home = await InnerTube.Instance.Home(null, _chipParams);
                shelves = home.Shelves;
                chips = home.Chips;
            }
            else shelves = await InnerTube.Instance.BrowseShelves(_browseId);
            Body.Children.Clear();
            if (chips.Count > 0) Body.Children.Add(BuildChips(chips));
            if (shelves.Count == 0)
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
            Body.Children.Add(BottomSpace());
        }
        catch (Exception e)
        {
            Log.Error("셸프 불러오기 실패", e);
            _status.Text = "불러오지 못했어요. 인터넷 연결을 확인해 주세요.";
            _loaded = false;
        }
    }

    /// <summary>홈 맨 위 분위기 칩 — 36px 알약, 고르면 흰 칩</summary>
    private FrameworkElement BuildChips(List<Chip> chips)
    {
        var sv = HorizontalRow(out var row);
        row.Spacing = 12;
        row.Padding = new Thickness(Left, 24, 40, 12);
        foreach (var c in chips)
        {
            var selected = c.Selected || (c.Params != null && c.Params == _chipParams);
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
            Anim.Pressable(chip, 0.94f);
            chip.Tapped += (_, _) =>
            {
                _chipParams = selected ? null : c.Params;
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
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        row = new StackPanel { Orientation = Orientation.Horizontal };
        sv.Content = row;
        // 셸프 위에서는 마우스 휠로도 옆으로 넘긴다 (pear 테마의 칩 줄처럼)
        sv.PointerWheelChanged += (_, e) =>
        {
            var props = e.GetCurrentPoint(sv).Properties;
            if (props.IsHorizontalMouseWheel || sv.ScrollableWidth <= 0) return;
            if (!e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift)) return;
            sv.ChangeView(sv.HorizontalOffset - props.MouseWheelDelta * 1.2, null, null);
            e.Handled = true;
        };
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
        box.Children.Add(head);

        var sv = HorizontalRow(out var row);
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
                var col = new StackPanel { Spacing = 20 };
                foreach (var c in s.Items.Skip(i).Take(2))
                {
                    var card = new CardView(c, 164);
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

    /// <summary>빠른 선곡 한 줄 (436×48) — 48px 둥근 아트 · 곡명 · 가수</summary>
    private static FrameworkElement QuickRow(Track t)
    {
        var g = new Grid { Height = 48, Background = new SolidColorBrush(Colors.Transparent) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var art = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(Theme.RadiusThumb), Background = Theme.B(Theme.Chip), HorizontalAlignment = HorizontalAlignment.Left };
        var img = new Image { Stretch = Stretch.UniformToFill, Source = Theme.Image(t.Thumb, 48) };
        Anim.V(img).Opacity = 0;
        img.ImageOpened += (_, _) => Anim.Opacity(img, 1, 300);
        art.Child = img;
        g.Children.Add(art);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Margin = new Thickness(8, 0, 0, 0) };
        text.Children.Add(Theme.Text(t.Title, 14, FontWeights.Bold, Theme.OnSurface));
        text.Children.Add(Theme.Text(t.Artist, 14, FontWeights.Normal, Theme.Secondary));
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        Anim.Pressable(art, 0.94f);
        g.Tapped += (_, _) => _ = PlayerService.Instance.PlaySong(t);
        return g;
    }
}

/// <summary>검색 결과 — 노래 목록 (유리 카드 안)</summary>
public sealed class SearchView : ScrollPage
{
    private readonly TextBlock _header = Theme.Text("", 22, FontWeights.ExtraBold, Theme.OnSurface);
    private readonly StackPanel _list = new();
    private CancellationTokenSource? _cts;

    public SearchView()
    {
        Body.Padding = new Thickness(Left, 32, 40, 0);
        Body.Spacing = 16;
        Body.Children.Add(_header);
        var card = new Border { CornerRadius = new CornerRadius(Theme.RadiusCard), Background = Theme.B(Color.FromArgb(14, 255, 255, 255)), Padding = new Thickness(8), Child = _list, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
        Body.Children.Add(card);
        Body.Children.Add(BottomSpace());
    }

    public async void Search(string q)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _header.Text = $"'{q}' 검색 중…";
        _list.Children.Clear();
        try
        {
            var results = await InnerTube.Instance.SearchSongs(q, cts.Token);
            if (cts.IsCancellationRequested) return;
            _header.Text = results.Count > 0 ? "노래" : $"'{q}' 검색 결과가 없어요";
            for (var i = 0; i < results.Count; i++)
            {
                var r = new TrackRow(results[i], divider: i < results.Count - 1);
                r.Click += row => _ = PlayerService.Instance.PlaySong(row.Track);
                _list.Children.Add(r);
                if (i < 12) Anim.Appear(r, i * 25, 8);
            }
            Scroller.ChangeView(null, 0, null, true);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Log.Error("검색 실패", e);
            _header.Text = "검색하지 못했어요. 인터넷 연결을 확인해 주세요.";
        }
    }
}
