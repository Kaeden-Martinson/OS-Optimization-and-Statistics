using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
    private bool _dragging;      // inside DragMove: layout changes must not move the window under the mouse
    private bool _handleBelow;   // move mode near the top edge: the handle bar sits below the panel
    private bool _panelRight;    // move mode near the right edge: a wider handle bar extends left of the panel

    public IntPtr Handle { get; private set; }

    /// <summary>Raised when the user presses Pin after moving the overlay.</summary>
    public event Action? Pinned;

    public bool IsMoving => _moving;

    /// <summary>Raised when the user drops the overlay after dragging it, or pins it.</summary>
    public event Action? Moved;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(Handle)?.AddHook(PlaceWhileResizing);
            ApplyWindowStyles();
        };
        // PlaceWhileResizing moves the window in the same step WPF resizes it; these passes run once layout has settled
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Reposition));
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Reposition));
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
    /// Places the panel at its corner or pinned spot on the chosen display (the main display if that one is gone),
    /// with the whole window inside that display's work area.
    /// While moving, the handle bar sits above the panel (below it near the top edge), so the panel itself never moves.
    /// </summary>
    public void Reposition()
    {
        if (_dragging || Handle == IntPtr.Zero || !Native.GetWindowRect(Handle, out var r)) return;
        if (Spot(r.Right - r.Left, r.Bottom - r.Top) is not { } spot || (spot.Left == r.Left && spot.Top == r.Top)) return;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, spot.Left, spot.Top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>
    /// WPF resizes the window (to fit its content, or for a display's scaling) keeping its top-left corner, and raises
    /// SizeChanged before the window has its new size. So the new position is worked out here, from the size the
    /// window is about to get, and applied in the same step: growing near the right or bottom edge never pushes it
    /// past that edge or onto the next display, not even for a frame.
    /// </summary>
    private IntPtr PlaceWhileResizing(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_WINDOWPOSCHANGING || _dragging) return IntPtr.Zero;
        var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
        if ((pos.flags & Native.SWP_NOSIZE) != 0 || !Native.GetWindowRect(hwnd, out var r)) return IntPtr.Zero;
        if (pos.cx == r.Right - r.Left && pos.cy == r.Bottom - r.Top) return IntPtr.Zero;
        if (Spot(pos.cx, pos.cy) is not { } spot) return IntPtr.Zero;
        pos.x = spot.Left;
        pos.y = spot.Top;
        pos.flags &= ~Native.SWP_NOMOVE;
        Marshal.StructureToPtr(pos, lParam, false);
        return IntPtr.Zero;
    }

    /// <summary>Where a window of this size (physical pixels) goes; null when no display is connected.</summary>
    private (int Left, int Top)? Spot(int width, int height)
    {
        if (Displays.Find(_settings.Monitor) is not { } display) return null;
        // Sizes in the window's own pixels; only the edge margin belongs to the target display
        var scale = PixelsPerDip(width);
        var m = (int)Math.Round(EdgeMarginDip * display.Dpi / 96.0);
        var work = display.WorkArea;
        var (left, top, below, right) = OverlayPlacement.Arrange(_settings.Position, _settings.CustomX, _settings.CustomY,
            (work.Left, work.Top, work.Right, work.Bottom), width, height, PanelWidth(scale, width), HandlePixels(scale), m);
        SetHandleSide(below, right);
        return (left, top);
    }

    /// <summary>
    /// Physical pixels per DIP for a window this wide: the scaling WPF actually drew it at. While the window is still
    /// on (or coming from) a display with other scaling this isn't the target display's, and mixing the two
    /// mismeasures it; once it is placed on its display, Windows rescales it and it is placed again.
    /// </summary>
    private double PixelsPerDip(int width) =>
        width > 0 && ActualWidth > 0 ? width / ActualWidth : VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>Height of the move handle and its gap in physical pixels; 0 when pinned.</summary>
    private int HandlePixels(double scale) =>
        _moving ? (int)Math.Round((MoveHandle.ActualHeight + MoveHandle.Margin.Top + MoveHandle.Margin.Bottom) * scale) : 0;

    /// <summary>The panel's own width in physical pixels; a wider handle bar can make the window wider than it.</summary>
    private int PanelWidth(double scale, int windowWidth)
    {
        var w = (int)Math.Round(PanelHost.ActualWidth * scale);
        return w <= 0 || w > windowWidth ? windowWidth : w;
    }

    /// <summary>Puts the handle bar above or below the panel, and the panel at the window's left or right.</summary>
    private void SetHandleSide(bool below, bool right)
    {
        if (below != _handleBelow)
        {
            _handleBelow = below;
            Grid.SetRow(MoveHandle, below ? 2 : 0);
            MoveHandle.Margin = below ? new Thickness(0, 4, 0, 0) : new Thickness(0, 0, 0, 4);
        }
        if (right != _panelRight)
        {
            _panelRight = right;
            PanelHost.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
    }

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
        _dragging = true;
        try { DragMove(); }
        catch (InvalidOperationException) { return; }
        finally { _dragging = false; }

        if (!Native.GetWindowRect(Handle, out var r)) return;
        // Measure the panel, not the window: the handle bar adds height above or below it and may add width
        int windowWidth = r.Right - r.Left;
        var scale = PixelsPerDip(windowWidth);
        var handle = HandlePixels(scale);
        var panelWidth = PanelWidth(scale, windowWidth);
        var panelLeft = r.Left + (_panelRight ? windowWidth - panelWidth : 0);
        var panelTop = r.Top + (_handleBelow ? 0 : handle);
        var panelHeight = r.Bottom - r.Top - handle;
        // The display is the one under the panel's center, not the window's
        int cx = panelLeft + panelWidth / 2, cy = panelTop + panelHeight / 2;
        var display = Displays.All().FirstOrDefault(d => cx >= d.Bounds.Left && cx < d.Bounds.Right && cy >= d.Bounds.Top && cy < d.Bounds.Bottom)
                      ?? Displays.Find(_settings.Monitor);
        if (display is null) return;
        var work = display.WorkArea;
        var (fx, fy) = OverlayPlacement.ToFractions(panelLeft, panelTop, (work.Left, work.Top, work.Right, work.Bottom),
            panelWidth, panelHeight);
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
        // The strip keeps the chip's slot while slowdowns are shown at all, so one coming and going doesn't resize it
        CLimit.Visibility = limits != Limit.None ? Visibility.Visible : _settings.ShowSlowdowns ? Visibility.Hidden : Visibility.Collapsed;
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
