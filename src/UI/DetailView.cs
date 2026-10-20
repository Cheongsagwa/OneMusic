using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// 앨범 · 플레이리스트 · 아티스트 상세 화면.
/// 뒤로 가기 / 머리(아트 · 제목 · 가수 · 재생 · 셔플) / 곡 목록(유리 카드) / 아래 셸프(다른 앨범 · 싱글 · 관련 아티스트 …)
/// </summary>
public sealed class DetailView : ScrollPage
{
    private readonly string _browseId;
    private readonly List<TrackRow> _rows = new();
    private readonly TextBlock _status = Theme.Text("불러오는 중…", 15, FontWeights.SemiBold, Theme.Secondary);
    private PageData? _page;
    public event Action? BackRequested;

    /// <summary>이 카드를 누르면 바로 재생하는 대신 상세 화면을 여는지</summary>
    public static bool Opens(Card c) =>
        c.BrowseId != null && c.VideoId == null && (c.PageType switch
        {
            "MUSIC_PAGE_TYPE_ALBUM" or "MUSIC_PAGE_TYPE_ARTIST" or "MUSIC_PAGE_TYPE_PLAYLIST" => true,
            null => c.BrowseId.StartsWith("MPRE") || c.BrowseId.StartsWith("UC") || c.BrowseId.StartsWith("VL"),
            _ => false,
        });

    public DetailView(string browseId)
    {
        _browseId = browseId;
        var back = new IconButton(40)
        {
            Content = Theme.Icon(Icons.ChevronLeft, 24, Colors.White, stroke: true, strokeWidth: 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(Left - 12, 8, 0, 0),
        };
        ToolTipService.SetToolTip(back, "뒤로");
        back.Click += () => BackRequested?.Invoke();
        Body.Children.Add(back);
        _status.Margin = new Thickness(Left, 24, 0, 0);
        Body.Children.Add(_status);

        var p = PlayerService.Instance;
        Loaded += (_, _) => { p.TrackChanged += MarkCurrent; MarkCurrent(); };
        Unloaded += (_, _) => p.TrackChanged -= MarkCurrent;
    }

    public async void Load()
    {
        try
        {
            var page = await InnerTube.Instance.BrowsePage(_browseId);
            _page = page;
            Body.Children.Remove(_status);
            if (page.Title.Length == 0 && page.Tracks.Count == 0 && page.Shelves.Count == 0)
            {
                _status.Text = "보여 줄 내용이 없어요.";
                Body.Children.Add(_status);
                return;
            }
            var head = BuildHeader(page);
            Body.Children.Add(head);
            Anim.Appear(head);
            if (page.Tracks.Count > 0)
            {
                var list = BuildTracks(page);
                Body.Children.Add(list);
                Anim.Appear(list, 80);
            }
            var delay = 140;
            foreach (var s in page.Shelves)
            {
                var shelf = ShelvesView.BuildShelf(s);
                Body.Children.Add(shelf);
                Anim.Appear(shelf, delay);
                delay = Math.Min(delay + 60, 400);
            }
            Body.Children.Add(BottomSpace());
            MarkCurrent();
        }
        catch (Exception e)
        {
            Log.Error("상세 화면 불러오기 실패", e);
            _status.Text = "불러오지 못했어요. 인터넷 연결을 확인해 주세요.";
        }
    }

    /// <summary>머리 — 왼쪽 큰 아트(아티스트는 동그라미), 오른쪽 종류 · 제목 · 가수 · 곡 수 · 설명 · 버튼</summary>
    private FrameworkElement BuildHeader(PageData page)
    {
        var artist = page.Kind == PageKind.Artist;
        var g = new Grid { Margin = new Thickness(Left, 16, 40, 8), ColumnSpacing = 32 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var size = artist ? 220 : 240;
        var art = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(artist ? size / 2.0 : 22),
            Background = Theme.B(Theme.Chip),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var img = new Image { Stretch = Stretch.UniformToFill, Source = Theme.Image(page.Thumb, size) };
        Anim.V(img).Opacity = 0;
        img.ImageOpened += (_, _) => Anim.Opacity(img, 1, 360);
        art.Child = img;
        g.Children.Add(art);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 6, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        // 앨범 · 플레이리스트는 YT Music 부제목('앨범 • 2023', 'EP • 2024') 그대로, 아티스트는 구독자 수 앞에 '아티스트'
        var sub = artist ? (page.Subtitle == null ? "아티스트" : $"아티스트 • {page.Subtitle}")
            : page.Subtitle ?? (page.Kind == PageKind.Album ? "앨범" : "플레이리스트");
        info.Children.Add(Theme.Text(sub, 15, FontWeights.SemiBold, Theme.Secondary));
        var title = Theme.Text(page.Title, artist ? 48 : 40, FontWeights.ExtraBold, Theme.OnSurface, 2);
        title.LineHeight = artist ? 56 : 48;
        info.Children.Add(title);

        if (page.Strapline != null)
        {
            var strap = Theme.Text(page.Strapline, 18, FontWeights.Bold, Theme.OnSurface);
            if (page.StraplineId != null)
            {
                var link = new Border { Background = new SolidColorBrush(Colors.Transparent), HorizontalAlignment = HorizontalAlignment.Left, Child = strap };
                link.PointerEntered += (_, _) => strap.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
                link.PointerExited += (_, _) => strap.TextDecorations = Windows.UI.Text.TextDecorations.None;
                link.Tapped += (_, _) => App.MainWin?.OpenBrowse(page.StraplineId);
                info.Children.Add(link);
            }
            else info.Children.Add(strap);
        }
        if (page.SecondSubtitle != null) info.Children.Add(Theme.Text(page.SecondSubtitle, 14, FontWeights.Normal, Theme.Secondary));
        if (page.Description != null)
        {
            var d = Theme.Text(page.Description, 14, FontWeights.Normal, Theme.Secondary, 3);
            d.Margin = new Thickness(0, 4, 0, 0);
            info.Children.Add(d);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 14, 0, 0) };
        var canPlay = page.Tracks.Count > 0 || page.PlayPlaylistId != null;
        if (canPlay)
        {
            buttons.Children.Add(Pill("재생", true, () => PlayAll(false), c => Theme.Icon(Icons.Play, 20, c)));
            if (page.Tracks.Count > 1)
                buttons.Children.Add(Pill("셔플", false, () => PlayAll(true), c => Theme.Icon(Icons.Shuffle, 20, c, stroke: true)));
        }
        if (page.RadioPlaylistId is { } radio)
            buttons.Children.Add(Pill("라디오", false, () => PlayPlaylist(radio, $"{page.Title} 라디오"), c => Theme.Icon(Icons.Radio, 20, c, stroke: true)));
        if (buttons.Children.Count > 0) info.Children.Add(buttons);

        Grid.SetColumn(info, 1);
        g.Children.Add(info);
        return g;
    }

    /// <summary>알약 버튼 44px — 주 버튼은 흰 바탕 검은 글씨, 나머지는 반투명 칩</summary>
    private static FrameworkElement Pill(string text, bool primary, Action click, Func<Color, FrameworkElement> icon)
    {
        var fg = primary ? Colors.Black : Colors.White;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(icon(fg));
        var label = Theme.Text(text, 15, FontWeights.Bold, fg);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        var idle = primary ? Colors.White : Theme.Chip;
        var hover = primary ? Color.FromArgb(255, 230, 230, 232) : Theme.ChipHover;
        var b = new Border
        {
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(20, 0, 24, 0),
            Background = Theme.B(idle),
            Child = row,
        };
        b.PointerEntered += (_, _) => b.Background = Theme.B(hover);
        b.PointerExited += (_, _) => b.Background = Theme.B(idle);
        Anim.Pressable(b, 0.95f);
        b.Tapped += (_, _) => click();
        return b;
    }

    /// <summary>곡 목록 — 제목(아티스트 페이지의 '노래' · 모두 보기) + 유리 카드 안의 줄들</summary>
    private FrameworkElement BuildTracks(PageData page)
    {
        var box = new StackPanel { Margin = new Thickness(Left, 24, 40, 0), Spacing = 12, MaxWidth = 1100 + Left, HorizontalAlignment = HorizontalAlignment.Left };
        if (page.Kind == PageKind.Artist)
        {
            var head = new Grid();
            head.Children.Add(Theme.Text(page.TracksTitle ?? "노래", 22, FontWeights.ExtraBold, Theme.OnSurface));
            if (page.TracksMoreId is { } more)
            {
                var all = new Border
                {
                    Height = 32,
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(14, 0, 14, 0),
                    Background = Theme.B(Theme.Chip),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Child = Theme.Text("모두 보기", 14, FontWeights.SemiBold, Theme.OnSurface),
                };
                ((TextBlock)all.Child).VerticalAlignment = VerticalAlignment.Center;
                all.PointerEntered += (_, _) => all.Background = Theme.B(Theme.ChipHover);
                all.PointerExited += (_, _) => all.Background = Theme.B(Theme.Chip);
                Anim.Pressable(all, 0.94f);
                all.Tapped += (_, _) => App.MainWin?.OpenBrowse(more);
                head.Children.Add(all);
            }
            box.Children.Add(head);
        }
        var list = new StackPanel();
        for (var i = 0; i < page.Tracks.Count; i++)
        {
            var idx = i;
            var r = new TrackRow(page.Tracks[i], divider: i < page.Tracks.Count - 1);
            r.Click += _ => _ = PlayerService.Instance.PlayList(page.Tracks, idx, page.Title);
            _rows.Add(r);
            list.Children.Add(r);
        }
        box.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(Theme.RadiusCard),
            Background = Theme.B(Color.FromArgb(14, 255, 255, 255)),
            Padding = new Thickness(8),
            Child = list,
        });
        return box;
    }

    private void PlayAll(bool shuffle)
    {
        var page = _page;
        if (page == null) return;
        // 아티스트 '재생' 은 인기곡 몇 개가 아니라 YT Music 이 주는 그 아티스트 곡 목록 전체
        if (!shuffle && page.Kind == PageKind.Artist && page.PlayPlaylistId != null)
        {
            PlayPlaylist(page.PlayPlaylistId, page.Title);
            return;
        }
        if (page.Tracks.Count == 0) return;
        var list = shuffle ? page.Tracks.OrderBy(_ => Random.Shared.Next()).ToList() : page.Tracks;
        _ = PlayerService.Instance.PlayList(list, 0, page.Title);
    }

    private static async void PlayPlaylist(string playlistId, string title)
    {
        try
        {
            var q = await InnerTube.Instance.GetQueue(null, playlistId);
            if (q.Tracks.Count > 0) await PlayerService.Instance.PlayList(q.Tracks, 0, q.Title ?? title, q.RelatedBrowseId);
        }
        catch (Exception e) { Log.Error("목록 재생 실패", e); }
    }

    private void MarkCurrent()
    {
        var id = PlayerService.Instance.Current?.VideoId;
        foreach (var r in _rows) r.SetCurrent(r.Track.VideoId == id);
    }
}
