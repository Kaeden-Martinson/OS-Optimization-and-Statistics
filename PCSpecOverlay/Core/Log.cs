using System.IO;

namespace PCSpecOverlay.Core;

public enum LogLevel { Info, Warning, Error }

public sealed record LogEntry(DateTime Time, LogLevel Level, string Area, string Message);

/// <summary>
/// Small structured log: one line per entry in <see cref="AppPaths.LogFile"/>, rotated at 1 MB
/// (one previous file kept), plus an in-memory list of recent entries for the diagnostics page.
/// Logging never throws.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private const int RecentCapacity = 300;

    private static readonly object Gate = new();
    private static readonly LinkedList<LogEntry> RecentEntries = new();

    public static void Info(string area, string message) => Write(LogLevel.Info, area, message);
    public static void Warn(string area, string message) => Write(LogLevel.Warning, area, message);
    public static void Error(string area, string message, Exception? e = null) =>
        Write(LogLevel.Error, area, e is null ? message : $"{message}: {e.GetType().Name}: {e.Message}");

    public static IReadOnlyList<LogEntry> Recent()
    {
        lock (Gate) return RecentEntries.ToList();
    }

    private static void Write(LogLevel level, string area, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, area, message.ReplaceLineEndings(" "));
        lock (Gate)
        {
            RecentEntries.AddLast(entry);
            if (RecentEntries.Count > RecentCapacity) RecentEntries.RemoveFirst();
            try
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                var file = new FileInfo(AppPaths.LogFile);
                if (file.Exists && file.Length > MaxBytes)
                    File.Move(file.FullName, file.FullName + ".1", overwrite: true);
                File.AppendAllText(AppPaths.LogFile,
                    $"{entry.Time:yyyy-MM-dd HH:mm:ss} {entry.Level.ToString().ToUpperInvariant(),-7} [{area}] {entry.Message}{Environment.NewLine}");
            }
            catch { /* logging is best-effort */ }
        }
    }
}
