using System.Windows;

namespace PCSpecOverlay.Monitors;

/// <summary>
/// The overlay's frame-time line: the worst frame of each FPS update, newest on the right. Fixed size,
/// so it never grows. The scale stretches to fit the biggest spike but never zooms in past 33.4 ms
/// (30 FPS), so a smooth game draws a calm, low line and a stutter stands out.
/// </summary>
public sealed class FrameTimeTrace
{
    public const double MinCeilingMs = 33.4;
    private const double Headroom = 1.15;

    private readonly double?[] _values;
    private int _next;

    public FrameTimeTrace(int capacity = 60)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        _values = new double?[capacity];
    }

    public int Capacity => _values.Length;
    public int Count { get; private set; }

    /// <param name="ms">Frame time in milliseconds; null, NaN, infinite or non-positive leave a gap.</param>
    public void Add(double? ms)
    {
        _values[_next] = ms is { } v && double.IsFinite(v) && v > 0 ? v : null;
        _next = (_next + 1) % Capacity;
        if (Count < Capacity) Count++;
    }

    /// <summary>The stored values, oldest first.</summary>
    public IReadOnlyList<double?> Values()
    {
        var list = new List<double?>(Count);
        for (var i = 0; i < Count; i++) list.Add(_values[(_next - Count + i + Capacity) % Capacity]);
        return list;
    }

    /// <summary>Points for a polyline in a width × height box (y grows downward); gaps are skipped.</summary>
    public List<Point> Plot(double width, double height)
    {
        var points = new List<Point>();
        if (width <= 0 || height <= 0) return points;
        var values = Values();
        var max = values.Max() ?? 0;
        if (max <= 0) return points;
        var ceiling = Math.Max(MinCeilingMs, max * Headroom);
        var step = width / (Capacity - 1);
        var offset = Capacity - values.Count; // a partly filled trace starts part-way across, ending at the right edge
        for (var i = 0; i < values.Count; i++)
            if (values[i] is { } v)
                points.Add(new Point((offset + i) * step, height - Math.Min(v / ceiling, 1) * height));
        return points;
    }
}
