namespace PCSpecOverlay.Optimizer;

/// <summary>
/// Which game Game Boost should prioritize. A game keeps its boost through short dips (menus, loading,
/// a quick Alt+Tab) for the same grace period gaming mode uses, so priorities don't flip back and
/// forth every few seconds.
/// </summary>
public sealed class BoostTarget(TimeProvider time)
{
    private string? _process;
    private long _lastSeen;

    /// <param name="game">The game in front now, or null.</param>
    /// <returns>The process to boost, or null.</returns>
    public string? Update(GameDetection? game, TimeSpan grace)
    {
        var now = time.GetTimestamp();
        if (game is { Optimize: true })
        {
            _process = game.Process;
            _lastSeen = now;
        }
        else if (_process != null && time.GetElapsedTime(_lastSeen, now) >= grace)
        {
            _process = null;
        }
        return _process;
    }

    public void Reset() => _process = null;
}
