using System.Diagnostics;

namespace OneMusic.Core;

/// <summary>
/// 곡 ID → 실제 오디오 스트림 주소. yt-dlp 로 뽑는다
/// (YouTube 가 막는 방식을 바꿔도 가장 빨리 대응하는 도구. 처음 필요할 때 한 번 내려받는다).
/// YoutubeExplode 도 써 봤지만 주소는 빨리 주는데 재생 몇 초 만에 끊기는 주소(서명 처리 문제)라 뺐다.
/// 대신 다음 곡 주소를 미리 받아 둬서 곡이 바뀔 때는 바로 시작된다.
/// </summary>
public sealed class StreamResolver
{
    public static StreamResolver Instance { get; } = new();
    public string LastSource { get; private set; } = "";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly Dictionary<string, (string Url, DateTime At)> _cache = new();
    private static readonly string YtDlpPath = IOPath.Combine(Log.AppDataDir, "yt-dlp.exe");
    private readonly SemaphoreSlim _ytdlpGate = new(1, 1);

    private readonly Dictionary<string, Task<string>> _prefetch = new();
    private readonly HttpClient _media = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = TimeSpan.FromMinutes(2) };
    /// <summary>오디오 파일 자체 (곡 하나 4~6MB, 30분짜리는 30MB 정도) — 최근 3곡만 들고 있는다</summary>
    private readonly Dictionary<string, Task<byte[]>> _audio = new();

    /// <summary>
    /// 곡 오디오를 통째로 메모리에 받아 온다 (YouTube 서버가 빨라서 4MB 가 0.1초 정도).
    /// Windows 재생기가 YouTube 주소를 직접 열면 곡에 따라 열자마자 끝나 버려서, 받아 둔 데이터를 재생기에 넘긴다.
    /// 덤으로 넘기기(시크)가 즉시 된다.
    /// </summary>
    public Task<byte[]> Audio(string videoId, bool fresh, CancellationToken ct)
    {
        lock (_audio)
        {
            if (!fresh && _audio.TryGetValue(videoId, out var t) && !t.IsFaulted && !t.IsCanceled) return t;
            var task = Download(videoId, fresh);
            _audio[videoId] = task;
            // 오래된 것은 버린다 (최근 3곡만)
            while (_audio.Count > 3) _audio.Remove(_audio.Keys.First(k => k != videoId));
            return task.WaitAsync(ct);
        }
    }

    /// <summary>다음 곡을 미리 받아 둔다 (곡이 바뀔 때 바로 시작되게)</summary>
    public void Prefetch(string videoId) => _ = Audio(videoId, false, CancellationToken.None).ContinueWith(_ => { });

    /// <summary>
    /// 2MB 조각으로 나눠 동시에 6개씩 받는다.
    /// YouTube 는 큰 파일을 한 번에 달라고 하면 재생 속도 정도로 느리게 줘서, 30분 넘는 곡은 시간 안에 다 못 받았다
    /// </summary>
    private async Task<byte[]> Download(string videoId, bool fresh)
    {
        // 오프라인 저장한 곡이면 인터넷 없이
        if (!fresh && await Offline.Instance.Read(videoId) is { } saved) return saved;
        var url = await Resolve(videoId, fresh, CancellationToken.None);
        var sw = Stopwatch.StartNew();
        const int chunk = 2 << 20;
        (byte[] Data, long? Total) first;
        try { first = await Range(url, 0, chunk - 1); }
        catch (Exception e) when (!fresh)
        {
            // 빠른 길로 받은 주소가 막혔으면 yt-dlp 주소로 한 번 더
            Log.Warn($"오디오 첫 조각 실패 ({e.Message}) → 새 주소로");
            url = await Resolve(videoId, true, CancellationToken.None);
            first = await Range(url, 0, chunk - 1);
        }
        var total = first.Total ?? first.Data.Length;
        var bytes = new byte[total];
        first.Data.CopyTo(bytes, 0);
        if (total > first.Data.Length)
        {
            using var gate = new SemaphoreSlim(6);
            var tasks = new List<Task>();
            for (long start = first.Data.Length; start < total; start += chunk)
            {
                var from = start;
                var to = Math.Min(start + chunk, total) - 1;
                tasks.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        // 조각 하나가 실패하면 두 번까지 다시
                        for (var attempt = 0; ; attempt++)
                        {
                            try
                            {
                                var part = await Range(url, from, to);
                                part.Data.CopyTo(bytes, from);
                                return;
                            }
                            catch when (attempt < 2) { await Task.Delay(300 * (attempt + 1)); }
                        }
                    }
                    finally { gate.Release(); }
                }));
            }
            await Task.WhenAll(tasks);
        }
        var fixedCount = Mp4Fix.NeutralizeEditLists(bytes);
        Log.Info($"오디오 받음 {videoId}: {bytes.Length / 1024} KB / {sw.ElapsedMilliseconds} ms{(fixedCount > 0 ? " (편집 목록 정리)" : "")}");
        return bytes;
    }

    private async Task<(byte[] Data, long? Total)> Range(string url, long from, long to)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Range", $"bytes={from}-{to}");
        using var res = await _media.SendAsync(req);
        res.EnsureSuccessStatusCode();
        var data = await res.Content.ReadAsByteArrayAsync();
        // 범위를 무시하고 전체(200)를 주면 그게 곧 전부
        long? total = res.StatusCode == System.Net.HttpStatusCode.PartialContent ? res.Content.Headers.ContentRange?.Length : data.Length;
        if (from > 0 && data.Length != to - from + 1) throw new IOException($"조각 크기 이상 {data.Length}/{to - from + 1}");
        return (data, total);
    }

    /// <summary>전문 통계용 — 받은 주소의 형식(itag · mime) · 크기 · 출처</summary>
    public List<(string, string)> Info(string videoId)
    {
        var list = new List<(string, string)>();
        string? url = null;
        lock (_cache) if (_cache.TryGetValue(videoId, out var c)) url = c.Url;
        if (Offline.Instance.Has(videoId)) list.Add(("출처", "오프라인 저장"));
        else list.Add(("출처", url != null ? (LastSource == "innertube" ? "YouTube (직접)" : "yt-dlp (YouTube)") : "-"));
        if (url != null)
        {
            var q = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
            if (q["itag"] is { } itag) list.Add(("itag", itag));
            if (q["mime"] is { } mime) list.Add(("형식", mime));
            if (q["clen"] is { } clen && long.TryParse(clen, out var n)) list.Add(("크기", $"{n / 1024.0 / 1024.0:0.0} MB"));
            if (q["dur"] is { } dur) list.Add(("길이", $"{dur} 초"));
            if (q["expire"] is { } exp && long.TryParse(exp, out var e))
                list.Add(("주소 만료", DateTimeOffset.FromUnixTimeSeconds(e).ToLocalTime().ToString("HH:mm")));
        }
        lock (_audio)
            if (_audio.TryGetValue(videoId, out var a) && a.IsCompletedSuccessfully)
                list.Add(("메모리", $"{a.Result.Length / 1024.0 / 1024.0:0.0} MB"));
        return list;
    }

    public async Task<string> Resolve(string videoId, bool useFallback, CancellationToken ct)
    {
        Task<string>? pending = null;
        lock (_cache)
        {
            if (!useFallback && _prefetch.Remove(videoId, out var pf)) pending = pf;
        }
        if (pending != null)
        {
            try { return await pending.WaitAsync(ct); }
            catch (OperationCanceledException) { throw; }
            catch { /* 미리 받기 실패 → 아래에서 다시 */ }
        }
        lock (_cache)
        {
            // 스트림 주소는 몇 시간 유효 — 1시간 안이면 다시 쓴다
            if (!useFallback && _cache.TryGetValue(videoId, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromHours(1))
                return c.Url;
        }
        string url;
        // 1차: YouTube 내부 API 를 직접 (0.2~0.5초). yt-dlp 는 실행할 때마다 압축을 풀고 페이지를 받느라 2~3초 걸린다
        if (!useFallback)
        {
            try
            {
                var fast = await ViaInnerTube(videoId, ct);
                if (fast != null)
                {
                    Remember(videoId, fast);
                    LastSource = "innertube";
                    return fast;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { Log.Warn($"빠른 주소 받기 실패 ({videoId}): {e.Message} → yt-dlp"); }
        }
        try { url = await ViaYtDlp(videoId, ct); }
        catch (Exception e) when (e is not OperationCanceledException && !_updatedAfterFail)
        {
            // YouTube 가 바뀌어서 옛 yt-dlp 가 실패하는 경우가 많다 → 한 번 새 버전으로 올리고 다시
            _updatedAfterFail = true;
            Log.Warn($"yt-dlp 실패 → 업데이트 후 다시: {e.Message}");
            await UpdateYtDlp();
            url = await ViaYtDlp(videoId, ct);
        }
        Remember(videoId, url);
        LastSource = "yt-dlp";
        return url;
    }

    private readonly HttpClient _player = new(new SocketsHttpHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    })
    { Timeout = TimeSpan.FromSeconds(6) };

    private const string VrVersion = "1.65.10";
    private const string VrAgent = "com.google.android.apps.youtube.vr.oculus/" + VrVersion +
                                   " (Linux; U; Android 12L; eureka-user Build/SQ3A.220605.009.A1) gzip";

    /// <summary>
    /// YouTube 내부 player API 를 ANDROID_VR 클라이언트로 불러 오디오 주소를 바로 받는다
    /// (이 클라이언트는 서명 풀기 · PoToken 이 필요 없는 주소를 준다 — yt-dlp 도 같은 방법을 쓴다).
    /// 재생할 수 없는 응답이면 null → yt-dlp 로
    /// </summary>
    private async Task<string?> ViaInnerTube(string videoId, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["context"] = new JsonObject
            {
                ["client"] = new JsonObject
                {
                    ["clientName"] = "ANDROID_VR",
                    ["clientVersion"] = VrVersion,
                    ["deviceMake"] = "Oculus",
                    ["deviceModel"] = "Quest 3",
                    ["androidSdkVersion"] = 32,
                    ["osName"] = "Android",
                    ["osVersion"] = "12L",
                    ["hl"] = "ko",
                    ["gl"] = "KR",
                },
            },
            ["videoId"] = videoId,
            ["contentCheckOk"] = true,
            ["racyCheckOk"] = true,
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://www.youtube.com/youtubei/v1/player?prettyPrint=false")
        {
            Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("User-Agent", VrAgent);
        req.Headers.TryAddWithoutValidation("X-YouTube-Client-Name", "28");
        req.Headers.TryAddWithoutValidation("X-YouTube-Client-Version", VrVersion);
        var sw = Stopwatch.StartNew();
        using var res = await _player.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;
        var json = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
        var status = json.P("playabilityStatus", "status").Str();
        if (status != "OK")
        {
            Log.Info($"빠른 주소: {videoId} 재생 불가 ({status}) → yt-dlp");
            return null;
        }
        if (json.P("streamingData", "adaptiveFormats") is not JsonArray formats) return null;
        // 서명 없이 바로 쓸 수 있는 주소가 있는 오디오만, Windows 재생기가 확실히 여는 AAC(mp4) 우선
        var audio = formats
            .Where(f => (f.P("mimeType").Str() ?? "").StartsWith("audio/") && f.P("url").Str() != null)
            .OrderByDescending(f => (f.P("mimeType").Str() ?? "").StartsWith("audio/mp4") ? 1 : 0)
            .ThenByDescending(f => (long?)f?["bitrate"]?.GetValue<double>() ?? 0)
            .FirstOrDefault();
        var url = audio.P("url").Str();
        if (url == null || !(audio.P("mimeType").Str() ?? "").StartsWith("audio/mp4")) return null;
        Log.Info($"빠른 주소 받음 {videoId}: itag {audio.P("itag").Str()} / {sw.ElapsedMilliseconds} ms");
        return url;
    }

    private void Remember(string id, string url)
    {
        lock (_cache)
        {
            // 한 시간 지난 주소는 어차피 다시 받으니 버린다
            if (_cache.Count > 200)
                foreach (var k in _cache.Where(x => DateTime.UtcNow - x.Value.At > TimeSpan.FromHours(1)).Select(x => x.Key).ToList())
                    _cache.Remove(k);
            _cache[id] = (url, DateTime.UtcNow);
        }
    }

    private bool _updateChecked, _updatedAfterFail;

    /// <summary>yt-dlp 가 사흘 넘게 묵었으면 뒤에서 조용히 새 버전으로 (YouTube 가 막는 방식을 바꿔도 계속 재생되게)</summary>
    private void MaybeUpdate()
    {
        if (_updateChecked) return;
        _updateChecked = true;
        try
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(YtDlpPath) < TimeSpan.FromDays(3)) return;
        }
        catch { return; }
        _ = Task.Run(UpdateYtDlp);
    }

    private async Task UpdateYtDlp()
    {
        await _ytdlpGate.WaitAsync();
        try
        {
            var psi = new ProcessStartInfo(YtDlpPath, "-U --no-warnings")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            // 이미 최신이어도 다음 확인은 사흘 뒤
            try { File.SetLastWriteTimeUtc(YtDlpPath, DateTime.UtcNow); } catch { }
            Log.Info($"yt-dlp 업데이트 확인: {output.Trim().Split('\n').LastOrDefault()}");
        }
        catch (Exception e) { Log.Warn($"yt-dlp 업데이트 실패: {e.Message}"); }
        finally { _ytdlpGate.Release(); }
    }

    /// <summary>
    /// 뮤직비디오 재생용 — 소리와 화면이 한 파일에 든 mp4 (18 = 360p 는 늘 있다, 더 좋은 것이 있으면 그것).
    /// Windows 재생기가 주소를 바로 열어 흘려 받는다 (영상은 커서 통째로 받지 않는다)
    /// </summary>
    public async Task<string> VideoUrl(string videoId, CancellationToken ct)
    {
        var key = "v:" + videoId;
        lock (_cache)
            if (_cache.TryGetValue(key, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromHours(1)) return c.Url;
        var url = await ViaYtDlp(videoId, ct, "best[ext=mp4][vcodec!=none][acodec!=none][height<=1080]/18/best");
        Remember(key, url);
        return url;
    }

    private async Task<string> ViaYtDlp(string videoId, CancellationToken ct, string format = "bestaudio[ext=m4a]/bestaudio")
    {
        await EnsureYtDlp(ct);
        var psi = new ProcessStartInfo(YtDlpPath,
            $"-f \"{format}\" -g --no-warnings --no-playlist \"https://www.youtube.com/watch?v={videoId}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("yt-dlp 실행 실패");
        var output = await p.StandardOutput.ReadToEndAsync(ct);
        var err = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        var url = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(url) || !url.StartsWith("http"))
            throw new InvalidOperationException($"yt-dlp 실패: {err.Trim()}");
        return url;
    }

    private async Task EnsureYtDlp(CancellationToken ct)
    {
        if (File.Exists(YtDlpPath))
        {
            MaybeUpdate();
            return;
        }
        await _ytdlpGate.WaitAsync(ct);
        try
        {
            if (File.Exists(YtDlpPath)) return;
            Log.Info("yt-dlp 내려받는 중…");
            var bytes = await _http.GetByteArrayAsync(
                "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", ct);
            var tmp = YtDlpPath + ".part";
            await File.WriteAllBytesAsync(tmp, bytes, ct);
            File.Move(tmp, YtDlpPath, true);
            Log.Info($"yt-dlp 준비됨 ({bytes.Length / 1024 / 1024} MB)");
        }
        finally
        {
            _ytdlpGate.Release();
        }
    }
}
