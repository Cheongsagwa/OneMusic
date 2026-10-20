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
    /// <summary>창이 앞에 있는지 (곡 바뀜 알림은 뒤에 있을 때만)</summary>
    public bool IsActiveWindow { get; private set; } = true;
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
    private readonly Grid _playerWash = new() { IsHitTestVisible = false };
    private Border _navGlass = null!;
    private Grid _guide = null!;
    private Grid _layoutGrid = null!;
    private bool _navGlassOn;
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
            // 이어 듣기: 지금 목록 · 위치를 남긴다
            if (Settings.Current.ResumeSession) Player.Snapshot().Save();
            Notifier.Unregister();
        };
        Activated += (_, e) => IsActiveWindow = e.WindowActivationState != WindowActivationState.Deactivated;
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
        // 재생 화면: 앨범 색을 더 진하게 (pear 22 — 900×700 at 30% 45%, 50%)
        Root.Children.Add(_playerWash);
        Anim.V(_playerWash).Opacity = 0;
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
        // 기록 (최근 재생)
        var history = new IconButton(40) { Content = Theme.Icon(Icons.History, 24, Colors.White, stroke: true, strokeWidth: 1.8) };
        Tip.Set(history, "기록");
        history.Click += () => OpenBrowse("FEmusic_history");
        rightBar.Children.Add(history);
        var settings = new IconButton(40) { Content = Theme.Icon(24, Colors.White, Icons.Settings), Margin = new Thickness(16, 0, 0, 0) };
        Tip.Set(settings, "계정");
        settings.Click += () => AccountMenu.Show(this, settings);
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
        // 화면은 창 맨 위부터 — 위 122px(창 끌기 + 상단 바) 밑으로 흘러 들어가며 유리 상단 바 뒤로 흐리게 비친다 (pear 6 · 22)
        Grid.SetRow(_content, 0);
        Grid.SetRowSpan(_content, 3);
        Grid.SetColumn(_content, 1);
        Canvas.SetZIndex(_content, 0);
        layout.Children.Add(_content);
        // 상단 바 유리 — 내용이 스크롤되면 나타난다 (YT: nav-bar-background, opacity medium ease)
        _navGlass = new Border { Background = Theme.Glass(0.55, Color.FromArgb(255, 36, 36, 40)), IsHitTestVisible = false };
        Grid.SetRowSpan(_navGlass, 2);
        Grid.SetColumnSpan(_navGlass, 2);
        Canvas.SetZIndex(_navGlass, 1);
        Anim.V(_navGlass).Opacity = 0;
        layout.Children.Add(_navGlass);
        Canvas.SetZIndex(drag, 2);
        Canvas.SetZIndex(_nav, 3);
        Canvas.SetZIndex(_rail, 3);
        ScrollPage.ScrolledChanged += (page, scrolled) => { if (page.Visibility == Visibility.Visible) SetNavGlass(scrolled); };
        // 펼친 사이드바 (≡)
        _guide = BuildGuide();
        Grid.SetRow(_guide, 2);
        Grid.SetColumnSpan(_guide, 2);
        Canvas.SetZIndex(_guide, 4);
        layout.Children.Add(_guide);
        menu.Click += ToggleGuide;
        _layoutGrid = layout;

        // 재생 화면 (상단 바 · 레일까지 덮음)
        PlayerPage = new PlayerView();
        Grid.SetRow(PlayerPage, 1);
        Grid.SetRowSpan(PlayerPage, 2);
        Grid.SetColumnSpan(PlayerPage, 2);
        Canvas.SetZIndex(PlayerPage, 5);
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
        _drop = new SearchDropdown(Root);
        _drop.Chosen += Suggest;
        Tip.Attach(Root);
        OneMenu.Attach(Root);

        Content = Root;
        Root.Loaded += (_, _) =>
        {
            Home.LoadOnce();
            if (Settings.Current.GuideOpen) ToggleGuide();
            // 이어 듣기: 지난번 목록 · 위치 (멈춘 채로)
            if (Settings.Current.ResumeSession && SessionState.Load() is { } session) Player.Restore(session);
            if (Player.Current == null) ShowBar(false);
        };
        // 단축키 — 목록 · 스크롤이 키를 먼저 써도(handled) 받도록
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Escape && Shortcuts.Handle(this, e)) { e.Handled = true; return; }
            if (e.Key != Windows.System.VirtualKey.Escape) return;
            if (OneMenu.IsOpen) OneMenu.CloseAll();
            else if (PlayerPage.IsOpen) ClosePlayer();
            else if (e.OriginalSource is not TextBox) Back();
        }), true);
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
            // pear 22: 검색창은 흰 12% 유리 알약 (뒤가 흐리게)
            Background = Theme.GlassLight(0.12),
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
            // ↑↓ 로 고른 추천이 있으면 그것, 아니면 입력한 글자로 검색
            var chosen = _drop?.Selected;
            _drop?.Hide();
            if (chosen != null) { Suggest(chosen); return; }
            if (!string.IsNullOrWhiteSpace(e.QueryText)) ShowSearch(e.QueryText.Trim());
        };
        _search.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (_drop == null) return;
            if (e.Key == Windows.System.VirtualKey.Down) { _drop.Move(1); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Up) { _drop.Move(-1); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Escape && _drop.IsOpen) { _drop.Hide(); e.Handled = true; }
        }), true);
        _search.LostFocus += async (_, _) =>
        {
            await Task.Delay(180);
            if (_drop is { PointerInside: false }) _drop.Hide();
        };
        _search.GotFocus += (_, _) => { if (_search.Text.Trim().Length > 0) OnSearchText(_search, null); };
        pill.Children.Add(_search);
        _searchPill = pill;
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
        SyncNavGlass();
    }

    public void ShowHome() => _rail.Select(0);

    // ───────────────────────── 펼친 사이드바 (≡) ─────────────────────────

    private bool _guideOpen;
    private readonly List<(Border Pill, TextBlock Label)> _guideItems = new();
    private readonly StackPanel _guideLists = new() { Spacing = 2 };

    /// <summary>
    /// ≡ 를 누르면 펼쳐지는 사이드바 (YT Music guide) — 홈 · 둘러보기 · 보관함 + 새 재생목록 + 내 재생목록들.
    /// 미끄러지며 나타나고 사라진다 (pear 39: transform medium ease · opacity), 화면은 옆으로 비켜 간다
    /// </summary>
    private Grid BuildGuide()
    {
        var g = new Grid { Width = 240, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Background = new SolidColorBrush(Colors.Transparent) };
        var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Padding = new Thickness(8, 16, 8, Theme.BarHeight + Theme.FloatGap * 2) };
        var stack = new StackPanel { Spacing = 2 };
        var entries = new[] { ("홈", Icons.HomeOutline, Icons.HomeFilled), ("둘러보기", Icons.ExploreOutline, Icons.ExploreFilled), ("보관함", Icons.LibraryOutline, Icons.LibraryFilled) };
        for (var i = 0; i < entries.Length; i++)
        {
            var idx = i;
            var (label, outline, filled) = entries[i];
            var row = new Grid { Height = 48, Background = new SolidColorBrush(Colors.Transparent) };
            var pill = new Border { CornerRadius = new CornerRadius(23), Background = Theme.B(Color.FromArgb(128, 80, 80, 84)) };
            Anim.V(pill).Opacity = 0;
            row.Children.Add(pill);
            var hover = new Border { CornerRadius = new CornerRadius(23), Background = Theme.B(Color.FromArgb(15, 255, 255, 255)) };
            Anim.V(hover).Opacity = 0;
            row.Children.Add(hover);
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(Theme.Icon(24, Colors.White, idx == 0 ? filled : outline));
            var t = Theme.Text(label, 16, FontWeights.SemiBold, Theme.OnSurface);
            t.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(t);
            row.Children.Add(line);
            row.PointerEntered += (_, _) => Anim.Opacity(hover, 1, Anim.Medium);
            row.PointerExited += (_, _) => Anim.Opacity(hover, 0, Anim.Medium);
            row.Tapped += (_, _) => _rail.Select(idx);
            Anim.Pressable(row, 0.96f);
            _guideItems.Add((pill, t));
            stack.Children.Add(row);
        }
        stack.Children.Add(new XRect { Height = 1, Fill = Theme.B(Theme.Divider), Margin = new Thickness(12, 14, 12, 14) });
        // 새 재생목록 (흰 테두리 알약)
        var add = new Border
        {
            Height = 40,
            CornerRadius = new CornerRadius(20),
            Background = Theme.GlassLight(0.12),
            Margin = new Thickness(8, 0, 8, 12),
        };
        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        addRow.Children.Add(Theme.Icon(Icons.QueueAdd, 20, Colors.White, stroke: true));
        addRow.Children.Add(Theme.Text("새 재생목록", 14, FontWeights.SemiBold, Theme.OnSurface));
        add.Child = addRow;
        Anim.Pressable(add);
        add.Tapped += async (_, _) =>
        {
            var name = await Prompt.Ask(Root.XamlRoot, "새 재생목록", "재생목록 이름");
            if (string.IsNullOrWhiteSpace(name)) return;
            var id = await InnerTube.Instance.CreatePlaylist(name.Trim(), Array.Empty<string>());
            Toast.Show(id != null ? $"'{name.Trim()}' 재생목록을 만들었어요" : "만들지 못했어요");
            if (id != null) LoadGuideLists();
        };
        stack.Children.Add(add);
        stack.Children.Add(_guideLists);
        sv.Content = stack;
        g.Children.Add(sv);
        _rail.Changed += SyncGuide;
        SyncGuide(_rail.Selected);
        return g;
    }

    private void SyncGuide(int idx)
    {
        for (var i = 0; i < _guideItems.Count; i++)
        {
            Anim.Opacity(_guideItems[i].Pill, i == idx ? 1 : 0, Anim.Medium);
            _guideItems[i].Label.FontWeight = i == idx ? FontWeights.Bold : FontWeights.SemiBold;
        }
    }

    /// <summary>내 재생목록 (보관함 → 재생목록) 을 사이드바에</summary>
    private async void LoadGuideLists()
    {
        _guideLists.Children.Clear();
        if (!Auth.IsLoggedIn) return;
        try
        {
            var shelves = await InnerTube.Instance.BrowseShelves("FEmusic_liked_playlists");
            foreach (var c in shelves.SelectMany(x => x.Items).Where(c => c.BrowseId != null || c.PlaylistId != null).Take(60))
            {
                var row = new Grid { Height = 52, CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Colors.Transparent), Padding = new Thickness(18, 0, 12, 0) };
                var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
                col.Children.Add(Theme.Text(c.Title, 14, FontWeights.SemiBold, Theme.OnSurface));
                if (c.Subtitle.Length > 0) col.Children.Add(Theme.Text(c.Subtitle, 12, FontWeights.Normal, Theme.Secondary));
                row.Children.Add(col);
                row.PointerEntered += (_, _) => row.Background = Theme.B(Color.FromArgb(15, 255, 255, 255));
                row.PointerExited += (_, _) => row.Background = new SolidColorBrush(Colors.Transparent);
                row.Tapped += (_, _) => { if (c.BrowseId != null) OpenBrowse(c.BrowseId); else Play.Card(c); };
                row.RightTapped += (_, e) => { e.Handled = true; TrackMenu.ShowCard(row, c, e.GetPosition(null)); };
                _guideLists.Children.Add(row);
            }
        }
        catch (Exception e) { Log.Warn($"사이드바 재생목록 실패: {e.Message}"); }
    }

    public void ToggleGuide()
    {
        _guideOpen = !_guideOpen;
        Settings.Current.GuideOpen = _guideOpen;
        var from = _content.Margin.Left;
        var to = _guideOpen ? 240 - 72 : 0;
        Tween.Run(_content, Anim.Medium, t => RepeatIcon.Curve(Anim.Ease, t), k => _content.Margin = new Thickness(from + (to - from) * k, 0, 0, 0));
        if (_guideOpen)
        {
            if (_guideLists.Children.Count == 0) LoadGuideLists();
            _guide.Visibility = Visibility.Visible;
            Anim.SetTranslation(_guide, new Vector3(-40, 0, 0));
            Anim.V(_guide).Opacity = 0;
            Anim.Translate(_guide, Vector3.Zero);
            Anim.Opacity(_guide, 1, Anim.Medium);
            Anim.Opacity(_rail, 0, Anim.Fast);
            _rail.IsHitTestVisible = false;
        }
        else
        {
            Anim.Translate(_guide, new Vector3(-40, 0, 0));
            Anim.Opacity(_guide, 0, Anim.Fast);
            Anim.Opacity(_rail, 1, Anim.Medium);
            _rail.IsHitTestVisible = true;
            var gen = _guideOpen;
            DispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(Anim.Medium);
                if (!_guideOpen) _guide.Visibility = Visibility.Collapsed;
            });
        }
    }

    /// <summary>상단 바 유리 — 지금 화면이 스크롤되어 있으면 보인다</summary>
    private void SetNavGlass(bool on)
    {
        if (on == _navGlassOn) return;
        _navGlassOn = on;
        if (!PlayerPage.IsOpen) Anim.Opacity(_navGlass, on ? 1 : 0, Anim.Medium);
    }

    private void SyncNavGlass()
    {
        var page = _content.Children.OfType<ScrollPage>().LastOrDefault(v => v.Visibility == Visibility.Visible);
        SetNavGlass(page?.IsScrolled == true);
    }

    public void FocusSearch()
    {
        ClosePlayer();
        _search.Focus(FocusState.Programmatic);
    }

    public void ReloadAll()
    {
        LoadAvatar();
        if (_guideOpen) LoadGuideLists();
        else _guideLists.Children.Clear();
        Home.Reload();
        Explore.Reload();
        Library.Reload();
    }

    public void ShowSearch(string q)
    {
        _drop?.Hide();
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
        SetNavGlass(false);
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
        SyncNavGlass();
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
        Anim.Opacity(_playerWash, 1, Anim.Morph);
        Anim.Opacity(_navGlass, 0, Anim.Fast);
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
        Anim.Opacity(_playerWash, 0, Anim.Medium);
        if (_navGlassOn) Anim.Opacity(_navGlass, 1, Anim.Medium);
        Bar.SetPlayerOpen(false);
        foreach (var el in new UIElement[] { _content, _nav, _rail })
        {
            Anim.Opacity(el, 1, 320);
            el.IsHitTestVisible = true;
        }
    }

    private Grid? _searchPill;
    private SearchDropdown? _drop;

    /// <summary>추천을 고르면: 검색어는 검색, 곡은 재생(검색에서 튼 곡 → 자동재생 토글), 앨범 · 아티스트는 그 화면</summary>
    private void Suggest(object tag)
    {
        switch (tag)
        {
            case string q:
                ShowSearch(q);
                break;
            case SearchItem { Track: { } t }:
                _ = Player.PlaySong(t, fromSearch: true);
                break;
            case SearchItem { BrowseId: { } id }:
                OpenBrowse(id);
                break;
        }
    }

    private async void OnSearchText(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs? e)
    {
        if (e != null && e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _suggestCts?.Cancel();
        var q = sender.Text.Trim();
        if (q.Length == 0)
        {
            _drop?.Hide();
            return;
        }
        var cts = _suggestCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(180, cts.Token);
            var (words, items) = await InnerTube.Instance.SuggestionsFull(q, cts.Token);
            if (cts.IsCancellationRequested || _searchPill == null || _drop == null) return;
            if (_search.FocusState == FocusState.Unfocused && !_drop.PointerInside) return;
            _drop.Show(_searchPill, words, items);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Warn($"추천 검색어 실패: {ex.Message}"); }
    }

    private bool _barShown = true;
    private string? _notifiedFor;

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
        if (t.VideoId != _notifiedFor)
        {
            _notifiedFor = t.VideoId;
            Notifier.TrackChanged(t, IsActiveWindow);
        }
        var c = await AlbumColor.FromUrl(t.Thumb);
        if (Player.Current?.VideoId != t.VideoId) return;
        _accent = c;
        Bar.SetAccent(c);
        LyricsView.Glow = c;
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
        // 재생 화면 진한 앨범 색
        _playerWash.Children.Clear();
        _playerWash.Children.Add(new XRect { Fill = Radial(c.WithAlpha(0.5), new Point(w * 0.3, h * 0.45), 900, 700) });
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
