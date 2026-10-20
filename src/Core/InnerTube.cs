using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace OneMusic.Core;

/// <summary>
/// YouTube Music 웹이 쓰는 내부 API(youtubei, 클라이언트 이름 WEB_REMIX) 를 직접 부른다.
/// 로그인 없이: 검색 · 검색어 추천 · 홈 · 다음 트랙(라디오) · 앨범/플레이리스트 곡 목록.
/// </summary>
public sealed class InnerTube
{
    private const string Base = "https://music.youtube.com/youtubei/v1/";
    private const string ClientVersion = "1.20250929.03.00";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    public static InnerTube Instance { get; } = new();

    private HttpClient _http = Create();
    private CookieContainer? _jar;

    private static HttpClient Create() => new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        CookieContainer = Auth.Cookies,
        UseCookies = true,
    })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    /// <summary>로그인 상태가 바뀌면(쿠키 통째로 교체) 새 쿠키로 다시 만든다</summary>
    private HttpClient Http
    {
        get
        {
            if (!ReferenceEquals(_jar, Auth.Cookies))
            {
                _jar = Auth.Cookies;
                _http = Create();
                _visitorData = null;
            }
            return _http;
        }
    }

    private string? _visitorData;

    private const string WebVersion = "2.20250929.01.00";

    /// <param name="web">true 면 www.youtube.com 의 WEB 클라이언트로 (댓글은 YT Music 에 없어서 YouTube 것을 쓴다)</param>
    private async Task<JsonNode?> Post(string endpoint, JsonObject body, CancellationToken ct = default, bool web = false)
    {
        var origin = web ? "https://www.youtube.com" : "https://music.youtube.com";
        var client = new JsonObject
        {
            ["clientName"] = web ? "WEB" : "WEB_REMIX",
            ["clientVersion"] = web ? WebVersion : ClientVersion,
            ["hl"] = "ko",
            ["gl"] = "KR",
        };
        if (_visitorData != null) client["visitorData"] = _visitorData;
        body["context"] = new JsonObject { ["client"] = client, ["user"] = new JsonObject() };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{(web ? "https://www.youtube.com/youtubei/v1/" : Base)}{endpoint}{(endpoint.Contains('?') ? '&' : '?')}prettyPrint=false")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.UserAgent.ParseAdd(UserAgent);
        req.Headers.TryAddWithoutValidation("Origin", origin);
        req.Headers.Referrer = new Uri(origin + "/");
        req.Headers.TryAddWithoutValidation("X-Youtube-Client-Name", web ? "1" : "67");
        req.Headers.TryAddWithoutValidation("X-Youtube-Client-Version", web ? WebVersion : ClientVersion);
        req.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("ko-KR"));
        if (_visitorData != null) req.Headers.TryAddWithoutValidation("X-Goog-Visitor-Id", _visitorData);
        var auth = Auth.AuthorizationHeader(origin);
        if (auth != null)
        {
            req.Headers.TryAddWithoutValidation("Authorization", auth);
            req.Headers.TryAddWithoutValidation("X-Origin", origin);
            req.Headers.TryAddWithoutValidation("X-Goog-AuthUser", "0");
        }

        using var res = await Http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            Log.Warn($"InnerTube {endpoint} HTTP {(int)res.StatusCode}: {text[..Math.Min(300, text.Length)]}");
            return null;
        }
        if (Log.DevRoot != null)
        {
            // 개발 중엔 마지막 응답을 남겨 두고 구조를 확인한다
            try { File.WriteAllText(IOPath.Combine(Log.DevRoot, ".dev", $"last-{(web ? "web-" : "")}{endpoint.Split('?')[0].Replace('/', '_')}.json"), text); } catch { }
        }
        var json = JsonNode.Parse(text);
        if (!web) _visitorData ??= json.P("responseContext", "visitorData").Str();
        return json;
    }

    // ───────────────────────── 검색 ─────────────────────────

    /// <summary>노래만 검색 (YT Music 검색의 '노래' 필터)</summary>
    public async Task<List<Track>> SearchSongs(string query, CancellationToken ct = default)
    {
        var json = await Post("search", new JsonObject
        {
            ["query"] = query,
            ["params"] = "EgWKAQIIAWoMEA4QChADEAQQCRAF",
        }, ct);
        return json.FindAll("musicResponsiveListItemRenderer").Select(ParseResponsive).OfType<Track>()
            .DistinctBy(t => t.VideoId).ToList();
    }

    /// <summary>
    /// YT Music 검색 그대로 — 필터 없이 부르면 맨 위 결과 카드 + 노래 · 동영상 · 앨범 … 갈래,
    /// 칩(필터)의 params 로 부르면 그 갈래만
    /// </summary>
    public async Task<SearchData> Search(string query, string? @params = null, CancellationToken ct = default)
    {
        var body = new JsonObject { ["query"] = query };
        if (@params != null) body["params"] = @params;
        var json = await Post("search", body, ct);
        var chips = json.FindAll("chipCloudChipRenderer").Select(c => new Chip(
            c.P("text").Text(), null,
            c.P("navigationEndpoint", "searchEndpoint", "params").Str(),
            c.P("isSelected")?.GetValue<bool>() == true)).Where(c => c.Text.Length > 0).ToList();

        SearchItem? top = null;
        var topMore = new List<SearchItem>();
        if (json.FindFirst("musicCardShelfRenderer") is { } card)
        {
            var titleRun = card.P("title", "runs", 0);
            var nav = titleRun.P("navigationEndpoint");
            var sub = card.P("subtitle");
            var subText = sub.Text();
            var thumb = card.P("thumbnail").BestThumb();
            var videoId = nav.P("watchEndpoint", "videoId").Str();
            Track? track = null;
            if (videoId != null)
            {
                var segs = subText.Split(" • ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                if (segs.Count > 0 && TypeLabels.Contains(segs[0])) segs.RemoveAt(0);
                var time = segs.FirstOrDefault(x => TimeRx.IsMatch(x));
                if (time != null) segs.Remove(time);
                track = new Track(videoId, card.P("title").Text(), segs.FirstOrDefault() ?? "", null, thumb, Fmt.ParseTime(time), ArtistOf(sub), AlbumOf(sub));
            }
            top = new SearchItem(card.P("title").Text(), subText, thumb,
                nav.P("browseEndpoint", "browseId").Str(), PageTypeOf(nav.P("browseEndpoint")), track);
            if (card.P("contents") is JsonArray cc)
                topMore = cc.Select(x => x.P("musicResponsiveListItemRenderer")).OfType<JsonNode>().Select(ParseSearchItem).OfType<SearchItem>().ToList();
        }
        var sections = new List<SearchSection>();
        foreach (var shelf in json.FindAll("musicShelfRenderer"))
        {
            var items = (shelf.P("contents") as JsonArray ?? new JsonArray()).Select(x => x.P("musicResponsiveListItemRenderer"))
                .OfType<JsonNode>().Select(ParseSearchItem).OfType<SearchItem>().ToList();
            if (items.Count > 0) sections.Add(new SearchSection(shelf.P("title").Text(), items));
        }
        var corrected = json.FindFirst("showingResultsForRenderer").P("correctedQuery").Text();
        return new SearchData(top, topMore, sections, chips, corrected.Length > 0 ? corrected : null);
    }

    /// <summary>검색 결과 한 줄 — 곡이면 Track 도 같이, 앨범 · 아티스트 · 플레이리스트면 browseId</summary>
    private static SearchItem? ParseSearchItem(JsonNode item)
    {
        var cols = item.P("flexColumns") as JsonArray;
        var title = cols.P(0, "musicResponsiveListItemFlexColumnRenderer", "text").Text();
        if (string.IsNullOrWhiteSpace(title)) return null;
        var sub = string.Join(" • ", (cols ?? new JsonArray()).Skip(1)
            .Select(c => c.P("musicResponsiveListItemFlexColumnRenderer", "text").Text()).Where(x => x.Length > 0));
        var be = item.P("navigationEndpoint", "browseEndpoint");
        var track = be == null ? ParseResponsive(item) : null;
        if (track == null && be == null) return null;
        return new SearchItem(title, sub, item.P("thumbnail").BestThumb(), be.P("browseId").Str(), PageTypeOf(be), track);
    }

    // ───────────────────────── 가사 (YT Music) ─────────────────────────

    /// <summary>YT Music 자체 가사 — next 의 '가사' 탭 browseId 로 browse. 시간 정보 없는 글자만 온다</summary>
    public async Task<string?> YtLyrics(string videoId, CancellationToken ct = default)
    {
        var next = await Post("next", new JsonObject { ["videoId"] = videoId, ["isAudioOnly"] = true }, ct);
        string? id = null;
        foreach (var tab in next.FindAll("tabRenderer"))
            if (tab.P("endpoint", "browseEndpoint", "browseEndpointContextSupportedConfigs", "browseEndpointContextMusicConfig", "pageType").Str() == "MUSIC_PAGE_TYPE_TRACK_LYRICS")
                id = tab.P("endpoint", "browseEndpoint", "browseId").Str();
        if (id == null) return null;
        var json = await Post("browse", new JsonObject { ["browseId"] = id }, ct);
        var text = json.FindFirst("musicDescriptionShelfRenderer").P("description").Text();
        return text.Length > 0 ? text : null;
    }

    // ───────────────────────── 댓글 ─────────────────────────

    /// <summary>
    /// 곡 댓글 — YouTube(WEB) next 에서 댓글 칸의 이어 받기 토큰을 얻고, 그 토큰으로 댓글을 받는다.
    /// token 을 주면 그 다음 묶음 (인기순 → 끝까지 내리면 이어서)
    /// </summary>
    public async Task<CommentPage> Comments(string videoId, string? token = null, CancellationToken ct = default)
    {
        string? count = null;
        if (token == null)
        {
            var watch = await Post("next", new JsonObject { ["videoId"] = videoId }, ct, web: true);
            token = watch.FindAll("itemSectionRenderer")
                .Where(s => s.P("sectionIdentifier").Str() == "comment-item-section")
                .Select(s => s.FindFirst("continuationCommand").P("token").Str()).FirstOrDefault(t => t != null)
                ?? watch.FindAll("continuationCommand").Select(c => c.P("token").Str()).LastOrDefault(t => t != null);
            if (token == null) return new CommentPage(new(), null, null);
        }
        var json = await Post("next", new JsonObject { ["continuation"] = token }, ct, web: true);
        count = json.FindFirst("commentsHeaderRenderer").P("countText").Text();
        // 새 형식: 내용은 frameworkUpdates 의 commentEntityPayload 에
        var list = new List<Comment>();
        foreach (var p in json.FindAll("commentEntityPayload"))
        {
            var props = p.P("properties");
            if (props.P("replyLevel")?.GetValue<int>() is > 0) continue;
            list.Add(new Comment(
                p.P("author", "displayName").Str() ?? "",
                p.P("author", "avatarThumbnailUrl").Str(),
                props.P("content", "content").Str() ?? "",
                props.P("publishedTime").Str() ?? "",
                p.P("toolbar", "likeCountNotliked").Str() ?? "",
                p.P("toolbar", "replyCount").Str() ?? ""));
        }
        // 옛 형식
        if (list.Count == 0)
            foreach (var c in json.FindAll("commentRenderer"))
                list.Add(new Comment(c.P("authorText").Text(), c.P("authorThumbnail").BestThumb(), c.P("contentText").Text(),
                    c.P("publishedTimeText").Text(), c.P("voteCount").Text(), c.P("replyCount").Str() ?? ""));
        var next = json.FindAll("continuationItemRenderer")
            .Select(c => c.FindFirst("continuationCommand").P("token").Str()).LastOrDefault(t => t != null);
        return new CommentPage(list, count is { Length: > 0 } ? count : null, list.Count > 0 ? next : null);
    }

    // ───────────────────────── 좋아요 · 재생목록에 저장 ─────────────────────────

    /// <summary>좋아요(true) · 싫어요(false) · 지우기(null)</summary>
    public async Task<bool> Rate(string videoId, bool? like, CancellationToken ct = default)
    {
        var ep = like switch { true => "like/like", false => "like/dislike", _ => "like/removelike" };
        var json = await Post(ep, new JsonObject { ["target"] = new JsonObject { ["videoId"] = videoId } }, ct);
        if (json != null) lock (Likes) Likes[videoId] = like switch { true => "LIKE", false => "DISLIKE", _ => "INDIFFERENT" };
        return json != null;
    }

    /// <summary>'재생목록에 저장' 에 보일 내 재생목록들 (이름 · ID · 이미 들어 있는지)</summary>
    public async Task<List<(string Title, string PlaylistId, bool Contains)>> MyPlaylists(string videoId, CancellationToken ct = default)
    {
        var json = await Post("playlist/get_add_to_playlist", new JsonObject { ["videoIds"] = new JsonArray(videoId) }, ct);
        return json.FindAll("playlistAddToOptionRenderer").Select(r => (
            r.P("title").Text(),
            r.P("playlistId").Str() ?? "",
            r.P("containsSelectedVideos").Str() == "ALL")).Where(x => x.Item2.Length > 0).ToList();
    }

    public async Task<bool> AddToPlaylist(string playlistId, string videoId, CancellationToken ct = default)
    {
        var json = await Post("browse/edit_playlist", new JsonObject
        {
            ["playlistId"] = playlistId,
            ["actions"] = new JsonArray(new JsonObject { ["action"] = "ACTION_ADD_VIDEO", ["addedVideoId"] = videoId }),
        }, ct);
        return json.P("status").Str() == "STATUS_SUCCEEDED";
    }

    /// <summary>로그인한 계정의 프로필 사진</summary>
    public async Task<string?> AccountPhoto(CancellationToken ct = default)
    {
        var json = await Post("account/account_menu", new JsonObject(), ct);
        return json.FindFirst("accountPhoto").BestThumb();
    }

    public async Task<List<string>> Suggestions(string input, CancellationToken ct = default) =>
        (await SuggestionsFull(input, ct)).Words;

    /// <summary>검색어 추천 + 바로 갈 수 있는 결과(곡 · 동영상 · 아티스트 · 앨범) — pear 검색창 드롭다운처럼</summary>
    public async Task<(List<string> Words, List<SearchItem> Items)> SuggestionsFull(string input, CancellationToken ct = default)
    {
        var json = await Post("music/get_search_suggestions", new JsonObject { ["input"] = input }, ct);
        var words = json.FindAll("searchSuggestionRenderer")
            .Select(s => s.P("suggestion").Text())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .Take(6)
            .ToList();
        var items = json.FindAll("musicResponsiveListItemRenderer").Select(ParseSearchItem).OfType<SearchItem>().Take(4).ToList();
        return (words, items);
    }

    // ───────────────────────── 홈 ─────────────────────────

    public async Task<HomeData> Home(string? browseId = null, string? @params = null, CancellationToken ct = default)
    {
        var body = new JsonObject { ["browseId"] = browseId ?? "FEmusic_home" };
        if (@params != null) body["params"] = @params;
        var json = await Post("browse", body, ct);
        var chips = json.FindAll("chipCloudChipRenderer").Select(c => new Chip(
            c.P("text").Text(),
            c.P("navigationEndpoint", "browseEndpoint", "browseId").Str(),
            c.P("navigationEndpoint", "browseEndpoint", "params").Str(),
            c.P("isSelected")?.GetValue<bool>() == true)).Where(c => c.Text.Length > 0).ToList();
        return new HomeData(chips, ParseShelves(json), NextToken(json))
        {
            Buttons = ParseButtons(json),
            Title = json.FindFirst("musicHeaderRenderer").P("title").Text() is { Length: > 0 } t ? t : null,
        };
    }

    private static List<NavButton> ParseButtons(JsonNode? json) =>
        json.FindAll("musicNavigationButtonRenderer").Select(b =>
        {
            var be = b.P("clickCommand", "browseEndpoint");
            var stripe = b.P("solid", "leftStripeColor")?.GetValue<double>();
            Color? color = null;
            if (stripe is { } v)
            {
                var u = (uint)(long)v;
                color = Color.FromArgb(255, (byte)(u >> 16), (byte)(u >> 8), (byte)u);
            }
            return new NavButton(b.P("buttonText").Text(), be.P("browseId").Str() ?? "", be.P("params").Str(), color);
        }).Where(b => b.Text.Length > 0 && b.BrowseId.Length > 0).ToList();

    /// <summary>홈을 끝까지 내리면 이어지는 셸프들 (YT Music 홈은 6개쯤씩 나눠서 준다)</summary>
    public async Task<HomeData> HomeMore(string token, CancellationToken ct = default)
    {
        var json = await Post($"browse?ctoken={Uri.EscapeDataString(token)}&continuation={Uri.EscapeDataString(token)}&type=next", new JsonObject(), ct);
        return new HomeData(new(), ParseShelves(json), NextToken(json));
    }

    private static string? NextToken(JsonNode? json) =>
        json.FindFirst("sectionListContinuation").P("continuations", 0, "nextContinuationData", "continuation").Str()
        ?? json.FindFirst("sectionListRenderer").P("continuations", 0, "nextContinuationData", "continuation").Str();

    /// <summary>화면(홈 · 둘러보기 · 보관함 · 관련 항목) 응답에서 셸프들을 뽑는다</summary>
    public async Task<List<Shelf>> BrowseShelves(string browseId, CancellationToken ct = default)
    {
        var json = await Post("browse", new JsonObject { ["browseId"] = browseId }, ct);
        return ParseShelves(json);
    }

    private static List<Shelf> ParseShelves(JsonNode? json, bool lists = true)
    {
        var shelves = new List<Shelf>();
        foreach (var shelf in json.FindAll("musicCarouselShelfRenderer"))
        {
            var header = shelf.P("header", "musicCarouselShelfBasicHeaderRenderer");
            var title = header.P("title").Text();
            if (string.IsNullOrWhiteSpace(title)) continue;
            var (cards, tracks) = ParseItems(shelf.P("contents") as JsonArray);
            if (cards.Count > 0)
                shelves.Add(new Shelf(title, cards, tracks.Count > 0 ? tracks : null)
                {
                    MoreBrowseId = header.P("moreContentButton").FindFirst("browseEndpoint").P("browseId").Str(),
                    MoreParams = header.P("moreContentButton").FindFirst("browseEndpoint").P("params").Str(),
                    Strapline = header.P("strapline").Text() is { Length: > 0 } st ? st : null,
                    Avatar = header.P("thumbnail").BestThumb(),
                });
        }
        if (!lists) return shelves;
        // 보관함처럼 격자로 오는 화면
        foreach (var grid in json.FindAll("gridRenderer"))
        {
            var title = grid.P("header", "gridHeaderRenderer", "title").Text();
            var (cards, tracks) = ParseItems(grid.P("items") as JsonArray);
            if (cards.Count > 0) shelves.Add(new Shelf(title.Length > 0 ? title : "", cards, tracks.Count > 0 ? tracks : null) { Grid = true });
        }
        foreach (var ms in json.FindAll("musicShelfRenderer"))
        {
            var title = ms.P("title").Text();
            var (cards, tracks) = ParseItems(ms.P("contents") as JsonArray);
            if (tracks.Count > 0) shelves.Add(new Shelf(title.Length > 0 ? title : "노래", cards, tracks));
        }
        return shelves;
    }

    private static (List<Card>, List<Track>) ParseItems(JsonArray? items)
    {
        var cards = new List<Card>();
        var tracks = new List<Track>();
        if (items == null) return (cards, tracks);
        foreach (var item in items)
        {
            if (item.P("musicTwoRowItemRenderer") is { } two)
            {
                var c = ParseTwoRow(two);
                if (c != null) cards.Add(c);
            }
            else if (item.P("musicResponsiveListItemRenderer") is { } resp && ParseResponsive(resp) is { } t)
            {
                tracks.Add(t);
                cards.Add(new Card(t.Title, t.Artist, t.Thumb, false, t.VideoId, null, null));
            }
        }
        return (cards, tracks);
    }

    // ───────────────────────── 재생 목록 ─────────────────────────

    /// <summary>
    /// 다음 트랙 목록. videoId 만 주면 그 곡으로 시작하는 라디오(믹스),
    /// playlistId 를 주면 그 플레이리스트 · 앨범.
    /// </summary>
    public async Task<List<Track>> Queue(string? videoId, string? playlistId, CancellationToken ct = default) =>
        (await GetQueue(videoId, playlistId, ct)).Tracks;

    public async Task<QueueData> GetQueue(string? videoId, string? playlistId, CancellationToken ct = default) =>
        await GetQueue(videoId, playlistId, null, ct);

    /// <summary>params 는 자동재생 칩(친숙한 곡 · 2020년대 …) · 자동재생 목록이 주는 값 그대로</summary>
    public async Task<QueueData> GetQueue(string? videoId, string? playlistId, string? @params, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["isAudioOnly"] = true,
            ["enablePersistentPlaylistPanel"] = true,
            ["tunerSettingValue"] = "AUTOMIX_SETTING_NORMAL",
        };
        if (videoId != null) body["videoId"] = videoId;
        body["playlistId"] = playlistId ?? (videoId != null ? "RDAMVM" + videoId : null);
        if (@params != null) body["params"] = @params;
        var json = await Post("next", body, ct);
        if (videoId != null) RememberLike(videoId, json);
        return ParseQueue(json);
    }

    /// <summary>곡별 좋아요 상태 (LIKE · DISLIKE · INDIFFERENT) — next 응답에 같이 온다</summary>
    public static Dictionary<string, string> Likes { get; } = new();

    private static void RememberLike(string videoId, JsonNode? json)
    {
        var st = json.FindFirst("likeButtonRenderer").P("likeStatus").Str();
        if (st != null) lock (Likes) Likes[videoId] = st;
    }

    public async Task<string?> LikeStatus(string videoId, CancellationToken ct = default)
    {
        lock (Likes) if (Likes.TryGetValue(videoId, out var s)) return s;
        var json = await Post("next", new JsonObject { ["videoId"] = videoId, ["isAudioOnly"] = true }, ct);
        RememberLike(videoId, json);
        lock (Likes) return Likes.GetValueOrDefault(videoId);
    }

    private static QueueData ParseQueue(JsonNode? json)
    {
        // 자동재생 칩: musicQueueRenderer.subHeaderChipCloud 안의 칩들. 누르면 queueUpdateCommand 의 watchEndpoint 로 다시 부른다
        var chips = json.FindFirst("musicQueueRenderer").P("subHeaderChipCloud").FindAll("chipCloudChipRenderer").Select(c =>
        {
            var ep = c.P("navigationEndpoint").FindFirst("watchEndpoint") ?? c.P("navigationEndpoint").FindFirst("watchPlaylistEndpoint");
            return new QueueChip(c.P("text").Text(), ep.P("videoId").Str(), ep.P("playlistId").Str(), ep.P("params").Str(),
                c.P("isSelected")?.GetValue<bool>() == true) { Endpoint = ep?.ToJsonString() };
        }).Where(c => c.Text.Length > 0 && (c.PlaylistId != null || c.Params != null)).ToList();
        // 플레이리스트 · 앨범 뒤의 '자동재생' 미리보기 → 이어 붙일 목록 주소
        var automix = json.FindFirst("automixPreviewVideoRenderer").FindFirst("watchPlaylistEndpoint");
        var list = new List<Track>();
        var panel = json.FindFirst("playlistPanelRenderer");
        var title = panel.P("title").Text();
        string? related = null;
        foreach (var tab in json.FindAll("tabRenderer"))
            if (tab.P("endpoint", "browseEndpoint", "browseEndpointContextSupportedConfigs", "browseEndpointContextMusicConfig", "pageType").Str() == "MUSIC_PAGE_TYPE_TRACK_RELATED")
                related = tab.P("endpoint", "browseEndpoint", "browseId").Str();
        var extra = (QueueData q) => q with { Chips = chips, AutomixPlaylistId = automix.P("playlistId").Str(), AutomixParams = automix.P("params").Str() };
        if (panel.P("contents") is not JsonArray items) return extra(new QueueData(list, title, related));
        foreach (var item in items)
        {
            // 한 줄에 '음원(ATV)'과 '뮤직비디오(OMV)'가 함께 들어 있는 경우가 많다 → 음원만 고른다
            JsonNode? chosen = item.P("playlistPanelVideoRenderer");
            if (item.P("playlistPanelVideoWrapperRenderer") is { } wrap)
            {
                var versions = new List<JsonNode?> { wrap.P("primaryRenderer", "playlistPanelVideoRenderer") };
                if (wrap.P("counterpart") is JsonArray cps)
                    versions.AddRange(cps.Select(c => c.P("counterpartRenderer", "playlistPanelVideoRenderer")));
                chosen = versions.FirstOrDefault(v => VideoType(v) == "MUSIC_VIDEO_TYPE_ATV") ?? versions[0];
            }
            if (chosen != null && ParsePanel(chosen) is { } t) list.Add(t);
        }
        return extra(new QueueData(list.DistinctBy(t => t.VideoId).ToList(), title.Length > 0 ? title : null, related));
    }

    /// <summary>
    /// 자동재생 칩을 눌렀을 때 — 칩의 watchEndpoint 를 그대로 next 본문으로 보낸다 (YT 웹과 같게).
    /// 우리 기본 값(isAudioOnly · tunerSetting · persistent panel)을 섞으면 서버가 칩 params 를 무시하고 같은 목록을 줬다
    /// </summary>
    public async Task<QueueData> ChipQueue(QueueChip chip, string? seedVideoId, CancellationToken ct = default)
    {
        if (chip.Endpoint == null) return await GetQueue(chip.VideoId ?? seedVideoId, chip.PlaylistId, chip.Params, ct);
        var body = (JsonNode.Parse(chip.Endpoint) as JsonObject) ?? new JsonObject();
        body.Remove("watchEndpointMusicSupportedConfigs");
        if (!body.ContainsKey("videoId") && seedVideoId != null) body["videoId"] = seedVideoId;
        body["isAudioOnly"] = true;
        var json = await Post("next", body, ct);
        var q = ParseQueue(json);
        Log.Info($"칩 '{chip.Text}' → {q.Tracks.Count}곡: {string.Join(", ", q.Tracks.Take(4).Select(t => t.Title))}");
        return q;
    }

    /// <summary>앨범 · 플레이리스트 · 아티스트 페이지의 곡들</summary>
    public async Task<List<Track>> BrowseTracks(string browseId, CancellationToken ct = default)
    {
        var json = await Post("browse", new JsonObject { ["browseId"] = browseId }, ct);
        var albumThumb = json.P("header").BestThumb() ?? json.P("contents").FindFirst("musicResponsiveHeaderRenderer").BestThumb();
        var list = json.FindAll("musicResponsiveListItemRenderer").Select(ParseResponsive).OfType<Track>()
            .DistinctBy(t => t.VideoId).ToList();
        // 앨범 수록곡 줄에는 썸네일이 없어서 앨범 표지를 넣어 준다
        if (albumThumb != null)
            list = list.Select(t => t.Thumb == null ? t with { Thumb = albumThumb } : t).ToList();
        return list;
    }

    /// <summary>앨범 · 플레이리스트 · 아티스트 상세 화면 (머리 + 곡 목록 + 아래 셸프들)</summary>
    public async Task<PageData> BrowsePage(string browseId, CancellationToken ct = default)
    {
        var json = await Post("browse", new JsonObject { ["browseId"] = browseId }, ct);
        var artistHeader = json.FindFirst("musicImmersiveHeaderRenderer") ?? json.FindFirst("musicVisualHeaderRenderer");
        var header = artistHeader ?? json.FindFirst("musicResponsiveHeaderRenderer") ?? json.FindFirst("musicDetailHeaderRenderer");
        var kind = artistHeader != null || browseId.StartsWith("UC") ? PageKind.Artist
            : browseId.StartsWith("MPRE") ? PageKind.Album : PageKind.Playlist;

        var title = header.P("title").Text();
        var subtitle = header.P("subtitle").Text();
        var strap = header.P("straplineTextOne");
        var desc = header.P("description", "musicDescriptionShelfRenderer", "description").Text();
        if (desc.Length == 0) desc = header.P("description").Text();
        var second = header.P("secondSubtitle").Text();
        if (kind == PageKind.Artist)
        {
            // 아티스트: 부제목 자리에 구독자 · 월간 청취자 수
            var subs = header.FindFirst("subscriberCountText").Text();
            var monthly = header.P("monthlyListenerCount").Text();
            subtitle = string.Join(" • ", new[] { subs.Length > 0 ? $"구독자 {subs}" : "", monthly }.Where(x => x.Length > 0));
        }
        var thumb = header.P("thumbnail").BestThumb() ?? header.P("foregroundThumbnail").BestThumb();

        // 곡 목록: 앨범 · 아티스트는 musicShelfRenderer, 플레이리스트는 musicPlaylistShelfRenderer
        var list = json.FindFirst("musicPlaylistShelfRenderer") ?? json.FindFirst("musicShelfRenderer");
        var tracks = (list.P("contents") as JsonArray ?? new JsonArray()).Select(i => i.P("musicResponsiveListItemRenderer"))
            .OfType<JsonNode>().Select(ParseResponsive).OfType<Track>().DistinctBy(t => t.VideoId).ToList();
        var albumArtist = strap.Text();
        var albumArtistId = ArtistOf(strap);
        if (kind == PageKind.Album)
            // 앨범 수록곡 줄에는 썸네일 · 가수(한 사람 앨범일 때) · 앨범 이름이 비어 있다
            tracks = tracks.Select(t => t with
            {
                Thumb = t.Thumb ?? thumb,
                Artist = t.Artist.Length > 0 ? t.Artist : albumArtist,
                ArtistId = t.ArtistId ?? albumArtistId,
                Album = t.Album ?? title,
                AlbumId = t.AlbumId ?? browseId,
            }).ToList();
        else if (thumb != null)
            tracks = tracks.Select(t => t.Thumb == null ? t with { Thumb = thumb } : t).ToList();
        var tracksTitle = list.P("title").Text();
        var more = list.P("bottomEndpoint", "browseEndpoint", "browseId").Str()
                   ?? list.P("title", "runs", 0, "navigationEndpoint", "browseEndpoint", "browseId").Str();

        string? play = null, radio = null;
        if (kind == PageKind.Artist)
        {
            play = header.P("playButton").FindFirst("playlistId").Str();
            radio = header.P("startRadioButton").FindFirst("playlistId").Str();
        }

        return new PageData(kind, title,
            subtitle.Length > 0 ? subtitle : null,
            albumArtist.Length > 0 ? albumArtist : null,
            albumArtistId,
            second.Length > 0 ? second : null,
            desc.Length > 0 ? desc : null,
            thumb, tracks,
            tracksTitle.Length > 0 ? tracksTitle : null,
            more,
            ParseShelves(json, lists: false),
            play, radio)
        {
            StraplineThumb = header.P("straplineThumbnail").BestThumb(),
        };
    }

    // ───────────────────────── 파싱 ─────────────────────────

    private static string? PageTypeOf(JsonNode? browseEndpoint) =>
        browseEndpoint.P("browseEndpointContextSupportedConfigs", "browseEndpointContextMusicConfig", "pageType").Str();

    /// <summary>글자 조각들(runs) 중 아티스트 · 앨범 페이지로 가는 링크의 browseId</summary>
    private static string? LinkOf(JsonNode? node, string pageType) =>
        node.FindAll("browseEndpoint").FirstOrDefault(b => PageTypeOf(b) == pageType).P("browseId").Str();

    private static string? ArtistOf(JsonNode? node) => LinkOf(node, "MUSIC_PAGE_TYPE_ARTIST");
    private static string? AlbumOf(JsonNode? node) => LinkOf(node, "MUSIC_PAGE_TYPE_ALBUM");

    private static readonly Regex TimeRx = new(@"^\d{1,2}(:\d{2}){1,2}$", RegexOptions.Compiled);
    private static readonly string[] TypeLabels = ["노래", "동영상", "Song", "Video", "에피소드", "Episode"];

    /// <summary>'노래 • MC몽' 처럼 앞에 붙은 종류 글자를 뗀다</summary>
    public static string StripType(string subtitle)
    {
        foreach (var label in TypeLabels)
            if (subtitle.StartsWith(label + " • ")) return subtitle[(label.Length + 3)..];
        return subtitle;
    }

    private static Track? ParseResponsive(JsonNode item)
    {
        var videoId = item.P("playlistItemData", "videoId").Str()
                      ?? item.P("overlay").FindFirst("videoId").Str()
                      ?? item.P("flexColumns", 0).FindFirst("videoId").Str();
        if (videoId == null) return null;
        var cols = item.P("flexColumns") as JsonArray;
        var title = cols.P(0, "musicResponsiveListItemFlexColumnRenderer", "text").Text();
        if (string.IsNullOrWhiteSpace(title)) return null;

        var segs = new List<string>();
        if (cols != null)
            for (var i = 1; i < cols.Count; i++)
                segs.AddRange(cols.P(i, "musicResponsiveListItemFlexColumnRenderer", "text").Text()
                    .Split(" • ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (segs.Count > 0 && TypeLabels.Contains(segs[0])) segs.RemoveAt(0);

        var duration = 0;
        var fixedText = item.P("fixedColumns", 0, "musicResponsiveListItemFixedColumnRenderer", "text").Text();
        if (TimeRx.IsMatch(fixedText)) duration = Fmt.ParseTime(fixedText);
        var timeSeg = segs.LastOrDefault(s => TimeRx.IsMatch(s));
        if (timeSeg != null)
        {
            if (duration == 0) duration = Fmt.ParseTime(timeSeg);
            segs.Remove(timeSeg);
        }
        // 조회수 같은 꼬리 정보 빼기
        segs.RemoveAll(s => s.Contains("회") && s.Any(char.IsDigit) || s.EndsWith("views"));

        var artist = segs.Count > 0 ? segs[0] : "";
        var album = segs.Count > 1 ? segs[1] : null;
        var thumb = item.P("thumbnail").BestThumb();
        var rest = cols?.Skip(1).ToList() ?? new List<JsonNode?>();
        return new Track(videoId, title, artist, album, thumb, duration,
            rest.Select(ArtistOf).FirstOrDefault(x => x != null), rest.Select(AlbumOf).FirstOrDefault(x => x != null));
    }

    private static string? VideoType(JsonNode? r) =>
        r.P("navigationEndpoint", "watchEndpoint", "watchEndpointMusicSupportedConfigs", "watchEndpointMusicConfig", "musicVideoType").Str();

    private static Track? ParsePanel(JsonNode r)
    {
        var videoId = r.P("videoId").Str();
        if (videoId == null) return null;
        var title = r.P("title").Text();
        var bylineNode = r.P("longBylineText") ?? r.P("shortBylineText");
        var byline = bylineNode.Text();
        var segs = byline.Split(" • ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var artist = segs.Length > 0 ? segs[0] : "";
        var album = segs.Length > 1 && !Regex.IsMatch(segs[1], @"^\d{4}$") ? segs[1] : null;
        var duration = Fmt.ParseTime(r.P("lengthText").Text());
        return new Track(videoId, title, artist, album, r.P("thumbnail").BestThumb(), duration, ArtistOf(bylineNode), AlbumOf(bylineNode));
    }

    private static Card? ParseTwoRow(JsonNode two)
    {
        var title = two.P("title").Text();
        if (string.IsNullOrWhiteSpace(title)) return null;
        var subtitle = two.P("subtitle").Text();
        var thumbs = two.P("thumbnailRenderer").FindFirst("thumbnails") as JsonArray;
        var best = thumbs?.LastOrDefault();
        var w = (int?)best?["width"]?.GetValue<double>() ?? 1;
        var h = (int?)best?["height"]?.GetValue<double>() ?? 1;
        var nav = two.P("navigationEndpoint");
        var videoId = nav.P("watchEndpoint", "videoId").Str();
        var playlistId = nav.P("watchEndpoint", "playlistId").Str() ?? nav.P("watchPlaylistEndpoint", "playlistId").Str();
        var browseId = nav.P("browseEndpoint", "browseId").Str();
        return new Card(title, subtitle, best.P("url").Str(), w > h * 1.2, videoId, playlistId, browseId,
            PageTypeOf(nav.P("browseEndpoint")));
    }
}
