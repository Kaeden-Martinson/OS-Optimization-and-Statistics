using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PCSpecOverlay.Core;
using PCSpecOverlay.Monitors;
using PCSpecOverlay.Optimizer;

namespace PCSpecOverlay;

/// <summary>
/// The control center: live overview, optimizer, gaming, overlay, alerts, general settings and
/// diagnostics. Settings controls bind straight to <see cref="Settings"/>; any change is saved and
/// applied ~0.3 s later through <see cref="App.SaveSettings"/>, the single place settings are applied.
/// </summary>
public partial class ControlCenterWindow : Window
{
    private const int AppRows = 8;
    private const int PreviewMargin = 6;

    private readonly App _app;
    private readonly Engine _engine;
    private readonly Settings _settings;
    private readonly List<Check> _checks;
    private readonly ObservableCollection<GameRule> _rules;
    private readonly ObservableCollection<AppRow> _appRows = new();
    private readonly ProcessSampler _sampler = new();
    private readonly DispatcherTimer _refresh, _saveSoon;
    private bool _loading = true, _restarting;
    private int _tick;

    internal ControlCenterWindow(App app)
    {
        _app = app;
        _engine = app.Engine;
        _settings = app.Settings;
        _rules = new ObservableCollection<GameRule>(_settings.GameRules);
        InitializeComponent();
        VersionLabel.Text = AppIdentity.Version + (AppIdentity.IsDevBuild ? " dev" : "");
        DataContext = _settings;

        // Center on the primary screen ourselves: CenterScreen misplaces windows across monitors with different scaling
        var area = SystemParameters.WorkArea;
        Height = Math.Min(Height, area.Height - 40);
        Width = Math.Min(Width, area.Width - 40);
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
        SourceInitialized += (_, _) => TitleBar.Apply(this, app.Theme.Current == AppTheme.Dark);
        app.Theme.Changed += OnThemeChanged;

        Fill(DisplayBox, Displays.All().Select(d => ((object)(d.Primary ? "" : d.DeviceName), d.Label)));
        Fill(FpsRefreshBox, new (object, string)[] { (250, "4 times a second"), (500, "Twice a second"), (1000, "Once a second") });
        Fill(StatsRefreshBox, new (object, string)[] { (500, "Twice a second"), (1000, "Once a second"), (2000, "Every 2 seconds"), (5000, "Every 5 seconds") });

        _checks = Checks.Create(_engine.Journal, _engine.State);
        CheckList.ItemsSource = _checks;
        AppList.ItemsSource = _appRows;
        RuleList.ItemsSource = _rules;
        GpuScale.LevelsChanged += (warn, hot) => { _settings.GpuTempWarn = warn; _settings.GpuTempHot = hot; ScheduleSave(); };
        CpuScale.LevelsChanged += (warn, hot) => { _settings.CpuTempWarn = warn; _settings.CpuTempHot = hot; ScheduleSave(); };

        // Any edit to a bound control schedules one save
        _saveSoon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveSoon.Tick += (_, _) => { _saveSoon.Stop(); Save(); };
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnEdited));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(OnEdited));
        AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((s, e) => OnEdited(s, e)));
        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(OnEdited));
        AddHandler(UIElement.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((s, e) => { if (e.OriginalSource is TextBox t && !t.IsReadOnly) OnEdited(s, e); }));

        _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refresh.Tick += (_, _) => Refresh();
        _refresh.Start();
        Closed += (_, _) => { _app.Theme.Changed -= OnThemeChanged; _refresh.Stop(); if (_saveSoon.IsEnabled) { _saveSoon.Stop(); Save(); } };
        Loaded += (_, _) => { _loading = false; SyncThemeButtons(); RefreshAll(); };
    }

    private static void Fill(ComboBox box, IEnumerable<(object Value, string Label)> items)
    {
        foreach (var (value, label) in items) box.Items.Add(new ComboBoxItem { Content = label, Tag = value });
    }

    private void OnEdited(object sender, RoutedEventArgs e)
    {
        if (_loading || e.OriginalSource is TabControl) return;
        if (e.OriginalSource == ShowOverlay || e.OriginalSource == StartupSwitch) return; // handled by their own clicks
        if (e.OriginalSource == LightButton || e.OriginalSource == DarkButton) return; // saved by ChooseTheme
        if (e.OriginalSource is RadioButton) return; // layout cards and appearance segments save through their own clicks
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        if (_loading) return;
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    /// <summary>Sets controls from settings without that counting as an edit.</summary>
    private void Quietly(Action set)
    {
        var was = _loading;
        _loading = true;
        try { set(); }
        finally { _loading = was; }
    }

    private void Save()
    {
        _settings.GameRules = _rules.ToList();
        _app.SaveSettings();
        if (_settings.GameRules.Count != _rules.Count) // validation dropped empty or duplicate rules
        {
            _rules.Clear();
            foreach (var r in _settings.GameRules) _rules.Add(r);
        }
        UpdateBoostTargets();
        UpdateRestartHint();
        RefreshPreview();
    }

    private void OnRestartApp(object sender, RoutedEventArgs e) => _app.RestartApp();

    // ---- updates ----

    private void ShowUpdate(UpdateResult? r)
    {
        VersionValue.Text = AppIdentity.Version + (AppIdentity.IsDevBuild ? " dev" : "");
        UpdateStatus.Text = r?.Message is { Length: > 0 } m ? m
            : string.IsNullOrEmpty(Updates.Repository) ? "Updates aren't set up yet: this build doesn't know where releases are published."
            : "Not checked yet.";
        var available = r is { State: UpdateState.Available, Release: { } };
        InstallUpdateButton.Visibility = available && r!.Release!.CanInstall ? Visibility.Visible : Visibility.Collapsed;
        ReleasePageButton.Visibility = r?.Release?.PageUrl is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        CheckUpdatesButton.IsEnabled = !string.IsNullOrEmpty(Updates.Repository);
        if (available && !r!.Release!.CanInstall)
            UpdateStatus.Text += " This release has no verified setup file, so install it from its page.";
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateStatus.Text = "Checking…";
        ShowUpdate(await _app.CheckForUpdatesAsync(automatic: false));
    }

    private async void OnInstallUpdate(object sender, RoutedEventArgs e)
    {
        if (_app.LastUpdateCheck?.Release is not { } release) return;
        if (!Confirm($"Download and install {release.Title}?\n\nThe download is checked against its published SHA-256 before anything runs. The installer then asks before replacing the app, closes this copy cleanly (putting back anything it changed) and keeps the current version as a fallback.", "Install update"))
            return;
        InstallUpdateButton.IsEnabled = false;
        try
        {
            using var http = Updates.CreateClient();
            var progress = new Progress<double>(p => UpdateStatus.Text = $"Downloading… {p:P0}");
            var setup = await Updates.DownloadAsync(http, release, progress);
            UpdateStatus.Text = "Verified. Starting the installer…";
            Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UpdateStatus.Text = ex.Message;
            InstallUpdateButton.IsEnabled = true;
        }
    }

    private void OnReleasePage(object sender, RoutedEventArgs e)
    {
        if (_app.LastUpdateCheck?.Release?.PageUrl is { Length: > 0 } url && url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>Rendering mode only changes on restart, so offer one when the switch differs from the running mode.</summary>
    private void UpdateRestartHint() =>
        RestartAppButton.Visibility = _settings.GpuRendering != _app.StartedWithGpuRendering ? Visibility.Visible : Visibility.Collapsed;

    // ---- refresh ----

    private void OnPageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource == Nav && !_loading) RefreshAll();
    }

    private void OnLightTheme(object sender, RoutedEventArgs e) => ChooseTheme(Settings.Light);

    private void OnAppearance(object sender, RoutedEventArgs e) => ChooseTheme((string)((FrameworkElement)sender).Tag);

    private void OnDarkTheme(object sender, RoutedEventArgs e) => ChooseTheme(Settings.Dark);

    private void OnMoveOverlay(object sender, RoutedEventArgs e) => _app.MoveOverlay();

    /// <summary>The overlay was dragged or pinned: show its new display and spot without saving again.</summary>
    public void OverlayMoved()
    {
        Quietly(() => DisplayBox.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateTarget());
        RefreshPreview();
    }

    /// <summary>The tab-bar switch and the Appearance segments both land here; the switch picks Light or Dark outright.</summary>
    private void ChooseTheme(string appearance)
    {
        _settings.Appearance = appearance;
        _app.SaveSettings();
        SyncThemeButtons();
    }

    /// <summary>The pressed half of the switch shows the theme in use (also when it follows Windows).</summary>
    private void SyncThemeButtons()
    {
        LightButton.IsChecked = _app.Theme.Current == AppTheme.Light;
        DarkButton.IsChecked = _app.Theme.Current == AppTheme.Dark;
        Quietly(() =>
        {
            AppearanceLight.IsChecked = _settings.Appearance == Settings.Light;
            AppearanceDark.IsChecked = _settings.Appearance == Settings.Dark;
            AppearanceMatch.IsChecked = _settings.Appearance == Settings.MatchWindows;
        });
        AppearanceValue.Text = _settings.Appearance == Settings.MatchWindows
            ? $"Match Windows ({(_app.Theme.Current == AppTheme.Dark ? "dark" : "light")})"
            : _settings.Appearance;
    }

    /// <summary>Brushes made in code (banners, charts, temperature colors) are rebuilt by the next refresh.</summary>
    private void OnThemeChanged(AppTheme theme)
    {
        SyncThemeButtons();
        TitleBar.Apply(this, theme == AppTheme.Dark);
        HotkeyResult.Text = "";
        RefreshAll();
    }

    private void RefreshAll()
    {
        _tick = 0;
        Refresh();
        RefreshSlow();
    }

    private void Refresh()
    {
        _tick++;
        switch (Nav.SelectedIndex)
        {
            case 0: RefreshOverview(); break;
            case 1: if (_tick % 2 == 1) RefreshOptimizer(); break;
            case 2: RefreshGaming(); break;
            case 3: RefreshPreview(); break;
            case 4: RefreshAlerts(); break;
            case 6: if (_tick % 3 == 1) RefreshDiagnostics(); break;
        }
    }

    /// <summary>Things that change rarely: read when a page is opened.</summary>
    private void RefreshSlow()
    {
        ShowOverlay.IsChecked = _app.OverlayVisible;
        Quietly(() =>
        {
            StripCard.IsChecked = _settings.Layout == Settings.Compact;
            PanelCard.IsChecked = _settings.Layout == Settings.Expanded;
        });
        RefreshPreview();
        HotkeyHint.Text = _app.HotkeyInUse is { } h ? $"Shortcut: {h}" : "No shortcut is active; set one on the Settings page.";
        HotkeyBox.Text = _app.HotkeyInUse ?? _settings.Hotkey;
        var startup = StartupTask.Status();
        StartupSwitch.IsChecked = startup.Enabled;
        StartupValue.Text = startup.Enabled ? "With Windows" : "Off";
        StartupStatus.Text = startup.Describe() + (AppIdentity.IsDevBuild ? " · This is a development build; it uses its own startup entry." : "");
        UpdateBoostTargets();
        UpdateRestartHint();
        ShowUpdate(_app.LastUpdateCheck);

        var sessions = HistoryRecorder.LoadSessions(10);
        SessionList.ItemsSource = sessions.Select(DescribeSession).ToList();
        SessionsEmpty.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GpuScale.SetLevels(_settings.GpuTempWarn, _settings.GpuTempHot);
        CpuScale.SetLevels(_settings.CpuTempWarn, _settings.CpuTempHot);
        GpuSource.Text = _engine.Gpu is { } gpu ? $"{gpu.Name.Replace("NVIDIA ", "").Replace("GeForce ", "")} · NVIDIA driver" : "No NVIDIA GPU found";
        CpuSource.Text = _engine.System is { Ready: true, ThermalZones: 0 } ? "No thermal zones exposed by this PC" : "Hottest ACPI thermal zone (package, not per core)";
        RefreshAlerts();
        if (Nav.SelectedIndex == 6) RefreshDiagnostics();
    }

    // ---- alerts ----

    private void RefreshAlerts()
    {
        var t = _engine.Latest;
        GpuScale.SetCurrent(t.Gpu.TempC);
        CpuScale.SetCurrent(t.CpuTempC);
        GpuNow.Text = t.Gpu.TempC is { } g ? $"{g:0} °C" : t.Gpu.Status == GpuStatus.Sleeping ? "Sleeping" : "—";
        GpuNow.Foreground = Temp(t.Gpu.TempC, _settings.GpuTempWarn, _settings.GpuTempHot);
        CpuNow.Text = t.CpuTempC is { } c ? $"{c:0} °C" : "—";
        CpuNow.Foreground = Temp(t.CpuTempC, _settings.CpuTempWarn, _settings.CpuTempHot);
    }

    private static string DescribeSession(SessionSummary s)
    {
        var local = s.StartUtc.ToLocalTime();
        var parts = new List<string> { $"{s.Game} · {local:ddd d MMM, HH:mm} · {(int)s.Duration.TotalMinutes} min" };
        if (s.AvgFps is { } fps) parts.Add($"avg {fps:0} FPS" + (s.LowestOnePercentLow is { } low ? $", worst 1% low {low:0}" : ""));
        if (s.MaxGpuTempC is { } g) parts.Add($"GPU max {g:0} °C");
        if (s.MaxCpuTempC is { } c) parts.Add($"CPU max {c:0} °C");
        if (s.ThermalSlowdownSeconds > 0) parts.Add($"slowed by heat for {s.ThermalSlowdownSeconds / 60} min {s.ThermalSlowdownSeconds % 60} s");
        return string.Join(" · ", parts);
    }

    private void RefreshOverview()
    {
        var t = _engine.Latest;
        var s = _settings;

        TFps.Text = t.Fps is { } f ? $"{f.Fps:0}" : "—";
        TFpsSource.Text = t.FpsSource ?? "";
        TFpsSub.Text = t.Fps is { } ff ? $"{ff.FrameTimeMs:0.0} ms · 1% low {ff.OnePercentLow:0}"
            : t.FpsUnavailable ?? "No game or app rendering in front";
        var fpsTop = FpsTop(_app.History.Points.Where(p => p.Fps is > 0).Select(p => p.Fps!.Value));
        SetTicks(FpsTicks, t.Fps is { } fx ? fx.Fps / fpsTop : null);

        var g = t.Gpu;
        TGpuName.Text = _engine.Gpu?.Name.Replace("NVIDIA ", "").Replace("GeForce ", "").Replace(" Laptop GPU", "") ?? "";
        TGpuUnit.Visibility = g.TempC is null ? Visibility.Collapsed : Visibility.Visible;
        TGpu.Text = g.TempC is { } gt ? $"{gt:0}" : g.Status switch { GpuStatus.Sleeping => "Sleeping", GpuStatus.NotPresent => "None", _ => "—" };
        TGpu.Foreground = Temp(g.TempC, s.GpuTempWarn, s.GpuTempHot);
        TGpuSub.Text = g.Status switch
        {
            GpuStatus.Ok => $"{g.Load ?? 0}% · {g.ClockMhz} MHz · {g.PowerW:0} W",
            GpuStatus.Sleeping => "Powered down to save battery",
            GpuStatus.NotPresent => "No NVIDIA GPU or driver found",
            _ => "Driver isn't responding; retrying",
        };
        SetTicks(GpuTicks, g.Status == GpuStatus.Ok ? g.Load / 100.0 : null);

        TCpu.Text = t.CpuTempC is { } ct ? $"{ct:0}" : "—";
        TCpu.Foreground = Temp(t.CpuTempC, s.CpuTempWarn, s.CpuTempHot);
        TCpuSub.Text = $"{t.CpuLoad:0}% · {t.CpuClockGhz:0.0} GHz";
        SetTicks(CpuTicks, t.CpuLoad / 100.0);

        TRam.Text = t.Ram is { } r ? $"{r.UsedGb:0.0}" : "—";
        TRamTotal.Text = t.Ram is { } rt ? $"{rt.TotalGb:0} GB" : "";
        TRamSub.Text = g.VramUsed is { } vu && g.VramTotal is { } vt ? $"VRAM {vu / 1073741824.0:0.0} / {vt / 1073741824.0:0} GB" : "";
        SetTicks(RamTicks, t.Ram is { TotalGb: > 0 } ru ? ru.UsedGb / ru.TotalGb : null);

        DrawFpsChart();
        RefreshEvents();
        RefreshStatus();
        RefreshBanners();
    }

    /// <summary>Fills the first N of a reading's ten segments in the text color; the rest stay as divider lines.</summary>
    private static void SetTicks(UniformGrid host, double? fraction)
    {
        if (host.Children.Count == 0)
            for (var i = 0; i < OverviewData.TickCount; i++) host.Children.Add(new Rectangle { Margin = new Thickness(0, 0, 2, 0) });
        var filled = OverviewData.FilledTicks(fraction);
        for (var i = 0; i < host.Children.Count; i++)
            ((Rectangle)host.Children[i]).SetResourceReference(Shape.FillProperty, i < filled ? "Text" : "Line");
    }

    private void RefreshEvents()
    {
        var events = OverviewData.Events(Log.Recent(), 8);
        EventList.ItemsSource = events;
        EventsEmpty.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>One line at the bottom: what is running, power, and the overlay; Stop/Resume when they apply.</summary>
    private void RefreshStatus()
    {
        var gm = _engine.Gaming;
        string state, led;
        if (_engine.RecoveryMode) { state = "Recovery mode: automatic optimizations are off"; led = "Signal"; }
        else if (_engine.OptimizationsPaused) { state = "Optimizations paused"; led = "Signal"; }
        else if (gm.Active) { state = $"Gaming mode on for {string.Join(", ", gm.ActiveGames)}"; led = "Good"; }
        else if (_engine.Boost.Active) { state = "Game Boost on"; led = "Good"; }
        else if (_engine.Journal.Entries.Any(e => e.Owners.Contains(GamingMode.Owner))) { state = "Gaming mode changes still in effect"; led = "Signal"; }
        else if (_settings.AutoGamingMode) { state = "Gaming mode waiting for a game"; led = "Good"; }
        else { state = "Monitoring"; led = "Good"; }
        StatusLed.SetResourceReference(Shape.FillProperty, led);

        var power = PowerMode.Name(PowerMode.Current()) + (_engine.Journal.IsOwnedBy(SettingCatalog.Power.Id, GamingMode.Owner) ? " (gaming mode)" : "");
        var source = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline ? "On battery" : "Plugged in";
        var overlay = _app.OverlayVisible ? $"Overlay visible · {_app.HotkeyInUse ?? "no shortcut"}" : "Overlay hidden";
        StatusLine.Text = string.Join("  ·  ", state, source, power, overlay);
        StatusLine.ToolTip = StatusLine.Text;

        var somethingActive = gm.Active || _engine.Boost.Active || _engine.Journal.Entries.Any(e => e.Owners.Contains(GamingMode.Owner));
        StopButton.Visibility = !_engine.OptimizationsPaused && somethingActive ? Visibility.Visible : Visibility.Collapsed;
        ResumeButton.Visibility = _engine.OptimizationsPaused || _engine.RecoveryMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshBanners()
    {
        Banners.Children.Clear();
        if (_engine.RecoveryMode)
            Banners.Children.Add(Banner("Recovery mode", "The app didn't start normally several times in a row, so automatic optimizations are off for now. Turn them back on if everything looks fine.", "Turn optimizations on", (_, _) => { _engine.LeaveRecoveryMode(); RefreshAll(); }, warn: true));
        if (_engine.Latest.FpsUnavailable != null)
            Banners.Children.Add(Banner("FPS isn't available", "Reading frame rates needs administrator rights or membership in the Windows group \"Performance Log Users\". An administrator can add your account once (Computer Management › Local Users and Groups), then sign out and back in. Everything else works without it.", null, null, warn: true));
        var browsers = BrowsersNeedingRestart();
        if (browsers.Count > 0)
            Banners.Children.Add(Banner($"Restart {Names(browsers)} to finish", "A graphics setting changed while it was running.", "Go to Optimizer", (_, _) => Nav.SelectedIndex = 1, warn: false));
        foreach (var notice in _engine.Gaming.Notices)
            Banners.Children.Add(Banner("Gaming mode", notice, null, null, warn: true));
    }

    private Border Banner(string title, string text, string? button, RoutedEventHandler? click, bool warn)
    {
        var accent = (Brush)FindResource(warn ? "Warn" : "Accent");
        var c = ((SolidColorBrush)accent).Color;
        var dock = new DockPanel();
        if (button != null)
        {
            var b = new Button { Content = button, Style = (Style)FindResource("Btn"), Tag = "Primary", VerticalAlignment = VerticalAlignment.Center };
            b.Click += click;
            DockPanel.SetDock(b, Dock.Right);
            dock.Children.Add(b);
        }
        var stack = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = accent });
        stack.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Body") });
        dock.Children.Add(stack);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x1A, c.R, c.G, c.B)), BorderBrush = accent, BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 10), Child = dock,
        };
    }

    // ---- charts ----

    /// <summary>The chart's upper bound: 15% above the highest frame rate, in steps of 30, at least 60.</summary>
    private static double FpsTop(IEnumerable<double> realFps) =>
        Math.Max(60, Math.Ceiling(realFps.DefaultIfEmpty(0).Max() * 1.15 / 30) * 30);

    /// <summary>Frame rate over the history window on a light grid, with marked dips.</summary>
    private void DrawFpsChart()
    {
        var points = _app.History.Points.ToList();
        var fps = points.Select(p => p.Fps).ToList();
        var real = fps.Where(v => v is > 0).Select(v => v!.Value).ToList();
        var top = FpsTop(real);
        FpsChartRange.Text = real.Count > 0 ? $"avg {real.Average():0}  low {real.Min():0}" : "";

        FpsChart.Children.Clear();
        double w = FpsChart.ActualWidth, h = FpsChart.ActualHeight;
        if (w <= 0) return;
        for (var i = 1; i < 4; i++) AddGridLine(0, h * i / 4, w, h * i / 4);
        for (var i = 1; i < 8; i++) AddGridLine(w * i / 8, 0, w * i / 8, h);

        Plot(FpsChart, points, p => p.Fps, 0, top, (Brush)FindResource("Text"), clear: false);
        foreach (var i in OverviewData.Dips(fps))
        {
            var p = ChartPoint(points, i, fps[i]!.Value, 0, top, w, h);
            var dot = new Ellipse { Width = 6, Height = 6 };
            dot.SetResourceReference(Shape.FillProperty, "Signal");
            Canvas.SetLeft(dot, p.X - 3);
            Canvas.SetTop(dot, p.Y - 3);
            FpsChart.Children.Add(dot);
        }
    }

    private void AddGridLine(double x1, double y1, double x2, double y2)
    {
        var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, StrokeThickness = 1 };
        line.SetResourceReference(Shape.StrokeProperty, "Line2");
        FpsChart.Children.Add(line);
    }

    /// <summary>Where point <paramref name="index"/> sits on a chart of the history window (newest at the right).</summary>
    private static Point ChartPoint(List<HistoryPoint> points, int index, double value, double min, double max, double w, double h)
    {
        var x = w - (points[^1].Time - points[index].Time).TotalSeconds / HistoryRecorder.Window.TotalSeconds * w;
        var y = h - (Math.Clamp(value, min, max) - min) / (max - min) * h;
        return new Point(x, y);
    }

    private static void Plot(Canvas canvas, List<HistoryPoint> points, Func<HistoryPoint, double?> value, double min, double max, Brush brush, bool clear)
    {
        if (clear) canvas.Children.Clear();
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        if (w <= 0 || points.Count < 2) return;
        var line = new Polyline { Stroke = brush, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
        for (var i = 0; i < points.Count; i++)
        {
            if (value(points[i]) is not { } v) { if (line.Points.Count > 0) { canvas.Children.Add(line); line = new Polyline { Stroke = brush, StrokeThickness = 1.6 }; } continue; }
            line.Points.Add(ChartPoint(points, i, v, min, max, w, h));
        }
        if (line.Points.Count > 0) canvas.Children.Add(line);
    }

    // ---- optimizer ----

    private void RefreshOptimizer()
    {
        foreach (var c in _checks) c.Refresh();
        var pending = _checks.OfType<Tweak>().Count(t => t.NeedsFix);
        FixAllButton.IsEnabled = pending > 0;
        FixAllButton.Content = pending switch
        {
            0 => "Nothing to apply",
            1 => "Apply 1 recommendation",
            _ => $"Apply {pending} recommendations",
        };
        UpdateRestartBanner();

        var top = _sampler.Sample()
            .OrderByDescending(a => Math.Max(a.CpuPercent, a.GpuPercent))
            .ThenByDescending(a => a.RamMb)
            .Take(AppRows)
            .ToList();
        for (var i = 0; i < top.Count; i++) // update rows in place so buttons don't jump under the cursor
        {
            if (i >= _appRows.Count) _appRows.Add(new AppRow(this));
            _appRows[i].Set(top[i], _settings.BoostTargets.Contains(top[i].Name, StringComparer.OrdinalIgnoreCase));
        }
        while (_appRows.Count > top.Count) _appRows.RemoveAt(_appRows.Count - 1);
    }

    private List<GpuPreferenceTweak> BrowsersNeedingRestart() =>
        _checks.OfType<GpuPreferenceTweak>().Where(b => b.NeedsRestart).ToList();

    private static string Names(IEnumerable<GpuPreferenceTweak> b) => string.Join(" and ", b.Select(x => x.Browser.Name));

    private void UpdateRestartBanner()
    {
        if (_restarting) return;
        var browsers = BrowsersNeedingRestart();
        RestartBanner.Visibility = browsers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (browsers.Count == 0) return;

        var broken = browsers.Where(b => b.RestartBroken).ToList();
        var ok = browsers.Except(broken).ToList();
        RestartTitle.Text = $"Restart {Names(browsers)} to switch graphics chips";
        RestartDetail.Text = broken.Count > 0
            ? $"A previous restart of {Names(broken)} failed — it didn't reopen. Open it yourself once it works again."
            : "It closes the way Windows does at shutdown, saving every window and tab, then reopens them. You'll be asked first.";
        RestartButton.Visibility = ok.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RestartButton.Content = $"Restart {Names(ok)}";
        RestartButton.IsEnabled = true;
    }

    private async void OnRestartBrowser(object sender, RoutedEventArgs e)
    {
        var browsers = BrowsersNeedingRestart().Where(b => !b.RestartBroken).ToList();
        if (browsers.Count == 0) return;
        var names = Names(browsers);
        var profiles = browsers.Max(b => Browsers.OpenProfileCount(b.Browser));
        var profileNote = profiles > 1
            ? $"\n\nWindows from {profiles} profiles are open. Restoring was tested with one profile; with several, some windows may need reopening from the browser's History."
            : "";

        var answer = MessageBox.Show(this,
            $"{names} will close and reopen with all your windows and tabs.\n\nAnything unsaved on a page, like a form or a game in progress, will be lost.{profileNote}",
            $"Restart {names}?", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;

        _restarting = true;
        RestartButton.IsEnabled = false;
        RestartButton.Content = "Restarting…";
        var messages = new List<string>();
        foreach (var b in browsers)
        {
            var result = await BrowserRestart.RestartAsync(b.Browser, _engine.State);
            if (!result.Success) messages.Add(result.Message);
        }
        _restarting = false;
        RefreshOptimizer();
        if (messages.Count > 0)
            MessageBox.Show(this, string.Join("\n\n", messages), "Restart didn't finish", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnCheckAction(object sender, RoutedEventArgs e)
    {
        var check = (Check)((FrameworkElement)sender).DataContext;
        if (check is GpuPreferenceTweak { NeedsFix: true } browser && !Confirm(
                $"Set {browser.Browser.Name} to always use the NVIDIA GPU?\n\nBrowser games and WebGL usually run smoother, but battery life is shorter, and the NVIDIA GPU is shared with anything else using it. You can undo this at any time.",
                "Change graphics preference"))
            return;
        string? message;
        try { message = check.RunAction(); }
        catch (Exception ex) { message = ex.Message; }
        if (message != null) MessageBox.Show(this, message, check.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshOptimizer();
    }

    private void OnFixAll(object sender, RoutedEventArgs e)
    {
        var pending = _checks.OfType<Tweak>().Where(t => t.NeedsFix).ToList();
        if (pending.Count == 0) return;
        if (!Confirm("Apply these recommendations?\n\n" + string.Join("\n", pending.Select(t => $"• {t.Title}: {t.Status} → {t.Recommended}")) +
                     "\n\nEach can be undone individually afterwards.", "Apply recommendations"))
            return;
        var failed = new List<string>();
        foreach (var t in pending)
        {
            var m = t.RunAction();
            if (m != null) failed.Add($"{t.Title}: {m}");
        }
        RefreshOptimizer();
        if (failed.Count > 0)
            MessageBox.Show(this, string.Join("\n", failed), "Some changes didn't apply", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnEndApp(object sender, RoutedEventArgs e)
    {
        var row = (AppRow)((FrameworkElement)sender).DataContext;
        var name = row.Name;
        if (!Confirm($"End {name}?\n\nThis closes all {row.ProcessCount} of its processes immediately. Unsaved work in it will be lost.", "End app"))
            return;
        var failed = 0;
        foreach (var p in ProcessUtil.ByName(name))
            using (p)
                try { p.Kill(); } catch { failed++; }
        Log.Info("Optimizer", $"Ended {name} at the user's request");
        if (failed > 0)
            MessageBox.Show(this, $"{failed} {name} process(es) couldn't be ended.", "End app", MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshOptimizer();
    }

    internal void SetBoostTarget(string name, bool target)
    {
        _settings.BoostTargets.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (target) _settings.BoostTargets.Add(name);
        Save();
    }

    private void UpdateBoostTargets() =>
        BoostTargetsText.Text = _settings.BoostTargets.Count == 0
            ? "Apps lowered during games: none. Choose them on the Optimizer page (\"Lower during games\"), e.g. python for AI training."
            : "Apps lowered during games: " + string.Join(", ", _settings.BoostTargets);

    // ---- gaming ----

    private void RefreshGaming()
    {
        var gm = _engine.Gaming;
        var paused = _engine.OptimizationsPaused;
        GamingBar.Visibility = gm.Active || paused ? Visibility.Visible : Visibility.Collapsed;
        GamingBarText.Text = paused ? "Optimizations are paused. Gaming mode won't change anything until you resume."
            : gm.Active ? $"Gaming mode is on for {string.Join(", ", gm.ActiveGames)}: {string.Join("; ", gm.Optimizations)}"
            : "";
        GamingLed.SetResourceReference(Shape.FillProperty, paused ? "Signal" : "Good");
        GamingPause.Visibility = paused ? Visibility.Collapsed : Visibility.Visible;
        GamingResume.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;

        var recent = _engine.RecentGames.Select(g => new RecentGame(g,
            !_rules.Any(r => GameDetector.Matches(r, new ForegroundWindow(g.Process, g.Label, false, null))))).ToList();
        RecentList.ItemsSource = recent;
        RecentEmpty.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed record RecentGame(GameDetection Game, bool CanAdd)
    {
        public string Text => $"{Game.Label}  ·  {(Game.Confirmed ? "on your list" : "fullscreen, not on your list")}  ·  app: {Game.Process}";
    }

    private void OnAddRecent(object sender, RoutedEventArgs e)
    {
        var g = ((RecentGame)((FrameworkElement)sender).DataContext).Game;
        _rules.Add(new GameRule { Match = g.Process, Kind = GameMatchKind.ProcessName });
        Save();
        RefreshGaming();
    }

    private void OnAddRule(object sender, RoutedEventArgs e)
    {
        var text = NewRule.Text.Trim();
        if (text.Length == 0) return;
        _rules.Add(new GameRule { Match = text });
        NewRule.Clear();
        Save();
    }

    private void OnNewRuleKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnAddRule(sender, e);
    }

    private void OnRemoveRule(object sender, RoutedEventArgs e)
    {
        _rules.Remove((GameRule)((FrameworkElement)sender).DataContext);
        Save();
    }

    private void OnStopOptimizations(object sender, RoutedEventArgs e)
    {
        var report = _app.StopOptimizations();
        MessageBox.Show(this, string.Join("\n", report), "Optimizations stopped", MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshOverview();
        RefreshGaming();
    }

    private void OnResume(object sender, RoutedEventArgs e)
    {
        if (_engine.RecoveryMode) _engine.LeaveRecoveryMode();
        _app.ResumeOptimizations();
        RefreshOverview();
        RefreshGaming();
    }

    // ---- overlay / general ----

    /// <summary>A dashed corner on the preview: puts the overlay in that corner.</summary>
    private void OnCornerTarget(object sender, RoutedEventArgs e)
    {
        _settings.Position = (string)((Button)sender).CommandParameter;
        Save();
    }

    private void OnLayoutCard(object sender, RoutedEventArgs e)
    {
        _settings.Layout = (string)((FrameworkElement)sender).Tag;
        Save();
    }

    /// <summary>Draws the overlay on the preview screen where and how it will appear, with live readings.</summary>
    private void RefreshPreview()
    {
        var t = _engine.Latest;
        PreviewText.Text = _settings.Layout == Settings.Compact
            ? OverlayPreview.Strip(_settings, t.Fps?.Fps, t.Fps?.FrameTimeMs, t.Gpu.TempC, t.CpuTempC)
            : OverlayPreview.Panel(_settings, t.Fps?.Fps, t.Fps?.FrameTimeMs, t.Gpu.TempC, t.CpuTempC, t.Ram?.UsedGb);
        PreviewText.FontSize = 7 * _settings.Scale;
        PreviewOverlay.Opacity = _settings.Opacity;
        PreviewOverlay.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = PreviewOverlay.DesiredSize;
        var (x, y) = OverlayPlacement.Place(_settings.Position, _settings.CustomX, _settings.CustomY,
            (0, 0, (int)PreviewScreen.Width, (int)PreviewScreen.Height), (int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), PreviewMargin);
        Canvas.SetLeft(PreviewOverlay, x);
        Canvas.SetTop(PreviewOverlay, y);
        foreach (var target in new[] { CornerTopLeft, CornerTopRight, CornerBottomLeft, CornerBottomRight })
            target.Tag = (string)target.CommandParameter == _settings.Position ? "On" : null;

        var display = Displays.Find(_settings.Monitor);
        PreviewCaption.Text = (_settings.Position == Settings.Custom ? "Pinned spot. Use Move overlay to drag it" : "Click a corner to move it there") +
                              (display is null ? "" : $" · {display.Label}");
        PreviewHotkey.Text = _app.HotkeyInUse ?? "";
    }

    private void OnShowOverlay(object sender, RoutedEventArgs e)
    {
        if (ShowOverlay.IsChecked != _app.OverlayVisible) _app.ToggleOverlay();
    }

    private void OnStartupToggle(object sender, RoutedEventArgs e)
    {
        var want = StartupSwitch.IsChecked == true;
        var error = StartupTask.SetEnabled(want);
        if (error != null) MessageBox.Show(this, error, "Start with Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
        RefreshSlow();
    }

    private void OnHotkeyKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return; // wait for the actual key
        uint mods = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= Native.MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= Native.MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= Native.MOD_SHIFT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= Native.MOD_WIN;
        HotkeyBox.Text = Hotkeys.Format(mods, key);
        HotkeyResult.Text = "";
    }

    private void OnApplyHotkey(object sender, RoutedEventArgs e)
    {
        var error = _app.TryRegisterHotkey(HotkeyBox.Text);
        HotkeyResult.Foreground = (Brush)FindResource(error is null ? "Good" : "Warn");
        HotkeyResult.Text = error ?? $"Now using {_app.HotkeyInUse}.";
        RefreshSlow();
    }

    private void OnResetSettings(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Restore default settings on every page?\n\nYour games list and lowered apps are kept. Windows settings the app changed aren't touched.", "Restore defaults"))
            return;
        _loading = true;
        _app.ResetSettings();
        DataContext = null;
        DataContext = _settings;
        _loading = false;
        RefreshAll();
        SyncThemeButtons();
    }

    // ---- diagnostics ----

    private void RefreshDiagnostics()
    {
        var status = DiagnosticReport.Status(_engine).Where(s => s.Name != DiagnosticReport.ChangesCheck).ToList();
        var cells = status.Select(s => new StatusCell(s.Name, s.Detail, s.Level, Health.Word(s.Level))).ToList();
        if (StatusGrid.ItemsSource is not IEnumerable<StatusCell> shown || !shown.SequenceEqual(cells))
            StatusGrid.ItemsSource = cells;
        HealthText.Text = Health.Summary(status.Select(s => s.Level).ToList());

        var rows = JournalRows.Build(_engine.Journal.Entries, id => SettingCatalog.Resolve(id)?.Name, SettingCatalog.DescribeValue, DateTime.Now);
        if (JournalList.ItemsSource is not IEnumerable<JournalRow> listed || !listed.SequenceEqual(rows))
            JournalList.ItemsSource = rows;
        JournalEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed record StatusCell(string Name, string Detail, DiagLevel Level, string Word);

    private void OnUndoChange(object sender, RoutedEventArgs e)
    {
        var row = (JournalRow)((FrameworkElement)sender).DataContext;
        if (!Confirm($"Undo {row.Setting} ({row.Change})?\n\nIt goes back only if it's still what the app set. If you changed it yourself since, your choice is kept.", "Undo change"))
            return;
        ActionResult.Text = string.Join("\n", _engine.UndoChange(row.Id));
        foreach (var c in _checks) c.Refresh();
        RefreshDiagnostics();
    }

    private void OnCopyReport(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DiagnosticReport.Build(_engine, _app.HotkeyInUse));
            ActionResult.Text = "Report copied to the clipboard (personal paths and your user name are replaced).";
        }
        catch (Exception ex) { ActionResult.Text = $"Couldn't copy: {ex.Message}"; }
    }

    private void OnOpenLog(object sender, RoutedEventArgs e) => OpenPath(AppPaths.LogFile);
    private void OnOpenData(object sender, RoutedEventArgs e) => OpenPath(AppPaths.DataDirectory);

    private static void OpenPath(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OnUndoEverything(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Undo every Windows change the app still owns?\n\nThis stops gaming mode and Game Boost and puts back power mode, browser graphics preferences and other settings — but only where you haven't changed them yourself since.", "Undo all changes"))
            return;
        var report = _engine.UndoEverything();
        ActionResult.Text = string.Join("\n", report);
        foreach (var c in _checks) c.Refresh();
        RefreshDiagnostics();
    }

    private bool Confirm(string text, string title) =>
        MessageBox.Show(this, text, title, MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) == MessageBoxResult.OK;

    private Brush Temp(double? c, double warm, double hot) =>
        (Brush)FindResource(c is null ? "Text" : c >= hot ? "Hot" : c >= warm ? "Warn" : "Text");

    /// <summary>One row of the "What's using your PC" list.</summary>
    internal sealed class AppRow(ControlCenterWindow owner) : INotifyPropertyChanged
    {
        private bool _isBoostTarget;

        public string Name { get; private set; } = "";
        public string Cpu { get; private set; } = "";
        public string Gpu { get; private set; } = "";
        public string Ram { get; private set; } = "";
        public int ProcessCount { get; private set; }
        public bool CanEnd => !ProcessUtil.IsCritical(Name);
        public bool CanLower => !ProcessUtil.IsCritical(Name);

        public bool IsBoostTarget
        {
            get => _isBoostTarget;
            set
            {
                if (_isBoostTarget == value) return;
                _isBoostTarget = value;
                owner.SetBoostTarget(Name, value);
            }
        }

        public void Set(AppUsage usage, bool isBoostTarget)
        {
            Name = usage.Name;
            Cpu = $"{usage.CpuPercent:0}%";
            Gpu = usage.GpuPercent >= 0.5 ? $"{usage.GpuPercent:0}%" : "—";
            Ram = usage.RamMb >= 1024 ? $"{usage.RamMb / 1024:0.0} GB" : $"{usage.RamMb:0} MB";
            ProcessCount = usage.ProcessCount;
            _isBoostTarget = isBoostTarget;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
