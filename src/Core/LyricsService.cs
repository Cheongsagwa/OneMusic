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
                LyricsSource.LRCLib => await Lrclib(t, ct),
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

    /// <summary>LRCLib — 결과를 곡 · 출처별로 기억</summary>
    private async Task<LyricsResult?> Lrclib(Track t, CancellationToken ct)
    {
        if (_lrc.TryGetValue(t.VideoId, out var cached)) return cached;
        var r = await FetchLrclib(t, ct);
        _lrc[t.VideoId] = r;
        return r;
    }

    private readonly Dictionary<string, LyricsResult?> _lrc = new();

    /// <summary>
    /// 여러 방법으로 후보를 모아서 고른다.
    /// 1) get-cached (정확히)  2) track_name + artist_name 검색 (제목 그대로 · feat 뗀 것)  3) 자유 검색 (가수+제목, 제목+피처링 가수)
    /// 고르는 순서: 길이 ±8초 안 → 원어(한글 곡이면 한글 가사) → 시간 있는 가사 → 길이가 가까운 것.
    /// 원어인 시간 가사가 없으면 원어인 일반 가사(시간 없음)라도. 로마자 · 번역본만 있으면 없음 → 다음 출처
    /// </summary>
    private async Task<LyricsResult?> FetchLrclib(Track t, CancellationToken ct)
    {
        var full = t.Title.Trim();
        var title = Clean(full);
        var artists = new List<string> { t.Artist.Trim() };
        var m = Regex.Match(t.Artist, @"^(.*?)\s*\((.+?)\)\s*$");
        if (m.Success)
        {
            artists.Add(m.Groups[1].Value.Trim());
            artists.Add(m.Groups[2].Value.Trim());
        }
        // 여러 가수 '아이유, 박재범 · A 및 B' → 첫 가수만도
        artists.Add(Regex.Split(t.Artist, @",| 및 | & | x ")[0].Trim());
        artists = artists.Where(a => a.Length > 0).Distinct().ToList();
        var feat = Regex.Match(full, @"[\(\[](?:feat\.?|ft\.?|with)\s*([^\)\]]+)[\)\]]", RegexOptions.IgnoreCase);

        var all = new Dictionary<long, JsonNode>();
        void AddOne(JsonNode? x)
        {
            if (x?["id"] is { } id && (x.P("syncedLyrics").Str() != null || x.P("plainLyrics").Str() != null))
                all.TryAdd(id.GetValue<long>(), x);
        }
        async Task<JsonNode?> GetJson(string url)
        {
            try
            {
                using var res = await _http.GetAsync(url, ct);
                // LRCLib 은 자기 DB 에 없으면 바깥에서 찾다가 503 을 주기도 한다 → 그냥 다음 방법으로
                return res.IsSuccessStatusCode ? JsonNode.Parse(await res.Content.ReadAsStringAsync(ct)) : null;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; } // 요청 시간 초과
        }

        foreach (var artist in artists.Take(2))
        {
            var url = $"https://lrclib.net/api/get-cached?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}";
            if (t.DurationSec > 0) url += $"&duration={t.DurationSec}";
            AddOne(await GetJson(url));
        }
        foreach (var tn in new[] { full, title }.Distinct())
            foreach (var artist in artists)
                if (await GetJson($"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(tn)}&artist_name={Uri.EscapeDataString(artist)}") is JsonArray arr)
                    foreach (var x in arr) AddOne(x);
        var qs = artists.Select(a => $"{a} {title}").ToList();
        if (feat.Success) qs.Add($"{title} {feat.Groups[1].Value.Trim()}");
        qs.Add(title);
        foreach (var q in qs.Distinct())
        {
            if (await GetJson($"https://lrclib.net/api/search?q={Uri.EscapeDataString(q)}") is JsonArray arr)
                foreach (var x in arr) AddOne(x);
            if (all.Values.Any(x => Good(t, x, synced: true))) break;
        }

        var cands = all.Values.Where(x => t.DurationSec <= 0 || Math.Abs(Dur(x) - t.DurationSec) < 8)
            .OrderBy(x => Math.Abs(Dur(x) - t.DurationSec)).ToList();
        Log.Info($"LRCLib 후보 {all.Count}개 (길이 맞는 것 {cands.Count}): " +
                 string.Join(" / ", cands.Take(6).Select(x => $"#{x["id"]} {x.P("artistName").Str()} - {x.P("trackName").Str()} {Dur(x):0}s {(x.P("syncedLyrics").Str() != null ? "synced" : "plain")} 원어:{ScriptMatches(t, x.P("syncedLyrics").Str() ?? x.P("plainLyrics").Str())}")));
        var best = cands.FirstOrDefault(x => Good(t, x, synced: true));
        if (best != null) return Parse(best.P("syncedLyrics").Str()) is { } l ? new LyricsResult(l, true) : null;
        var plain = cands.FirstOrDefault(x => Good(t, x, synced: false));
        if (plain != null) return Plain(plain.P("plainLyrics").Str());
        return null;
    }

    private static double Dur(JsonNode x) => x["duration"]?.GetValue<double>() ?? 0;

    private static bool Good(Track t, JsonNode x, bool synced) =>
        synced ? x.P("syncedLyrics").Str() is { } s && ScriptMatches(t, s)
               : x.P("plainLyrics").Str() is { } p && ScriptMatches(t, p);

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
