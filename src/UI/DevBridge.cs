using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using OneMusic.Core;
using Windows.Graphics.Imaging;

namespace OneMusic.UI;

/// <summary>
/// ⚠️ 개발용 — 소스 폴더에서 실행했을 때만 켜진다 (dev-watch.ps1 이 있는 폴더).
/// .dev\app-cmd.txt 에 정해진 명령이 적히면 실행하고 .dev\app-out.txt 에 결과를 남긴다.
/// shot: 화면을 .dev\shot.png 로 / state: 지금 상태 / nav:home|player|lyrics|close / search:검색어 / play:first|곡ID / mem
/// </summary>
public sealed class DevBridge
{
    private readonly MainWindow _w;
    private readonly string _cmd, _out, _shot;
    private DateTime _last;
    private readonly DispatcherQueueTimer _timer; // 필드로 붙잡아 둬야 GC 에 수거되지 않는다

    public DevBridge(MainWindow w)
    {
        _w = w;
        var dev = IOPath.Combine(Log.DevRoot!, ".dev");
        _cmd = IOPath.Combine(dev, "app-cmd.txt");
        _out = IOPath.Combine(dev, "app-out.txt");
        _shot = IOPath.Combine(dev, "shot.png");
        if (!File.Exists(_cmd)) File.WriteAllText(_cmd, "");
        _last = File.GetLastWriteTimeUtc(_cmd);
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(400);
        _timer.Tick += (_, _) => Check();
        _timer.Start();
        Log.Info("개발용 통로 켜짐");
    }

    private async void Check()
    {
        try
        {
            var m = File.GetLastWriteTimeUtc(_cmd);
            if (m == _last) return;
            _last = m;
            var cmd = (await File.ReadAllTextAsync(_cmd)).Trim();
            if (cmd.Length == 0) return;
            string result;
            try { result = await Run(cmd); }
            catch (Exception e) { result = "ERROR " + e; }
            await File.WriteAllTextAsync(_out, $"#done {DateTime.Now:O}\n{result}");
        }
        catch (Exception e) { Log.Warn("DevBridge: " + e.Message); }
    }

    private async Task<string> Run(string cmd)
    {
        var p = _w.Player;
        if (cmd == "shot") return await Shot();
        if (cmd == "state")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"track: {p.Current?.Artist} - {p.Current?.Title} ({p.Current?.VideoId}) artist {p.Current?.ArtistId} album {p.Current?.AlbumId}");
            sb.AppendLine($"playing: {p.IsPlaying} buffering: {p.IsBuffering} pos: {p.Position:0.0}/{p.Duration:0.0}");
            sb.AppendLine($"queue: {p.Queue.Count} index: {p.Index} repeat: {p.Repeat} volume: {p.Volume:0.00} muted: {p.Muted}");
            sb.AppendLine($"lyrics: {LyricsState.Instance.Lines?.Count.ToString() ?? "none"} loading: {LyricsState.Instance.Loading}");
            sb.AppendLine($"player page: {_w.PlayerPage.IsOpen}");
            return sb.ToString();
        }
        if (cmd == "mem")
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            return $"private {proc.PrivateMemorySize64 / 1048576} MB, working set {proc.WorkingSet64 / 1048576} MB, managed {GC.GetTotalMemory(false) / 1048576} MB";
        }
        if (cmd == "nav:home") { _w.ShowHome(); return "ok"; }
        if (cmd == "login") { var lw = new LoginWindow(); lw.Activate(); _ = lw.Result.ContinueWith(t => { if (t.Result) _w.DispatcherQueue.TryEnqueue(() => _w.ReloadAll()); }); return "login window opened"; }
        if (cmd.StartsWith("ytdlp:"))
        {
            // 개발용 진단: yt-dlp 를 정해진 인자 + 고른 클라이언트로 실행해 결과와 주소 응답을 본다
            var parts = cmd[6..].Split('|');
            var id = parts[0];
            var client = parts.Length > 1 ? parts[1] : "";
            var exe = IOPath.Combine(Log.AppDataDir, "yt-dlp.exe");
            var args = $"-v -f \"bestaudio[ext=m4a]/bestaudio\" -g --no-playlist " +
                       (client.Length > 0 ? $"--extractor-args \"youtube:player_client={client}\" " : "") +
                       $"\"https://www.youtube.com/watch?v={id}\"";
            var psi = new System.Diagnostics.ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var pr = System.Diagnostics.Process.Start(psi)!;
            var o = await pr.StandardOutput.ReadToEndAsync();
            var er = await pr.StandardError.ReadToEndAsync();
            await pr.WaitForExitAsync();
            var url = o.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            var probe = "";
            if (url != null && url.StartsWith("http"))
            {
                using var http = new HttpClient();
                foreach (var range in new[] { "bytes=0-65535", "bytes=1500000-1565535", "bytes=0-", "bytes=0-20000000" })
                {
                    using var rq = new HttpRequestMessage(HttpMethod.Get, url);
                    rq.Headers.TryAddWithoutValidation("Range", range);
                    using var rs = await http.SendAsync(rq, HttpCompletionOption.ResponseHeadersRead);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var body = await rs.Content.ReadAsByteArrayAsync();
                    probe += $"{range}: {(int)rs.StatusCode} {rs.Content.Headers.ContentLength} got {body.Length} in {sw.ElapsedMilliseconds}ms type {rs.Content.Headers.ContentType}\n";
                    if (range == "bytes=0-") probe += "head: " + Convert.ToHexString(body.AsSpan(0, Math.Min(48, body.Length))) + "\n";
                }
            }
            var errTail = string.Join("\n", er.Split('\n').Where(l => l.Contains("[youtube]") || l.Contains("WARNING") || l.Contains("ERROR") || l.Contains("[info]")).TakeLast(25));
            return $"{probe}\nurl: {url?[..Math.Min(160, url.Length)]}\n{errTail}";
        }
        if (cmd.StartsWith("dump:"))
        {
            var id = cmd[5..];
            var bytes = await StreamResolver.Instance.Audio(id, false, CancellationToken.None);
            await File.WriteAllBytesAsync(IOPath.Combine(Log.DevRoot!, ".dev", id + ".m4a"), bytes);
            return $"dumped {bytes.Length}";
        }
        if (cmd == "auth") return $"logged in: {Auth.IsLoggedIn}, cookies: {Auth.AllCookies().Count}";
        if (cmd == "nav:player") { _w.OpenPlayer(); return "ok"; }
        if (cmd == "nav:close") { _w.ClosePlayer(); return "ok"; }
        if (cmd == "nav:lyrics") { _w.OpenPlayer(); _w.PlayerPage.ShowLyricsTab(); return "ok"; }
        if (cmd == "toggle") { p.TogglePlay(); return "ok"; }
        if (cmd == "next") { await p.Next(); return "ok"; }
        if (cmd.StartsWith("open:")) { _w.OpenBrowse(cmd[5..]); return "ok"; }
        if (cmd == "back") { _w.Back(); return "ok"; }
        if (cmd.StartsWith("search:")) { _w.ShowSearch(cmd[7..]); return "ok"; }
        if (cmd.StartsWith("seek:")) { p.Seek(double.Parse(cmd[5..])); return "ok"; }
        if (cmd == "play:first")
        {
            var list = await InnerTube.Instance.SearchSongs("아이유 밤편지");
            if (list.Count == 0) return "no results";
            await p.PlaySong(list[0]);
            return $"playing {list[0].Title}";
        }
        if (cmd.StartsWith("play:"))
        {
            await p.PlaySong(new Track(cmd[5..], cmd[5..], "", null, null, 0));
            return "ok";
        }
        return "unknown: " + cmd;
    }

    private async Task<string> Shot()
    {
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(_w.Root);
        var pixels = await rtb.GetPixelsAsync();
        using var fs = new FileStream(_shot, FileMode.Create, FileAccess.ReadWrite);
        var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, fs.AsRandomAccessStream());
        var dpi = (float)(_w.Root.XamlRoot?.RasterizationScale ?? 1) * 96;
        enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, dpi, dpi, pixels.ToArray());
        await enc.FlushAsync();
        return $"saved {rtb.PixelWidth}x{rtb.PixelHeight}";
    }
}
