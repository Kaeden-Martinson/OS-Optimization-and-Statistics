namespace PCSpecOverlay.Monitors;

public readonly record struct FpsSample(double Fps, double FrameTimeMs, double OnePercentLow, double WorstFrameMs = 0);

/// <summary>
/// Frame-rate math over a list of present timestamps (milliseconds, ascending).
/// <list type="bullet">
/// <item><b>FPS</b>: frames presented during the most recent 1 s, divided by the time they spanned.</item>
/// <item><b>Frame time</b>: the average interval between those frames (1000 / FPS).</item>
/// <item><b>1% low</b>: 1000 ÷ the 99th-percentile frame interval over the last 10 s, i.e. the frame rate
/// of the slowest 1% of frames. Capped at the current FPS.</item>
/// <item><b>Worst frame</b>: the longest interval among those frames — a stutter shows up here even when the average hides it.</item>
/// </list>
/// </summary>
public static class FrameStats
{
    public const double FpsWindowMs = 1_000;
    public const double LowWindowMs = 10_000;

    /// <param name="scratch">Reusable buffer for frame intervals (avoids allocating every sample).</param>
    public static FpsSample? Compute(IReadOnlyList<double> presents, List<double> scratch)
    {
        if (presents.Count < 2) return null;
        var end = presents[^1];

        var first = presents.Count - 1;
        while (first > 0 && end - presents[first - 1] <= FpsWindowMs) first--;
        var frames = presents.Count - 1 - first;
        var span = end - presents[first];
        if (frames < 1 || span <= 0) return null;
        var fps = frames * 1000.0 / span;

        var worst = 0.0;
        for (var i = first + 1; i < presents.Count; i++) worst = Math.Max(worst, presents[i] - presents[i - 1]);

        scratch.Clear();
        for (var i = 1; i < presents.Count; i++)
            if (end - presents[i - 1] <= LowWindowMs) scratch.Add(presents[i] - presents[i - 1]);
        scratch.Sort();
        var p99 = scratch[Math.Min(scratch.Count - 1, (int)(scratch.Count * 0.99))];
        var low = p99 > 0 ? Math.Min(1000.0 / p99, fps) : fps;

        return new FpsSample(fps, span / frames, low, worst);
    }

    /// <summary>Presents in the most recent second, used to pick the busiest swap chain.</summary>
    public static int RecentCount(IReadOnlyList<double> presents)
    {
        if (presents.Count == 0) return 0;
        var end = presents[^1];
        var i = presents.Count - 1;
        while (i > 0 && end - presents[i - 1] <= FpsWindowMs) i--;
        return presents.Count - i;
    }
}
