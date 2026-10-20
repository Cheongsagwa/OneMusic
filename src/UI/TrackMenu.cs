using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// ⋮ 메뉴 (곡 · 카드 · 재생 목록 줄 · 재생 바). pear 와 같은 구성:
///  맨 위 좋아요 | 싫어요 (pear player-extras) → 다운로드 (downloader 플러그인) →
///  YT Music 이 그 곡에 주는 메뉴 항목을 순서 · 글자 그대로 (믹스 시작 · 다음에 재생 · 현재 재생목록에 추가 ·
///  보관함에 추가/삭제 · 좋아요 표시한 노래에 추가 · 오프라인 저장 · 재생목록에 저장 · 현재 재생목록에서 삭제 ·
///  앨범으로 이동 · 아티스트 페이지로 이동 · 공유 · 신고 · 다시 듣기에 고정 …) → 재생 바면 현재 재생목록 닫기 · 전문 통계.
/// 각 항목은 YT 가 준 동작(엔드포인트)을 그대로 실행한다.
/// </summary>
public static class TrackMenu
{
    /// <param name="queueIndex">다음 트랙 목록 안의 자리 ('현재 재생목록에서 삭제')</param>
    /// <param name="pos">우클릭이면 누른 자리 (창 기준)</param>
    /// <param name="fromBar">재생 바 ⋮ — '현재 재생목록 닫기' · '전문 통계' 가 붙는다</param>
    public static async void Show(FrameworkElement at, Track t, int? queueIndex = null, Point? pos = null, bool fromBar = false)
    {
        try
        {
            var items = t.Menu;
            if (items == null && Auth.IsLoggedIn)
            {
                try { items = await InnerTube.Instance.TrackMenu(t.VideoId); }
                catch (Exception e) { Log.Warn($"곡 메뉴 받기 실패: {e.Message}"); }
            }
            var m = new OneMenu();
            LikeRow(m, at, t);
            m.Separator();
            m.Item("다운로드", Icons.Download, () => DownloadAll(new[] { t }));
            var ctx = new Ctx(at, pos, t, null, queueIndex);
            if (items != null) AddYt(m, items, ctx);
            else AddFallback(m, ctx);
            if (queueIndex is { } qi && qi != PlayerService.Instance.Index && items?.Any(x => IsRemoveFromQueue(x)) != true)
                m.Item("현재 재생목록에서 삭제", Icons.MinusCircle, () => PlayerService.Instance.RemoveFromQueue(qi));
            if (fromBar)
            {
                m.Item("현재 재생목록 닫기", Icons.QueueClose, () => PlayerService.Instance.CloseQueue());
                m.Item("전문 통계", Icons.Stats, () => ShowStats(at, t), keepOpen: true);
            }
            m.Show(at, pos);
        }
        catch (Exception e)
        {
            Log.Error("메뉴 만들기 실패", e);
            Toast.Show("메뉴를 열지 못했어요: " + e.Message);
        }
    }

    /// <summary>카드 우클릭 · ⋮ — 곡이면 곡 메뉴, 앨범 · 플레이리스트 · 아티스트면 YT 카드 메뉴(셔플 재생 · 믹스 시작 · 보관함에 저장 …)</summary>
    public static void ShowCard(FrameworkElement at, Card c, Point pos)
    {
        if (c.VideoId != null && c.PlaylistId == null)
        {
            var artist = InnerTube.StripType(c.Subtitle).Split(" • ")[0];
            Show(at, new Track(c.VideoId, c.Title, artist, null, c.Thumb, 0) { Menu = c.Menu }, null, pos);
            return;
        }
        var m = new OneMenu();
        var ctx = new Ctx(at, pos, null, c, null);
        m.Item("재생", Icons.Play, () => Play.Card(c with { PageType = "play" }));
        if (c.Menu != null) AddYt(m, c.Menu, ctx);
        else
        {
            if (c.BrowseId != null) m.Item("열기", Icons.Album, () => App.MainWin?.OpenBrowse(c.BrowseId));
            if (LinkOf(ctx) != null) m.Item("공유", Icons.Share, () => Share(ctx));
        }
        m.Show(at, pos);
    }

    // ───────────────────────── 만들기 ─────────────────────────

    private sealed record Ctx(FrameworkElement At, Point? Pos, Track? Track, Card? Card, int? QueueIndex);

    /// <summary>맨 위 좋아요 · 싫어요 (이미 눌렀으면 채운 아이콘 + '취소'). 상태를 모르면 받아 와서 바꿔 그린다</summary>
    private static void LikeRow(OneMenu m, FrameworkElement at, Track t)
    {
        void Pair(string? st) => m.Pair(
            (st == "LIKE" ? "좋아요 취소" : "좋아요", Icons.ThumbUp, st == "LIKE", () => Rate(t, st == "LIKE" ? null : true)),
            (st == "DISLIKE" ? "싫어요 취소" : "싫어요", Icons.ThumbDown, st == "DISLIKE", () => Rate(t, st == "DISLIKE" ? null : false)));
        string? known;
        lock (InnerTube.Likes) known = InnerTube.Likes.GetValueOrDefault(t.VideoId);
        Pair(known);
        if (known == null && Auth.IsLoggedIn)
            _ = InnerTube.Instance.LikeStatus(t.VideoId).ContinueWith(task =>
            {
                if (task.Result is { } st) at.DispatcherQueue.TryEnqueue(() => Pair(st));
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    private static void AddYt(OneMenu m, List<YtMenuItem> items, Ctx ctx)
    {
        foreach (var it in items)
        {
            if (it.Download)
            {
                if (ctx.Track is not { } t) continue;
                var off = Offline.Instance;
                if (off.Has(t.VideoId)) m.Item("오프라인 저장 삭제", Icons.Download, () => { off.Remove(t.VideoId); Toast.Show("오프라인 저장에서 뺐어요"); });
                else m.Item(it.Text, Icons.Download, () => SaveOffline(new[] { t }));
                continue;
            }
            var toggled = it.Toggle && it.Toggled;
            var text = toggled && !string.IsNullOrEmpty(it.ToggledText) ? it.ToggledText! : it.Text;
            var icon = IconOf(toggled ? it.ToggledIcon ?? it.Icon : it.Icon, text);
            var ep = toggled ? it.ToggledEndpoint ?? it.Endpoint : it.Endpoint;
            var keep = ep != null && ep.Contains("addToPlaylistEndpoint");
            m.Item(text, icon, () => Run(ep, text, ctx), keepOpen: keep);
        }
    }

    /// <summary>YT 메뉴를 못 받았을 때 (로그인 전 · 오프라인) — YT 곡 메뉴와 같은 순서로 직접</summary>
    private static void AddFallback(OneMenu m, Ctx ctx)
    {
        if (ctx.Track is not { } t) return;
        var p = PlayerService.Instance;
        m.Item("믹스 시작", Icons.Radio, () => _ = p.PlaySong(t));
        m.Item("다음에 재생", Icons.PlayNextIcon, () => { p.PlayNext(t); Toast.Show("다음에 재생할 곡에 추가했어요"); });
        m.Item("현재 재생목록에 추가", Icons.QueueAdd, () => { p.AddToQueue(t); Toast.Show("현재 재생목록에 추가했어요"); });
        m.Item("좋아요 표시한 노래에 추가", Icons.ThumbUp, () => Rate(t, true));
        var off = Offline.Instance;
        if (off.Has(t.VideoId)) m.Item("오프라인 저장 삭제", Icons.Download, () => { off.Remove(t.VideoId); Toast.Show("오프라인 저장에서 뺐어요"); });
        else m.Item("오프라인 저장", Icons.Download, () => SaveOffline(new[] { t }));
        m.Item("재생목록에 저장", Icons.PlaylistAddPath, () => SaveToPlaylist(ctx, t.VideoId, null), keepOpen: true);
        if (t.AlbumId != null) m.Item("앨범으로 이동", Icons.Album, () => App.MainWin?.OpenBrowse(t.AlbumId));
        if (t.ArtistId != null) m.Item("아티스트 페이지로 이동", Icons.Person, () => App.MainWin?.OpenBrowse(t.ArtistId));
        m.Item("공유", Icons.Share, () => Share(ctx));
    }

    private static bool IsRemoveFromQueue(YtMenuItem x) =>
        x.Endpoint?.Contains("removeFromQueueEndpoint") == true || x.Text == "현재 재생목록에서 삭제";

    /// <summary>YT 아이콘 이름 → 우리 아이콘 (pear 처럼 같은 선 굵기 · 둥근 끝)</summary>
    private static string IconOf(string? type, string text) => type switch
    {
        "MIX" or "RADIO" => Icons.Radio,
        "QUEUE_PLAY_NEXT" => Icons.PlayNextIcon,
        "ADD_TO_REMOTE_QUEUE" or "QUEUE_MUSIC" => Icons.QueueAdd,
        "LIBRARY_ADD" or "LIBRARY_MUSIC" => Icons.LibraryAdd,
        "LIBRARY_SAVED" or "LIBRARY_REMOVE" or "BOOKMARK" => Icons.LibrarySaved,
        "FAVORITE" or "LIKE" or "THUMB_UP" => Icons.ThumbUp,
        "UNFAVORITE" => Icons.ThumbUp,
        "OFFLINE_DOWNLOAD" or "DOWNLOAD" => Icons.Download,
        "ADD_TO_PLAYLIST" or "PLAYLIST_ADD" => Icons.PlaylistAddPath,
        "REMOVE" or "DELETE" or "REMOVE_FROM_QUEUE" => Icons.MinusCircle,
        "ALBUM" => Icons.Album,
        "ARTIST" or "PERSON" or "ACCOUNT_BOX" => Icons.Person,
        "SHARE" => Icons.Share,
        "FLAG" => Icons.Flag,
        "KEEP" or "KEEP_OFF" or "PIN" => Icons.Pin,
        "MUSIC_SHUFFLE" or "SHUFFLE" => Icons.Shuffle,
        "DISMISS_QUEUE" => Icons.QueueClose,
        _ => text.Contains("공유") ? Icons.Share : text.Contains("신고") ? Icons.Flag : text.Contains("고정") ? Icons.Pin
            : text.Contains("삭제") ? Icons.MinusCircle : text.Contains("재생목록") ? Icons.PlaylistAddPath : Icons.QueueAdd,
    };

    // ───────────────────────── 실행 ─────────────────────────

    /// <summary>YT 가 준 동작을 실행한다. 모르는 동작이면 안쪽에서 아는 동작을 찾아 본다 (확인 대화상자 등)</summary>
    private static async void Run(string? endpoint, string text, Ctx ctx)
    {
        try
        {
            if (endpoint == null) { Toast.Show("이 항목은 지원하지 않아요"); return; }
            var ep = JsonNode.Parse(endpoint);
            if (await RunNode(ep, text, ctx)) return;
            Log.Warn($"메뉴 동작 모름: {text} {endpoint[..Math.Min(200, endpoint.Length)]}");
            Toast.Show($"'{text}'은(는) 아직 지원하지 않아요");
        }
        catch (Exception e)
        {
            Log.Error($"메뉴 동작 실패: {text}", e);
            Toast.Show("하지 못했어요: " + e.Message);
        }
    }

    private static readonly string[] Known =
    [
        "watchEndpoint", "watchPlaylistEndpoint", "queueAddEndpoint", "browseEndpoint", "shareEntityEndpoint",
        "addToPlaylistEndpoint", "feedbackEndpoint", "likeEndpoint", "playlistEditEndpoint", "removeFromQueueEndpoint",
        "dismissQueueCommand", "offlineVideoEndpoint", "getReportFormEndpoint", "flagEndpoint", "urlEndpoint",
    ];

    private static async Task<bool> RunNode(JsonNode? ep, string text, Ctx ctx)
    {
        if (ep is not JsonObject o) return false;
        var p = PlayerService.Instance;
        var api = InnerTube.Instance;
        // 맨 위 키가 아는 동작이 아니면(예: openPopupAction → 확인 대화상자) 안쪽에서 찾는다
        var key = Known.FirstOrDefault(o.ContainsKey);
        JsonNode? node = key != null ? o[key] : null;
        if (key == null)
            foreach (var k in Known)
                if (o.FindFirst(k) is { } inner) { key = k; node = inner; break; }
        if (key == null || node == null) return false;

        switch (key)
        {
            case "watchEndpoint":
            {
                var vid = node.P("videoId").Str();
                var pl = node.P("playlistId").Str();
                var prm = node.P("params").Str();
                if (pl != null) await p.PlayRadio(vid, pl, prm, ctx.Card?.Title ?? ctx.Track?.Title);
                else if (ctx.Track is { } t && vid == t.VideoId) await p.PlaySong(t);
                else if (vid != null) await p.PlaySong(new Track(vid, ctx.Card?.Title ?? "", "", null, ctx.Card?.Thumb, 0));
                return true;
            }
            case "watchPlaylistEndpoint":
                await p.PlayRadio(null, node.P("playlistId").Str(), node.P("params").Str(), ctx.Card?.Title);
                return true;
            case "queueAddEndpoint":
            {
                var target = node.P("queueTarget");
                var next = node.P("queueInsertPosition").Str() == "INSERT_AFTER_CURRENT_VIDEO";
                List<Track> tracks;
                if (target.P("videoId").Str() is { } vid)
                    tracks = ctx.Track is { } t && t.VideoId == vid ? [t] : [new Track(vid, ctx.Card?.Title ?? "", InnerTube.StripType(ctx.Card?.Subtitle ?? ""), null, ctx.Card?.Thumb, 0)];
                else if (target.P("playlistId").Str() is { } pl)
                    tracks = (await api.GetQueue(null, pl)).Tracks;
                else return false;
                if (next) p.PlayNext(tracks); else p.AddToQueue(tracks);
                Toast.Show(next ? "다음에 재생할 곡에 추가했어요" : "현재 재생목록에 추가했어요");
                return true;
            }
            case "browseEndpoint":
                if (node.P("browseId").Str() is { } bid) App.MainWin?.OpenBrowse(bid, node.P("params").Str());
                return true;
            case "shareEntityEndpoint":
                Share(ctx);
                return true;
            case "addToPlaylistEndpoint":
                SaveToPlaylist(ctx, node.P("videoId").Str(), node.P("playlistId").Str());
                return true;
            case "feedbackEndpoint":
            {
                var ok = node.P("feedbackToken").Str() is { } tok && await api.Feedback(tok);
                Toast.Show(ok ? Done(text) : "로그인이 필요해요");
                return true;
            }
            case "likeEndpoint":
            {
                var ok = await api.RateTarget(node.P("target") ?? new JsonObject(), node.P("status").Str() ?? "INDIFFERENT");
                Toast.Show(ok ? Done(text) : "로그인이 필요해요");
                return true;
            }
            case "playlistEditEndpoint":
            {
                var ok = await api.EditPlaylist(node);
                Toast.Show(ok ? Done(text) : "하지 못했어요");
                return true;
            }
            case "removeFromQueueEndpoint":
            {
                var idx = ctx.QueueIndex ?? (ctx.Track is { } t ? p.Queue.FindIndex(x => x.VideoId == t.VideoId) : -1);
                p.RemoveFromQueue(idx);
                return true;
            }
            case "dismissQueueCommand":
                p.CloseQueue();
                return true;
            case "offlineVideoEndpoint":
                if (ctx.Track is { } ot) SaveOffline(new[] { ot });
                return true;
            case "getReportFormEndpoint":
            case "flagEndpoint":
            {
                // 신고 양식은 YouTube 화면에서만 열 수 있어서 그 곡 페이지를 연다
                var id = ctx.Track?.VideoId ?? ctx.Card?.VideoId;
                var url = id != null ? $"https://www.youtube.com/watch?v={id}" : LinkOf(ctx) ?? "https://www.youtube.com/";
                await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                Toast.Show("YouTube 에서 신고할 수 있게 열었어요");
                return true;
            }
            case "urlEndpoint":
                if (node.P("url").Str() is { } u) await Windows.System.Launcher.LaunchUriAsync(new Uri(u.StartsWith("/") ? "https://music.youtube.com" + u : u));
                return true;
        }
        return false;
    }

    /// <summary>'보관함에 추가' → '보관함에 추가했어요'</summary>
    private static string Done(string text)
    {
        foreach (var verb in new[] { "추가", "삭제", "고정", "해제", "저장", "취소" })
            if (text.EndsWith(verb)) return text + "했어요";
        return "완료했어요";
    }

    private static string? LinkOf(Ctx ctx)
    {
        if (ctx.Track is { } t) return $"https://music.youtube.com/watch?v={t.VideoId}";
        if (ctx.Card is not { } c) return null;
        if (c.PlaylistId != null) return $"https://music.youtube.com/playlist?list={c.PlaylistId}";
        if (c.BrowseId?.StartsWith("VL") == true) return $"https://music.youtube.com/playlist?list={c.BrowseId[2..]}";
        if (c.BrowseId != null) return $"https://music.youtube.com/browse/{c.BrowseId}";
        return c.VideoId != null ? $"https://music.youtube.com/watch?v={c.VideoId}" : null;
    }

    private static void Share(Ctx ctx)
    {
        if (LinkOf(ctx) is not { } link) return;
        var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
        pkg.SetText(link);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
        Toast.Show("링크를 복사했어요");
    }

    private static async void Rate(Track t, bool? like)
    {
        try
        {
            var ok = await InnerTube.Instance.Rate(t.VideoId, like);
            Toast.Show(!ok ? "로그인이 필요해요" : like switch
            {
                true => "좋아요 표시한 노래에 추가했어요",
                false => "싫어요를 표시했어요",
                _ => "평가를 취소했어요",
            });
            // 싫어요 한 곡이 지금 곡이면 넘긴다 (pear skip-disliked-songs)
            if (ok && like == false && PlayerService.Instance.Current?.VideoId == t.VideoId) _ = PlayerService.Instance.Next();
        }
        catch (Exception e) { Log.Warn($"좋아요 실패: {e.Message}"); }
    }

    /// <summary>재생목록에 저장 — 같은 메뉴 모양으로 내 재생목록을 보여 주고 고르면 넣는다 (곡 또는 재생목록 통째로)</summary>
    private static async void SaveToPlaylist(Ctx ctx, string? videoId, string? sourcePlaylistId)
    {
        try
        {
            var probe = videoId ?? ctx.Track?.VideoId;
            var lists = await InnerTube.Instance.MyPlaylists(probe, sourcePlaylistId);
            OneMenu.CloseAll();
            if (lists.Count == 0) { Toast.Show("재생목록이 없어요"); return; }
            var m = new OneMenu();
            m.Header("재생목록에 저장");
            m.Item("새 재생목록", Icons.QueueAdd, () => NewPlaylist(ctx, videoId, sourcePlaylistId));
            m.Separator();
            foreach (var (title, id, contains) in lists.Take(30))
                m.Item(contains ? $"{title}  ✓" : title, Icons.PlaylistAddPath, async () =>
                {
                    if (contains) { Toast.Show($"이미 '{title}'에 있어요"); return; }
                    var ok = sourcePlaylistId != null
                        ? await InnerTube.Instance.AddPlaylistToPlaylist(id, sourcePlaylistId)
                        : videoId != null && await InnerTube.Instance.AddToPlaylist(id, videoId);
                    Toast.Show(ok ? $"'{title}'에 저장했어요" : "저장하지 못했어요");
                });
            m.Show(ctx.At, ctx.Pos);
        }
        catch (Exception e)
        {
            Log.Warn($"재생목록 목록 실패: {e.Message}");
            OneMenu.CloseAll();
            Toast.Show("재생목록을 불러오지 못했어요");
        }
    }

    /// <summary>새 재생목록 — 이름을 받아 만들고 곡을 넣는다</summary>
    private static async void NewPlaylist(Ctx ctx, string? videoId, string? sourcePlaylistId)
    {
        var name = await Prompt.Ask(ctx.At.XamlRoot, "새 재생목록", "재생목록 이름");
        if (string.IsNullOrWhiteSpace(name)) return;
        var ids = new List<string>();
        if (videoId != null) ids.Add(videoId);
        else if (sourcePlaylistId != null) ids.AddRange((await InnerTube.Instance.GetQueue(null, sourcePlaylistId)).Tracks.Select(t => t.VideoId));
        var id = await InnerTube.Instance.CreatePlaylist(name.Trim(), ids);
        Toast.Show(id != null ? $"'{name.Trim()}' 재생목록을 만들었어요" : "만들지 못했어요");
    }

    /// <summary>전문 통계 (YT '전문 통계') — 곡 ID · 형식 · 크기 · 출처 · 재생 상태</summary>
    private static void ShowStats(FrameworkElement at, Track t)
    {
        OneMenu.CloseAll();
        var p = PlayerService.Instance;
        var info = StreamResolver.Instance.Info(t.VideoId);
        var m = new OneMenu(width: 340);
        m.Header("전문 통계");
        m.Info("동영상 ID", t.VideoId);
        m.Info("제목", t.Title);
        m.Info("종류", t.VideoType switch
        {
            "MUSIC_VIDEO_TYPE_ATV" => "음원 (ATV)",
            "MUSIC_VIDEO_TYPE_OMV" => "뮤직비디오 (OMV)",
            "MUSIC_VIDEO_TYPE_UGC" => "동영상 (UGC)",
            null => "-",
            var v => v,
        });
        foreach (var (k, v) in info) m.Info(k, v);
        m.Info("재생 위치", $"{Fmt.Time(p.Position)} / {Fmt.Time(p.Duration)}");
        m.Info("볼륨", $"{Math.Round(p.Volume * 100)}%{(p.Muted ? " (음소거)" : "")}");
        m.Info("다음 트랙", $"{p.Queue.Count}곡 · {(p.Radio ? "라디오" : p.ShowAutoplayToggle ? (p.Autoplay ? "자동재생 켜짐" : "자동재생 꺼짐") : "목록")}");
        m.Show(at, null);
    }

    // ───────────────────────── 다운로드 · 오프라인 ─────────────────────────

    /// <summary>음악 폴더\OneMusic 에 m4a 로 (여러 곡이면 차례로)</summary>
    public static async void DownloadAll(IEnumerable<Track> tracks)
    {
        var list = tracks.ToList();
        var ok = 0;
        Toast.Show(list.Count == 1 ? $"'{list[0].Title}' 다운로드 중…" : $"{list.Count}곡 다운로드 중…");
        foreach (var t in list)
        {
            try { await Offline.Download(t); ok++; }
            catch (Exception e) { Log.Error($"다운로드 실패: {t.Title}", e); }
        }
        Toast.Show(ok == list.Count ? $"다운로드 완료 — {Offline.DownloadDir}" : $"{ok}/{list.Count}곡 다운로드 (나머지 실패)");
    }

    public static async void SaveOffline(IEnumerable<Track> tracks)
    {
        var list = tracks.ToList();
        Toast.Show(list.Count == 1 ? $"'{list[0].Title}' 오프라인 저장 중…" : $"{list.Count}곡 오프라인 저장 중…");
        var ok = 0;
        foreach (var t in list) if (await Offline.Instance.Save(t)) ok++;
        Toast.Show(ok == list.Count ? "오프라인 저장 완료 — 보관함에서 볼 수 있어요" : $"{ok}/{list.Count}곡 저장 (나머지 실패)");
    }
}

/// <summary>한 줄 입력 대화상자 (새 재생목록 이름 등) — One UI 유리 대화상자</summary>
public static class Prompt
{
    public static async Task<string?> Ask(XamlRoot? root, string title, string placeholder)
    {
        if (root == null) return null;
        var box = new TextBox { PlaceholderText = placeholder, FontFamily = Theme.Font, FontSize = 15, MinWidth = 320 };
        var dlg = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = box,
            PrimaryButtonText = "만들기",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
            CornerRadius = new CornerRadius(28),
            Background = Theme.Glass(0.86, Color.FromArgb(255, 46, 46, 48)),
            RequestedTheme = ElementTheme.Dark,
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary ? box.Text : null;
    }
}
