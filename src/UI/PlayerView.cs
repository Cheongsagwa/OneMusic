using Microsoft.UI.Input;
using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>One UI 알약 탭 — 둥근 트랙 안에서 고른 탭만 알약으로 채워지고, 알약이 스프링으로 미끄러진다</summary>
public sealed class PillTabs : Grid
{
    private readonly Border _pill = new() { CornerRadius = new CornerRadius(18), Background = Theme.B(Theme.Selected), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Grid _items = new();
    private readonly List<TextBlock> _labels = new();
    public int Selected { get; private set; }
    public event Action<int>? Changed;

    public PillTabs(params string[] names)
    {
        Height = 44;
        Padding = new Thickness(4);
        CornerRadius = new CornerRadius(22);
        Background = Theme.B(Theme.Chip);
        Children.Add(_pill);
        Children.Add(_items);
        for (var i = 0; i < names.Length; i++)
        {
            _items.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = Theme.Text(names[i], 15, FontWeights.SemiBold, Theme.Secondary);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            var hit = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            hit.Children.Add(label);
            var idx = i;
            hit.Tapped += (_, _) => Select(idx);
            hit.PointerPressed += (_, _) => Anim.Scale(label, 0.94f, 0.9f, 30);
            hit.PointerReleased += (_, _) => Anim.Scale(label, 1f);
            hit.PointerExited += (_, _) => Anim.Scale(label, 1f);
            Grid.SetColumn(hit, i);
            _items.Children.Add(hit);
            _labels.Add(label);
        }
        SizeChanged += (_, _) => Place(false);
        Loaded += (_, _) => Place(false);
        Select(0, false);
    }

    public void Select(int i, bool notify = true)
    {
        Selected = i;
        for (var k = 0; k < _labels.Count; k++)
        {
            _labels[k].Foreground = Theme.B(k == i ? Theme.OnSurface : Theme.Secondary);
            _labels[k].FontWeight = k == i ? FontWeights.Bold : FontWeights.SemiBold;
        }
        Place(true);
        if (notify) Changed?.Invoke(i);
    }

    private void Place(bool animate)
    {
        if (_labels.Count == 0 || ActualWidth <= 0) return;
        var w = (ActualWidth - 8) / _labels.Count;
        _pill.Width = w;
        var to = new Vector3((float)(w * Selected), 0, 0);
        if (animate) Anim.Translate(_pill, to, 0.75f, 55);
        else Anim.SetTranslation(_pill, to);
    }
}

/// <summary>
/// 재생 화면 (pear-desktop One UI 테마의 재생 화면과 같은 배치) — 아래에서 스프링으로 올라온다.
/// 왼쪽: 노래/동영상 전환 + 큰 앨범아트 / 오른쪽: 450px 유리 패널 [다음 트랙 | 가사 | 댓글 | 관련 항목]
/// </summary>
public sealed class PlayerView : Grid
{
    private readonly PlayerService _p = PlayerService.Instance;
    private readonly Border _art = new() { CornerRadius = new CornerRadius(Theme.RadiusCard) };
    private readonly ArtView _artView = new((float)Theme.RadiusCard, 670);
    private readonly MediaPlayerElement _videoView = new() { AreTransportControlsEnabled = false, Stretch = Stretch.Uniform, Background = Theme.B(Colors.Black) };
    private readonly Grid _artBox = new() { Width = 670, Height = 648 };
    private readonly PillTabs _tabs = new("다음 트랙", "가사", "댓글", "관련 항목");
    private readonly StackPanel _queue = new();
    private readonly ScrollViewer _queueScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    private readonly Grid _queueTab = new();
    private readonly TextBlock _source = Theme.Text("", 14, FontWeights.Bold, Theme.OnSurface);
    private readonly OneToggle _autoplay;
    private Grid _autoRow = null!;
    private readonly LyricsView _lyrics = new();
    private readonly Grid _lyricsTab = new();
    private readonly CommentsView _comments = new();
    private readonly ScrollViewer _related = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    private readonly StackPanel _relatedList = new() { Spacing = 8 };
    private string? _relatedFor;
    private readonly List<TrackRow> _rows = new();
    private readonly Grid _panel;
    private readonly Grid _left = new();
    public bool IsOpen { get; private set; }

    public PlayerView()
    {
        Background = new SolidColorBrush(Colors.Transparent);
        Padding = new Thickness(56, 54, 56, Theme.BarHeight + Theme.FloatGap * 2 + 28);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(450) });
        ColumnSpacing = 56;

        // ── 왼쪽: 노래/동영상 + 앨범아트 ──
        var left = _left;
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var av = new AvToggle { Margin = new Thickness(0, 0, 0, 24) };
        left.Children.Add(av);
        // 뮤직비디오는 아트 자리에서 화면으로 (재생기 하나를 그대로 붙인다)
        var artStack = new Grid();
        artStack.Children.Add(_artView);
        _videoView.SetMediaPlayer(_p.Media);
        _videoView.Visibility = Visibility.Collapsed;
        artStack.Children.Add(_videoView);
        _art.Child = artStack;
        _p.VideoChanged += OnVideo;
        _artBox.Children.Add(_art);
        var artHolder = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, VerticalAlignment = VerticalAlignment.Center, Child = _artBox };
        _art.Shadow = new ThemeShadow();
        _art.Translation = new Vector3(0, 0, 40);
        Grid.SetRow(artHolder, 1);
        left.Children.Add(artHolder);
        Children.Add(left);

        // ── 오른쪽: 유리 패널 ──
        _panel = new Grid
        {
            CornerRadius = new CornerRadius(32),
            Background = Theme.Glass(0.32, Color.FromArgb(255, 30, 30, 34)),
            Padding = new Thickness(12),
        };
        _panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _panel.Children.Add(_tabs);
        var content = new Grid { Margin = new Thickness(0, 8, 0, 0) };

        // 다음 트랙: 출처 머리 + 자동재생 + 목록
        _queueTab.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _queueTab.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _queueTab.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new Grid { Margin = new Thickness(8, 12, 8, 8) };
        var headText = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 96, 0) };
        headText.Children.Add(Theme.Text("재생 중인 트랙 출처", 12, FontWeights.Bold, Color.FromArgb(178, 255, 255, 255)));
        headText.Children.Add(_source);
        head.Children.Add(headText);
        var save = new Border { Height = 32, CornerRadius = new CornerRadius(16), Background = Theme.B(Theme.Chip), Padding = new Thickness(12, 0, 14, 0), HorizontalAlignment = HorizontalAlignment.Right };
        var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        saveRow.Children.Add(Theme.Icon(20, Colors.White, Icons.PlaylistAdd));
        var saveText = Theme.Text("저장", 14, FontWeights.SemiBold, Theme.OnSurface);
        saveText.VerticalAlignment = VerticalAlignment.Center;
        saveRow.Children.Add(saveText);
        save.Child = saveRow;
        Tip.Set(save, "재생목록에 저장");
        save.Tapped += (_, _) => SaveQueue(save);
        Anim.Pressable(save);
        head.Children.Add(save);
        _queueTab.Children.Add(head);
        var autoRow = _autoRow = new Grid { Height = 52, Margin = new Thickness(8, 0, 8, 4), Padding = new Thickness(8, 0, 2, 0), Background = new SolidColorBrush(Colors.Transparent) };
        var autoText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1, Margin = new Thickness(0, 0, 60, 0) };
        autoText.Children.Add(Theme.Text("자동재생", 14, FontWeights.Bold, Theme.OnSurface));
        autoText.Children.Add(Theme.Text("유사한 콘텐츠를 현재 재생목록 끝에 추가", 12, FontWeights.Normal, Theme.Secondary));
        autoRow.Children.Add(autoText);
        _autoplay = new OneToggle(_p.Autoplay) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _autoplay.Toggled += on => _p.Autoplay = on;
        autoRow.Children.Add(_autoplay);
        // 줄 아무 데나 눌러도 켜고 끈다
        autoRow.Tapped += (_, _) =>
        {
            var on = !_p.Autoplay;
            _autoplay.Set(on, true);
            _p.Autoplay = on;
        };
        Grid.SetRow(autoRow, 1);
        _queueTab.Children.Add(autoRow);
        _queueScroll.Content = _queue;
        // 패널 안 목록도 더 있는 쪽 끝을 흐린다 (pear scroll-fade: 위 44 · 아래 56)
        ListFade.Attach(_queueScroll);
        Grid.SetRow(_queueScroll, 2);
        _queueTab.Children.Add(_queueScroll);
        content.Children.Add(_queueTab);

        // 가사 탭: 출처 고르기 카드 + 가사
        _lyricsTab.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _lyricsTab.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _lyricsTab.Children.Add(new LyricsSourceBar());
        Grid.SetRow(_lyrics, 1);
        _lyricsTab.Children.Add(_lyrics);
        content.Children.Add(_lyricsTab);
        content.Children.Add(_comments);
        _related.Content = _relatedList;
        ListFade.Attach(_related);
        content.Children.Add(_related);
        Grid.SetRow(content, 1);
        _panel.Children.Add(content);
        Grid.SetColumn(_panel, 1);
        Children.Add(_panel);
        ShowTab(0);
        _tabs.Changed += ShowTab;

        _p.TrackChanged += OnTrack;
        _p.QueueChanged += BuildQueue;
        OnTrack();
        BuildQueue();
        Visibility = Visibility.Collapsed;
    }

    private void ShowTab(int i)
    {
        var views = new FrameworkElement[] { _queueTab, _lyricsTab, _comments, _related };
        for (var k = 0; k < views.Length; k++) views[k].Visibility = k == i ? Visibility.Visible : Visibility.Collapsed;
        _lyrics.Active = i == 1 && IsOpen;
        Anim.Appear(views[i], 0, 10);
        if (i == 2) _comments.Load(_p.Current?.VideoId);
        if (i == 3) LoadRelated();
    }

    public void ShowLyricsTab() => _tabs.Select(1);

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        Visibility = Visibility.Visible;
        // pear 64: 화면은 640ms 우아한 감속으로 올라오고, 왼쪽 · 오른쪽 패널은 720ms 동안 떠오른다(오른쪽 60ms 늦게)
        var h = (float)Math.Max(300, ActualHeight * 0.35);
        Anim.V(this).Opacity = 0;
        Anim.SetTranslation(this, new Vector3(0, h, 0));
        Anim.Slide(this, Vector3.Zero, 640);
        Anim.Opacity(this, 1, 300);
        Anim.Rise(_left, 0);
        Anim.Rise(_panel, 60);
        _lyrics.Active = _tabs.Selected == 1;
        ScrollToCurrent();
    }

    public async void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _lyrics.Active = false;
        Anim.Slide(this, new Vector3(0, (float)Math.Max(300, ActualHeight * 0.35), 0), 640);
        Anim.Opacity(this, 0, 240);
        await Task.Delay(320);
        if (!IsOpen) Visibility = Visibility.Collapsed;
    }

    /// <summary>노래 ↔ 동영상: 영상이면 16:9 화면, 노래면 아트</summary>
    private void OnVideo()
    {
        var video = _p.PlayingVideo;
        _videoView.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
        if (video) Anim.Appear(_videoView, 0, 0);
        var wide = video || _p.Current?.Thumb?.Contains("ytimg.com") == true;
        _artBox.Height = wide ? 377 : 648;
    }

    private void OnTrack()
    {
        var t = _p.Current;
        // 뮤직비디오(동영상 썸네일)는 16:9, 노래는 정사각형에 가깝게 (pear 와 같은 670×648)
        // 뮤직비디오 · 동영상(16:9 썸네일)은 자르지 않고 16:9 그대로 (pear 와 같게), 노래는 670×648. 둘 다 세로 가운데
        var wide = _p.PlayingVideo || t?.Thumb?.Contains("ytimg.com") == true;
        _artBox.Height = wide ? 377 : 648;
        _artView.Show(t?.Thumb);
        for (var i = 0; i < _rows.Count; i++) _rows[i].SetCurrent(i == _p.Index);
        _relatedFor = null;
        if (_tabs.Selected == 3) LoadRelated();
        if (_tabs.Selected == 2) _comments.Load(t?.VideoId);
        ScrollToCurrent();
    }

    /// <summary>
    /// 다음 트랙 목록 — 고른 곡들, 그 아래 '자동재생 사용 중' + 칩 줄 + 자동재생 곡들 (YT Music 과 같은 배치).
    /// 자동재생을 끄면 아래 구간은 숨긴다
    /// </summary>
    private void BuildQueue()
    {
        _source.Text = _p.QueueTitle ?? (_p.Current != null ? $"{_p.Current.Title} 믹스" : "");
        _autoplay.Set(_p.Autoplay, false);
        _queue.Children.Clear();
        _rows.Clear();
        var q = _p.Queue;
        var autoStart = Math.Min(_p.AutoStart, q.Count);
        void AddRows(int from, int to, Panel into)
        {
            for (var i = from; i < to; i++)
            {
                var idx = i;
                var row = new TrackRow(q[i], divider: i < to - 1 && i != _p.Index && i + 1 != _p.Index);
                row.Click += r => _ = _p.PlayAt(idx);
                row.QueueIndex = idx;
                EnableDrag(row, idx);
                row.SetCurrent(i == _p.Index);
                _rows.Add(row);
                into.Children.Add(row);
            }
        }
        // 검색에서 튼 곡이 아니면: 알고리즘 칩이 맨 위, 목록은 하나로 (자동재생 토글 없음)
        _autoRow.Visibility = _p.ShowAutoplayToggle ? Visibility.Visible : Visibility.Collapsed;
        if (!_p.ShowAutoplayToggle)
        {
            if (_p.AutoChips.Count > 0) _queue.Children.Add(BuildAutoChips());
            var all = new StackPanel { Spacing = 2 };
            AddRows(0, q.Count, all);
            if (q.Count <= 1 && _p.AutoLoading)
            {
                var w = Theme.Text("비슷한 곡을 찾는 중…", 14, FontWeights.SemiBold, Theme.Secondary);
                w.Margin = new Thickness(16, 8, 8, 8);
                all.Children.Add(w);
            }
            // 칩을 바꿔 다시 받는 동안: 지금 곡까지는 그대로, 그 뒤만 흐리게
            for (var k = _p.Index + 1; k < _rows.Count; k++) Anim.Opacity(_rows[k], _p.AutoLoading && q.Count > 1 ? 0.45f : 1f, 220);
            _queue.Children.Add(all);
            return;
        }
        var user = new StackPanel { Spacing = 2 };
        AddRows(0, autoStart, user);
        _queue.Children.Add(user);

        if (!_p.Autoplay) return;
        var section = new StackPanel { Spacing = 2 };
        var label = Theme.Text("자동재생 사용 중", 14, FontWeights.Bold, Theme.Secondary);
        label.Margin = new Thickness(16, 24, 8, 12);
        section.Children.Add(label);
        if (_p.AutoChips.Count > 0) section.Children.Add(BuildAutoChips());
        var autoRows = new StackPanel { Spacing = 2 };
        AddRows(autoStart, q.Count, autoRows);
        if (q.Count == autoStart && _p.AutoLoading)
        {
            var wait = Theme.Text("비슷한 곡을 찾는 중…", 14, FontWeights.SemiBold, Theme.Secondary);
            wait.Margin = new Thickness(16, 8, 8, 8);
            autoRows.Children.Add(wait);
        }
        // 칩을 바꿔 다시 받는 동안은 지금 목록을 흐리게
        Anim.Opacity(autoRows, _p.AutoLoading && q.Count > autoStart ? 0.45f : 1f, 220);
        section.Children.Add(autoRows);
        _queue.Children.Add(section);
    }

    private ScrollViewer? _chipScroll;

    /// <summary>자동재생 칩 줄 — 36px 알약, 고르면 흰 칩 · 검은 글씨, 휠로 옆으로 넘김 (pear 의 OneUiChip)</summary>
    private FrameworkElement BuildAutoChips()
    {
        var offset = _chipScroll?.HorizontalOffset ?? 0;
        var sv = ShelvesView.HorizontalRow(out var row);
        _chipScroll = sv;
        row.Spacing = 8;
        row.Padding = new Thickness(8, 0, 8, 12);
        // 칩 줄 위에서는 Shift 없이도 휠로 옆으로
        sv.PointerWheelChanged += (_, e) =>
        {
            var props = e.GetCurrentPoint(sv).Properties;
            if (props.IsHorizontalMouseWheel || sv.ScrollableWidth <= 0) return;
            sv.ChangeView(sv.HorizontalOffset - props.MouseWheelDelta, null, null);
            e.Handled = true;
        };
        for (var i = 0; i < _p.AutoChips.Count; i++)
        {
            var c = _p.AutoChips[i];
            var idx = i;
            var chip = new Border
            {
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(14, 0, 14, 0),
                Background = Theme.B(c.Selected ? Colors.White : Theme.Chip),
                Child = Theme.Text(c.Text, 14, FontWeights.SemiBold, c.Selected ? Colors.Black : Theme.OnSurface),
            };
            ((TextBlock)chip.Child).VerticalAlignment = VerticalAlignment.Center;
            chip.PointerEntered += (_, _) => { if (!c.Selected) chip.Background = Theme.B(Theme.Selected); };
            chip.PointerExited += (_, _) => { if (!c.Selected) chip.Background = Theme.B(Theme.Chip); };
            Anim.Pressable(chip);
            chip.Tapped += (_, _) => { if (!c.Selected) _ = _p.SelectChip(idx); };
            row.Children.Add(chip);
        }
        sv.Loaded += (_, _) => sv.ChangeView(offset, null, null, true);
        return sv;
    }

    /// <summary>다음 트랙 줄 끌어서 순서 바꾸기 (YT Music 처럼) — 놓을 자리에 흰 줄이 보인다</summary>
    private void EnableDrag(TrackRow row, int idx)
    {
        row.CanDrag = true;
        row.AllowDrop = true;
        var line = new XRect { Height = 2, RadiusX = 1, RadiusY = 1, Fill = Theme.B(Colors.White), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, -1, 12, 0), Opacity = 0, IsHitTestVisible = false };
        row.Children.Add(line);
        row.DragStarting += (_, e) =>
        {
            e.Data.SetText($"queue:{idx}");
            e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            Anim.Opacity(row, 0.4f, Anim.Fast);
        };
        row.DropCompleted += (_, _) => Anim.Opacity(row, 1, Anim.Medium);
        row.DragOver += (_, e) =>
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            e.DragUIOverride.IsCaptionVisible = false;
            line.Opacity = 1;
        };
        row.DragLeave += (_, _) => line.Opacity = 0;
        row.Drop += async (_, e) =>
        {
            line.Opacity = 0;
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) return;
            var text = await e.DataView.GetTextAsync();
            if (text.StartsWith("queue:") && int.TryParse(text[6..], out var from))
                _p.MoveQueue(from, from < idx ? idx - 1 : idx);
        };
    }

    /// <summary>다음 트랙 '저장' — 지금 목록(자동재생 구간 빼고)을 새 재생목록으로 또는 내 재생목록에</summary>
    private async void SaveQueue(FrameworkElement at)
    {
        var ids = _p.Queue.Take(_p.ShowAutoplayToggle ? _p.AutoStart : _p.Queue.Count).Select(t => t.VideoId).Distinct().ToList();
        if (ids.Count == 0) return;
        if (!Auth.IsLoggedIn) { Toast.Show("로그인이 필요해요"); return; }
        List<(string Title, string PlaylistId, bool Contains)> lists;
        try { lists = await InnerTube.Instance.MyPlaylists(ids[0]); }
        catch (Exception e) { Log.Warn($"재생목록 목록 실패: {e.Message}"); lists = new(); }
        var m = new OneMenu();
        m.Header($"재생목록에 저장 · {ids.Count}곡");
        m.Item("새 재생목록", Icons.QueueAdd, async () =>
        {
            var name = await Prompt.Ask(XamlRoot, "새 재생목록", _p.QueueTitle ?? "재생목록 이름");
            if (string.IsNullOrWhiteSpace(name)) return;
            var id = await InnerTube.Instance.CreatePlaylist(name.Trim(), ids);
            Toast.Show(id != null ? $"'{name.Trim()}' 재생목록을 만들었어요" : "만들지 못했어요");
        });
        if (lists.Count > 0) m.Separator();
        foreach (var (title, id, _) in lists.Take(30))
            m.Item(title, Icons.PlaylistAddPath, async () =>
            {
                var ok = await InnerTube.Instance.AddVideosToPlaylist(id, ids);
                Toast.Show(ok ? $"'{title}'에 {ids.Count}곡을 저장했어요" : "저장하지 못했어요");
            });
        m.Show(at);
    }

    /// <summary>관련 항목 — 지금 곡의 '관련' 페이지 셸프들을 패널 안에 세로로</summary>
    private async void LoadRelated()
    {
        var id = _p.RelatedBrowseId;
        if (id == null || id == _relatedFor) return;
        _relatedFor = id;
        _relatedList.Children.Clear();
        var loading = Theme.Text("불러오는 중…", 14, FontWeights.SemiBold, Theme.Secondary);
        loading.Margin = new Thickness(8);
        _relatedList.Children.Add(loading);
        try
        {
            var shelves = await InnerTube.Instance.BrowseShelves(id);
            if (_relatedFor != id) return;
            _relatedList.Children.Clear();
            // pear 관련 항목: 셸프 제목 + ‹ ›, 곡은 4줄 × 2칸씩 넘기는 격자, 나머지는 카드(플레이리스트는 동그라미)
            foreach (var s in shelves.Take(8))
            {
                var head = new Grid { Margin = new Thickness(4, 16, 0, 8) };
                head.Children.Add(Theme.Text(s.Title, 22, FontWeights.ExtraBold, Theme.OnSurface));
                var sv = ShelvesView.HorizontalRow(out var row);
                row.Spacing = 16;
                row.Padding = new Thickness(4, 0, 4, 4);
                if (s.Tracks != null && s.Tracks.Count == s.Items.Count)
                {
                    for (var k = 0; k < s.Tracks.Count; k += 4)
                    {
                        var col = new StackPanel { Width = 196, Spacing = 16 };
                        foreach (var t in s.Tracks.Skip(k).Take(4)) col.Children.Add(ShelvesView.QuickRow(t, 196));
                        row.Children.Add(col);
                    }
                }
                else
                {
                    foreach (var c in s.Items)
                    {
                        var round = c.PageType is "MUSIC_PAGE_TYPE_PLAYLIST" or "MUSIC_PAGE_TYPE_ARTIST" || c.BrowseId?.StartsWith("VL") == true;
                        var card = new CardView(c, 160, square: true, round: round);
                        card.Click += cc => Play.Card(cc, s);
                        row.Children.Add(card);
                    }
                }
                var arrows = ShelvesView.ShelfArrows(sv);
                arrows.Margin = new Thickness(0, 0, 4, 0);
                arrows.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(arrows);
                _relatedList.Children.Add(head);
                _relatedList.Children.Add(sv);
            }
            _relatedList.Children.Add(new Border { Height = 16 });
        }
        catch (Exception e)
        {
            Log.Warn($"관련 항목 실패: {e.Message}");
            loading.Text = "불러오지 못했어요";
        }
    }

    private void ScrollToCurrent()
    {
        if (_p.Index < 0 || _p.Index >= _rows.Count) return;
        var row = _rows[_p.Index];
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var pt = row.TransformToVisual(_queue).TransformPoint(new Point(0, 0));
                _queueScroll.ChangeView(null, Math.Max(0, pt.Y - 60), null, false);
            }
            catch { /* 아직 배치 전 */ }
        });
    }
}
