using System.Text.Json.Serialization;
using PCSpecOverlay.Core;

namespace PCSpecOverlay;

public enum GameMatchKind { WindowTitle, ProcessName }

/// <summary>A user rule that marks an app as a game, with per-game options.</summary>
public sealed class GameRule
{
    public string Match { get; set; } = "";
    public GameMatchKind Kind { get; set; } = GameMatchKind.WindowTitle;
    public bool Enabled { get; set; } = true;

    /// <summary>Whether gaming mode may optimize while this game runs (off = detect and show only).</summary>
    public bool Optimize { get; set; } = true;

    /// <summary>Overlay layout while this game is in front: null = your normal layout.</summary>
    public string? Layout { get; set; }

    /// <summary>For the dropdown: "Default" stands for null.</summary>
    [JsonIgnore]
    public string LayoutChoice
    {
        get => Layout ?? "Default";
        set => Layout = value == "Default" ? null : value;
    }
}

/// <summary>
/// User preferences, stored in %AppData%\PCSpecOverlay\settings.json. Runtime state that must survive
/// crashes (what the app changed and how to undo it) lives in <see cref="AppState"/> instead.
/// Every value is range-checked on load, so a hand-edited or damaged file can't misconfigure the app.
/// </summary>
public sealed class Settings
{
    public const string Expanded = "Expanded", Compact = "Compact";
    public static readonly string[] Corners = ["TopRight", "TopLeft", "BottomRight", "BottomLeft"];

    public int Version { get; set; } = 2;

    // Overlay
    public bool Visible { get; set; } = true;
    public string Hotkey { get; set; } = "Alt+Shift+O";
    public string Position { get; set; } = "TopRight";
    /// <summary>Monitor device name (e.g. \\.\DISPLAY2); empty = primary monitor.</summary>
    public string Monitor { get; set; } = "";
    public string Layout { get; set; } = Expanded;
    public double Opacity { get; set; } = 1.0;
    public double Scale { get; set; } = 1.0;
    public bool ClickThrough { get; set; } = true;
    public bool ShowFrameTime { get; set; } = true;
    public bool ShowOnePercentLow { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowVram { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowSlowdowns { get; set; } = true;
    /// <summary>Draw with the graphics chip instead of the CPU (applies after the app restarts).</summary>
    public bool GpuRendering { get; set; }
    public int FpsRefreshMs { get; set; } = 500;
    public int StatsRefreshMs { get; set; } = 1000;

    // Temperatures (°C)
    public int GpuTempWarn { get; set; } = 75;
    public int GpuTempHot { get; set; } = 85;
    public int CpuTempWarn { get; set; } = 80;
    public int CpuTempHot { get; set; } = 92;

    // Alerts
    public bool Alerts { get; set; } = true;
    public int AlertAfterSeconds { get; set; } = 10;
    public int AlertCooldownMinutes { get; set; } = 10;

    // Gaming
    public bool AutoGamingMode { get; set; }
    public List<GameRule> GameRules { get; set; } = new();
    public bool DetectFullscreenGames { get; set; } = true;
    /// <summary>Also optimize fullscreen apps that aren't on the games list (off: they're only shown).</summary>
    public bool OptimizeFullscreenGames { get; set; }
    public int GamingEnterSeconds { get; set; } = 5;
    public int GamingExitSeconds { get; set; } = 20;
    public bool GamingPowerMode { get; set; } = true;
    public bool GamingProcessPriority { get; set; } = true;

    // Game Boost (manual)
    public bool GameBoost { get; set; }
    public List<string> BoostTargets { get; set; } = new();

    // Telemetry
    /// <summary>Don't wake a sleeping laptop GPU just to read its sensors.</summary>
    public bool LetGpuSleep { get; set; } = true;
    public bool HistoryEnabled { get; set; } = true;
    /// <summary>Look for a newer release on GitHub once a day (only when a release location is built in).</summary>
    public bool CheckForUpdates { get; set; } = true;
    public int HistoryRetentionDays { get; set; } = 14;

    // Legacy (version 1) fields, read once for migration and never written back
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, string?>? TweakBackups { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, DateTime>? BrowserGpuChanged { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? GamingRestorePowerMode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Games { get; set; }

    public static string FilePath => AppPaths.SettingsFile;

    public static Settings Load()
    {
        var s = JsonFile.Load<Settings>(AppPaths.SettingsFile, "Settings");
        s.Validate();
        return s;
    }

    public void Save() => JsonFile.Save(AppPaths.SettingsFile, this, "Settings");

    /// <summary>Clamps every value into its supported range and fixes anything malformed.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Hotkey)) Hotkey = "Alt+Shift+O";
        if (!Corners.Contains(Position)) Position = "TopRight";
        Monitor ??= "";
        if (Layout != Compact) Layout = Expanded;
        Opacity = Clamp(Opacity, 0.3, 1.0, 1.0);
        Scale = Clamp(Scale, 0.75, 1.75, 1.0);
        FpsRefreshMs = Math.Clamp(FpsRefreshMs, 250, 2000);
        StatsRefreshMs = Math.Clamp(StatsRefreshMs, 500, 5000);

        GpuTempWarn = Math.Clamp(GpuTempWarn, 50, 105);
        GpuTempHot = Math.Clamp(GpuTempHot, GpuTempWarn, 110);
        CpuTempWarn = Math.Clamp(CpuTempWarn, 50, 105);
        CpuTempHot = Math.Clamp(CpuTempHot, CpuTempWarn, 110);

        AlertAfterSeconds = Math.Clamp(AlertAfterSeconds, 5, 300);
        AlertCooldownMinutes = Math.Clamp(AlertCooldownMinutes, 1, 240);

        GameRules = (GameRules ?? new())
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Match))
            .Select(r => { r.Match = r.Match.Trim(); if (r.Layout is not (null or Expanded or Compact)) r.Layout = null; return r; })
            .GroupBy(r => (r.Kind, r.Match.ToLowerInvariant())).Select(g => g.First())
            .ToList();
        GamingEnterSeconds = Math.Clamp(GamingEnterSeconds, 2, 60);
        GamingExitSeconds = Math.Clamp(GamingExitSeconds, 5, 600);

        BoostTargets = (BoostTargets ?? new()).Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        HistoryRetentionDays = Math.Clamp(HistoryRetentionDays, 1, 365);

        // Version 1 kept a plain list of window-title matches
        if (Games is { Count: > 0 })
            foreach (var g in Games.Where(g => !string.IsNullOrWhiteSpace(g)))
                if (!GameRules.Any(r => r.Match.Equals(g.Trim(), StringComparison.OrdinalIgnoreCase)))
                    GameRules.Add(new GameRule { Match = g.Trim() });
        Games = null;
        Version = 2;
    }

    private static double Clamp(double v, double min, double max, double fallback) =>
        double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
}
