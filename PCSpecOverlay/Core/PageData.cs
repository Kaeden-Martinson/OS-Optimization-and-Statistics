using System.Globalization;
using PCSpecOverlay.Optimizer;

namespace PCSpecOverlay.Core;

/// <summary>The temperature scale on the Alerts page: 30–100 °C in whole degrees, Warning always below Hot.</summary>
public static class TempScale
{
    public const int Min = 30, Max = 100;

    public static double Fraction(double celsius) => Math.Clamp((celsius - Min) / (Max - Min), 0, 1);

    public static int ValueAt(double fraction) =>
        (int)Math.Round(Min + Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1) * (Max - Min));

    public static (int Warn, int Hot) MoveWarn(int warn, int hot, int to) => (Math.Clamp(to, Min, Math.Max(Min, hot - 1)), hot);

    public static (int Warn, int Hot) MoveHot(int warn, int hot, int to) => (warn, Math.Clamp(to, Math.Min(Max, warn + 1), Max));
}

/// <summary>One change still in effect, as the Diagnostics journal lists it.</summary>
public sealed record JournalRow(string Id, string When, string Setting, string Change, string Owners);

public static class JournalRows
{
    public static string OwnerLabel(string owner) => owner switch
    {
        Tweak.Owner => "Optimizer",
        GamingMode.Owner => "Gaming mode",
        _ => owner,
    };

    /// <summary>Changes the app still owns, newest first, with values in words.</summary>
    public static List<JournalRow> Build(IEnumerable<JournalEntry> entries, Func<string, string?> name,
        Func<string, string?, string> describe, DateTime nowLocal) =>
        entries.OrderByDescending(e => e.AppliedUtc).Select(e =>
        {
            var at = e.AppliedUtc.ToLocalTime();
            var when = at.Date == nowLocal.Date
                ? "Today " + at.ToString("HH:mm", CultureInfo.InvariantCulture)
                : at.ToString("MMM d HH:mm", CultureInfo.InvariantCulture);
            return new JournalRow(e.Id, when, name(e.Id) ?? e.Id,
                $"{describe(e.Id, e.Original)} → {describe(e.Id, e.Applied)}", string.Join(", ", e.Owners.Select(OwnerLabel)));
        }).ToList();
}

/// <summary>The text the Overlay page's preview draws, from the same switches the overlay uses.</summary>
public static class OverlayPreview
{
    public static string Strip(Settings s, double? fps, double? frameMs, double? gpuC, double? cpuC)
    {
        var parts = new List<string> { $"{Whole(fps)} FPS" };
        if (s.ShowFrameTime) parts.Add($"{Tenths(frameMs)} ms");
        if (s.ShowGpu) parts.Add($"GPU {Whole(gpuC)}°");
        if (s.ShowCpu) parts.Add($"CPU {Whole(cpuC)}°");
        return string.Join("  ", parts);
    }

    public static string Panel(Settings s, double? fps, double? frameMs, double? gpuC, double? cpuC, double? ramGb)
    {
        var lines = new List<string> { AppIdentity.ProductName.ToUpperInvariant(), $"{Whole(fps)} FPS" };
        if (s.ShowFrameTime) lines.Add($"{Tenths(frameMs)} ms");
        if (s.ShowGpu) lines.Add($"GPU  {Whole(gpuC)}°");
        if (s.ShowCpu) lines.Add($"CPU  {Whole(cpuC)}°");
        if (s.ShowRam) lines.Add($"RAM  {Tenths(ramGb)} GB");
        return string.Join("\n", lines);
    }

    private static string Whole(double? v) => v is { } x ? x.ToString("0", CultureInfo.InvariantCulture) : "—";
    private static string Tenths(double? v) => v is { } x ? x.ToString("0.0", CultureInfo.InvariantCulture) : "—";
}

/// <summary>Wording for the Diagnostics health grid.</summary>
public static class Health
{
    public static string Summary(IReadOnlyCollection<DiagLevel> levels)
    {
        var problems = levels.Count(l => l is DiagLevel.Warn or DiagLevel.Error);
        return problems == 0
            ? $"All {levels.Count} checks look fine."
            : $"{levels.Count - problems} of {levels.Count} checks look fine; {problems} {(problems == 1 ? "needs" : "need")} a look.";
    }

    public static string Word(DiagLevel level) => level switch
    {
        DiagLevel.Ok => "OK",
        DiagLevel.Warn => "Check",
        DiagLevel.Error => "Problem",
        _ => "Note",
    };
}
