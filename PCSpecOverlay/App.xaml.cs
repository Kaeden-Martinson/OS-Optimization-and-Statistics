using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using PCSpecOverlay.Monitors;
using PCSpecOverlay.Optimizer;

namespace PCSpecOverlay;

public partial class App : Application
{
    private const int HotkeyId = 0x5043;
    private const string OpenOptimizerEvent = @"Local\PCSpecOverlay.OpenOptimizer";

    private Mutex? _singleInstance;
    private EventWaitHandle? _openOptimizer;
    private Settings _settings = new();
    private OverlayWindow _overlay = null!;
    private TrayIcon _tray = null!;

    private NvidiaGpu? _gpu;
    private SystemStats _system = null!;
    private readonly FpsMonitor _fps = new();
    private readonly GameBoost _boost = new();
    private GamingMode _gaming = null!;
    private readonly List<DispatcherTimer> _timers = [];
    private OptimizerWindow? _optimizer;
    private string? _gameInFront; // process name of the foreground app while it renders like a game

    private const double GameFpsThreshold = 30; // below this the foreground app is idle UI, not a game

    private readonly LimitTracker _limits = new();
    private readonly Dictionary<string, long> _lastAlert = new();
    private static readonly TimeSpan AlertAfter = TimeSpan.FromSeconds(10); // ignore brief spikes
    private const long AlertCooldownMs = 10 * 60_000;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(false, @"Local\PCSpecOverlay.SingleInstance");
        bool owned;
        try { owned = _singleInstance.WaitOne(TimeSpan.FromSeconds(3)); } // allow an elevating restart to hand over
        catch (AbandonedMutexException) { owned = true; }
        if (!owned)
        {
            // Already running: launching again opens the optimizer in the running instance
            if (EventWaitHandle.TryOpenExisting(OpenOptimizerEvent, out var open)) using (open) open.Set();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _openOptimizer = new EventWaitHandle(false, EventResetMode.AutoReset, OpenOptimizerEvent);
        ThreadPool.RegisterWaitForSingleObject(_openOptimizer,
            (_, _) => Dispatcher.BeginInvoke(OpenOptimizer), null, Timeout.Infinite, executeOnlyOnce: false);

        // A small overlay renders fine on the CPU; hardware rendering would load the GPU driver into this
        // process (~200 MB) and keep the GPU busy compositing a layered window.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        _settings = Settings.Load();
        _gpu = NvidiaGpu.TryOpen();
        _system = new SystemStats();
        if (!_fps.Start() && _fps.LastError is { } fpsError)
            Log($"FPS monitor failed to start: {fpsError}");

        _overlay = new OverlayWindow();
        _overlay.SetGpuName(_gpu?.Name ?? "");
        _overlay.ApplySettings(_settings);
        _overlay.EnsureHandle();
        HwndSource.FromHwnd(_overlay.Handle).AddHook(WndProc);

        var hotkeyOk = RegisterHotkey(_settings.Hotkey);
        _tray = new TrayIcon(this, hotkeyOk ? _settings.Hotkey : null);

        if (_settings.Visible) _overlay.Show();

        UpdateSystem();
        UpdateFps();
        StartTimer(TimeSpan.FromMilliseconds(500), UpdateFps);
        StartTimer(TimeSpan.FromSeconds(1), UpdateSystem);
        StartTimer(TimeSpan.FromSeconds(2), _overlay.KeepOnTop);
        _boost.SetEnabled(_settings.GameBoost);
        _gaming = new GamingMode(_settings, _boost);
        _gaming.RestorePowerMode(); // in case we exited mid-game last time
        StartTimer(TimeSpan.FromSeconds(2), UpdateBoost);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(_overlay.Reposition);

        if (!hotkeyOk)
            _tray.Notify($"The hotkey {_settings.Hotkey} is in use by another app. Change it in settings.json.");
    }

    public bool OverlayVisible => _overlay.IsVisible;
    public string Position => _settings.Position;
    public bool FpsAvailable => _fps.IsRunning;
    public bool GameBoostEnabled => _boost.Enabled;

    public void SetGameBoost(bool enabled)
    {
        _boost.SetEnabled(enabled);
        _settings.GameBoost = enabled;
        _settings.Save();
        UpdateBoost();
    }

    public void OpenOptimizer()
    {
        if (_optimizer is null)
        {
            _optimizer = new OptimizerWindow(this, _settings);
            _optimizer.Closed += (_, _) => _optimizer = null;
            _optimizer.Show();
        }
        if (_optimizer.WindowState == WindowState.Minimized) _optimizer.WindowState = WindowState.Normal;
        _optimizer.Activate();
    }

    public void ToggleOverlay()
    {
        if (_overlay.IsVisible) _overlay.Hide();
        else
        {
            _overlay.Show();
            _overlay.KeepOnTop();
        }
        _settings.Visible = _overlay.IsVisible;
        _settings.Save();
    }

    public void SetPosition(string corner)
    {
        _settings.Position = corner;
        _settings.Save();
        _overlay.ApplySettings(_settings);
    }

    public void OpenSettingsFile()
    {
        _settings.Save();
        Process.Start(new ProcessStartInfo(Settings.FilePath) { UseShellExecute = true });
    }

    public void RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Shutdown();
        }
        catch { /* UAC declined */ }
    }

    private void UpdateFps()
    {
        if (!_fps.IsRunning)
        {
            _gameInFront = null;
            if (_overlay.IsVisible) _overlay.ShowFps(null, "FPS needs admin or Performance Log Users");
            return;
        }
        // Sampled even while the overlay is hidden: Game Boost and gaming mode need to know when a game is in front
        var hwnd = Native.GetForegroundWindow();
        var pid = Native.ForegroundProcessId();
        var sample = pid == Environment.ProcessId ? null : _fps.Sample(pid);
        _gameInFront = sample?.Fps >= GameFpsThreshold && ProcessName(pid) is { } name ? GameName(hwnd, name) : null;
        if (_overlay.IsVisible) _overlay.ShowFps(sample, null);
    }

    /// <summary>
    /// A window rendering steadily is only a game if it's fullscreen or on the user's games list;
    /// otherwise animated apps (chat apps, videos, browsers) would count too.
    /// </summary>
    /// <returns>The process name (what Game Boost raises), or null when it isn't a game.</returns>
    private string? GameName(IntPtr hwnd, string process)
    {
        if (process.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return null;

        var title = Native.WindowTitle(hwnd);
        var listed = _settings.Games.FirstOrDefault(g => g.Equals(process, StringComparison.OrdinalIgnoreCase) ||
                                                         title.Contains(g, StringComparison.OrdinalIgnoreCase));
        if (listed is null && !Native.IsFullscreen(hwnd)) return null;
        _gameLabel = listed ?? process;
        return process;
    }

    private string? _gameLabel; // what to call the game in the UI, e.g. "Starve.io" rather than "msedge"

    private void UpdateBoost()
    {
        _gaming.Tick(_gameInFront is null ? null : _gameLabel);
        _boost.Tick(_gameInFront, _settings.BoostTargets);
        _overlay.ShowBoost(_boost.Active, _gaming.Active);
    }

    public bool GamingModeActive => _gaming.Active;
    public bool AutoGamingModeEnabled => _settings.AutoGamingMode;
    public string? GamingGame => _gaming.Active ? _gaming.Game : null;

    public void SetAutoGamingMode(bool enabled)
    {
        _settings.AutoGamingMode = enabled;
        _settings.Save();
        UpdateBoost();
    }

    private static string? ProcessName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch { return null; }
    }

    private void UpdateSystem()
    {
        var cpuLoad = _system.CpuLoad(); // sample even while hidden so the next delta is short
        if (!_overlay.IsVisible && !_settings.Alerts) return;

        var gpu = _gpu?.Read();
        var cpuTemp = _system.CpuTempC();
        var limits = _limits.Update((gpu?.Limits ?? Limit.None) | _system.CpuLimits(cpuTemp));
        if (_settings.Alerts) CheckAlerts(gpu, cpuTemp);

        if (!_overlay.IsVisible) return;
        _overlay.ShowGpu(gpu);
        _overlay.ShowCpu(cpuLoad, cpuTemp, _system.CpuClockGhz(), _system.Ram());
        _overlay.ShowLimits(limits);
    }

    private void CheckAlerts(GpuSample? gpu, double? cpuTemp)
    {
        const string fixes = "Set NitroSense fans to Max, raise the back of the laptop for airflow, or pause heavy jobs like AI training.";

        if (_limits.ActiveFor(Limit.GpuThermal) >= AlertAfter)
            Alert("gpu-hot", "GPU is overheating",
                $"Your GPU{(gpu?.TempC is { } t ? $" is at {t} °C and" : " is")} slowing itself down to cool off, so FPS will drop. {fixes}");

        if (_limits.ActiveFor(Limit.CpuThermal) >= AlertAfter)
            Alert("cpu-hot", "CPU is overheating",
                $"Your CPU{(cpuTemp is { } c ? $" is at {c:0} °C and" : " is")} slowing itself down to cool off, which causes stutter. {fixes}");
    }

    private void Alert(string key, string title, string message)
    {
        var now = Environment.TickCount64;
        if (_lastAlert.TryGetValue(key, out var last) && now - last < AlertCooldownMs) return;
        _lastAlert[key] = now;
        _tray.Notify(message, title);
        Log($"Alert: {title}. {message}");
    }

    public bool AlertsEnabled => _settings.Alerts;

    public void SetAlerts(bool enabled)
    {
        _settings.Alerts = enabled;
        _settings.Save();
    }

    private static void Log(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Settings.FilePath)!, "overlay.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { /* logging is best-effort */ }
    }

    private void StartTimer(TimeSpan interval, Action tick)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = interval };
        timer.Tick += (_, _) => tick();
        timer.Start();
        _timers.Add(timer);
    }

    private bool RegisterHotkey(string text)
    {
        uint mods = Native.MOD_NOREPEAT;
        Key key = Key.None;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "alt": mods |= Native.MOD_ALT; break;
                case "ctrl" or "control": mods |= Native.MOD_CONTROL; break;
                case "shift": mods |= Native.MOD_SHIFT; break;
                case "win": mods |= Native.MOD_WIN; break;
                default: Enum.TryParse(part, ignoreCase: true, out key); break;
            }
        }
        return key != Key.None &&
               Native.RegisterHotKey(_overlay.Handle, HotkeyId, mods, (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            ToggleOverlay();
            handled = true;
        }
        return IntPtr.Zero;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        foreach (var t in _timers) t.Stop();
        _gaming?.Exit();
        _boost.RestoreAll();
        if (_overlay?.Handle is { } h && h != IntPtr.Zero) Native.UnregisterHotKey(h, HotkeyId);
        _tray?.Dispose();
        _fps.Dispose();
        _gpu?.Dispose();
        _system?.Dispose();
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
