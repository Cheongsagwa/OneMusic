using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// 재생 화면 '댓글' 탭 (pear 스크린샷 · YouTube 댓글과 같게):
///  머리 '댓글 810개' + 정렬(인기 댓글 · 최신순) / 내 사진 + '댓글 추가...' 입력 (Enter 로 올리기) /
///  40px 동그란 사진 · @이름 · 언제 · 내용 · 좋아요 수 · 싫어요 · 답글 / '답글 N개 ⌄' 를 누르면 답글이 펼쳐진다(왼쪽 이음선).
///  끝까지 내리면 다음 묶음을 이어 받는다
/// </summary>
public sealed class CommentsView : Grid
{
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    private readonly StackPanel _list = new() { Spacing = 20, Padding = new Thickness(4, 8, 8, 24) };
    private readonly TextBlock _header = Theme.Text("댓글", 22, FontWeights.ExtraBold, Theme.OnSurface);
    private readonly IconButton _sort = new(36);
    private readonly Grid _compose = new() { Margin = new Thickness(4, 4, 8, 4), Visibility = Visibility.Collapsed, ColumnSpacing = 12 };
    private readonly TextBox _input = new() { FontFamily = Theme.Font, FontSize = 14, BorderThickness = new Thickness(0, 0, 0, 1), Background = new SolidColorBrush(Colors.Transparent), AcceptsReturn = false };
    private readonly Border _myAvatar = new() { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Theme.B(Theme.Chip), VerticalAlignment = VerticalAlignment.Center };
    private string? _for, _next, _createParams;
    private List<(string Title, string Token, bool Selected)> _sorts = new();
    private bool _loading;
    private int _gen;

    public CommentsView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(4, 16, 8, 8) };
        _header.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(_header);
        _sort.Content = Theme.Icon(Icons.SortLines, 22, Colors.White, stroke: true, strokeWidth: 1.8);
        _sort.Visibility = Visibility.Collapsed;
        _sort.VerticalAlignment = VerticalAlignment.Center;
        Tip.Set(_sort, "정렬");
        _sort.Click += ShowSort;
        head.Children.Add(_sort);
        Children.Add(head);

        // 댓글 추가 — 내 사진 + 밑줄 입력칸
        _compose.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _compose.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _compose.Children.Add(_myAvatar);
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
            _input.Resources[key] = new SolidColorBrush(Colors.Transparent);
        _input.Resources["TextControlBorderBrush"] = Theme.B(Theme.Divider);
        _input.Resources["TextControlBorderBrushPointerOver"] = Theme.B(Theme.Secondary);
        _input.Resources["TextControlBorderBrushFocused"] = Theme.B(Colors.White);
        _input.KeyDown += async (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            await Send();
        };
        SetColumn(_input, 1);
        _compose.Children.Add(_input);
        SetRow(_compose, 1);
        Children.Add(_compose);

        _scroll.Content = _list;
        ListFade.Attach(_scroll);
        SetRow(_scroll, 2);
        Children.Add(_scroll);
        _scroll.ViewChanged += (_, _) =>
        {
            if (_scroll.VerticalOffset > _scroll.ScrollableHeight - 400) More();
        };
    }

    /// <summary>탭을 열었을 때 · 곡이 바뀌었을 때 (같은 곡이면 그대로)</summary>
    public void Load(string? videoId)
    {
        if (videoId == null || videoId == _for) return;
        _for = videoId;
        Reload(null);
    }

    private async void Reload(string? token)
    {
        if (_for is not { } videoId) return;
        var gen = ++_gen;
        _next = null;
        _list.Children.Clear();
        var status = Theme.Text("불러오는 중…", 14, FontWeights.SemiBold, Theme.Secondary);
        _list.Children.Add(status);
        try
        {
            var page = await InnerTube.Instance.Comments(videoId, token);
            if (gen != _gen) return;
            _list.Children.Clear();
            if (token == null || page.Count != null) _header.Text = page.Count != null ? $"댓글 {page.Count}" : "댓글";
            if (page.Sorts.Count > 0) _sorts = page.Sorts;
            _sort.Visibility = _sorts.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (page.CreateParams != null)
            {
                _createParams = page.CreateParams;
                _compose.Visibility = Visibility.Visible;
                _input.PlaceholderText = page.Placeholder ?? "댓글 추가...";
                if (page.MyAvatar != null) _myAvatar.Child = new Image { Source = Theme.Image(page.MyAvatar, 28), Stretch = Stretch.UniformToFill };
            }
            if (page.Items.Count == 0)
            {
                status.Text = "댓글이 없거나 사용 중지된 곡이에요";
                _list.Children.Add(status);
                return;
            }
            Add(page, _list);
            _scroll.ChangeView(null, 0, null, true);
        }
        catch (Exception e)
        {
            Log.Warn($"댓글 실패: {e.Message}");
            if (gen == _gen) { status.Text = "댓글을 불러오지 못했어요"; _for = token == null ? null : _for; }
        }
    }

    /// <summary>정렬 메뉴 (인기 댓글 · 최신순)</summary>
    private void ShowSort()
    {
        var m = new OneMenu();
        foreach (var (title, token, selected) in _sorts)
            m.Item(selected ? $"{title}  ✓" : title, Icons.SortLines, () =>
            {
                _sorts = _sorts.Select(x => (x.Title, x.Token, x.Token == token)).ToList();
                Reload(token);
            });
        m.Show(_sort);
    }

    private async Task Send()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0 || _createParams == null) return;
        if (!Auth.IsLoggedIn) { Toast.Show("로그인이 필요해요"); return; }
        _input.IsEnabled = false;
        try
        {
            var ok = await InnerTube.Instance.PostComment(text, _createParams);
            if (ok)
            {
                _input.Text = "";
                var mine = Row(new Comment("나", null, text, "방금 전", "", ""), reply: false);
                _list.Children.Insert(0, mine);
                Anim.Appear(mine);
                Toast.Show("댓글을 올렸어요");
            }
            else Toast.Show("댓글을 올리지 못했어요");
        }
        catch (Exception e)
        {
            Log.Warn($"댓글 쓰기 실패: {e.Message}");
            Toast.Show("댓글을 올리지 못했어요");
        }
        finally { _input.IsEnabled = true; }
    }

    private async void More()
    {
        if (_next == null || _loading || _for == null) return;
        _loading = true;
        var gen = _gen;
        try
        {
            var page = await InnerTube.Instance.Comments(_for, _next);
            if (gen == _gen) Add(page, _list);
        }
        catch (Exception e) { Log.Warn($"댓글 이어 받기 실패: {e.Message}"); _next = null; }
        finally { _loading = false; }
    }

    private void Add(CommentPage page, Panel into)
    {
        _next = page.Next;
        var delay = 0;
        foreach (var c in page.Items)
        {
            var row = Row(c, reply: false);
            into.Children.Add(row);
            Anim.Appear(row, delay);
            delay = Math.Min(delay + 30, 240);
        }
    }

    private FrameworkElement Row(Comment c, bool reply)
    {
        var size = reply ? 28 : 40;
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(size) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var av = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2.0), Background = Theme.B(Theme.Chip), VerticalAlignment = VerticalAlignment.Top };
        if (c.Avatar != null) av.Child = new Image { Source = Theme.Image(c.Avatar, size), Stretch = Stretch.UniformToFill };
        g.Children.Add(av);
        var body = new StackPanel { Spacing = 4 };
        var who = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        who.Children.Add(Theme.Text(c.Author, 13, FontWeights.SemiBold, Theme.OnSurface));
        who.Children.Add(Theme.Text(c.Time, 13, FontWeights.Normal, Theme.Secondary));
        body.Children.Add(who);
        var text = Theme.Text(c.Text, 15, FontWeights.Normal, Theme.OnSurface, 40);
        text.TextWrapping = TextWrapping.Wrap;
        text.IsTextSelectionEnabled = true;
        body.Children.Add(text);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        bar.Children.Add(Theme.Icon(Icons.ThumbUp, 18, Theme.Secondary, stroke: true));
        if (c.Likes.Length > 0) bar.Children.Add(Theme.Text(c.Likes, 13, FontWeights.Normal, Theme.Secondary));
        var down = Theme.Icon(Icons.ThumbDown, 18, Theme.Secondary, stroke: true);
        down.Margin = new Thickness(8, 0, 0, 0);
        bar.Children.Add(down);
        body.Children.Add(bar);

        // 답글 N개 ⌄ — 누르면 펼쳐지고, 다시 누르면 접힌다
        if (!reply && c.RepliesToken != null && c.Replies is { Length: > 0 } r && r != "0")
        {
            var replies = new StackPanel { Spacing = 14, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
            var toggle = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0), Background = new SolidColorBrush(Colors.Transparent) };
            var label = Theme.Text($"답글 {r}개", 14, FontWeights.Bold, Theme.OnSurface);
            label.VerticalAlignment = VerticalAlignment.Center;
            var chevron = Theme.Icon(Icons.ChevronDown, 18, Colors.White, stroke: true, strokeWidth: 2);
            chevron.RenderTransformOrigin = new Point(0.5, 0.5);
            chevron.RenderTransform = new RotateTransform();
            toggle.Children.Add(label);
            toggle.Children.Add(chevron);
            var loaded = false;
            toggle.Tapped += async (_, _) =>
            {
                var open = replies.Visibility != Visibility.Visible;
                replies.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                var rot = (RotateTransform)chevron.RenderTransform;
                Tween.Run(rot, Anim.Medium, Tween.Out, k => rot.Angle = open ? 180 * k : 180 * (1 - k));
                if (!open || loaded || _for == null) return;
                loaded = true;
                var wait = Theme.Text("불러오는 중…", 13, FontWeights.SemiBold, Theme.Secondary);
                replies.Children.Add(wait);
                try
                {
                    var page = await InnerTube.Instance.Comments(_for, c.RepliesToken, replies: true);
                    replies.Children.Clear();
                    var delay = 0;
                    foreach (var rc in page.Items)
                    {
                        var rr = Row(rc, reply: true);
                        replies.Children.Add(rr);
                        Anim.Appear(rr, delay, 8);
                        delay = Math.Min(delay + 30, 200);
                    }
                }
                catch (Exception e)
                {
                    Log.Warn($"답글 실패: {e.Message}");
                    wait.Text = "답글을 불러오지 못했어요";
                    loaded = false;
                }
            };
            Anim.Pressable(toggle, 0.96f);
            body.Children.Add(toggle);
            // 답글은 왼쪽에 이음선을 두고 들여 쓴다
            var thread = new Grid { ColumnSpacing = 10 };
            thread.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
            thread.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            thread.Children.Add(new XRect { Width = 2, RadiusX = 1, RadiusY = 1, Fill = Theme.B(Theme.Divider) });
            SetColumn(replies, 1);
            thread.Children.Add(replies);
            replies.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => thread.Visibility = replies.Visibility);
            thread.Visibility = Visibility.Collapsed;
            body.Children.Add(thread);
        }
        SetColumn(body, 1);
        g.Children.Add(body);
        return g;
    }
}
