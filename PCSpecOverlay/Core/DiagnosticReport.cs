using System.Security.Principal;
using System.Text;
using System.Text.Json;
using PCSpecOverlay.Monitors;
using PCSpecOverlay.Optimizer;

namespace PCSpecOverlay.Core;

public enum DiagLevel { Ok, Info, Warn, Error }

public sealed record DiagStatus(string Name, string Detail, DiagLevel Level);

/// <summary>What works on this PC and why, for the Diagnostics page and the copyable report.</summary>
internal static class DiagnosticReport
{
    private const string PerformanceLogUsersSid = "S-1-5-32-559";

    public static List<DiagStatus> Status(Engine engine)
    {
        var list = new List<DiagStatus>();
        var build = AppIdentity.IsInstalledCopy ? "installed" : "development build";
        list.Add(new("App", $"Version {AppIdentity.Version} ({build}) · {AppIdentity.Redact(Environment.ProcessPath)}", DiagLevel.Info));

        using (var id = WindowsIdentity.GetCurrent())
        {
            var perfLog = id.Groups?.Any(g => g.Value == PerformanceLogUsersSid) == true;
            var account = StartupTask.IsAdminAccount ? "administrator account" : "standard account";
            list.Add(new("Permissions",
                $"{account}{(StartupTask.IsElevated ? ", running elevated" : ", not elevated")}; " +
                (perfLog ? "member of Performance Log Users" : "not in Performance Log Users"),
                DiagLevel.Info));
        }

        list.Add(engine.Fps.IsRunning
            ? new("FPS monitoring", engine.Fps.KernelPresents
                ? "Running: DirectX (per swap chain) plus Vulkan and OpenGL (graphics kernel, per window)"
                : "Running: DirectX only (the graphics kernel provider couldn't be enabled, so Vulkan/OpenGL games show —)",
                engine.Fps.KernelPresents ? DiagLevel.Ok : DiagLevel.Warn)
            : new("FPS monitoring", $"Not running: {engine.Fps.LastError?.Message ?? "unknown"}. Needs administrator rights or the Performance Log Users group (then sign out and in).", DiagLevel.Warn));

        var gpu = engine.Latest.Gpu;
        list.Add(engine.Gpu is null
            ? new("NVIDIA GPU", "Not found (no NVIDIA driver). GPU readings and GPU-specific controls are hidden.", DiagLevel.Info)
            : new("NVIDIA GPU", $"{engine.Gpu.Name} · {gpu.Status switch
            {
                GpuStatus.Ok => "reading normally",
                GpuStatus.Sleeping => "asleep (not being woken)",
                GpuStatus.Unavailable => "driver not responding, retrying",
                _ => gpu.Status.ToString(),
            }}", gpu.Status == GpuStatus.Unavailable ? DiagLevel.Warn : DiagLevel.Ok));
        if (engine.Gpu != null)
            list.Add(new("GPU sleep detection", engine.Gpu.CanDetectSleep
                ? (engine.Settings.LetGpuSleep ? "Available; the GPU isn't woken just for readings" : "Available but turned off in Settings")
                : "Unavailable on this PC; GPU readings may keep a laptop GPU awake", engine.Gpu.CanDetectSleep ? DiagLevel.Ok : DiagLevel.Warn));

        if (!engine.System.Ready)
            list.Add(new("CPU sensors", "Still opening Windows performance counters…", DiagLevel.Info));
        else list.Add(engine.System.ThermalZones > 0
            ? new("CPU temperature", $"ACPI thermal zones ({engine.System.ThermalZones}); hottest zone shown", DiagLevel.Ok)
            : new("CPU temperature", "No thermal zones exposed by this PC; shown as unavailable", DiagLevel.Warn));
        if (engine.System.Ready)
            list.Add(new("CPU slowdown detection", engine.System.CanDetectLimits ? "Available (Windows performance-limit counter)" : "Unavailable", engine.System.CanDetectLimits ? DiagLevel.Ok : DiagLevel.Warn));

        var mode = PowerMode.Current();
        list.Add(mode is null
            ? new("Power mode", $"Not offered with the current plan ({PowerMode.PlanName() ?? "unknown"}); gaming mode skips it", DiagLevel.Warn)
            : new("Power mode", $"{PowerMode.Name(mode)} on the {PowerMode.PlanName() ?? "unknown"} plan", DiagLevel.Ok));

        var startup = StartupTask.Status();
        list.Add(new("Start with Windows", startup.Describe(), startup.Enabled && !startup.PointsHere ? DiagLevel.Warn : DiagLevel.Info));

        var browsers = Browsers.Installed();
        list.Add(new("Browsers", browsers.Count == 0 ? "No supported browser found"
            : string.Join(", ", browsers.Select(b => b.Name + (engine.State.BrowserRestartFailedUtc.ContainsKey(b.ExePath) ? " (last restart failed)" : ""))),
            browsers.Any(b => engine.State.BrowserRestartFailedUtc.ContainsKey(b.ExePath)) ? DiagLevel.Warn : DiagLevel.Info));

        var changes = engine.Journal.Entries;
        list.Add(new("Windows changes by the app", changes.Count == 0 ? "None"
            : string.Join("; ", changes.Select(c => $"{SettingCatalog.Resolve(c.Id)?.Name ?? c.Id} (by {string.Join(", ", c.Owners)}, since {c.AppliedUtc.ToLocalTime():g})")),
            DiagLevel.Info));
        list.Add(new("Process priorities changed", engine.State.Priorities.Count == 0 ? "None"
            : string.Join(", ", engine.State.Priorities.Select(p => $"{p.Name} {p.Original}→{p.Applied}")), DiagLevel.Info));

        if (engine.RecoveryMode) list.Add(new("Recovery mode", "On — automatic optimizations are off after repeated failed starts", DiagLevel.Warn));
        if (engine.OptimizationsPaused) list.Add(new("Optimizations", "Paused by you", DiagLevel.Info));

        var errors = Log.Recent().Count(e => e.Level == LogLevel.Error);
        list.Add(new("Recent errors", errors == 0 ? "None this session" : $"{errors} — see Recent events below", errors == 0 ? DiagLevel.Ok : DiagLevel.Error));
        return list;
    }

    /// <summary>Plain-text report with personal paths and the user name redacted.</summary>
    public static string Build(Engine engine, string? hotkey)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AppIdentity.ProductName} diagnostic report — {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Windows {Environment.OSVersion.Version} · .NET {Environment.Version} · {Environment.ProcessorCount} logical CPUs");
        sb.AppendLine();
        foreach (var s in Status(engine)) sb.AppendLine($"[{s.Level}] {s.Name}: {s.Detail}");
        sb.AppendLine($"[Info] Hotkey: {hotkey ?? "none registered"}");
        sb.AppendLine();
        sb.AppendLine("Settings:");
        sb.AppendLine(JsonSerializer.Serialize(engine.Settings, new JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine();
        sb.AppendLine("Recent events:");
        foreach (var e in Log.Recent().TakeLast(100))
            sb.AppendLine($"{e.Time:HH:mm:ss} {e.Level,-7} [{e.Area}] {e.Message}");
        return AppIdentity.Redact(sb.ToString());
    }
}
