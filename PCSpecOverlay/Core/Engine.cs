using System.Diagnostics;
using PCSpecOverlay.Monitors;
using PCSpecOverlay.Optimizer;

namespace PCSpecOverlay.Core;

/// <summary>The latest readings. Null means "not available", never "zero".</summary>
public sealed record Telemetry
{
    public DateTime Time { get; init; }
    public FpsSample? Fps { get; init; }
    public string? FpsUnavailable { get; init; }
    /// <summary>Where the frame rate comes from: "DXGI" (DirectX) or "KERNEL" (graphics kernel: Vulkan, OpenGL).</summary>
    public string? FpsSource { get; init; }
    public GpuSample Gpu { get; init; } = GpuSample.Of(GpuStatus.NotPresent);
    public double? CpuLoad { get; init; }
    public double? CpuTempC { get; init; }
    public double? CpuClockGhz { get; init; }
    public (double UsedGb, double TotalGb)? Ram { get; init; }
    public Limit Limits { get; init; }
}

public sealed record AlertMessage(string Key, string Title, string Message, bool DriverConfirmed);

/// <summary>
/// Owns the monitors, optimizers and app state, so the overlay and the control center read from one
/// place. The WPF side drives it with timers on the UI thread; nothing here is thread-shared except
/// the FPS monitor, which locks internally.
/// </summary>
internal sealed class Engine : IDisposable
{
    public const int RecoveryAfterFailedStarts = 3;

    public Settings Settings { get; }
    public AppState State { get; }
    public ChangeJournal Journal { get; }
    public NvidiaGpu? Gpu { get; }
    public SystemStats System { get; }
    public FpsMonitor Fps { get; } = new();
    public GameBoost Boost { get; }
    public GamingMode Gaming { get; }

    public Telemetry Latest { get; private set; } = new();
    public GameDetection? Game { get; private set; }
    public bool RecoveryMode { get; private set; }
    public List<string> StartupReport { get; } = new();

    public event Action<AlertMessage>? Alert;

    private readonly LimitTracker _limits = new();
    private readonly SustainedThreshold _gpuHot = new(), _cpuHot = new();
    private readonly AlertThrottle _throttle = new();

    public Engine()
    {
        var clock = Stopwatch.StartNew();
        void Step(string what) { Log.Info("Startup", $"{what} ({clock.ElapsedMilliseconds} ms)"); clock.Restart(); }

        Settings = Settings.Load();
        State = AppState.Load();
        Journal = new ChangeJournal(State, TimeProvider.System);
        Migration.Run(Settings, State, SettingCatalog.Resolve);

        // Several starts in a row that never reached "running" (see MarkRunning): keep optimizations off
        State.UnfinishedStarts++;
        State.Save();
        RecoveryMode = State.UnfinishedStarts > RecoveryAfterFailedStarts;
        if (RecoveryMode) Log.Warn("App", $"Recovery mode: {State.UnfinishedStarts - 1} unfinished starts in a row; automatic optimizations are off");

        Step("Settings and state loaded");
        Gpu = NvidiaGpu.TryCreate();
        if (Gpu != null) Gpu.LetSleep = Settings.LetGpuSleep;
        Step("GPU monitor ready");
        System = new SystemStats(); // CPU counters open in the background
        Boost = new GameBoost(State);
        Gaming = new GamingMode(Settings, Journal, Boost, SettingCatalog.Power, TimeProvider.System);

        RecoverFromLastRun();
        Step("Recovery check done");
        Fps.Start();
        Step("FPS monitor ready");
        Boost.SetEnabled(Settings.GameBoost && !RecoveryMode);
    }

    /// <summary>Undo whatever a previous run (possibly a crash) left applied by temporary features.</summary>
    private void RecoverFromLastRun()
    {
        try
        {
            StartupReport.AddRange(Journal.RecoverTemporary([GamingMode.Owner], SettingCatalog.Resolve)
                .Where(m => !m.Contains("wasn't changed")));
            if (Boost.RecoverAfterCrash() is > 0 and var n)
                StartupReport.Add($"Restored {n} process priorit{(n == 1 ? "y" : "ies")} left from the last run");
        }
        catch (Exception e) { Log.Error("App", "Recovery from the last run failed", e); }
        foreach (var line in StartupReport) Log.Info("Recovery", line);
    }

    /// <summary>Called once the app has run normally for a while.</summary>
    public void MarkRunning()
    {
        if (State.UnfinishedStarts == 0) return;
        State.UnfinishedStarts = 0;
        State.Save();
    }

    public void LeaveRecoveryMode()
    {
        RecoveryMode = false;
        MarkRunning();
        Boost.SetEnabled(Settings.GameBoost);
        Log.Info("App", "Recovery mode turned off by the user");
    }

    // ---- sampling ----

    public void SampleFps()
    {
        if (!Fps.IsRunning)
        {
            Game = null;
            Latest = Latest with { Fps = null, FpsSource = null, FpsUnavailable = "Needs admin or Performance Log Users" };
            return;
        }
        var hwnd = Native.GetForegroundWindow();
        var pid = Native.ForegroundProcessId();
        var sample = pid == Environment.ProcessId ? null : Fps.Sample(pid, hwnd);
        var source = sample is null ? null : Fps.LastSource switch
        {
            FpsMonitor.Source.DirectX => "DXGI",
            FpsMonitor.Source.Kernel => "KERNEL",
            _ => null,
        };
        Latest = Latest with { Fps = sample, FpsSource = source, FpsUnavailable = null };

        Game = ProcessName(pid) is { } name
            ? GameDetector.Detect(new ForegroundWindow(name, Native.WindowTitle(hwnd), Native.IsFullscreen(hwnd), sample?.Fps), Settings)
            : null;

        if (Game != null)
        {
            RecentGames.RemoveAll(g => g.Process.Equals(Game.Process, StringComparison.OrdinalIgnoreCase));
            RecentGames.Insert(0, Game);
            if (RecentGames.Count > 8) RecentGames.RemoveAt(RecentGames.Count - 1);
        }
    }

    /// <summary>Games seen in front recently (newest first), so they can be added to the list with one click.</summary>
    public List<GameDetection> RecentGames { get; } = new();

    public void SampleStats()
    {
        var gpu = Gpu?.Read() ?? GpuSample.Of(GpuStatus.NotPresent);
        var cpuTemp = System.CpuTempC();
        var limits = _limits.Update(gpu.Limits | System.CpuLimits(cpuTemp));
        Latest = Latest with
        {
            Time = DateTime.Now,
            Gpu = gpu,
            CpuLoad = System.CpuLoad(),
            CpuTempC = cpuTemp,
            CpuClockGhz = System.CpuClockGhz(),
            Ram = System.Ram(),
            Limits = limits,
        };
        if (Settings.Alerts) CheckAlerts();
    }

    /// <summary>Set by the emergency stop; automatic optimizations stay off until resumed.</summary>
    public bool OptimizationsPaused { get; private set; }

    public void ResumeOptimizations()
    {
        OptimizationsPaused = false;
        Boost.SetEnabled(Settings.GameBoost && !RecoveryMode);
        Log.Info("Gaming", "Optimizations resumed");
    }

    private readonly BoostTarget _boostTarget = new(TimeProvider.System);

    public void TickGaming()
    {
        var game = RecoveryMode || OptimizationsPaused ? null : Game;
        Gaming.Tick(game);
        if (game is null && (RecoveryMode || OptimizationsPaused)) _boostTarget.Reset();
        Boost.Tick(_boostTarget.Update(game, TimeSpan.FromSeconds(Settings.GamingExitSeconds)), Settings.BoostTargets);
    }

    // ---- alerts ----

    private void CheckAlerts()
    {
        var t = Latest;
        var after = TimeSpan.FromSeconds(Settings.AlertAfterSeconds);
        const string fixes = "Try fans on Max in your laptop's control app, raise the back of the laptop for airflow, or pause heavy background work.";

        // Reported by the driver / Windows: the hardware is actually slowing down
        var gpuThrottling = _limits.ActiveFor(Limit.GpuThermal) >= after;
        var cpuThrottling = _limits.ActiveFor(Limit.CpuThermal) >= after;
        if (gpuThrottling)
            Raise(new("gpu-throttle", "GPU is slowing down from heat",
                $"The NVIDIA driver reports the GPU{Temp(t.Gpu.TempC)} is reducing its speed to cool off, so FPS will drop. {fixes}", true));
        if (cpuThrottling)
            Raise(new("cpu-throttle", "CPU is slowing down from heat",
                $"Windows reports the CPU{Temp(t.CpuTempC)} is capped below full speed while hot, which causes stutter. {fixes}", true));

        // Warnings based on your levels: hot, but not necessarily a problem. Skipped while the stronger,
        // confirmed alert above covers the same part.
        if (_gpuHot.Update(t.Gpu.TempC, Settings.GpuTempHot) >= after && !gpuThrottling)
            Raise(new("gpu-hot", "GPU running hot",
                $"GPU{Temp(t.Gpu.TempC)} has been above your {Settings.GpuTempHot} °C level for a while. This is a warning, not a fault; laptop GPUs are built to run warm.", false));
        if (_cpuHot.Update(t.CpuTempC, Settings.CpuTempHot) >= after && !cpuThrottling)
            Raise(new("cpu-hot", "CPU running hot",
                $"CPU{Temp(t.CpuTempC)} has been above your {Settings.CpuTempHot} °C level for a while. This is a warning, not a fault.", false));
    }

    private static string Temp(double? c) => c is { } v ? $" ({v:0} °C)" : "";

    private void Raise(AlertMessage alert)
    {
        if (!_throttle.ShouldNotify(alert.Key, TimeSpan.FromMinutes(Settings.AlertCooldownMinutes), out var shown)) return;
        if (shown == AlertThrottle.RepeatsBeforeBackoff)
            alert = alert with { Message = alert.Message + " Since this keeps happening, further reminders will come at most hourly while it continues." };
        else if (shown > AlertThrottle.RepeatsBeforeBackoff)
            alert = alert with { Title = alert.Title + " (still)" };
        Log.Warn("Alert", $"{alert.Title}. {alert.Message}");
        Alert?.Invoke(alert);
    }

    // ---- recovery actions ----

    /// <summary>Stops gaming mode and Game Boost right now and puts back what they changed.</summary>
    public List<string> StopOptimizations()
    {
        OptimizationsPaused = true;
        Gaming.Exit("stopped by you");
        Boost.RestoreAll();
        Boost.SetEnabled(false);
        var report = new List<string>(Gaming.Notices) { "Gaming mode and Game Boost are paused and their changes were undone. Resume them from the control center or tray." };
        Gaming.Notices.Clear();
        return report;
    }

    /// <summary>Undoes every Windows change the app still owns (Optimizer fixes included).</summary>
    public List<string> UndoEverything()
    {
        var report = StopOptimizations();
        foreach (var entry in Journal.Entries.ToList())
        {
            if (SettingCatalog.Resolve(entry.Id) is not { } setting) continue;
            report.AddRange(Journal.ReleaseAll(setting).Select(r => r.Message));
        }
        return report;
    }

    /// <summary>Undoes one change for all its owners (Diagnostics journal); restored only if it's still what the app set.</summary>
    public List<string> UndoChange(string id) =>
        SettingCatalog.Resolve(id) is { } setting
            ? Journal.ReleaseAll(setting).Select(r => r.Message).ToList()
            : new List<string> { "This change can't be undone from here (its setting isn't available on this PC)." };

    private static string? ProcessName(int pid) => Native.ProcessName(pid);

    public void Dispose()
    {
        Gaming.Exit("app closing");
        Boost.RestoreAll();
        Fps.Dispose();
        Gpu?.Dispose();
        System.Dispose();
    }
}
