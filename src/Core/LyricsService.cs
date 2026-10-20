using System.Text.RegularExpressions;

namespace OneMusic.Core;

/// <summary>
/// 가사 — LRCLib(시간 있는 공개 DB) · YT Music(글자만) · Musixmatch(시간 있음, 데스크톱 앱 토큰).
/// 출처마다 따로 찾고 곡 · 출처별로 기억해 둔다
/// </summary>
public sealed class LyricsService
{
    public static LyricsService Instance { get; } = new();

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Dictionary<string, List<LyricLine>?> _cache = new();
    private readonly Dictionary<(string, LyricsSource), LyricsResult?> _bySource = new();
    private string? _mxmToken;

    /// <summary>찾는 순서: 고른 출처 → LRCLib → YT Music → Musixmatch</summary>
    public static IEnumerable<LyricsSource> Order(LyricsSource preferred) =>
        new[] { preferred, LyricsSource.LRCLib, LyricsSource.YTMusic, LyricsSource.Musixmatch }.Distinct();

    public async Task<LyricsResult?> Get(Track t, LyricsSource source, CancellationToken ct)
    {
        lock (_bySource) if (_bySource.TryGetValue((t.VideoId, source), out var c)) return c;
        LyricsResult? r = null;
        try
        {
            r = source switch
            {
                LyricsSource.LRCLib => await Get(t, ct) is { } l ? new LyricsResult(l, true) : null,
                LyricsSource.YTMusic => Plain(await InnerTube.Instance.YtLyrics(t.VideoId, ct)),
                _ => await Musixmatch(t, ct),
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { Log.Warn($"가사({source}) 실패: {e.Message}"); }
        lock (_bySource) _bySource[(t.VideoId, source)] = r;
        return r;
    }

    private static LyricsResult? Plain(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lines = text.Replace("\r", "").Split('\n').Select(x => new LyricLine(-1, x.Trim())).ToList();
        return new LyricsResult(lines, false);
    }

    private const string MxmBase = "https://apic-desktop.musixmatch.com/ws/1.1/";

    /// <summary>Musixmatch 데스크톱 앱과 같은 방식 — 토큰을 받고 macro.subtitles.get 으로 시간 있는 가사, 없으면 글자 가사</summary>
    private async Task<LyricsResult?> Musixmatch(Track t, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            _mxmToken ??= await MxmToken(ct);
            if (_mxmToken == null) return null;
            var url = $"{MxmBase}macro.subtitles.get?format=json&namespace=lyrics_richsynched&subtitle_format=mxm&app_id=web-desktop-app-v1.0" +
                      $"&q_track={Uri.EscapeDataString(Clean(t.Title))}&q_artist={Uri.EscapeDataString(t.Artist)}" +
                      (t.DurationSec > 0 ? $"&q_duration={t.DurationSec}" : "") + $"&usertoken={Uri.EscapeDataString(_mxmToken)}";
            var json = await MxmGet(url, ct);
            var status = json.P("message", "header", "status_code")?.GetValue<int>();
            if (status == 401) { _mxmToken = null; continue; }
            var calls = json.P("message", "body", "macro_calls");
            var body = calls.P("track.subtitles.get", "message", "body", "subtitle_list", 0, "subtitle", "subtitle_body").Str();
            if (body != null && JsonNode.Parse(body) is JsonArray arr && arr.Count > 0)
                return new LyricsResult(arr.Select(x => new LyricLine(
                    (int)((x.P("time", "total")?.GetValue<double>() ?? 0) * 1000), x.P("text").Str() ?? "")).ToList(), true);
            var plain = calls.P("track.lyrics.get", "message", "body", "lyrics", "lyrics_body").Str();
            // 무료 응답 끝의 '******* This Lyrics is NOT for Commercial use' 같은 줄은 뺀다
            if (plain != null) plain = string.Join('\n', plain.Split('\n').TakeWhile(l => !l.StartsWith("*******")));
            return Plain(plain);
        }
        return null;
    }

    private async Task<string?> MxmToken(CancellationToken ct)
    {
        var json = await MxmGet($"{MxmBase}token.get?app_id=web-desktop-app-v1.0", ct);
        return json.P("message", "body", "user_token").Str() is { Length: > 0 } tok && !tok.StartsWith("UpgradeOnly") ? tok : null;
    }

    private async Task<JsonNode?> MxmGet(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Cookie", "AWSELBCORS=0; AWSELB=0");
        using var res = await _http.SendAsync(req, ct);
        return JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
    }
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
            var synced = json.P("syncedLyrics").Str();
            // 원어가 아닌 것(한국 곡인데 로마자 · 영어 번역)은 버리고 검색으로
            if (!ScriptMatches(t, synced)) { Log.Info($"LRCLib 결과가 원어가 아님 → 건너뜀: {t.Title}"); continue; }
            var lrc = Parse(synced);
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
                .Where(x => x.P("syncedLyrics").Str() != null && ScriptMatches(t, x.P("syncedLyrics").Str()))
                .Where(x => t.DurationSec <= 0 || Math.Abs((x?["duration"]?.GetValue<double>() ?? 0) - t.DurationSec) < 8)
                .OrderBy(x => Math.Abs((x?["duration"]?.GetValue<double>() ?? 0) - t.DurationSec))
                .FirstOrDefault();
            var lrc = Parse(best.P("syncedLyrics").Str());
            if (lrc != null) return lrc;
        }
        return null;
    }

    private static bool HasHangul(string s) => s.Any(c => c is >= '\uAC00' and <= '\uD7A3' or >= '\u1100' and <= '\u11FF' or >= '\u3130' and <= '\u318F');
    private static bool HasKana(string s) => s.Any(c => c is >= '\u3040' and <= '\u30FF');

    /// <summary>
    /// 가사가 곡의 원어인지 — 제목 · 가수 이름에 한글이 있으면 가사에도 한글이 꽤 있어야 하고(일본어 가나도 같게).
    /// LRCLib 에는 한국 곡의 로마자 · 영어 번역 가사가 원본처럼 올라와 있는 경우가 많다
    /// </summary>
    private static bool ScriptMatches(Track t, string? lyrics)
    {
        if (string.IsNullOrWhiteSpace(lyrics)) return false;
        var meta = $"{t.Title} {t.Artist} {t.Album}";
        // [mm:ss.xx] 시간 표시를 뺀 글자 중 비율
        var text = Regex.Replace(lyrics, @"\[\d+:\d+(?:\.\d+)?\]", "");
        var letters = text.Where(char.IsLetter).ToList();
        if (letters.Count == 0) return true;
        if (HasHangul(meta))
        {
            var ko = letters.Count(c => c is >= '\uAC00' and <= '\uD7A3');
            // 로마자 · 번역본은 한글이 0 이다. 영어가 많은 K-pop 도 살리려고 기준은 낮게
            return ko >= Math.Max(5, letters.Count * 0.03);
        }
        if (HasKana(meta))
        {
            var ja = letters.Count(c => c is >= '\u3040' and <= '\u30FF' or >= '\u4E00' and <= '\u9FFF');
            return ja >= Math.Max(5, letters.Count * 0.03);
        }
        return true;
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
