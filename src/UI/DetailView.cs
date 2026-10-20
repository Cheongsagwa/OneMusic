using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// 앨범 · 플레이리스트 · 아티스트 상세 화면.
/// 뒤로 가기 / 머리(아트 · 제목 · 가수 · 재생 · 셔플) / 곡 목록(유리 카드) / 아래 셸프(다른 앨범 · 싱글 · 관련 아티스트 …)
/// </summary>
public sealed class DetailView : ScrollPage
{
    private readonly string _browseId;
    public string BrowseId => _browseId;
    public string? Params { get; }
    /// <summary>앨범 · 플레이리스트 · 아티스트가 아닌 화면 (최신 음악 · 차트 · 분위기 및 장르 · 더보기) — 셸프 · 버튼만</summary>
    private bool Generic => Params != null || _browseId.StartsWith("FEmusic_");
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

    public DetailView(string browseId, string? @params = null)
    {
        _browseId = browseId;
        Params = @params;
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
        if (Generic)
        {
            LoadGeneric();
            return;
        }
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
            if (page.Kind != PageKind.Artist)
            {
                // 앨범 · 플레이리스트: pear(YT Music) 처럼 왼쪽 가운데 정렬 머리 + 오른쪽 곡 목록 카드
                var two = BuildTwoColumn(page);
                Body.Children.Add(two);
                Anim.Appear(two);
            }
            else
            {
                var head = BuildHeader(page);
                Body.Children.Add(head);
                Anim.Appear(head);
            }
            if (page.Kind == PageKind.Artist && page.Tracks.Count > 0)
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

    private async void LoadGeneric()
    {
        try
        {
            var data = await InnerTube.Instance.Home(_browseId, Params);
            Body.Children.Remove(_status);
            if (data.Title != null)
            {
                var t = Theme.Text(data.Title, 32, FontWeights.ExtraBold, Theme.OnSurface);
                t.Margin = new Thickness(Left, 8, 40, 0);
                Body.Children.Add(t);
                Anim.Appear(t);
            }
            if (data.Buttons.Count > 0)
            {
                var b = ShelvesView.BuildButtons(data.Buttons);
                Body.Children.Add(b);
                Anim.Appear(b, 60);
            }
            var delay = 100;
            foreach (var s in data.Shelves)
            {
                var shelf = ShelvesView.BuildShelf(s);
                Body.Children.Add(shelf);
                Anim.Appear(shelf, delay);
                delay = Math.Min(delay + 60, 400);
            }
            if (data.Shelves.Count == 0 && data.Buttons.Count == 0)
            {
                _status.Text = "보여 줄 내용이 없어요.";
                Body.Children.Add(_status);
            }
            Body.Children.Add(BottomSpace());
        }
        catch (Exception e)
        {
            Log.Error("화면 불러오기 실패", e);
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

    /// <summary>
    /// 앨범 · 플레이리스트 화면 — 왼쪽 칸(아트 · 제목 32 · 만든 사람 · 정보 · 셔플 / ▶ / 링크) 가운데 정렬,
    /// 오른쪽 칸은 둥근 카드 안의 곡 목록
    /// </summary>
    private FrameworkElement BuildTwoColumn(PageData page)
    {
        var g = new Grid { Margin = new Thickness(Left, 8, 40, 0), ColumnSpacing = 48 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 760 });

        var left = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 0) };
        // 동영상 썸네일(ytimg)로 된 플레이리스트 표지는 16:9, 나머지는 정사각
        var wide = page.Thumb?.Contains("ytimg.com") == true;
        var art = new Border { Width = 264, Height = wide ? 148 : 264, CornerRadius = new CornerRadius(Theme.RadiusArt), Background = Theme.B(Theme.Chip), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
        var img = new Image { Stretch = Stretch.UniformToFill, Source = Theme.Image(page.Thumb, 264) };
        Anim.V(img).Opacity = 0;
        img.ImageOpened += (_, _) => Anim.Opacity(img, 1, 360);
        art.Child = img;
        left.Children.Add(art);
        var title = Theme.Text(page.Title, 32, FontWeights.ExtraBold, Theme.OnSurface, 2);
        title.TextAlignment = TextAlignment.Center;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        left.Children.Add(title);

        if (page.Strapline != null)
        {
            var who = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Background = new SolidColorBrush(Colors.Transparent) };
            if (page.StraplineThumb != null)
                who.Children.Add(new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Child = new Image { Source = Theme.Image(page.StraplineThumb, 24), Stretch = Stretch.UniformToFill } });
            var name = Theme.Text(page.Strapline, 14, FontWeights.SemiBold, Theme.OnSurface);
            name.VerticalAlignment = VerticalAlignment.Center;
            who.Children.Add(name);
            if (page.StraplineId != null)
            {
                who.PointerEntered += (_, _) => name.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
                who.PointerExited += (_, _) => name.TextDecorations = Windows.UI.Text.TextDecorations.None;
                who.Tapped += (_, _) => App.MainWin?.OpenBrowse(page.StraplineId);
            }
            left.Children.Add(who);
        }
        foreach (var line in new[] { page.Subtitle, page.SecondSubtitle })
        {
            if (line == null) continue;
            var t = Theme.Text(line, 14, FontWeights.Normal, Theme.Secondary);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Margin = new Thickness(0, -4, 0, 0);
            left.Children.Add(t);
        }

        // 버튼: 셔플 · ▶(64 흰 동그라미) · 링크 복사
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        if (page.Tracks.Count > 0) buttons.Children.Add(Circle(40, false, Icons.Download, true, "다운로드 · 오프라인 저장", () =>
        {
            var f = new MenuFlyout();
            var dl = new MenuFlyoutItem { Text = $"다운로드 ({page.Tracks.Count}곡, 음악 폴더)" };
            dl.Click += (_, _) => TrackMenu.DownloadAll(page.Tracks);
            var off = new MenuFlyoutItem { Text = $"오프라인 저장 ({page.Tracks.Count}곡)" };
            off.Click += (_, _) => TrackMenu.SaveOffline(page.Tracks);
            f.Items.Add(dl);
            f.Items.Add(off);
            f.ShowAt(buttons);
        }));
        if (page.Tracks.Count > 1) buttons.Children.Add(Circle(40, false, Icons.Shuffle, true, "셔플", () => PlayAll(true)));
        if (page.Tracks.Count > 0) buttons.Children.Add(Circle(64, true, Icons.Play, false, "재생", () => PlayAll(false)));
        buttons.Children.Add(Circle(40, false, Icons.Link, true, "링크 복사", () => CopyLink(page)));
        left.Children.Add(buttons);
        if (page.Description != null)
        {
            var d = Theme.Text(page.Description, 14, FontWeights.Normal, Theme.Secondary, 4);
            d.TextAlignment = TextAlignment.Center;
            d.Margin = new Thickness(0, 12, 0, 0);
            left.Children.Add(d);
        }
        g.Children.Add(left);

        if (page.Tracks.Count > 0)
        {
            var list = new StackPanel();
            var album = page.Kind == PageKind.Album;
            for (var i = 0; i < page.Tracks.Count; i++)
            {
                var idx = i;
                var t = page.Tracks[i];
                // 앨범은 번호, 플레이리스트는 '가수 • 앨범'
                var r = new TrackRow(t, divider: i < page.Tracks.Count - 1,
                    subtitle: album || t.Album == null ? null : $"{t.Artist} • {t.Album}",
                    number: album ? i + 1 : null);
                r.Click += row => _ = PlayerService.Instance.PlayList(page.Tracks, idx, page.Title);
                _rows.Add(r);
                list.Children.Add(r);
            }
            var card = new Border
            {
                CornerRadius = new CornerRadius(Theme.RadiusCard),
                Background = Theme.B(Color.FromArgb(20, 255, 255, 255)),
                Padding = new Thickness(12, 8, 12, 8),
                Child = list,
                Margin = new Thickness(0, 24, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            Grid.SetColumn(card, 1);
            g.Children.Add(card);
        }
        return g;
    }

    private static FrameworkElement Circle(double size, bool primary, string icon, bool stroke, string tip, Action click)
    {
        var b = new IconButton(size);
        var grid = new Grid();
        grid.Children.Add(new XEllipse { Width = size, Height = size, Fill = Theme.B(primary ? Colors.White : Theme.Chip) });
        grid.Children.Add(Theme.Icon(icon, primary ? 30 : 20, primary ? Colors.Black : Colors.White, stroke: stroke, strokeWidth: 2));
        b.Content = grid;
        if (primary) b.HoverBrush = new SolidColorBrush(Colors.Transparent);
        ToolTipService.SetToolTip(b, tip);
        b.Click += click;
        return b;
    }

    private void CopyLink(PageData page)
    {
        var url = _browseId.StartsWith("VL") ? $"https://music.youtube.com/playlist?list={_browseId[2..]}" : $"https://music.youtube.com/browse/{_browseId}";
        var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
        pkg.SetText(url);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
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
        Anim.Pressable(b);
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
                Anim.Pressable(all);
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
            r.Click += row => _ = PlayerService.Instance.PlayList(page.Tracks, idx, page.Title);
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
            if (q.Tracks.Count > 0) await PlayerService.Instance.PlayList(q.Tracks, 0, q.Title ?? title, q.RelatedBrowseId, q.AutomixPlaylistId, q.AutomixParams);
        }
        catch (Exception e) { Log.Error("목록 재생 실패", e); }
    }

    private void MarkCurrent()
    {
        var id = PlayerService.Instance.Current?.VideoId;
        foreach (var r in _rows) r.SetCurrent(r.Track.VideoId == id);
    }
}
