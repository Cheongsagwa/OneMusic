using System.Text.RegularExpressions;

namespace OneMusic.Core;

/// <summary>LRCLib(무료 공개 가사 DB)에서 시간 정보가 있는 가사를 가져온다</summary>
public sealed class LyricsService
{
    public static LyricsService Instance { get; } = new();

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Dictionary<string, List<LyricLine>?> _cache = new();
    private static readonly Regex LineRx = new(@"^\[(\d+):(\d+(?:\.\d+)?)\](.*)$", RegexOptions.Compiled);

    public LyricsService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OneMusic v1.0 (desktop music player)");
    }

    public async Task<List<LyricLine>?> Get(Track t, CancellationToken ct)
    {
        if (_cache.TryGetValue(t.VideoId, out var cached)) return cached;
        List<LyricLine>? result = null;
        for (var attempt = 0; attempt < 1 && result == null; attempt++)
        {
            try
            {
                result = await Fetch(t, ct);
                break;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                Log.Warn($"가사 불러오기 실패: {e.Message}");
                await Task.Delay(1000 * (1 << attempt), ct);
            }
        }
        _cache[t.VideoId] = result;
        return result;
    }

    private async Task<List<LyricLine>?> Fetch(Track t, CancellationToken ct)
    {
        var title = Clean(t.Title);
        // 가수 이름 후보: "아이유(IU)" → "아이유(IU)", "아이유", "IU"
        var artists = new List<string> { t.Artist.Trim() };
        var m = Regex.Match(t.Artist, @"^(.*?)\s*\((.+?)\)\s*$");
        if (m.Success)
        {
            artists.Add(m.Groups[1].Value.Trim());
            artists.Add(m.Groups[2].Value.Trim());
        }
        artists = artists.Where(a => a.Length > 0).Distinct().ToList();

        // 1) 정확히 찾기 (곡 길이까지)
        foreach (var artist in artists)
        {
            var url = $"https://lrclib.net/api/get-cached?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}";
            if (t.DurationSec > 0) url += $"&duration={t.DurationSec}";
            using var res = await _http.GetAsync(url, ct);
            // LRCLib 은 자기 DB 에 없으면 바깥에서 찾다가 503 을 주기도 한다 → 그냥 다음 방법으로
            if (!res.IsSuccessStatusCode) continue;
            var json = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
            var lrc = Parse(json.P("syncedLyrics").Str());
            if (lrc != null) return lrc;
        }
        // 2) 검색해서 길이가 가장 비슷한 것
        foreach (var q in artists.Select(a => $"{a} {title}").Append(title))
        {
            var search = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(q)}";
            using var sres = await _http.GetAsync(search, ct);
            if (!sres.IsSuccessStatusCode) continue;
            var arr = JsonNode.Parse(await sres.Content.ReadAsStringAsync(ct)) as JsonArray;
            if (arr == null) continue;
            var best = arr
                .Where(x => x.P("syncedLyrics").Str() != null)
                .Where(x => t.DurationSec <= 0 || Math.Abs((x?["duration"]?.GetValue<double>() ?? 0) - t.DurationSec) < 8)
                .OrderBy(x => Math.Abs((x?["duration"]?.GetValue<double>() ?? 0) - t.DurationSec))
                .FirstOrDefault();
            var lrc = Parse(best.P("syncedLyrics").Str());
            if (lrc != null) return lrc;
        }
        return null;
    }

    /// <summary>제목의 (feat. …), [MV] 같은 꼬리를 떼서 검색이 잘 되게</summary>
    private static string Clean(string s)
    {
        s = Regex.Replace(s, @"\s*[\(\[](feat|ft|prod|with|official|mv|m/v|lyrics?)[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase);
        return s.Trim();
    }

    public static List<LyricLine>? Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc)) return null;
        var lines = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var m = LineRx.Match(raw.Trim());
            if (!m.Success) continue;
            var ms = int.Parse(m.Groups[1].Value) * 60000 + (int)(double.Parse(m.Groups[2].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 1000);
            lines.Add(new LyricLine(ms, m.Groups[3].Value.Trim()));
        }
        return lines.Count > 0 ? lines.OrderBy(l => l.TimeMs).ToList() : null;
    }
}
