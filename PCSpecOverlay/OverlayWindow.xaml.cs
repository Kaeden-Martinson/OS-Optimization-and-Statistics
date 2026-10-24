using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using PCSpecOverlay.Monitors;

namespace PCSpecOverlay;

public partial class OverlayWindow : Window
{
    private static readonly Brush Normal = Frozen(0xF3, 0xF5, 0xF6);
    private static readonly Brush Warm = Frozen(0xF5, 0xB7, 0x31);
    private static readonly Brush Hot = Frozen(0xFF, 0x4D, 0x4F);
    private static readonly Brush Muted = Frozen(0x69, 0x72, 0x7B);
    private const double EdgeMargin = 14;

    private string _corner = "TopRight";

    public IntPtr Handle { get; private set; }

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            // Click-through, never focused, hidden from Alt+Tab
            var ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE,
                ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        };
        SizeChanged += (_, _) => Reposition();
    }

    public void EnsureHandle() => Handle = new WindowInteropHelper(this).EnsureHandle();

    public void ApplySettings(Settings s)
    {
        _corner = s.Position;
        Opacity = Math.Clamp(s.Opacity, 0.2, 1.0);
        PanelScale.ScaleX = PanelScale.ScaleY = Math.Clamp(s.Scale, 0.6, 2.0);
        Reposition();
    }

    public void Reposition()
    {
        var area = SystemParameters.WorkArea;
        Left = _corner.EndsWith("Left") ? area.Left + EdgeMargin : area.Right - ActualWidth - EdgeMargin;
        Top = _corner.StartsWith("Bottom") ? area.Bottom - ActualHeight - EdgeMargin : area.Top + EdgeMargin;
    }

    /// <summary>Fullscreen/borderless apps can push themselves above us; re-assert z-order.</summary>
    public void KeepOnTop()
    {
        if (Handle != IntPtr.Zero && IsVisible)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public void SetGpuName(string name) =>
        GpuName.Text = name.Replace("NVIDIA ", "").Replace("GeForce ", "").Replace(" Laptop GPU", " Laptop");

    public void ShowBoost(bool boostActive, bool gamingMode)
    {
        BoostBadgeText.Text = gamingMode ? "GAMING MODE" : "BOOST";
        BoostBadge.Visibility = boostActive || gamingMode ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowFps(FpsSample? sample, string? hint)
    {
        FpsHint.Text = hint;
        FpsHint.Visibility = hint is null ? Visibility.Collapsed : Visibility.Visible;

        if (sample is { } s)
        {
            FpsValue.Text = Math.Round(s.Fps).ToString("0");
            FrameTime.Text = s.FrameTimeMs.ToString("0.0");
            LowFps.Text = Math.Round(s.OnePercentLow).ToString("0");
            FpsValue.Foreground = Normal;
        }
        else
        {
            FpsValue.Text = FrameTime.Text = LowFps.Text = "—";
            FpsValue.Foreground = Muted;
        }
    }

    public void ShowGpu(GpuSample? gpu)
    {
        var g = gpu ?? default;
        Set(GpuLoad, g.Load, "0");
        Set(GpuTemp, g.TempC, "0");
        Set(GpuClock, g.ClockMhz, "0");
        Set(GpuPower, g.PowerW, "0");
        GpuTemp.Foreground = TempBrush(g.TempC, warm: 75, hot: 85);
        SetBar(GpuBar, g.Load);

        const double gb = 1024.0 * 1024 * 1024;
        Set(VramUsed, g.VramUsed / gb, "0.0");
        VramTotal.Text = g.VramTotal is { } total ? $"/ {total / gb:0.0} GB" : "/ — GB";
    }

    public void ShowCpu(double? load, double? tempC, double? clockGhz, (double UsedGb, double TotalGb)? ram)
    {
        Set(CpuLoad, load, "0");
        Set(CpuTemp, tempC, "0");
        Set(CpuClock, clockGhz, "0.0");
        CpuTemp.Foreground = TempBrush(tempC, warm: 80, hot: 92);
        SetBar(CpuBar, load);

        Set(RamUsed, ram?.UsedGb, "0.0");
        RamTotal.Text = ram is { } r ? $"/ {r.TotalGb:0.0} GB" : "/ — GB";
    }

    private Limit _shownLimits = Limit.None;

    /// <summary>Red tags for overheating, amber for power caps, e.g. "GPU · TOO HOT".</summary>
    public void ShowLimits(Limit limits)
    {
        if (limits == _shownLimits) return;
        _shownLimits = limits;

        LimitsRow.Children.Clear();
        foreach (var (flag, text, hot) in new[]
                 {
                     (Limit.GpuThermal, "GPU · TOO HOT, SLOWING DOWN", true),
                     (Limit.CpuThermal, "CPU · TOO HOT, SLOWING DOWN", true),
                     (Limit.GpuPower, "GPU · POWER LIMIT", false),
                     (Limit.CpuPower, "CPU · POWER LIMIT", false),
                 })
        {
            if (!limits.HasFlag(flag)) continue;
            var color = hot ? ((SolidColorBrush)Hot).Color : ((SolidColorBrush)Warm).Color;
            LimitsRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 1, 6, 2),
                Margin = new Thickness(0, 0, 6, 4),
                Child = new TextBlock
                {
                    Text = text,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(color),
                },
            });
        }
        LimitsRow.Visibility = limits == Limit.None ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void Set(TextBlock block, double? value, string format) =>
        block.Text = value is { } v ? v.ToString(format) : "—";

    private static void SetBar(FrameworkElement fill, double? percent) =>
        ((ScaleTransform)fill.RenderTransform).ScaleX = Math.Clamp((percent ?? 0) / 100.0, 0, 1);

    private static Brush TempBrush(double? c, double warm, double hot) =>
        c is null ? Normal : c >= hot ? Hot : c >= warm ? Warm : Normal;

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
