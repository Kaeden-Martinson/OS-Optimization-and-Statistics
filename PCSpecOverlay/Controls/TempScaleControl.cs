using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PCSpecOverlay.Core;

namespace PCSpecOverlay.Controls;

/// <summary>
/// One sensor's 30–100 °C scale on the Alerts page: the warning band (Signal) and hot band (Hot) shaded, a line at
/// the current reading, and two handles that drag or step with the keyboard. Warning always stays below Hot.
/// </summary>
public sealed class TempScaleControl : Canvas
{
    private const double TrackTop = 22, TrackHeight = 8, AxisTop = 40, HandleWidth = 10, HandleHeight = 22;
    private static readonly int[] AxisMarks = [30, 50, 70, 90, 100];

    private readonly Rectangle _track = new() { Height = TrackHeight, StrokeThickness = 1 };
    private readonly Rectangle _warnBand = new() { Height = TrackHeight, Opacity = 0.35 };
    private readonly Rectangle _hotBand = new() { Height = TrackHeight, Opacity = 0.35 };
    private readonly Rectangle _current = new() { Width = 2, Height = TrackHeight + 8, Visibility = Visibility.Collapsed };
    private readonly Thumb _warnHandle = new(), _hotHandle = new();
    private readonly TextBlock _warnLabel = Label("Signal"), _hotLabel = Label("Hot");
    private readonly List<(TextBlock Text, int Celsius)> _axis;
    private double? _currentC;
    private double _dragX;

    public TempScaleControl()
    {
        Height = 56;
        _track.SetResourceReference(Shape.FillProperty, "Panel");
        _track.SetResourceReference(Shape.StrokeProperty, "Line");
        _warnBand.SetResourceReference(Shape.FillProperty, "Signal");
        _hotBand.SetResourceReference(Shape.FillProperty, "Hot");
        _current.SetResourceReference(Shape.FillProperty, "Text");
        _axis = AxisMarks.Select(c => (Label("Muted", c == TempScale.Max ? $"{c} °C" : $"{c}"), c)).ToList();

        foreach (var e in new UIElement[] { _track, _warnBand, _hotBand, _current, _warnLabel, _hotLabel }) Children.Add(e);
        foreach (var (text, _) in _axis) Children.Add(text);
        foreach (var (handle, warn) in new[] { (_warnHandle, true), (_hotHandle, false) })
        {
            handle.SetResourceReference(StyleProperty, "ScaleHandle");
            handle.DragStarted += (_, _) => _dragX = GetLeft(handle) + HandleWidth / 2;
            handle.DragDelta += (_, e) =>
            {
                _dragX += e.HorizontalChange;
                Move(warn, TempScale.ValueAt(_dragX / Math.Max(1, ActualWidth)));
            };
            handle.PreviewKeyDown += (_, e) => OnKey(warn, e);
            AutomationProperties.SetHelpText(handle, "Drag, or use the arrow keys to change it by 1 °C (Page Up and Page Down by 5).");
            Children.Add(handle);
        }
        SizeChanged += (_, _) => Place();
    }

    /// <summary>Sensor name used in the handles' screen-reader names, e.g. "GPU".</summary>
    public string Sensor { get; set; } = "";

    public int Warn { get; private set; } = 75;
    public int Hot { get; private set; } = 85;

    /// <summary>Raised after the user moves a handle, with the new warning and hot levels.</summary>
    public event Action<int, int>? LevelsChanged;

    public void SetLevels(int warn, int hot)
    {
        (Warn, Hot) = (warn, hot);
        Place();
    }

    public void SetCurrent(double? celsius)
    {
        _currentC = celsius;
        Place();
    }

    private double X(double celsius) => TempScale.Fraction(celsius) * ActualWidth;

    private void Place()
    {
        var w = ActualWidth;
        if (w <= 0) return;
        _track.Width = w;
        SetTop(_track, TrackTop);

        double xw = X(Warn), xh = X(Hot);
        SetLeft(_warnBand, xw); SetTop(_warnBand, TrackTop); _warnBand.Width = Math.Max(0, xh - xw);
        SetLeft(_hotBand, xh); SetTop(_hotBand, TrackTop); _hotBand.Width = Math.Max(0, w - xh);
        foreach (var (handle, x) in new[] { (_warnHandle, xw), (_hotHandle, xh) })
        {
            SetLeft(handle, x - HandleWidth / 2);
            SetTop(handle, TrackTop + TrackHeight / 2 - HandleHeight / 2);
        }

        _warnLabel.Text = $"warn {Warn}";
        _hotLabel.Text = $"hot {Hot}";
        var warnWidth = Measure(_warnLabel);
        var hotWidth = Measure(_hotLabel);
        var warnLeft = Math.Clamp(xw - HandleWidth / 2, 0, Math.Max(0, w - warnWidth));
        SetLeft(_warnLabel, warnLeft);
        SetLeft(_hotLabel, Math.Clamp(Math.Max(xh - HandleWidth / 2, warnLeft + warnWidth + 8), 0, Math.Max(0, w - hotWidth)));

        _current.Visibility = _currentC is null ? Visibility.Collapsed : Visibility.Visible;
        if (_currentC is { } c) { SetLeft(_current, X(c) - 1); SetTop(_current, TrackTop - 4); }

        foreach (var (text, celsius) in _axis)
        {
            var width = Measure(text);
            SetLeft(text, Math.Clamp(X(celsius) - width / 2, 0, Math.Max(0, w - width)));
            SetTop(text, AxisTop);
        }

        AutomationProperties.SetName(_warnHandle, $"{Sensor} warning level, {Warn} °C");
        AutomationProperties.SetName(_hotHandle, $"{Sensor} hot level, {Hot} °C");
    }

    private void OnKey(bool warn, KeyEventArgs e)
    {
        var step = e.Key switch
        {
            Key.Left or Key.Down => -1,
            Key.Right or Key.Up => 1,
            Key.PageDown => -5,
            Key.PageUp => 5,
            _ => 0,
        };
        if (step == 0) return;
        e.Handled = true;
        Move(warn, (warn ? Warn : Hot) + step);
    }

    private void Move(bool warn, int to)
    {
        var (w, h) = warn ? TempScale.MoveWarn(Warn, Hot, to) : TempScale.MoveHot(Warn, Hot, to);
        if (w == Warn && h == Hot) return;
        (Warn, Hot) = (w, h);
        Place();
        LevelsChanged?.Invoke(w, h);
    }

    private static double Measure(TextBlock t)
    {
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return t.DesiredSize.Width;
    }

    private static TextBlock Label(string brush, string text = "")
    {
        var t = new TextBlock { Text = text, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 10.5 };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }
}
