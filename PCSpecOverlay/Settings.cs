using System.IO;
using System.Text.Json;

namespace PCSpecOverlay;

/// <summary>User settings, stored in %AppData%\PCSpecOverlay\settings.json.</summary>
public sealed class Settings
{
    public string Hotkey { get; set; } = "Alt+Shift+O";
    public string Position { get; set; } = "TopRight"; // TopRight, TopLeft, BottomRight, BottomLeft
    public bool Visible { get; set; } = true;
    public double Opacity { get; set; } = 1.0;
    public double Scale { get; set; } = 1.0;
    public bool Alerts { get; set; } = true;

    /// <summary>Original values of settings the optimizer changed, keyed by tweak id (null = value was absent).</summary>
    public Dictionary<string, string?> TweakBackups { get; set; } = new();

    /// <summary>When the optimizer last changed each browser's graphics preference, keyed by exe path.</summary>
    public Dictionary<string, DateTime> BrowserGpuChanged { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Process names Game Boost lowers in priority while a game is in front.</summary>
    public List<string> BoostTargets { get; set; } = new();
    public bool GameBoost { get; set; }

    /// <summary>Switch to Best performance + Game Boost while a game is in front, and back afterwards.</summary>
    public bool AutoGamingMode { get; set; }

    /// <summary>Apps or window titles that count as games even when not fullscreen (e.g. "Starve.io").</summary>
    public List<string> Games { get; set; } = new();

    /// <summary>Power mode to return to; set only while gaming mode is on, so a crash can be recovered from.</summary>
    public string? GamingRestorePowerMode { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCSpecOverlay", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* corrupt file: fall back to defaults */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { /* non-fatal */ }
    }
}
