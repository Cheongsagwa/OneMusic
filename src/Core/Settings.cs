using System.Text.Json;

namespace OneMusic.Core;

/// <summary>설정 (볼륨 · 반복 모드 · 마지막 창 크기) — %LocalAppData%\OneMusic\settings.json</summary>
public sealed class Settings
{
    public double Volume { get; set; } = 0.6;
    public bool Muted { get; set; }
    public RepeatMode Repeat { get; set; } = RepeatMode.Off;
    public bool Autoplay { get; set; } = true;
    /// <summary>가사를 먼저 찾을 곳 (☆ 로 바꾼다)</summary>
    public LyricsSource LyricsSource { get; set; } = LyricsSource.LRCLib;
    /// <summary>창이 뒤에 있을 때 곡이 바뀌면 윈도우 알림 (pear notifications)</summary>
    public bool Notifications { get; set; } = true;
    /// <summary>앱을 다시 켜면 듣던 곡 · 위치 그대로</summary>
    public bool ResumeSession { get; set; } = true;
    /// <summary>사이드바(≡)를 펼쳐 둘지</summary>
    public bool GuideOpen { get; set; }
    public int WindowWidth { get; set; } = 1440;
    public int WindowHeight { get; set; } = 920;

    private static readonly string FilePath = IOPath.Combine(Log.AppDataDir, "settings.json");
    public static Settings Current { get; } = Load();

    private static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception e) { Log.Warn($"설정 읽기 실패: {e.Message}"); }
        return new Settings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); }
        catch (Exception e) { Log.Warn($"설정 저장 실패: {e.Message}"); }
    }
}
