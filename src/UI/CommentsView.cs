using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// 재생 화면 '댓글' 탭 (pear 와 같은 배치) — '댓글 810개' / 40px 동그란 사진 · @이름 · 언제 / 내용 / 좋아요 수 · 답글 수.
/// 끝까지 내리면 다음 묶음을 이어 받는다
/// </summary>
public sealed class CommentsView : Grid
{
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    private readonly StackPanel _list = new() { Spacing = 20, Padding = new Thickness(4, 8, 8, 24) };
    private readonly TextBlock _header = Theme.Text("댓글", 22, FontWeights.ExtraBold, Theme.OnSurface);
    private string? _for, _next;
    private bool _loading;
    private int _gen;

    public CommentsView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _header.Margin = new Thickness(4, 16, 8, 8);
        Children.Add(_header);
        _scroll.Content = _list;
        ListFade.Attach(_scroll);
        SetRow(_scroll, 1);
        Children.Add(_scroll);
        _scroll.ViewChanged += (_, _) =>
        {
            if (_scroll.VerticalOffset > _scroll.ScrollableHeight - 400) More();
        };
    }

    /// <summary>탭을 열었을 때 · 곡이 바뀌었을 때 (같은 곡이면 그대로)</summary>
    public async void Load(string? videoId)
    {
        if (videoId == null || videoId == _for) return;
        _for = videoId;
        var gen = ++_gen;
        _next = null;
        _list.Children.Clear();
        _header.Text = "댓글";
        var status = Theme.Text("불러오는 중…", 14, FontWeights.SemiBold, Theme.Secondary);
        _list.Children.Add(status);
        try
        {
            var page = await InnerTube.Instance.Comments(videoId);
            if (gen != _gen) return;
            _list.Children.Clear();
            _header.Text = page.Count != null ? $"댓글 {page.Count}" : "댓글";
            if (page.Items.Count == 0)
            {
                status.Text = "댓글이 없거나 사용 중지된 곡이에요";
                _list.Children.Add(status);
                return;
            }
            Add(page);
            _scroll.ChangeView(null, 0, null, true);
        }
        catch (Exception e)
        {
            Log.Warn($"댓글 실패: {e.Message}");
            if (gen == _gen) { status.Text = "댓글을 불러오지 못했어요"; _for = null; }
        }
    }

    private async void More()
    {
        if (_next == null || _loading || _for == null) return;
        _loading = true;
        var gen = _gen;
        try
        {
            var page = await InnerTube.Instance.Comments(_for, _next);
            if (gen == _gen) Add(page);
        }
        catch (Exception e) { Log.Warn($"댓글 이어 받기 실패: {e.Message}"); _next = null; }
        finally { _loading = false; }
    }

    private void Add(CommentPage page)
    {
        _next = page.Next;
        var delay = 0;
        foreach (var c in page.Items)
        {
            var row = Row(c);
            _list.Children.Add(row);
            Anim.Appear(row, delay);
            delay = Math.Min(delay + 30, 240);
        }
    }

    private static FrameworkElement Row(Comment c)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var av = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = Theme.B(Theme.Chip), VerticalAlignment = VerticalAlignment.Top };
        if (c.Avatar != null) av.Child = new Image { Source = Theme.Image(c.Avatar, 40), Stretch = Stretch.UniformToFill };
        g.Children.Add(av);
        var body = new StackPanel { Spacing = 4 };
        var who = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        who.Children.Add(Theme.Text(c.Author, 13, FontWeights.SemiBold, Theme.OnSurface));
        who.Children.Add(Theme.Text(c.Time, 13, FontWeights.Normal, Theme.Secondary));
        body.Children.Add(who);
        var text = Theme.Text(c.Text, 15, FontWeights.Normal, Theme.OnSurface, 12);
        text.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(text);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        bar.Children.Add(Theme.Icon(Icons.ThumbUp, 18, Theme.Secondary, stroke: true));
        if (c.Likes.Length > 0) bar.Children.Add(Theme.Text(c.Likes, 13, FontWeights.Normal, Theme.Secondary));
        var down = Theme.Icon(Icons.ThumbUp, 18, Theme.Secondary, stroke: true);
        down.RenderTransformOrigin = new Point(0.5, 0.5);
        down.RenderTransform = new ScaleTransform { ScaleY = -1 };
        down.Margin = new Thickness(8, 0, 0, 0);
        bar.Children.Add(down);
        if (c.Replies is { Length: > 0 } r && r != "0")
        {
            var rep = Theme.Text($"답글 {r}개", 13, FontWeights.Bold, Theme.OnSurface);
            rep.Margin = new Thickness(12, 0, 0, 0);
            bar.Children.Add(rep);
        }
        body.Children.Add(bar);
        Grid.SetColumn(body, 1);
        g.Children.Add(body);
        return g;
    }
}
