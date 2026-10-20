using Microsoft.UI.Windowing;
using OneMusic.Core;
using Windows.Graphics;

namespace OneMusic.UI;

/// <summary>
/// 메인 창 — pear-desktop One UI 테마와 같은 배치.
/// 맨 위 32px 창 끌기 / 90px 상단 바(메뉴 · 로고 · 검색) / 왼쪽 72px 레일 / 화면 / 떠 있는 재생 바
/// </summary>
public sealed class MainWindow : Window
{
    public PlayerService Player { get; }
    public Grid Root { get; } = new();
    public ShelvesView Home { get; private set; } = null!;
    public ShelvesView Explore { get; private set; } = null!;
    public ShelvesView Library { get; private set; } = null!;
    public SearchView Search { get; private set; } = null!;
    public PlayerView PlayerPage { get; private set; } = null!;
    public FloatingBar Bar { get; private set; } = null!;
    /// <summary>열어 둔 상세 화면들 (맨 뒤가 지금 화면) + 그 밑에 깔린 탭 · 검색 화면</summary>
    private readonly List<DetailView> _details = new();
    private FrameworkElement? _underDetails;
    private readonly Grid _wash = new() { IsHitTestVisible = false };
    private IconButton _backBtn = null!, _fwdBtn = null!, _account = null!;
    /// <summary>뒤로 간 상세 화면들 (앞으로 가기용)</summary>
    private readonly List<(string Id, string? Params)> _forward = new();
    private readonly Grid _content = new();
    private Grid _nav = null!;
    private NavRail _rail = null!;
    private AutoSuggestBox _search = null!;
    private CancellationTokenSource? _suggestCts;
    private Color _accent = AlbumColor.Fallback;
    private DevBridge? _dev;

    public MainWindow()
    {
        Title = "OneMusic";
        Player = new PlayerService(DispatcherQueue.GetForCurrentThread());
        ExtendsContentIntoTitleBar = true;
        var s = Settings.Current;
        AppWindow.Resize(new SizeInt32(s.WindowWidth, s.WindowHeight));
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = Colors.White;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(30, 255, 255, 255);
        AppWindow.Closing += (_, _) =>
        {
            Settings.Current.WindowWidth = AppWindow.Size.Width;
            Settings.Current.WindowHeight = AppWindow.Size.Height;
            Settings.Current.Save();
            App.SaveSession();
        };
        Build();
        LyricsState.Instance.Load(null);
        Player.TrackChanged += OnTrack;
        if (Log.DevRoot != null) _dev = new DevBridge(this);
    }

    private void Build()
    {
        Root.Background = Theme.B(Theme.Bg);
        // 바탕색 + 앨범 색 번짐을 한 층에 — 스크롤 흐림 덮개(WashFade)가 이 층을 그대로 비춰 그린다
        _wash.Background = Theme.B(Theme.Bg);
        WashFade.Source = _wash;
        Root.RequestedTheme = ElementTheme.Dark;
        Root.Children.Add(_wash);
        Root.SizeChanged += (_, _) => SetWash(_accent, false);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(90) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Root.Children.Add(layout);

        // 창 끌기 영역
        var drag = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        Grid.SetColumnSpan(drag, 2);
        layout.Children.Add(drag);
        SetTitleBar(drag);

        // 상단 바: ≡ · 로고 · 검색(480×40 알약) · 설정
        _nav = new Grid();
        Grid.SetRow(_nav, 1);
        Grid.SetColumnSpan(_nav, 2);
        var menu = new IconButton(40) { Content = Theme.Icon(24, Colors.White, Icons.Drawer), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(16, 0, 0, 0) };
        _nav.Children.Add(menu);
        var logo = Theme.Text("OneMusic", 20, FontWeights.ExtraBold, Theme.OnSurface);
        logo.HorizontalAlignment = HorizontalAlignment.Left;
        logo.VerticalAlignment = VerticalAlignment.Center;
        logo.Margin = new Thickness(68, 0, 0, 0);
        logo.Width = 100;
        _nav.Children.Add(logo);
        _nav.Children.Add(BuildSearch());
        // 오른쪽: ‹ › (뒤로 · 앞으로) + 프로필 사진(누르면 메뉴) — pear 상단 바
        var rightBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 100, 0) };
        _backBtn = new IconButton(40) { Content = Theme.Icon(Icons.ChevronLeft, 24, Colors.White, stroke: true, strokeWidth: 2) };
        _fwdBtn = new IconButton(40) { Content = Theme.Icon(Icons.ChevronRight, 24, Colors.White, stroke: true, strokeWidth: 2) };
        _backBtn.Click += Back;
        _fwdBtn.Click += Forward;
        Tip.Set(_backBtn, "뒤로");
        Tip.Set(_fwdBtn, "앞으로");
        rightBar.Children.Add(_backBtn);
        rightBar.Children.Add(_fwdBtn);
        var settings = new IconButton(40) { Content = Theme.Icon(24, Colors.White, Icons.Settings), Margin = new Thickness(16, 0, 0, 0) };
        Tip.Set(settings, "계정");
        var flyout = new MenuFlyout();
        var logout = new MenuFlyoutItem { Text = "로그아웃" };
        logout.Click += (_, _) => { Auth.Logout(); ReloadAll(); };
        flyout.Items.Add(logout);
        settings.Click += () => flyout.ShowAt(settings);
        rightBar.Children.Add(settings);
        _nav.Children.Add(rightBar);
        _account = settings;
        LoadAvatar();
        UpdateNavButtons();
        layout.Children.Add(_nav);

        // 왼쪽 레일
        _rail = new NavRail();
        Grid.SetRow(_rail, 2);
        _rail.Changed += ShowTab;
        layout.Children.Add(_rail);

        // 화면
        Home = new ShelvesView("FEmusic_home", withChips: true);
        Explore = new ShelvesView("FEmusic_explore", withChips: true) { Visibility = Visibility.Collapsed };
        Library = new ShelvesView("FEmusic_library_landing", withChips: true) { Visibility = Visibility.Collapsed };
        Search = new SearchView { Visibility = Visibility.Collapsed };
        foreach (var v in new FrameworkElement[] { Home, Explore, Library, Search }) _content.Children.Add(v);
        Grid.SetRow(_content, 2);
        Grid.SetColumn(_content, 1);
        layout.Children.Add(_content);

        // 재생 화면 (상단 바 · 레일까지 덮음)
        PlayerPage = new PlayerView();
        PlayerPage.CloseRequested += ClosePlayer;
        Grid.SetRow(PlayerPage, 1);
        Grid.SetRowSpan(PlayerPage, 2);
        Grid.SetColumnSpan(PlayerPage, 2);
        layout.Children.Add(PlayerPage);

        // 떠 있는 재생 바
        Bar = new FloatingBar
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(Theme.FloatGap, 0, Theme.FloatGap, Theme.FloatGap),
        };
        Bar.OpenPlayer += TogglePlayer;
        Root.Children.Add(Bar);
        Toast.Attach(Root);
        Tip.Attach(Root);
        OneMenu.Attach(Root);

        Content = Root;
        Root.Loaded += (_, _) =>
        {
            Home.LoadOnce();
            if (Player.Current == null) ShowBar(false);
        };
        Root.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape && OneMenu.IsOpen) OneMenu.CloseAll();
            else if (e.Key == Windows.System.VirtualKey.Escape && PlayerPage.IsOpen) ClosePlayer();
            else if (e.Key == Windows.System.VirtualKey.Escape && e.OriginalSource is not TextBox) Back();
        };
        // 마우스 뒤로 버튼
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (!e.GetCurrentPoint(Root).Properties.IsXButton1Pressed) return;
            if (PlayerPage.IsOpen) ClosePlayer();
            else Back();
            e.Handled = true;
        }), true);
    }

    /// <summary>검색 알약 — 480×40, 반투명 흰 배경, 왼쪽 돋보기 (pear 테마와 같은 위치 x=172)</summary>
    private FrameworkElement BuildSearch()
    {
        var pill = new Grid
        {
            Width = 480,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            Background = Theme.B(Color.FromArgb(31, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(172, 0, 0, 0),
        };
        var icon = Theme.Icon(18, Theme.Secondary, Icons.SearchSesl);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        icon.Margin = new Thickness(17, 0, 0, 0);
        pill.Children.Add(icon);
        _search = new AutoSuggestBox
        {
            PlaceholderText = "노래, 앨범, 아티스트, 팟캐스트 검색",
            FontFamily = Theme.Font,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(40, 0, 8, 0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        // 입력칸의 기본 배경 · 테두리(포커스 때 진해지는 것) 지우기
        var clear = new SolidColorBrush(Colors.Transparent);
        foreach (var key in new[]
                 {
                     "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
                     "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused",
                 })
            _search.Resources[key] = clear;
        _search.Resources["TextControlForeground"] = Theme.B(Colors.White);
        _search.Resources["TextControlForegroundFocused"] = Theme.B(Colors.White);
        _search.Resources["TextControlPlaceholderForeground"] = Theme.B(Theme.Secondary);
        _search.Resources["TextControlPlaceholderForegroundFocused"] = Theme.B(Theme.Secondary);
        // 추천 목록: 검색창 밑 8px 떨어진 둥근(24) 유리 카드, 테두리 없음 (pear 35 · 58)
        _search.Resources["AutoSuggestBoxSuggestionsListBackground"] = Theme.Glass(0.86, Color.FromArgb(255, 46, 46, 48));
        _search.Resources["AutoSuggestBoxSuggestionsListBorderBrush"] = new SolidColorBrush(Colors.Transparent);
        _search.Resources["AutoSuggestListBorderThemeThickness"] = new Thickness(0);
        _search.Resources["AutoSuggestListMargin"] = new Thickness(-40, 14, -8, 0);
        _search.Resources["AutoSuggestListPadding"] = new Thickness(6);
        _search.Resources["OverlayCornerRadius"] = new CornerRadius(24);
        _search.Resources["ListViewItemCornerRadius"] = new CornerRadius(16);
        _search.TextChanged += OnSearchText;
        // 추천 줄은 글자가 아니라 화면 요소라서, 고를 때 입력칸 글자를 바꾸지 않는다
        _search.UpdateTextOnSelect = false;
        _search.QuerySubmitted += (box, e) =>
        {
            if (e.ChosenSuggestion is FrameworkElement { Tag: SearchItem it })
            {
                if (it.Track is { } t) _ = Player.PlaySong(t, fromSearch: true);
                else if (it.BrowseId != null) OpenBrowse(it.BrowseId);
                return;
            }
            var q = (e.ChosenSuggestion as FrameworkElement)?.Tag as string ?? e.QueryText;
            if (!string.IsNullOrWhiteSpace(q)) ShowSearch(q.Trim());
        };
        pill.Children.Add(_search);
        return pill;
    }

    private void ShowTab(int i)
    {
        ClosePlayer();
        ClearDetails();
        var views = new ShelvesView[] { Home, Explore, Library };
        Search.Visibility = Visibility.Collapsed;
        for (var k = 0; k < views.Length; k++)
        {
            var show = k == i;
            if (show && views[k].Visibility != Visibility.Visible) Anim.Appear(views[k], 0, 12);
            views[k].Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
        views[i].LoadOnce();
    }

    public void ShowHome() => _rail.Select(0);

    public void ReloadAll()
    {
        LoadAvatar();
        Home.Reload();
        Explore.Reload();
        Library.Reload();
    }

    public void ShowSearch(string q)
    {
        ClosePlayer();
        ClearDetails();
        _search.Text = q;
        foreach (var v in new FrameworkElement[] { Home, Explore, Library }) v.Visibility = Visibility.Collapsed;
        Search.Visibility = Visibility.Visible;
        Anim.Appear(Search, 0, 12);
        Search.Search(q);
    }

    /// <summary>앨범 · 플레이리스트 · 아티스트 상세 화면을 지금 화면 위에 연다 (뒤로 가면 원래 화면 · 스크롤 위치 그대로)</summary>
    public void OpenBrowse(string browseId, string? @params = null) => OpenBrowse(browseId, @params, fromForward: false);

    private void OpenBrowse(string browseId, string? @params, bool fromForward)
    {
        ClosePlayer();
        if (!fromForward) _forward.Clear();
        var current = _content.Children.OfType<FrameworkElement>().LastOrDefault(v => v.Visibility == Visibility.Visible);
        if (_details.Count == 0) _underDetails = current;
        if (current != null) current.Visibility = Visibility.Collapsed;
        // 너무 깊어지면 가장 오래된 상세 화면부터 버린다 (메모리)
        if (_details.Count >= 8)
        {
            _content.Children.Remove(_details[0]);
            _details.RemoveAt(0);
        }
        var d = new DetailView(browseId, @params);
        d.BackRequested += Back;
        _details.Add(d);
        _content.Children.Add(d);
        Anim.Appear(d, 0, 12);
        d.Load();
        UpdateNavButtons();
    }

    public void Forward()
    {
        if (_forward.Count == 0) return;
        var (id, p) = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        OpenBrowse(id, p, fromForward: true);
    }

    private void UpdateNavButtons()
    {
        _backBtn.Opacity = _details.Count > 0 ? 1 : 0.35;
        _fwdBtn.Opacity = _forward.Count > 0 ? 1 : 0.35;
    }

    /// <summary>계정 버튼을 프로필 사진으로 (로그인했을 때)</summary>
    private async void LoadAvatar()
    {
        if (!Auth.IsLoggedIn) return;
        try
        {
            var url = await InnerTube.Instance.AccountPhoto();
            if (url == null) return;
            _account.Content = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Child = new Image { Source = Theme.Image(url, 32), Stretch = Stretch.UniformToFill } };
        }
        catch (Exception e) { Log.Warn($"프로필 사진 실패: {e.Message}"); }
    }

    public void Back()
    {
        if (_details.Count == 0) return;
        _forward.Add((_details[^1].BrowseId, _details[^1].Params));
        _content.Children.Remove(_details[^1]);
        _details.RemoveAt(_details.Count - 1);
        UpdateNavButtons();
        FrameworkElement? prev = _details.Count > 0 ? _details[^1] : _underDetails;
        if (prev == null) return;
        prev.Visibility = Visibility.Visible;
        Anim.Appear(prev, 0, 12);
    }

    private void ClearDetails()
    {
        foreach (var d in _details) _content.Children.Remove(d);
        _details.Clear();
        _underDetails = null;
        _forward.Clear();
        UpdateNavButtons();
    }

    public void TogglePlayer()
    {
        if (PlayerPage.IsOpen) ClosePlayer();
        else OpenPlayer();
    }

    public void OpenPlayer()
    {
        PlayerPage.Open();
        Bar.SetPlayerOpen(true);
        foreach (var el in new UIElement[] { _content, _nav, _rail })
        {
            Anim.Opacity(el, 0, 220);
            el.IsHitTestVisible = false;
        }
    }

    public void ClosePlayer()
    {
        if (!PlayerPage.IsOpen) return;
        PlayerPage.Close();
        Bar.SetPlayerOpen(false);
        foreach (var el in new UIElement[] { _content, _nav, _rail })
        {
            Anim.Opacity(el, 1, 320);
            el.IsHitTestVisible = true;
        }
    }

    private async void OnSearchText(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _suggestCts?.Cancel();
        var q = sender.Text.Trim();
        if (q.Length == 0)
        {
            sender.ItemsSource = null;
            return;
        }
        var cts = _suggestCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(180, cts.Token);
            var (words, items) = await InnerTube.Instance.SuggestionsFull(q, cts.Token);
            if (cts.IsCancellationRequested) return;
            var rows = new List<FrameworkElement>();
            foreach (var w in words) rows.Add(SuggestRow(w, null));
            foreach (var it in items) rows.Add(SuggestRow(it.Title, it));
            sender.ItemsSource = rows;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Warn($"추천 검색어 실패: {ex.Message}"); }
    }

    /// <summary>검색 추천 한 줄 — 돋보기 + 검색어, 또는 아트 + 제목 + '노래 • MC몽 • …' (pear 검색 드롭다운)</summary>
    private static FrameworkElement SuggestRow(string text, SearchItem? item)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Height = item == null ? 36 : 48, Tag = (object?)item ?? text };
        if (item == null)
        {
            var icon = Theme.Icon(18, Theme.Secondary, Icons.SearchSesl);
            icon.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(icon);
            var t = Theme.Text(text, 15, FontWeights.Normal, Theme.Secondary);
            t.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(t);
            return row;
        }
        row.Children.Add(new Border
        {
            Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(item.Round ? 20 : 6),
            Background = Theme.B(Theme.Chip),
            Child = new Image { Width = 40, Height = 40, Source = Theme.Image(item.Thumb, 72), Stretch = Stretch.UniformToFill },
        });
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        col.Children.Add(Theme.Text(item.Title, 15, FontWeights.Bold, Theme.OnSurface));
        col.Children.Add(Theme.Text(item.Subtitle, 13, FontWeights.Normal, Theme.Secondary));
        row.Children.Add(col);
        return row;
    }

    private bool _barShown = true;

    /// <summary>재생할 곡이 없으면 재생 바를 아래로 내려 숨긴다 (YT Music: 처음 · 현재 재생목록 닫기)</summary>
    private void ShowBar(bool show)
    {
        if (show == _barShown) return;
        _barShown = show;
        Bar.IsHitTestVisible = show;
        Anim.Slide(Bar, new Vector3(0, show ? 0 : (float)(Theme.BarHeight + Theme.FloatGap * 2), 0), 640);
        Anim.Opacity(Bar, show ? 1 : 0, show ? Anim.Medium : Anim.Fast);
        if (!show) ClosePlayer();
    }

    private async void OnTrack()
    {
        var t = Player.Current;
        LyricsState.Instance.Load(t);
        ShowBar(t != null);
        if (t == null) return;
        var c = await AlbumColor.FromUrl(t.Thumb);
        if (Player.Current?.VideoId != t.VideoId) return;
        _accent = c;
        Bar.SetAccent(c);
        SetWash(c, true);
    }

    /// <summary>
    /// 앨범 색 번짐 (pear 테마의 --oneui-album-wash 와 같은 두 개의 타원):
    /// 오른쪽 위 1100×720 (58%) · 왼쪽 아래 900×640 (36%). 새 색 층이 서서히 나타나고 옛 층은 사라진다.
    /// </summary>
    private void SetWash(Color c, bool animate)
    {
        var w = Math.Max(1, Root.ActualWidth);
        var h = Math.Max(1, Root.ActualHeight);
        var layer = new Grid { IsHitTestVisible = false };
        layer.Children.Add(new XRect { Fill = Radial(c.WithAlpha(0.58), new Point(w * 0.88, -h * 0.12), 1100, 720) });
        layer.Children.Add(new XRect { Fill = Radial(c.WithAlpha(0.36), new Point(-w * 0.08, h * 1.08), 900, 640) });
        var old = _wash.Children.ToList();
        _wash.Children.Add(layer);
        if (!animate)
        {
            foreach (var o in old) _wash.Children.Remove(o);
            return;
        }
        Anim.V(layer).Opacity = 0;
        Anim.Opacity(layer, 1, 900);
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(950);
            foreach (var o in old) _wash.Children.Remove(o);
        });
    }

    private static RadialGradientBrush Radial(Color c, Point center, double rx, double ry)
    {
        var b = new RadialGradientBrush
        {
            Center = center,
            GradientOrigin = center,
            RadiusX = rx,
            RadiusY = ry,
            MappingMode = BrushMappingMode.Absolute,
        };
        b.GradientStops.Add(new GradientStop { Color = c, Offset = 0 });
        b.GradientStops.Add(new GradientStop { Color = c.WithAlpha(0), Offset = 0.7 });
        return b;
    }
}
