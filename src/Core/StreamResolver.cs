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
        var url = await Resolve(videoId, fresh, CancellationToken.None);
        var sw = Stopwatch.StartNew();
        const int chunk = 2 << 20;
        var first = await Range(url, 0, chunk - 1);
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
        url = await ViaYtDlp(videoId, ct);
        Remember(videoId, url);
        LastSource = "yt-dlp";
        return url;
    }

    private void Remember(string id, string url)
    {
        lock (_cache) _cache[id] = (url, DateTime.UtcNow);
    }

    private async Task<string> ViaYtDlp(string videoId, CancellationToken ct)
    {
        await EnsureYtDlp(ct);
        var psi = new ProcessStartInfo(YtDlpPath,
            $"-f \"bestaudio[ext=m4a]/bestaudio\" -g --no-warnings --no-playlist \"https://www.youtube.com/watch?v={videoId}\"")
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
        if (File.Exists(YtDlpPath)) return;
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
