using PCSpecOverlay.Core;

namespace PCSpecOverlay.Optimizer;

/// <summary>What the foreground window looks like right now.</summary>
public sealed record ForegroundWindow(string Process, string Title, bool Fullscreen, double? Fps);

/// <summary>A game in front. <see cref="Optimize"/> is false for detect-only (unconfirmed or opted-out) games.</summary>
public sealed record GameDetection(string Process, string Label, GameRule? Rule, bool Optimize)
{
    public bool Confirmed => Rule != null;
}

/// <summary>
/// Decides whether the foreground window is a game. It must be rendering steadily (30+ FPS) and either
/// match one of the user's game rules, or be fullscreen. Fullscreen-only matches are "unconfirmed":
/// they're shown, but don't change Windows settings unless the user opts in — a fullscreen video
/// would otherwise look like a game.
/// </summary>
public static class GameDetector
{
    public const double MinFps = 30;

    public static GameDetection? Detect(ForegroundWindow w, Settings s)
    {
        if (w.Fps is not >= MinFps || ProcessUtil.IsCritical(w.Process)) return null;

        var rule = s.GameRules.FirstOrDefault(r => r.Enabled && Matches(r, w));
        if (rule != null) return new GameDetection(w.Process, rule.Match, rule, rule.Optimize);

        return s.DetectFullscreenGames && w.Fullscreen
            ? new GameDetection(w.Process, w.Process, null, s.OptimizeFullscreenGames)
            : null;
    }

    public static bool Matches(GameRule r, ForegroundWindow w) => r.Kind switch
    {
        GameMatchKind.ProcessName => string.Equals(r.Match, w.Process, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(r.Match, w.Process + ".exe", StringComparison.OrdinalIgnoreCase),
        _ => w.Title.Contains(r.Match, StringComparison.OrdinalIgnoreCase),
    };
}
