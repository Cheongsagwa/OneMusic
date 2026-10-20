using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace OneMusic.Core;

/// <summary>
/// 재생 담당. Windows 기본 재생기(MediaPlayer)를 쓰므로 키보드 미디어 키 · 작업 표시줄 · 잠금 화면 미디어 컨트롤이 그냥 된다.
/// 모든 이벤트는 화면(UI) 스레드에서 알린다.
/// </summary>
public sealed class PlayerService
{
    public static PlayerService Instance { get; private set; } = null!;

    private readonly MediaPlayer _mp = new() { AudioCategory = MediaPlayerAudioCategory.Media, AutoPlay = false };
    private readonly DispatcherQueue _ui;
    private CancellationTokenSource? _loadCts;
    private bool _triedFallback;
    private bool _extending;
    private DateTime _openedAt;

    public List<Track> Queue { get; private set; } = new();
    public int Index { get; private set; } = -1;
    public Track? Current => Index >= 0 && Index < Queue.Count ? Queue[Index] : null;
    public RepeatMode Repeat { get; private set; } = Settings.Current.Repeat;
    /// <summary>'재생 중인 트랙 출처' 에 보일 이름 (예: NEW YORK 믹스)</summary>
    public string? QueueTitle { get; private set; }
    public string? RelatedBrowseId { get; private set; }
    /// <summary>자동재생 — 목록 끝에 다다르면 비슷한 곡을 이어서 튼다</summary>
    public bool Autoplay
    {
        get => Settings.Current.Autoplay;
        set { Settings.Current.Autoplay = value; QueueChanged?.Invoke(); }
    }
    public bool Loading { get; private set; }

    public event Action? TrackChanged;
    public event Action? StateChanged;
    public event Action? QueueChanged;
    public event Action? VolumeChanged;
    public event Action? RepeatChanged;

    public PlayerService(DispatcherQueue ui)
    {
        Instance = this;
        _ui = ui;
        _mp.Volume = Settings.Current.Volume;
        _mp.IsMuted = Settings.Current.Muted;
        _mp.PlaybackSession.PlaybackStateChanged += (_, _) => Ui(() => StateChanged?.Invoke());
        _mp.MediaEnded += (_, _) => Ui(OnEnded);
        _mp.MediaFailed += (_, e) => Ui(() => OnFailed(e.ErrorMessage));
        _mp.MediaOpened += (_, _) => Ui(() =>
        {
            _openedAt = DateTime.UtcNow;
            Loading = false;
            StateChanged?.Invoke();
        });

        // 미디어 키 · 시스템 미디어 컨트롤의 이전 / 다음
        var cm = _mp.CommandManager;
        cm.NextBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        cm.PreviousBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        cm.NextReceived += (s, e) => { e.Handled = true; Ui(() => _ = Next()); };
        cm.PreviousReceived += (s, e) => { e.Handled = true; Ui(() => _ = Previous()); };
    }

    private void Ui(Action a) => _ui.TryEnqueue(() =>
    {
        try { a(); }
        catch (Exception e) { Log.Error("재생 이벤트 처리 중 오류", e); }
    });

    // ───────────────────────── 상태 ─────────────────────────

    public bool IsPlaying => _mp.PlaybackSession.PlaybackState is MediaPlaybackState.Playing;
    public bool IsBuffering => Loading || _mp.PlaybackSession.PlaybackState is MediaPlaybackState.Opening or MediaPlaybackState.Buffering;

    public double Position => _mp.PlaybackSession.Position.TotalSeconds;

    public double Duration
    {
        get
        {
            var d = _mp.PlaybackSession.NaturalDuration.TotalSeconds;
            return d > 1 ? d : Current?.DurationSec ?? 0;
        }
    }

    public double Volume
    {
        get => _mp.Volume;
        set
        {
            _mp.Volume = Math.Clamp(value, 0, 1);
            if (_mp.IsMuted && value > 0) _mp.IsMuted = false;
            Settings.Current.Volume = _mp.Volume;
            Settings.Current.Muted = _mp.IsMuted;
            VolumeChanged?.Invoke();
        }
    }

    public bool Muted
    {
        get => _mp.IsMuted || _mp.Volume <= 0.001;
        set
        {
            _mp.IsMuted = value;
            Settings.Current.Muted = value;
            VolumeChanged?.Invoke();
        }
    }

    // ───────────────────────── 조작 ─────────────────────────

    /// <summary>곡 하나를 틀고, 그 곡으로 시작하는 라디오를 다음 트랙으로 채운다</summary>
    public async Task PlaySong(Track t)
    {
        Queue = new List<Track> { t };
        Index = 0;
        QueueTitle = null;
        RelatedBrowseId = null;
        QueueChanged?.Invoke();
        await Load();
        try
        {
            var data = await InnerTube.Instance.GetQueue(t.VideoId, null);
            if (Current?.VideoId != t.VideoId) return; // 그 사이 다른 곡을 틀었음
            QueueTitle = data.Title;
            RelatedBrowseId = data.RelatedBrowseId;
            Queue = new List<Track> { t };
            Queue.AddRange(data.Tracks.Where(x => x.VideoId != t.VideoId));
            QueueChanged?.Invoke();
            if (Queue.Count > 1) StreamResolver.Instance.Prefetch(Queue[1].VideoId);
        }
        catch (Exception e) { Log.Warn($"라디오 불러오기 실패: {e.Message}"); }
    }

    public async Task PlayList(List<Track> list, int start = 0, string? title = null, string? related = null)
    {
        if (list.Count == 0) return;
        QueueTitle = title;
        RelatedBrowseId = related;
        Queue = new List<Track>(list);
        Index = Math.Clamp(start, 0, list.Count - 1);
        QueueChanged?.Invoke();
        await Load();
    }

    public async Task PlayAt(int index)
    {
        if (index < 0 || index >= Queue.Count) return;
        Index = index;
        await Load();
    }

    public void TogglePlay()
    {
        if (Current == null) return;
        if (IsPlaying) _mp.Pause();
        else _mp.Play();
    }

    public void Seek(double seconds)
    {
        if (Current == null) return;
        _mp.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Math.Max(0, Duration - 0.5)));
        StateChanged?.Invoke();
    }

    public async Task Next()
    {
        if (Queue.Count == 0) return;
        if (Index + 1 < Queue.Count) Index++;
        else if (Repeat == RepeatMode.All) Index = 0;
        else
        {
            if (!Autoplay) return;
            await Extend();
            if (Index + 1 >= Queue.Count) return;
            Index++;
        }
        await Load();
    }

    public async Task Previous()
    {
        if (Queue.Count == 0) return;
        if (Position > 3 || Index == 0)
        {
            Seek(0);
            return;
        }
        Index--;
        await Load();
    }

    public void CycleRepeat()
    {
        Repeat = Repeat switch { RepeatMode.Off => RepeatMode.All, RepeatMode.All => RepeatMode.One, _ => RepeatMode.Off };
        Settings.Current.Repeat = Repeat;
        RepeatChanged?.Invoke();
    }

    /// <summary>지금 곡 뒤의 목록을 한 번 섞는다 (YT Music 의 셔플과 같은 '한 번 섞기')</summary>
    public void Shuffle()
    {
        if (Queue.Count - Index <= 2) return;
        var rest = Queue.Skip(Index + 1).OrderBy(_ => Random.Shared.Next()).ToList();
        Queue = Queue.Take(Index + 1).Concat(rest).ToList();
        QueueChanged?.Invoke();
    }

    // ───────────────────────── 내부 ─────────────────────────

    private async Task Load()
    {
        var t = Current;
        if (t == null) return;
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        _triedFallback = false;
        Loading = true;
        TrackChanged?.Invoke();
        StateChanged?.Invoke();
        await Open(t, false, cts.Token);
    }

    private async Task Open(Track t, bool fallback, CancellationToken ct)
    {
        try
        {
            var bytes = await StreamResolver.Instance.Audio(t.VideoId, fallback, ct);
            if (ct.IsCancellationRequested) return;
            var ras = new InMemoryRandomAccessStream();
            await ras.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(bytes));
            ras.Seek(0);
            if (ct.IsCancellationRequested) return;
            var item = new MediaPlaybackItem(MediaSource.CreateFromStream(ras, "audio/mp4"));
            var props = item.GetDisplayProperties();
            props.Type = MediaPlaybackType.Music;
            props.MusicProperties.Title = t.Title;
            props.MusicProperties.Artist = t.Artist;
            if (t.Album != null) props.MusicProperties.AlbumTitle = t.Album;
            if (t.Thumb != null) props.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(Fmt.Thumb(t.Thumb, 300)!));
            item.ApplyDisplayProperties(props);
            _mp.Source = item;
            _mp.Play();
            Log.Info($"재생: {t.Artist} - {t.Title} ({StreamResolver.Instance.LastSource})");
            // 다음 곡 주소를 미리 받아 둔다
            if (Index + 1 < Queue.Count) StreamResolver.Instance.Prefetch(Queue[Index + 1].VideoId);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Log.Error($"스트림 열기 실패: {t.Title}", e);
            if (!fallback && !ct.IsCancellationRequested)
            {
                _triedFallback = true;
                await Open(t, true, ct);
            }
            else
            {
                Loading = false;
                StateChanged?.Invoke();
            }
        }
    }

    private void OnFailed(string message)
    {
        Log.Warn($"재생 실패: {message}");
        var t = Current;
        if (t != null && !_triedFallback && _loadCts != null)
        {
            _triedFallback = true;
            _ = Open(t, true, _loadCts.Token);
            return;
        }
        Loading = false;
        StateChanged?.Invoke();
    }

    private void OnEnded()
    {
        // 열자마자 끝났다 = 끊긴 주소 → 새 주소로 한 번 더
        if (DateTime.UtcNow - _openedAt < TimeSpan.FromSeconds(4) && Duration > 10 && Current is { } cur && !_triedFallback && _loadCts != null)
        {
            Log.Warn("열자마자 끝남 → 새 스트림 주소로 다시 시도");
            _triedFallback = true;
            _ = Open(cur, true, _loadCts.Token);
            return;
        }
        if (Repeat == RepeatMode.One)
        {
            Seek(0);
            _mp.Play();
            return;
        }
        _ = Next();
    }

    /// <summary>목록 끝에 다다르면 마지막 곡으로 라디오를 이어 붙인다</summary>
    private async Task Extend()
    {
        if (_extending || Queue.Count == 0) return;
        _extending = true;
        try
        {
            var last = Queue[^1];
            var more = await InnerTube.Instance.Queue(last.VideoId, null);
            var known = Queue.Select(q => q.VideoId).ToHashSet();
            var add = more.Where(m => !known.Contains(m.VideoId)).ToList();
            if (add.Count > 0)
            {
                Queue.AddRange(add);
                QueueChanged?.Invoke();
            }
        }
        catch (Exception e) { Log.Warn($"다음 트랙 이어 붙이기 실패: {e.Message}"); }
        finally { _extending = false; }
    }
}
