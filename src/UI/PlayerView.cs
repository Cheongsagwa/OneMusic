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
    private readonly Border _art = new() { CornerRadius = new CornerRadius(Theme.RadiusCard), Background = Theme.B(Color.FromArgb(20, 255, 255, 255)) };
    private readonly Image _artImg = new() { Stretch = Stretch.UniformToFill };
    private readonly Image _artOld = new() { Stretch = Stretch.UniformToFill };
    private readonly Grid _artBox = new() { Width = 670, Height = 648 };
    private readonly PillTabs _tabs = new("다음 트랙", "가사", "댓글", "관련 항목");
    private readonly StackPanel _queue = new();
    private readonly ScrollViewer _queueScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    private readonly Grid _queueTab = new();
    private readonly TextBlock _source = Theme.Text("", 14, FontWeights.Bold, Theme.OnSurface);
    private readonly OneToggle _autoplay;
    private Grid _autoRow = null!;
    private readonly LyricsView _lyrics = new();
    private readonly Grid _comments = new();
    private readonly ScrollViewer _related = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    private readonly StackPanel _relatedList = new() { Spacing = 8 };
    private string? _relatedFor;
    private readonly List<TrackRow> _rows = new();
    private readonly Grid _panel;
    public bool IsOpen { get; private set; }
    public event Action? CloseRequested;

    public PlayerView()
    {
        Background = new SolidColorBrush(Colors.Transparent);
        Padding = new Thickness(56, 54, 56, Theme.BarHeight + Theme.FloatGap * 2 + 28);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(450) });
        ColumnSpacing = 56;

        // ── 왼쪽: 노래/동영상 + 앨범아트 ──
        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var av = new AvToggle { Margin = new Thickness(0, 0, 0, 24) };
        left.Children.Add(av);
        var artGrid = new Grid();
        artGrid.Children.Add(_artOld);
        artGrid.Children.Add(_artImg);
        _art.Child = artGrid;
        _artBox.Children.Add(_art);
        var artHolder = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, VerticalAlignment = VerticalAlignment.Top, Child = _artBox };
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
        var headText = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
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
        ToolTipService.SetToolTip(save, "재생목록으로 저장은 준비 중이에요");
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
        Grid.SetRow(_queueScroll, 2);
        _queueTab.Children.Add(_queueScroll);
        content.Children.Add(_queueTab);

        content.Children.Add(_lyrics);
        var cm = Theme.Text("댓글은 준비 중이에요", 15, FontWeights.SemiBold, Theme.Secondary);
        cm.HorizontalAlignment = HorizontalAlignment.Center;
        cm.VerticalAlignment = VerticalAlignment.Center;
        _comments.Children.Add(cm);
        content.Children.Add(_comments);
        _related.Content = _relatedList;
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
        KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) CloseRequested?.Invoke(); };
    }

    private void ShowTab(int i)
    {
        var views = new FrameworkElement[] { _queueTab, _lyrics, _comments, _related };
        for (var k = 0; k < views.Length; k++) views[k].Visibility = k == i ? Visibility.Visible : Visibility.Collapsed;
        _lyrics.Active = i == 1 && IsOpen;
        Anim.Appear(views[i], 0, 10);
        if (i == 3) LoadRelated();
    }

    public void ShowLyricsTab() => _tabs.Select(1);

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        Visibility = Visibility.Visible;
        var h = (float)Math.Max(300, ActualHeight * 0.35);
        Anim.V(this).Opacity = 0;
        Anim.SetTranslation(this, new Vector3(0, h, 0));
        Anim.Translate(this, Vector3.Zero, 0.82f, 70);
        Anim.Opacity(this, 1, 300);
        // 패널은 살짝 늦게 떠오른다 (pear: 60ms 지연)
        Anim.Appear(_panel, 60, 40);
        _lyrics.Active = _tabs.Selected == 1;
        ScrollToCurrent();
    }

    public async void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _lyrics.Active = false;
        Anim.Translate(this, new Vector3(0, (float)Math.Max(300, ActualHeight * 0.35), 0), 1f, 60);
        Anim.Opacity(this, 0, 240);
        await Task.Delay(320);
        if (!IsOpen) Visibility = Visibility.Collapsed;
    }

    private void OnTrack()
    {
        var t = _p.Current;
        // 뮤직비디오(동영상 썸네일)는 16:9, 노래는 정사각형에 가깝게 (pear 와 같은 670×648)
        var wide = t?.Thumb?.Contains("ytimg.com") == true;
        _artBox.Height = wide ? 377 : 648;
        _artOld.Source = _artImg.Source;
        var bmp = Theme.Image(t?.Thumb, 670);
        Anim.V(_artImg).Opacity = 0;
        bmp.ImageOpened += (_, _) =>
        {
            Anim.V(_artImg).CenterPoint = new Vector3(335, (float)_artBox.Height / 2, 0);
            Anim.V(_artImg).Scale = new Vector3(1.04f, 1.04f, 1);
            Anim.Scale(_artImg, 1f, 0.9f, 110);
            Anim.Opacity(_artImg, 1, 700);
        };
        _artImg.Source = bmp;
        for (var i = 0; i < _rows.Count; i++) _rows[i].SetCurrent(i == _p.Index);
        _relatedFor = null;
        if (_tabs.Selected == 3) LoadRelated();
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
            foreach (var s in shelves.Take(6))
            {
                var title = Theme.Text(s.Title, 18, FontWeights.ExtraBold, Theme.OnSurface);
                title.Margin = new Thickness(8, 12, 8, 4);
                _relatedList.Children.Add(title);
                var items = s.Tracks ?? s.Items.Select(c => new Track(c.VideoId ?? "", c.Title, InnerTube.StripType(c.Subtitle), null, c.Thumb, 0)).ToList();
                foreach (var (t, i) in items.Take(8).Select((t, i) => (t, i)))
                {
                    var card = s.Items.ElementAtOrDefault(i);
                    var row = new TrackRow(t, divider: false);
                    row.Click += r =>
                    {
                        if (s.Tracks != null) _ = _p.PlaySong(r.Track);
                        else if (card != null) Play.Card(card, s);
                    };
                    _relatedList.Children.Add(row);
                }
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
