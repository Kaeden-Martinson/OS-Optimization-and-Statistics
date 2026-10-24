using System.IO;

namespace PCSpecOverlay.Core;

/// <summary>Where the app keeps its files. Everything is per-user; nothing needs administrator rights.</summary>
public static class AppPaths
{
    /// <summary>Overridable so tests never touch the real profile.</summary>
    public static string DataDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCSpecOverlay");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string StateFile => Path.Combine(DataDirectory, "state.json");
    public static string LogFile => Path.Combine(DataDirectory, "overlay.log");
    public static string HistoryDirectory => Path.Combine(DataDirectory, "sessions");
}
