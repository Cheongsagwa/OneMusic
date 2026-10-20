using System.Text.Json;

namespace OneMusic.Core;

/// <summary>
/// 다운로드 · 오프라인 저장 (pear 의 downloader 플러그인 · YT Music 오프라인 저장).
/// - 다운로드: 음악 폴더\OneMusic\가수 - 제목.m4a 로 파일을 남긴다
/// - 오프라인 저장: %LocalAppData%\OneMusic\offline\곡ID.m4a + 목록(index.json). 재생할 때 인터넷 대신 이 파일을 쓴다
/// </summary>
public sealed class Offline
{
    public static Offline Instance { get; } = new();

    private static readonly string Dir = IOPath.Combine(Log.AppDataDir, "offline");
    private static readonly string IndexPath = IOPath.Combine(Dir, "index.json");
    public static string DownloadDir { get; } = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "OneMusic");

    private readonly List<Track> _tracks;
    private readonly HashSet<string> _busy = new();
    public event Action? Changed;

    private Offline()
    {
        Directory.CreateDirectory(Dir);
        try
        {
            _tracks = File.Exists(IndexPath) ? JsonSerializer.Deserialize<List<Track>>(File.ReadAllText(IndexPath)) ?? new() : new();
            _tracks.RemoveAll(t => !File.Exists(PathOf(t.VideoId)));
        }
        catch (Exception e)
        {
            Log.Warn($"오프라인 목록 읽기 실패: {e.Message}");
            _tracks = new();
        }
    }

    /// <summary>최근에 저장한 것이 앞</summary>
    public IReadOnlyList<Track> Tracks { get { lock (_tracks) return _tracks.ToList(); } }

    public bool Has(string videoId) { lock (_tracks) return _tracks.Any(t => t.VideoId == videoId); }
    public bool IsBusy(string videoId) { lock (_busy) return _busy.Contains(videoId); }

    private static string PathOf(string videoId) => IOPath.Combine(Dir, videoId + ".m4a");

    /// <summary>저장된 오디오 (없으면 null)</summary>
    public async Task<byte[]?> Read(string videoId)
    {
        if (!Has(videoId)) return null;
        try { return await File.ReadAllBytesAsync(PathOf(videoId)); }
        catch { return null; }
    }

    public async Task<bool> Save(Track t)
    {
        if (Has(t.VideoId)) return true;
        lock (_busy) if (!_busy.Add(t.VideoId)) return false;
        try
        {
            var bytes = await StreamResolver.Instance.Audio(t.VideoId, false, CancellationToken.None);
            await File.WriteAllBytesAsync(PathOf(t.VideoId), bytes);
            lock (_tracks)
            {
                _tracks.RemoveAll(x => x.VideoId == t.VideoId);
                _tracks.Insert(0, t);
            }
            WriteIndex();
            Log.Info($"오프라인 저장: {t.Artist} - {t.Title}");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"오프라인 저장 실패: {t.Title}", e);
            return false;
        }
        finally
        {
            lock (_busy) _busy.Remove(t.VideoId);
            Changed?.Invoke();
        }
    }

    public void Remove(string videoId)
    {
        lock (_tracks) _tracks.RemoveAll(x => x.VideoId == videoId);
        try { File.Delete(PathOf(videoId)); } catch { }
        WriteIndex();
        Changed?.Invoke();
    }

    private void WriteIndex()
    {
        try { lock (_tracks) File.WriteAllText(IndexPath, JsonSerializer.Serialize(_tracks)); }
        catch (Exception e) { Log.Warn($"오프라인 목록 저장 실패: {e.Message}"); }
    }

    /// <summary>음악 폴더에 파일로 내려받는다. 저장한 파일 경로를 돌려준다</summary>
    public static async Task<string> Download(Track t)
    {
        var bytes = await Instance.Read(t.VideoId) ?? await StreamResolver.Instance.Audio(t.VideoId, false, CancellationToken.None);
        Directory.CreateDirectory(DownloadDir);
        var name = $"{t.Artist} - {t.Title}";
        foreach (var c in IOPath.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        if (name.Length > 150) name = name[..150];
        var path = IOPath.Combine(DownloadDir, name + ".m4a");
        await File.WriteAllBytesAsync(path, bytes);
        Log.Info($"다운로드: {path}");
        return path;
    }
}
