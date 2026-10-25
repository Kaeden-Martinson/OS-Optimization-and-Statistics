namespace PCSpecOverlay.Core;

/// <summary>The small calculations behind the Overview page, kept apart from the window so they can be tested.</summary>
public static class OverviewData
{
    public const int TickCount = 10;

    /// <summary>How many of the ten segments of a reading's bar are filled (fraction 0–1; anything else counts as empty or full).</summary>
    public static int FilledTicks(double? fraction) =>
        fraction is { } f && double.IsFinite(f)
            ? (int)Math.Round(Math.Clamp(f, 0, 1) * TickCount, MidpointRounding.AwayFromZero)
            : 0;

    /// <summary>
    /// Frame-rate dips worth marking on the chart: points below <paramref name="ratio"/> × the average that are
    /// lower than the point before and no higher than the point after (a flat low stretch is marked once, at its
    /// start). Gaps (null or non-positive) are skipped; with fewer than three real values nothing is marked.
    /// </summary>
    public static List<int> Dips(IReadOnlyList<double?> values, double ratio = 0.7)
    {
        var dips = new List<int>();
        var valid = values.Where(v => v is > 0).Select(v => v!.Value).ToList();
        if (valid.Count < 3) return dips;
        var limit = valid.Average() * ratio;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is not { } v || v <= 0 || v >= limit) continue;
            var before = Neighbour(values, i, -1);
            var after = Neighbour(values, i, +1);
            if ((before is null || v < before) && (after is null || v <= after)) dips.Add(i);
        }
        return dips;
    }

    /// <summary>The nearest real value in one direction, skipping gaps.</summary>
    private static double? Neighbour(IReadOnlyList<double?> values, int index, int step)
    {
        for (var i = index + step; i >= 0 && i < values.Count; i += step)
            if (values[i] is > 0) return values[i];
        return null;
    }

    /// <summary>Log areas whose entries describe something the app did (as opposed to start-up chatter).</summary>
    private static readonly HashSet<string> ActionAreas = new(StringComparer.OrdinalIgnoreCase)
        { "Gaming", "GameBoost", "Changes", "Alert", "Optimizer", "Browser", "Updates", "Recovery", "Theme" };

    /// <summary>What the app did and why, newest first: actions from those areas plus every warning and error.</summary>
    public static List<LogEntry> Events(IReadOnlyList<LogEntry> log, int max) =>
        log.Where(e => e.Level != LogLevel.Info || ActionAreas.Contains(e.Area)).Reverse().Take(max).ToList();
}
