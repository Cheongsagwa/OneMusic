namespace OneMusic.Core;

public sealed record Track(
    string VideoId,
    string Title,
    string Artist,
    string? Album,
    string? Thumb,
    int DurationSec,
    string? ArtistId = null,
    string? AlbumId = null)
{
    public string DurationText => DurationSec > 0 ? Fmt.Time(DurationSec) : "";
}

/// <summary>메인 화면 카드 (곡 · 앨범 · 플레이리스트 · 아티스트)</summary>
public sealed record Card(
    string Title,
    string Subtitle,
    string? Thumb,
    bool Wide,
    string? VideoId,
    string? PlaylistId,
    string? BrowseId,
    string? PageType = null);

public sealed record Shelf(string Title, List<Card> Items, List<Track>? Tracks = null)
{
    /// <summary>셸프 머리 '더보기' (예: 새 앨범 및 싱글 → 전체, 아티스트 앨범 → 모두)</summary>
    public string? MoreBrowseId { get; init; }
    public string? MoreParams { get; init; }
    /// <summary>보관함처럼 줄바꿈 격자로 보여 줄 셸프</summary>
    public bool Grid { get; init; }
    /// <summary>제목 위 작은 글씨 (예: 다시 듣기 위의 내 이름)</summary>
    public string? Strapline { get; init; }
    /// <summary>제목 왼쪽 동그란 사진 (예: 내 프로필)</summary>
    public string? Avatar { get; init; }
}

/// <summary>홈 맨 위 분위기 칩 (휴식 · 운동 …)</summary>
public sealed record Chip(string Text, string? BrowseId, string? Params, bool Selected);

public sealed record HomeData(List<Chip> Chips, List<Shelf> Shelves, string? Continuation = null)
{
    /// <summary>둘러보기 맨 위 큰 버튼 (최신 음악 · 차트 · 분위기 및 장르) · 분위기 화면의 장르 버튼들</summary>
    public List<NavButton> Buttons { get; init; } = new();
    /// <summary>화면 제목 (분위기 · 장르 하위 화면 등)</summary>
    public string? Title { get; init; }
}

public sealed record NavButton(string Text, string BrowseId, string? Params, Color? Stripe);

/// <summary>
/// 다음 트랙 목록 + 출처 이름(예: 'NEW YORK 믹스') + 관련 항목 페이지
/// + 자동재생 칩(전체 · 친숙한 곡 · 인기 · 2020년대 …) + 플레이리스트 뒤에 이어 붙일 자동재생 목록 주소
/// </summary>
public sealed record QueueData(List<Track> Tracks, string? Title, string? RelatedBrowseId)
{
    public List<QueueChip> Chips { get; init; } = new();
    public string? AutomixPlaylistId { get; init; }
    public string? AutomixParams { get; init; }
}

/// <summary>자동재생 칩 하나 — 누르면 이 주소(next)로 자동재생 목록을 다시 받는다</summary>
public sealed record QueueChip(string Text, string? VideoId, string? PlaylistId, string? Params, bool Selected)
{
    /// <summary>칩이 가진 watchEndpoint JSON 그대로 (YT 웹이 칩을 누를 때 보내는 값을 그대로 보낸다)</summary>
    public string? Endpoint { get; init; }
}

public enum PageKind { Album, Playlist, Artist }

/// <summary>앨범 · 플레이리스트 · 아티스트 상세 화면 한 장</summary>
public sealed record PageData(
    PageKind Kind,
    string Title,
    string? Subtitle,
    string? Strapline,
    string? StraplineId,
    string? SecondSubtitle,
    string? Description,
    string? Thumb,
    List<Track> Tracks,
    string? TracksTitle,
    string? TracksMoreId,
    List<Shelf> Shelves,
    string? PlayPlaylistId,
    string? RadioPlaylistId)
{
    /// <summary>만든 사람(플레이리스트 주인 · 앨범 가수) 동그란 사진</summary>
    public string? StraplineThumb { get; init; }
}

/// <summary>검색 결과 한 줄 (노래 · 동영상 · 앨범 · 아티스트 · 플레이리스트 …)</summary>
public sealed record SearchItem(string Title, string Subtitle, string? Thumb, string? BrowseId, string? PageType, Track? Track)
{
    public bool Round => PageType is "MUSIC_PAGE_TYPE_ARTIST" or "MUSIC_PAGE_TYPE_USER_CHANNEL";
}

public sealed record SearchSection(string Title, List<SearchItem> Items);

/// <summary>검색 결과 — 맨 위 결과 카드 + 갈래별 목록 + 필터 칩 (노래 · 동영상 · 아티스트 …) + 고친 검색어</summary>
public sealed record SearchData(SearchItem? Top, List<SearchItem> TopMore, List<SearchSection> Sections, List<Chip> Chips, string? Corrected);

public sealed record LyricLine(int TimeMs, string Text);

public enum RepeatMode { Off, All, One }

public static class Fmt
{
    public static string Time(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(Math.Floor(seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    public static int ParseTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var parts = s.Trim().Split(':');
        var total = 0;
        foreach (var p in parts)
        {
            if (!int.TryParse(p, out var n)) return 0;
            total = total * 60 + n;
        }
        return total;
    }

    /// <summary>썸네일 주소를 원하는 크기로 (googleusercontent 의 =w60-h60 꼴)</summary>
    public static string? Thumb(string? url, int size)
    {
        if (string.IsNullOrEmpty(url)) return url;
        var m = System.Text.RegularExpressions.Regex.Replace(url, @"=w\d+-h\d+", $"=w{size}-h{size}");
        return m.StartsWith("//") ? "https:" + m : m;
    }
}

public sealed record Comment(string Author, string? Avatar, string Text, string Time, string Likes, string Replies);

/// <summary>댓글 한 묶음 + '댓글 810개' + 다음 묶음 토큰</summary>
public sealed record CommentPage(List<Comment> Items, string? Count, string? Next);
