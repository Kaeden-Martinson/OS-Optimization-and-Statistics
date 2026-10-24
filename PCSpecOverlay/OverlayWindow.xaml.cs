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
/// center. It never takes focus, stays out of Alt+Tab, and is click-through unless the user turns
/// that off to drag it (it then snaps to the nearest corner of whichever monitor it's dropped on).
/// Positioning works in physical pixels per monitor, so mixed display scaling can't misplace it.
/// </summary>
public partial class OverlayWindow : Window
{
    private static readonly Brush Normal = Frozen(0xF3, 0xF5, 0xF6);
    private static readonly Brush Warm = Frozen(0xF5, 0xB7, 0x31);
    private static readonly Brush Hot = Frozen(0xFF, 0x4D, 0x4F);
    private static readonly Brush Muted = Frozen(0x69, 0x72, 0x7B);
    private const double EdgeMarginDip = 14;

    private Settings _settings = new();
    private string _layout = Settings.Expanded;
    private Limit _shownLimits = (Limit)(-1);

    public IntPtr Handle { get; private set; }

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
    }

    public void EnsureHandle() => Handle = new WindowInteropHelper(this).EnsureHandle();

    /// <param name="layoutOverride">A per-game layout, or null for the user's normal one.</param>
    public void ApplySettings(Settings s, string? layoutOverride = null)
    {
        _settings = s;
        _layout = layoutOverride ?? s.Layout;
        Opacity = s.Opacity;
        PanelScale.ScaleX = PanelScale.ScaleY = s.Scale;

        var expanded = _layout != Settings.Compact;
        ExpandedLayout.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        CompactLayout.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        FrameTimeRow.Visibility = CFrameTimeBox.Visibility = Show(s.ShowFrameTime);
        LowRow.Visibility = Show(s.ShowOnePercentLow);
        CpuRow.Visibility = CCpuBox.Visibility = Show(s.ShowCpu);
        RamRow.Visibility = Show(s.ShowRam);
        _shownLimits = (Limit)(-1); // re-render limit chips

        ApplyWindowStyles();
        Reposition();
    }

    private static Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    private void ApplyWindowStyles()
    {
        if (Handle == IntPtr.Zero) return;
        var ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE) | Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
        ex = _settings.ClickThrough ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
        Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
        Cursor = _settings.ClickThrough ? null : Cursors.SizeAll;
    }

    /// <summary>Places the overlay in its corner of the chosen monitor (primary if that one is gone).</summary>
    public void Reposition()
    {
        if (Handle == IntPtr.Zero || Displays.Find(_settings.Monitor) is not { } display) return;
        if (!Native.GetWindowRect(Handle, out var r)) return;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        var m = (int)Math.Round(EdgeMarginDip * display.Dpi / 96.0);
        var work = display.WorkArea;
        var x = _settings.Position.EndsWith("Left") ? work.Left + m : work.Right - w - m;
        var y = _settings.Position.StartsWith("Bottom") ? work.Bottom - h - m : work.Top + m;
        if (x != r.Left || y != r.Top)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (_settings.ClickThrough) return;
        try { DragMove(); } catch (InvalidOperationException) { return; }

        // Snap to the nearest corner of the monitor the overlay was dropped on
        if (!Native.GetWindowRect(Handle, out var r)) return;
        var cx = (r.Left + r.Right) / 2;
        var cy = (r.Top + r.Bottom) / 2;
        var display = Displays.All().FirstOrDefault(d => cx >= d.Bounds.Left && cx < d.Bounds.Right && cy >= d.Bounds.Top && cy < d.Bounds.Bottom);
        if (display is null) { Reposition(); return; }
        var left = cx < (display.WorkArea.Left + display.WorkArea.Right) / 2;
        var top = cy < (display.WorkArea.Top + display.WorkArea.Bottom) / 2;
        _settings.Position = (top ? "Top" : "Bottom") + (left ? "Left" : "Right");
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
        Badge.Visibility = CBadge.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        Badge.Background = CBadge.Background = status.Paused ? Muted : (Brush)FindResource("Accent");
    }

    public void ShowFps(Telemetry t)
    {
        FpsHint.Text = t.FpsUnavailable;
        FpsHint.Visibility = t.FpsUnavailable is null ? Visibility.Collapsed : Visibility.Visible;

        if (t.Fps is { } s)
        {
            FpsValue.Text = CFps.Text = Math.Round(s.Fps).ToString("0");
            FrameTime.Text = CFrameTime.Text = s.FrameTimeMs.ToString("0.0");
            LowFps.Text = Math.Round(s.OnePercentLow).ToString("0");
            FpsValue.Foreground = CFps.Foreground = Normal;
        }
        else
        {
            FpsValue.Text = CFps.Text = FrameTime.Text = CFrameTime.Text = LowFps.Text = "—";
            FpsValue.Foreground = CFps.Foreground = Muted;
        }
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
        GpuTemp.Foreground = TempBrush(g.TempC, s.GpuTempWarn, s.GpuTempHot);
        SetBar(GpuBar, g.Load);
        const double gb = 1024.0 * 1024 * 1024;
        Set(VramUsed, g.VramUsed / gb, "0.0");
        VramTotal.Text = g.VramTotal is { } total ? $"/ {total / gb:0.0} GB" : "/ — GB";

        Set(CpuLoad, t.CpuLoad, "0");
        Set(CpuTemp, t.CpuTempC, "0");
        Set(CpuClock, t.CpuClockGhz, "0.0");
        CpuTemp.Foreground = TempBrush(t.CpuTempC, s.CpuTempWarn, s.CpuTempHot);
        SetBar(CpuBar, t.CpuLoad);

        Set(RamUsed, t.Ram?.UsedGb, "0.0");
        RamTotal.Text = t.Ram is { } r ? $"/ {r.TotalGb:0.0} GB" : "/ — GB";

        // Compact line
        CGpu.Text = ok ? $"{Num(g.Load, "0")}% · {Num(g.TempC, "0")}°" : g.Status == GpuStatus.Sleeping ? "sleeping" : "—";
        CGpu.Foreground = TempBrush(g.TempC, s.GpuTempWarn, s.GpuTempHot);
        CCpu.Text = $"{Num(t.CpuLoad, "0")}% · {Num(t.CpuTempC, "0")}°";
        CCpu.Foreground = TempBrush(t.CpuTempC, s.CpuTempWarn, s.CpuTempHot);

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
            if (limits.HasFlag(flag)) LimitsRow.Children.Add(Chip(text, hot ? Hot : Warm, 9.5));
        }
        LimitsRow.Visibility = Show(limits != Limit.None);

        var thermal = (limits & (Limit.GpuThermal | Limit.CpuThermal)) != 0;
        CLimit.Visibility = Show(limits != Limit.None);
        CLimitText.Text = thermal ? "HEAT" : "POWER";
        var color = ((SolidColorBrush)(thermal ? Hot : Warm)).Color;
        CLimitText.Foreground = new SolidColorBrush(color);
        CLimit.Background = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B));
    }

    private static Border Chip(string text, Brush brush, double size)
    {
        var c = ((SolidColorBrush)brush).Color;
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x30, c.R, c.G, c.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, c.R, c.G, c.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 1, 6, 2),
            Margin = new Thickness(0, 0, 6, 4),
            Child = new TextBlock
            {
                Text = text, FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.Bold,
                FontSize = size, Foreground = brush,
            },
        };
    }

    private static string Num(double? v, string format) => v is { } x ? x.ToString(format) : "—";

    private static void Set(TextBlock block, double? value, string format) => block.Text = Num(value, format);

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

    private static Brush TempBrush(double? c, double warm, double hot) =>
        c is null ? Normal : c >= hot ? Hot : c >= warm ? Warm : Normal;

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
