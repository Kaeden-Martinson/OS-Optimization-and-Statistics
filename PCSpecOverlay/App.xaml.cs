using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using PCSpecOverlay.Core;
using PCSpecOverlay.Monitors;

namespace PCSpecOverlay;

public partial class App : Application
{
    private const int HotkeyId = 0x5043;
    private const string OpenControlCenterEvent = @"Local\PCSpecOverlay.OpenOptimizer";

    private Mutex? _singleInstance;
    private EventWaitHandle? _openControlCenter, _exitRequest;
    private Engine _engine = null!;
    private ThemeManager _theme = null!;
    private OverlayWindow _overlay = null!;
    private TrayIcon _tray = null!;
    private ControlCenterWindow? _controlCenter;
    private HistoryRecorder _history = null!;
    private DispatcherTimer _fpsTimer = null!, _statsTimer = null!;
    private readonly List<DispatcherTimer> _timers = [];
    private string? _registeredHotkey;
    private string? _layoutOverride;

    internal Engine Engine => _engine;
    internal HistoryRecorder History => _history;
    public Settings Settings => _engine.Settings;
    public ThemeManager Theme => _theme;
    public bool OverlayVisible => _overlay.IsVisible;
    public string? HotkeyInUse => _registeredHotkey;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.DataDirectory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCSpecOverlay" + AppIdentity.Suffix);
        if (Installer.HandleCommandLine(e.Args)) { Shutdown(); return; }

        _singleInstance = new Mutex(false, @"Local\PCSpecOverlay.SingleInstance" + AppIdentity.Suffix);
        bool owned;
        try { owned = _singleInstance.WaitOne(TimeSpan.FromSeconds(10)); } // a restarting or updating copy may still be closing
        catch (AbandonedMutexException) { owned = true; } // previous instance crashed
        if (!owned)
        {
            // Already running: launching again opens the control center in the running instance
            if (EventWaitHandle.TryOpenExisting(OpenControlCenterEvent + AppIdentity.Suffix, out var open)) using (open) open.Set();
            _singleInstance = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("App", "Unexpected error", args.Exception);
            args.Handled = true; // keep running; the error is visible in Diagnostics
        };

        _openControlCenter = new EventWaitHandle(false, EventResetMode.AutoReset, OpenControlCenterEvent + AppIdentity.Suffix);
        ThreadPool.RegisterWaitForSingleObject(_openControlCenter,
            (_, _) => Dispatcher.BeginInvoke(OpenControlCenter), null, Timeout.Infinite, executeOnlyOnce: false);
        // The installer asks a running copy to exit (cleanly, restoring what it changed) before replacing it
        _exitRequest = new EventWaitHandle(false, EventResetMode.AutoReset, Installer.ExitEventName);
        ThreadPool.RegisterWaitForSingleObject(_exitRequest,
            (_, _) => Dispatcher.BeginInvoke(Shutdown), null, Timeout.Infinite, executeOnlyOnce: true);

        Log.Info("App", $"Starting {Installer.Version} from {Installer.Describe(Environment.ProcessPath)}");
        _engine = new Engine();

        // By default the overlay draws on the CPU: GPU rendering loads the graphics driver into this process
        // (~100 MB more). Optional, because on some PCs it lowers CPU use. Must be set before any window exists.
        StartedWithGpuRendering = Settings.GpuRendering;
        if (!StartedWithGpuRendering) System.Windows.Media.RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        Log.Info("App", $"Drawing with the {(StartedWithGpuRendering ? "graphics chip" : "CPU")}");
        _theme = new ThemeManager(Resources);
        _theme.Apply(Settings.Appearance);
        _engine.Alert += a => _tray?.Notify(a.Message, a.Title, a.DriverConfirmed);
        _history = new HistoryRecorder(_engine.Settings);

        _overlay = new OverlayWindow();
        _overlay.SetGpuName(_engine.Gpu?.Name);
        _overlay.ApplySettings(Settings);
        _overlay.SetAppTheme(_theme.Current);
        _theme.Changed += _overlay.SetAppTheme;
        _overlay.EnsureHandle();
        _overlay.Moved += SaveSettings;
        _overlay.Moved += () => _controlCenter?.OverlayMoved();
        _overlay.Pinned += () => _tray?.Notify("It stays here and clicks pass through to your game. Use Move overlay in the tray to move it again.", "Overlay pinned");
        HwndSource.FromHwnd(_overlay.Handle).AddHook(WndProc);

        var hotkeyError = TryRegisterHotkey(Settings.Hotkey);
        _tray = new TrayIcon(this);
        if (Settings.Visible) _overlay.Show();

        _fpsTimer = StartTimer(TimeSpan.FromMilliseconds(Settings.FpsRefreshMs), UpdateFps);
        _statsTimer = StartTimer(TimeSpan.FromMilliseconds(Settings.StatsRefreshMs), UpdateStats);
        StartTimer(TimeSpan.FromSeconds(2), UpdateGaming);
        StartTimer(TimeSpan.FromSeconds(2), _overlay.KeepOnTop);
        var running = StartTimer(TimeSpan.FromSeconds(60), () => _engine.MarkRunning());
        running.Tick += (_, _) => running.Stop();
        // Update check: shortly after start (not during sign-in), then hourly; it only contacts GitHub once a day
        StartTimer(TimeSpan.FromHours(1), () => _ = CheckForUpdatesAsync(automatic: true));
        var firstCheck = StartTimer(TimeSpan.FromMinutes(2), () => _ = CheckForUpdatesAsync(automatic: true));
        firstCheck.Tick += (_, _) => firstCheck.Stop();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        UpdateStats();
        UpdateFps();

        if (e.Args.Contains("--startup-on", StringComparer.OrdinalIgnoreCase) && StartupTask.SetEnabled(true) is { } startupError)
            _tray.Notify(startupError, "Start with Windows", true);
        StartupTask.RepairIfMoved();
        if (hotkeyError != null) _tray.Notify(hotkeyError, "Hotkey not available");
        if (_engine.RecoveryMode)
            _tray.Notify("The app didn't start normally several times in a row, so automatic optimizations are off. Open the control center to turn them back on.", "Recovery mode", true);
        else if (_engine.StartupReport.Count > 0)
            _tray.Notify(string.Join(" ", _engine.StartupReport), "Restored settings from the last run");
    }

    private void OnDisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(_overlay.Reposition);

    // ---- periodic work ----

    private bool _statsChanged;

    /// <summary>
    /// The overlay is repainted only here: once per FPS tick, including any new sensor readings.
    /// It renders on the CPU, so every repaint costs; one combined repaint instead of one per timer
    /// roughly halves the overlay's CPU use.
    /// </summary>
    private void UpdateFps()
    {
        _engine.SampleFps(); // also when hidden: gaming mode needs to know when a game is in front
        if (!_overlay.IsVisible) return;
        _overlay.ShowFps(_engine.Latest);
        if (_statsChanged)
        {
            _overlay.ShowStats(_engine.Latest);
            _statsChanged = false;
        }
    }

    private void UpdateStats()
    {
        // Sensors are only read when something uses them, so a hidden overlay costs nothing
        var needed = _overlay.IsVisible || Settings.Alerts || _controlCenter != null || _engine.Gaming.Active;
        if (!needed) return;
        _engine.SampleStats();
        _history.Record(_engine.Latest, _engine.Game?.Label);
        _statsChanged = true;
    }

    private void UpdateGaming()
    {
        _engine.TickGaming();
        _overlay.ShowStatus(new OverlayStatus(_engine.Gaming.Active, _engine.Boost.Active, _engine.OptimizationsPaused));

        // Per-game overlay layout
        var layout = _engine.Game?.Rule?.Layout;
        if (layout != _layoutOverride)
        {
            _layoutOverride = layout;
            _overlay.ApplySettings(Settings, layout);
        }
    }

    private DispatcherTimer StartTimer(TimeSpan interval, Action tick)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = interval };
        timer.Tick += (_, _) =>
        {
            try { tick(); }
            catch (Exception ex) { Log.Error("App", "Periodic update failed", ex); }
        };
        timer.Start();
        _timers.Add(timer);
        return timer;
    }

    // ---- actions used by the tray and control center ----

    public void OpenControlCenter()
    {
        if (_controlCenter is null)
        {
            _controlCenter = new ControlCenterWindow(this);
            _controlCenter.Closed += (_, _) => _controlCenter = null;
            _controlCenter.Show();
        }
        if (_controlCenter.WindowState == WindowState.Minimized) _controlCenter.WindowState = WindowState.Normal;
        _controlCenter.Activate();
    }

    /// <summary>Shows the overlay if needed and unlocks it for dragging; Pin on the overlay locks it again.</summary>
    public void MoveOverlay()
    {
        if (!_overlay.IsVisible) ToggleOverlay();
        _overlay.BeginMove();
    }

    public void ToggleOverlay()
    {
        if (_overlay.IsVisible) _overlay.Hide();
        else
        {
            _overlay.Show();
            _overlay.ShowFps(_engine.Latest);
            _overlay.ShowStats(_engine.Latest);
            _overlay.KeepOnTop();
        }
        Settings.Visible = _overlay.IsVisible;
        Settings.Save();
    }

    /// <summary>Validates, saves and applies settings after any change in the control center.</summary>
    public void SaveSettings()
    {
        Settings.Validate();
        Settings.Save();
        _overlay.ApplySettings(Settings, _layoutOverride);
        _fpsTimer.Interval = TimeSpan.FromMilliseconds(Settings.FpsRefreshMs);
        _statsTimer.Interval = TimeSpan.FromMilliseconds(Settings.StatsRefreshMs);
        if (_engine.Gpu != null) _engine.Gpu.LetSleep = Settings.LetGpuSleep;
        if (!_engine.OptimizationsPaused && !_engine.RecoveryMode && !_engine.Gaming.Active)
            _engine.Boost.SetEnabled(Settings.GameBoost);
        _theme.Apply(Settings.Appearance);
    }

    /// <summary>Restores default preferences. Your games list and lowered-apps list are kept.</summary>
    public void ResetSettings()
    {
        var fresh = new Settings { Visible = Settings.Visible, GameRules = Settings.GameRules, BoostTargets = Settings.BoostTargets };
        foreach (var p in typeof(Settings).GetProperties().Where(p => p.CanWrite && p.GetIndexParameters().Length == 0))
            p.SetValue(Settings, p.GetValue(fresh));
        SaveSettings();
        TryRegisterHotkey(Settings.Hotkey);
        Log.Info("Settings", "Settings restored to defaults");
    }

    public bool StartedWithGpuRendering { get; private set; }

    // ---- updates ----

    public UpdateResult? LastUpdateCheck { get; private set; }

    /// <summary>Checks GitHub for a newer release. Automatic checks run at most once a day and announce each version once.</summary>
    public async Task<UpdateResult> CheckForUpdatesAsync(bool automatic)
    {
        if (automatic && (!Settings.CheckForUpdates || string.IsNullOrEmpty(Updates.Repository) ||
                          DateTime.UtcNow - _engine.State.LastUpdateCheckUtc < TimeSpan.FromDays(1)))
            return LastUpdateCheck ?? new UpdateResult(UpdateState.UpToDate, null, "");

        using var http = Updates.CreateClient();
        var current = Version.Parse(AppIdentity.Version);
        var result = await Updates.CheckAsync(http, current);
        LastUpdateCheck = result;
        if (result.State != UpdateState.Failed) _engine.State.LastUpdateCheckUtc = DateTime.UtcNow;

        if (automatic && result is { State: UpdateState.Available, Release: { } r } && _engine.State.AnnouncedVersion != r.Version.ToString())
        {
            _engine.State.AnnouncedVersion = r.Version.ToString();
            _tray.Notify($"{r.Title} is available. Open the control center (Settings › Updates) to install it.", "Update available");
        }
        _engine.State.Save();
        Log.Info("Updates", result.Message);
        return result;
    }

    /// <summary>Restarts the app (e.g. to switch rendering). The new copy waits for this one to exit.</summary>
    public void RestartApp()
    {
        Log.Info("App", "Restarting at the user's request");
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
        Shutdown();
    }

    public List<string> StopOptimizations() => _engine.StopOptimizations();
    public void ResumeOptimizations() => _engine.ResumeOptimizations();

    // ---- hotkey ----

    /// <returns>Null on success, otherwise why it couldn't be registered (the previous hotkey stays active).</returns>
    public string? TryRegisterHotkey(string text)
    {
        if (!Hotkeys.TryParse(text, out var mods, out var key))
            return $"\"{text}\" isn't a valid shortcut. Use something like Alt+Shift+O.";

        var previous = _registeredHotkey;
        if (previous != null) Native.UnregisterHotKey(_overlay.Handle, HotkeyId);
        if (Native.RegisterHotKey(_overlay.Handle, HotkeyId, mods | Native.MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(key)))
        {
            _registeredHotkey = Hotkeys.Format(mods, key);
            Settings.Hotkey = _registeredHotkey;
            Settings.Save();
            Log.Info("Hotkey", $"Using {_registeredHotkey}");
            return null;
        }

        Log.Warn("Hotkey", $"{text} is already used by another app");
        if (previous != null && Hotkeys.TryParse(previous, out var pm, out var pk))
            Native.RegisterHotKey(_overlay.Handle, HotkeyId, pm | Native.MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(pk));
        else _registeredHotkey = null;
        return $"{text} is already used by another app" + (previous != null ? $"; still using {previous}." : ". Choose another shortcut in the control center.");
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
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        _theme?.Dispose();
        if (_engine != null)
        {
            _history?.Flush();
            _engine.Dispose(); // ends gaming mode and Game Boost, restoring what they changed
            _engine.MarkRunning(); // a clean exit isn't a failed start
            Log.Info("App", "Exited cleanly");
        }
        if (_overlay?.Handle is { } h && h != IntPtr.Zero) Native.UnregisterHotKey(h, HotkeyId);
        _tray?.Dispose();
        try { _singleInstance?.ReleaseMutex(); } catch { /* not owned */ }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}

/// <summary>Parsing and formatting of shortcuts like "Ctrl+Alt+P".</summary>
public static class Hotkeys
{
    public static bool TryParse(string text, out uint mods, out Key key)
    {
        mods = 0;
        key = Key.None;
        foreach (var part in (text ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "alt": mods |= Native.MOD_ALT; break;
                case "ctrl" or "control": mods |= Native.MOD_CONTROL; break;
                case "shift": mods |= Native.MOD_SHIFT; break;
                case "win": mods |= Native.MOD_WIN; break;
                default:
                    if (key != Key.None || !Enum.TryParse(part, ignoreCase: true, out key)) return false;
                    break;
            }
        }
        // A bare letter would steal normal typing; require a modifier unless it's a function key
        var functionKey = key is >= Key.F1 and <= Key.F24;
        return key != Key.None && (mods != 0 || functionKey);
    }

    public static string Format(uint mods, Key key)
    {
        var parts = new List<string>();
        if ((mods & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & Native.MOD_ALT) != 0) parts.Add("Alt");
        if ((mods & Native.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((mods & Native.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }
}
