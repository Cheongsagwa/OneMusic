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
    /// <summary>제목 위 작은 글씨 (예: 다시 듣기 위의 내 이름)</summary>
    public string? Strapline { get; init; }
    /// <summary>제목 왼쪽 동그란 사진 (예: 내 프로필)</summary>
    public string? Avatar { get; init; }
}

/// <summary>홈 맨 위 분위기 칩 (휴식 · 운동 …)</summary>
public sealed record Chip(string Text, string? BrowseId, string? Params, bool Selected);

public sealed record HomeData(List<Chip> Chips, List<Shelf> Shelves);

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
public sealed record QueueChip(string Text, string? VideoId, string? PlaylistId, string? Params, bool Selected);

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
    string? RadioPlaylistId);

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
