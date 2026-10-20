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
    /// <summary>
    /// 자동재생 구간이 시작하는 자리. Queue[..AutoStart] 는 사용자가 고른 곡(곡 하나 · 앨범 · 플레이리스트),
    /// Queue[AutoStart..] 는 '자동재생 사용 중' 아래에 보이는 비슷한 곡들 (칩으로 고른 알고리즘에 따라 바뀜)
    /// </summary>
    public int AutoStart { get; private set; }
    /// <summary>자동재생 칩 (전체 · 친숙한 곡 · 인기 · 처음 듣는 곡 · 숨은 명곡 · 2020년대 …)</summary>
    public List<QueueChip> AutoChips { get; private set; } = new();
    public bool AutoLoading { get; private set; }
    /// <summary>
    /// 곡 하나로 시작한 라디오(믹스)인지. YT Music 처럼 라디오는 목록 전체가 하나로 이어지고
    /// 칩이 목록 맨 위에 붙는다 (자동재생 줄 · '자동재생 사용 중' 구분 없음)
    /// </summary>
    public bool Radio { get; private set; }
    private int _autoVersion;

    /// <summary>자동재생 — 목록 끝에 다다르면 비슷한 곡을 이어서 튼다</summary>
    public bool Autoplay
    {
        get => Settings.Current.Autoplay;
        set
        {
            Settings.Current.Autoplay = value;
            QueueChanged?.Invoke();
            if (value && !Radio && AutoStart >= Queue.Count && Queue.Count > 0) _ = LoadAuto(Queue[^1].VideoId, null, null);
        }
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
        AutoStart = 1;
        Radio = true;
        AutoChips = new();
        QueueTitle = null;
        RelatedBrowseId = null;
        var version = ++_autoVersion;
        AutoLoading = true;
        QueueChanged?.Invoke();
        await Load();
        try
        {
            // YT Music 처럼: 고른 곡 하나 + 그 곡의 라디오가 '자동재생' 구간
            var data = await InnerTube.Instance.GetQueue(t.VideoId, null);
            if (version != _autoVersion) return; // 그 사이 다른 곡을 틀었음
            QueueTitle = data.Title;
            RelatedBrowseId = data.RelatedBrowseId;
            AutoChips = data.Chips;
            Queue = new List<Track> { t };
            Queue.AddRange(data.Tracks.Where(x => x.VideoId != t.VideoId));
            if (Queue.Count > 1) StreamResolver.Instance.Prefetch(Queue[1].VideoId);
        }
        catch (Exception e) { Log.Warn($"라디오 불러오기 실패: {e.Message}"); }
        finally
        {
            if (version == _autoVersion)
            {
                AutoLoading = false;
                QueueChanged?.Invoke();
            }
        }
    }

    /// <summary>앨범 · 플레이리스트를 튼다. automix 가 있으면 그 목록, 없으면 마지막 곡의 라디오를 자동재생 구간으로 붙인다</summary>
    public async Task PlayList(List<Track> list, int start = 0, string? title = null, string? related = null,
        string? automixPlaylistId = null, string? automixParams = null)
    {
        if (list.Count == 0) return;
        QueueTitle = title;
        RelatedBrowseId = related;
        Queue = new List<Track>(list);
        AutoStart = Queue.Count;
        AutoChips = new();
        Radio = false;
        Index = Math.Clamp(start, 0, list.Count - 1);
        var version = ++_autoVersion;
        QueueChanged?.Invoke();
        var load = Load();
        if (Autoplay) _ = LoadAuto(automixPlaylistId == null ? list[^1].VideoId : null, automixPlaylistId, automixParams, version);
        await load;
    }

    /// <summary>자동재생 칩을 고른다 — 자동재생 구간을 그 알고리즘의 목록으로 바꾼다 (이미 지나간 곡 · 지금 곡은 그대로)</summary>
    public async Task SelectChip(int i)
    {
        if (i < 0 || i >= AutoChips.Count) return;
        var chip = AutoChips[i];
        AutoChips = AutoChips.Select((c, k) => c with { Selected = k == i }).ToList();
        var seed = chip.VideoId ?? Queue.ElementAtOrDefault(Math.Max(0, AutoStart - 1))?.VideoId;
        await LoadAuto(seed, chip.PlaylistId, chip.Params, keepChips: true);
    }

    /// <summary>자동재생 구간을 새로 받아 바꿔 끼운다</summary>
    private async Task LoadAuto(string? videoId, string? playlistId, string? @params, int? version = null, bool keepChips = false)
    {
        var v = version ?? ++_autoVersion;
        if (v != _autoVersion) return;
        AutoLoading = true;
        QueueChanged?.Invoke();
        try
        {
            var data = await InnerTube.Instance.GetQueue(videoId, playlistId, @params);
            if (v != _autoVersion) return;
            // 지금 곡이 자동재생 구간에 있으면 그 곡까지는 남긴다 (라디오는 지금 곡 뒤를 통째로 바꾼다)
            var keep = Queue.Take(Radio ? Index + 1 : Math.Max(AutoStart, Index + 1)).ToList();
            var known = keep.Select(t => t.VideoId).ToHashSet();
            Queue = keep.Concat(data.Tracks.Where(t => known.Add(t.VideoId))).ToList();
            if (!keepChips || AutoChips.Count == 0) AutoChips = data.Chips;
            RelatedBrowseId ??= data.RelatedBrowseId;
            if ((Autoplay || Radio) && Index + 1 < Queue.Count) StreamResolver.Instance.Prefetch(Queue[Index + 1].VideoId);
        }
        catch (Exception e) { Log.Warn($"자동재생 목록 불러오기 실패: {e.Message}"); }
        finally
        {
            if (v == _autoVersion)
            {
                AutoLoading = false;
                QueueChanged?.Invoke();
            }
        }
    }

    /// <summary>지금 곡 바로 뒤에 끼운다 (YT Music '다음에 재생')</summary>
    public void PlayNext(Track t)
    {
        if (Current == null) { _ = PlaySong(t); return; }
        Remove(t.VideoId, keepCurrent: true);
        Queue.Insert(Index + 1, t);
        if (Index + 1 < AutoStart || Radio) AutoStart++;
        else AutoStart = Index + 2;
        QueueChanged?.Invoke();
        StreamResolver.Instance.Prefetch(t.VideoId);
    }

    /// <summary>고른 곡들 끝(자동재생 구간 앞)에 붙인다 ('현재 재생목록에 추가')</summary>
    public void AddToQueue(Track t)
    {
        if (Current == null) { _ = PlaySong(t); return; }
        Remove(t.VideoId, keepCurrent: true);
        var at = Radio ? Queue.Count : Math.Min(AutoStart, Queue.Count);
        Queue.Insert(at, t);
        if (!Radio) AutoStart = at + 1;
        QueueChanged?.Invoke();
    }

    /// <summary>목록에서 뺀다 (지금 곡은 못 뺌)</summary>
    public void RemoveFromQueue(int index)
    {
        if (index < 0 || index >= Queue.Count || index == Index) return;
        Queue.RemoveAt(index);
        if (index < Index) Index--;
        if (index < AutoStart) AutoStart--;
        QueueChanged?.Invoke();
    }

    private void Remove(string videoId, bool keepCurrent)
    {
        for (var i = Queue.Count - 1; i >= 0; i--)
            if (Queue[i].VideoId == videoId && !(keepCurrent && i == Index))
            {
                Queue.RemoveAt(i);
                if (i < Index) Index--;
                if (i < AutoStart) AutoStart--;
            }
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
        // 자동재생을 끄면 자동재생 구간은 건너뛴다 (목록 끝 = 사용자가 고른 곡 끝)
        var end = Autoplay || Radio ? Queue.Count : Math.Min(AutoStart, Queue.Count);
        if (Index + 1 < end) Index++;
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
        // 사용자가 고른 곡들 안에서만 섞는다 (지금 곡이 자동재생 구간이면 그 뒤를)
        var end = Radio || Index >= AutoStart ? Queue.Count : AutoStart;
        if (end - Index <= 2) return;
        var mid = Queue.Skip(Index + 1).Take(end - Index - 1).OrderBy(_ => Random.Shared.Next());
        Queue = Queue.Take(Index + 1).Concat(mid).Concat(Queue.Skip(end)).ToList();
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
