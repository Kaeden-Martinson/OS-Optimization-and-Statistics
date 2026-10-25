using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PCSpecOverlay.Core;
using PCSpecOverlay.Monitors;

namespace PCSpecOverlay;

/// <summary>What the overlay shows about gaming mode, separate from raw telemetry.</summary>
public readonly record struct OverlayStatus(bool GamingMode, bool Boost, bool Paused);

/// <summary>
/// The always-on-top performance panel. It only displays; all configuration lives in the control
/// center. It never takes focus, stays out of Alt+Tab, and is click-through except while the user moves it
/// (tray or control center: Move overlay); Pin locks it where it was dropped.
/// Positioning works in physical pixels per monitor, so mixed display scaling can't misplace it.
/// Its colors come from Themes/Overlay.*.xaml merged into this window only, so the app theme never
/// recolors it unless the overlay theme is "Same as app".
/// </summary>
public partial class OverlayWindow : Window
{
    private const double EdgeMarginDip = 14;

    private Settings _settings = new();
    private string _layout = Settings.Compact;
    private Limit _shownLimits = (Limit)(-1);
    private AppTheme _appTheme = AppTheme.Dark;
    private AppTheme _shownTheme = AppTheme.Dark; // OverlayWindow.xaml merges the dark colors
    private readonly FrameTimeTrace _trace = new();

    private bool _moving;

    public IntPtr Handle { get; private set; }

    /// <summary>Raised when the user presses Pin after moving the overlay.</summary>
    public event Action? Pinned;

    public bool IsMoving => _moving;

    /// <summary>Raised after the user drags the overlay to a new corner or monitor.</summary>
    public event Action? Moved;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            ApplyWindowStyles();
        };
        SizeChanged += (_, _) => Reposition();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Reposition);
        MouseLeftButtonDown += OnDrag;
        TraceCanvas.SizeChanged += (_, _) => DrawTrace();
    }

    public void EnsureHandle() => Handle = new WindowInteropHelper(this).EnsureHandle();

    /// <param name="layoutOverride">A per-game layout, or null for the user's normal one.</param>
    public void ApplySettings(Settings s, string? layoutOverride = null)
    {
        _settings = s;
        _layout = layoutOverride ?? s.Layout;
        Opacity = s.Opacity;
        PanelScale.ScaleX = PanelScale.ScaleY = s.Scale;

        var panel = _layout == Settings.Expanded;
        ExpandedLayout.Visibility = Show(panel);
        CompactLayout.Visibility = Show(!panel);
        FrameTimeRow.Visibility = CFrameTimeBox.Visibility = TraceArea.Visibility = Show(s.ShowFrameTime);
        LowRow.Visibility = Show(s.ShowOnePercentLow);
        CpuRow.Visibility = CCpuBox.Visibility = Show(s.ShowCpu);
        RamRow.Visibility = Show(s.ShowRam);
        _shownLimits = (Limit)(-1); // re-render limit chips

        ApplyTheme();
        ApplyWindowStyles();
        Reposition();
        DrawTrace();
    }

    /// <summary>The control center's theme; used when the overlay theme is "Same as app".</summary>
    public void SetAppTheme(AppTheme theme)
    {
        _appTheme = theme;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var theme = ThemeResolver.ResolveOverlay(_settings.OverlayTheme, _appTheme);
        if (theme == _shownTheme) return;
        try
        {
            var colors = new ResourceDictionary { Source = new Uri($"pack://application:,,,/PCSpecOverlay;component/Themes/Overlay.{theme}.xaml") };
            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(colors);
            _shownTheme = theme;
        }
        catch (Exception e)
        {
            Log.Error("Overlay", $"Couldn't load the {theme.ToString().ToLowerInvariant()} overlay colors", e);
        }
    }

    private static Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    private void ApplyWindowStyles()
    {
        if (Handle == IntPtr.Zero) return;
        var ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE) | Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
        ex = _moving ? ex & ~Native.WS_EX_TRANSPARENT : ex | Native.WS_EX_TRANSPARENT;
        Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
        Cursor = _moving ? Cursors.SizeAll : null;
    }

    /// <summary>
    /// Places the panel at its corner or pinned spot on the chosen display (the main display if that one is gone).
    /// While moving, the handle bar sits above the panel, so the window starts that much higher.
    /// </summary>
    public void Reposition()
    {
        if (Handle == IntPtr.Zero || Displays.Find(_settings.Monitor) is not { } display) return;
        if (!Native.GetWindowRect(Handle, out var r)) return;
        var handle = HandlePixels(display.Dpi);
        int w = r.Right - r.Left, h = r.Bottom - r.Top - handle;
        var m = (int)Math.Round(EdgeMarginDip * display.Dpi / 96.0);
        var work = display.WorkArea;
        var (x, y) = OverlayPlacement.Place(_settings.Position, _settings.CustomX, _settings.CustomY,
            (work.Left, work.Top, work.Right, work.Bottom), w, h, m);
        var top = y - handle; // near the top edge the handle may sit above the work area; the panel stays put
        if (x != r.Left || top != r.Top)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, x, top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>Height of the move handle and its gap in physical pixels; 0 when pinned.</summary>
    private int HandlePixels(uint dpi) =>
        _moving ? (int)Math.Round((MoveHandle.ActualHeight + MoveHandle.Margin.Bottom) * dpi / 96.0) : 0;

    /// <summary>Unlocks the overlay: outline and handle shown, clicks no longer pass through, dragging moves it.</summary>
    public void BeginMove()
    {
        if (_moving) return;
        _moving = true;
        MoveHandle.Visibility = MoveOutline.Visibility = Visibility.Visible;
        ApplyWindowStyles();
        Reposition();
    }

    private void OnPin(object sender, RoutedEventArgs e)
    {
        _moving = false;
        MoveHandle.Visibility = MoveOutline.Visibility = Visibility.Collapsed;
        ApplyWindowStyles();
        Reposition();
        Moved?.Invoke();
        Pinned?.Invoke();
    }

    /// <summary>In move mode, dragging pins the panel exactly where it's dropped, on the display under its center.</summary>
    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (!_moving) return;
        try { DragMove(); } catch (InvalidOperationException) { return; }

        if (!Native.GetWindowRect(Handle, out var r)) return;
        // The display is the one under the panel's center, not the window's (which includes the handle bar)
        var panelTop = r.Top + HandlePixels((uint)Math.Round(VisualTreeHelper.GetDpi(this).PixelsPerInchY));
        int cx = (r.Left + r.Right) / 2, cy = (panelTop + r.Bottom) / 2;
        var display = Displays.All().FirstOrDefault(d => cx >= d.Bounds.Left && cx < d.Bounds.Right && cy >= d.Bounds.Top && cy < d.Bounds.Bottom)
                      ?? Displays.Find(_settings.Monitor);
        if (display is null) return;
        var work = display.WorkArea;
        var (fx, fy) = OverlayPlacement.ToFractions(r.Left, panelTop, (work.Left, work.Top, work.Right, work.Bottom),
            r.Right - r.Left, r.Bottom - panelTop);
        _settings.Position = Settings.Custom;
        _settings.CustomX = fx;
        _settings.CustomY = fy;
        _settings.Monitor = display.Primary ? "" : display.DeviceName;
        Reposition();
        Moved?.Invoke();
    }

    /// <summary>Fullscreen/borderless apps can push themselves above us; re-assert z-order.</summary>
    public void KeepOnTop()
    {
        if (Handle != IntPtr.Zero && IsVisible)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public void SetGpuName(string? name) =>
        GpuName.Text = name?.Replace("NVIDIA ", "").Replace("GeForce ", "").Replace(" Laptop GPU", " Laptop") ?? "";

    // ---- rendering ----

    public void ShowStatus(OverlayStatus status)
    {
        var text = status.GamingMode ? "GAMING MODE" : status.Paused ? "PAUSED" : status.Boost ? "BOOST" : null;
        BadgeText.Text = text ?? "";
        CBadgeText.Text = status.GamingMode ? "GAME" : text ?? "";
        Badge.Visibility = CBadge.Visibility = Show(text is not null);
        var fill = status.Paused ? "O.Muted" : "O.Text";
        Badge.SetResourceReference(Border.BackgroundProperty, fill);
        CBadge.SetResourceReference(Border.BackgroundProperty, fill);
    }

    public void ShowFps(Telemetry t)
    {
        FpsHint.Text = t.FpsUnavailable;
        FpsHint.Visibility = Show(t.FpsUnavailable is not null);
        FpsSource.Text = t.FpsSource ?? "";

        if (t.Fps is { } s)
        {
            FpsValue.Text = CFps.Text = Math.Round(s.Fps).ToString("0");
            FrameTime.Text = CFrameTime.Text = s.FrameTimeMs.ToString("0.0");
            LowFps.Text = Math.Round(s.OnePercentLow).ToString("0");
        }
        else
        {
            FpsValue.Text = CFps.Text = FrameTime.Text = CFrameTime.Text = LowFps.Text = "—";
        }
        var color = t.Fps is null ? "O.Muted" : "O.Text";
        FpsValue.SetResourceReference(TextBlock.ForegroundProperty, color);
        CFps.SetResourceReference(TextBlock.ForegroundProperty, color);

        _trace.Add(t.Fps?.WorstFrameMs);
        DrawTrace();
    }

    private void DrawTrace()
    {
        if (ExpandedLayout.Visibility != Visibility.Visible || TraceArea.Visibility != Visibility.Visible) return;
        TraceLine.Points = new PointCollection(_trace.Plot(TraceCanvas.ActualWidth, TraceCanvas.ActualHeight));
    }

    public void ShowStats(Telemetry t)
    {
        var s = _settings;
        var g = t.Gpu;

        // GPU: hide entirely without an NVIDIA GPU; say why when there's no reading
        var hasGpu = g.Status != GpuStatus.NotPresent;
        GpuRow.Visibility = Show(hasGpu && s.ShowGpu);
        VramRow.Visibility = Show(hasGpu && s.ShowVram);
        CGpuBox.Visibility = Show(hasGpu && s.ShowGpu);
        var ok = g.Status == GpuStatus.Ok;
        GpuValues.Visibility = GpuTrack.Visibility = Show(ok);
        GpuState.Visibility = Show(!ok);
        GpuState.Text = g.Status switch
        {
            GpuStatus.Sleeping => "Sleeping (saving power)",
            GpuStatus.Unavailable => "Driver not responding",
            _ => "",
        };

        Set(GpuLoad, g.Load, "0");
        Set(GpuTemp, g.TempC, "0");
        Set(GpuClock, g.ClockMhz, "0");
        Set(GpuPower, g.PowerW, "0");
        SetTemp(GpuTemp, g.TempC, s.GpuTempWarn, s.GpuTempHot);
        SetBar(GpuBar, g.Load);
        const double gb = 1024.0 * 1024 * 1024;
        Set(VramUsed, g.VramUsed / gb, "0.0");
        VramTotal.Text = g.VramTotal is { } total ? $"/ {total / gb:0.0} GB" : "/ — GB";

        Set(CpuLoad, t.CpuLoad, "0");
        Set(CpuTemp, t.CpuTempC, "0");
        Set(CpuClock, t.CpuClockGhz, "0.0");
        SetTemp(CpuTemp, t.CpuTempC, s.CpuTempWarn, s.CpuTempHot);
        SetBar(CpuBar, t.CpuLoad);

        Set(RamUsed, t.Ram?.UsedGb, "0.0");
        RamTotal.Text = t.Ram is { } r ? $"/ {r.TotalGb:0.0} GB" : "/ — GB";

        // Strip: temperature first, then load
        CGpu.Text = ok ? $"{Num(g.TempC, "0")}° {Num(g.Load, "0")}%" : g.Status == GpuStatus.Sleeping ? "sleeping" : "—";
        SetTemp(CGpu, g.TempC, s.GpuTempWarn, s.GpuTempHot);
        CCpu.Text = $"{Num(t.CpuTempC, "0")}° {Num(t.CpuLoad, "0")}%";
        SetTemp(CCpu, t.CpuTempC, s.CpuTempWarn, s.CpuTempHot);

        ShowLimits(s.ShowSlowdowns ? t.Limits : Limit.None);
    }

    private void ShowLimits(Limit limits)
    {
        if (limits == _shownLimits) return;
        _shownLimits = limits;

        LimitsRow.Children.Clear();
        foreach (var (flag, text, hot) in new[]
                 {
                     (Limit.GpuThermal, "GPU · SLOWING DOWN (HEAT)", true),
                     (Limit.CpuThermal, "CPU · SLOWING DOWN (HEAT)", true),
                     (Limit.GpuPower, "GPU · AT POWER LIMIT", false),
                     (Limit.CpuPower, "CPU · AT POWER LIMIT", false),
                 })
        {
            if (limits.HasFlag(flag)) LimitsRow.Children.Add(Chip(text, hot ? "O.Hot" : "O.Signal"));
        }
        LimitsRow.Visibility = Show(limits != Limit.None);

        var thermal = (limits & (Limit.GpuThermal | Limit.CpuThermal)) != 0;
        CLimit.Visibility = Show(limits != Limit.None);
        CLimitText.Text = thermal ? "HEAT" : "POWER";
        var key = thermal ? "O.Hot" : "O.Signal";
        CLimitText.SetResourceReference(TextBlock.ForegroundProperty, key);
        CLimit.SetResourceReference(Border.BackgroundProperty, key + "Soft");
    }

    /// <summary>A square warning chip in the given color token (its "Soft" variant fills it).</summary>
    private static Border Chip(string text, string key)
    {
        var label = new TextBlock { Text = text, FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold, FontSize = 9.5 };
        label.SetResourceReference(TextBlock.ForegroundProperty, key);
        var chip = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(0, 0, 6, 4), Child = label };
        chip.SetResourceReference(Border.BackgroundProperty, key + "Soft");
        chip.SetResourceReference(Border.BorderBrushProperty, key);
        return chip;
    }

    private static string Num(double? v, string format) => v is { } x ? x.ToString(format) : "—";

    private static void Set(TextBlock block, double? value, string format) => block.Text = Num(value, format);

    /// <summary>Amber at the warning level, red at the hot level, otherwise the normal text color.</summary>
    private static void SetTemp(TextBlock block, double? c, double warm, double hot) =>
        block.SetResourceReference(TextBlock.ForegroundProperty, c is null ? "O.Text" : c >= hot ? "O.Hot" : c >= warm ? "O.Signal" : "O.Text");

    /// <summary>
    /// Bars ease only on noticeable changes (≥10 points), briefly and at a low frame rate; small
    /// changes jump. The overlay renders in software, so every animation frame repaints the panel.
    /// </summary>
    private static void SetBar(FrameworkElement fill, double? percent)
    {
        var scale = (ScaleTransform)fill.RenderTransform;
        var target = Math.Clamp((percent ?? 0) / 100.0, 0, 1);
        var change = Math.Abs(scale.ScaleX - target);
        if (change < 0.01) return;
        if (change < 0.10)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.ScaleX = target;
            return;
        }
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuadraticEase() };
        Timeline.SetDesiredFrameRate(anim, 20);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
    }
}
