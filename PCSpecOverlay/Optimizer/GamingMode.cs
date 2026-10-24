namespace PCSpecOverlay.Optimizer;

/// <summary>
/// Automatic gaming mode: once a game has been in front for a few seconds, switch Windows to
/// Best performance and turn on Game Boost; once no game has been in front for a while, put both
/// back. The short alt-tab grace period stops it flapping when you check another window mid-game.
/// </summary>
internal sealed class GamingMode(Settings settings, GameBoost boost)
{
    private const long EnterAfterMs = 5_000;  // a game must render this long before switching
    private const long ExitAfterMs = 20_000;  // alt-tab grace before switching back
    private const long GapMs = 3_000;         // short FPS dips (menus, loading) don't reset the timer

    private long _gameSince, _lastGame;

    public bool Active { get; private set; }
    public string? Game { get; private set; }

    /// <summary>Called every couple of seconds with the game in front, or null.</summary>
    public void Tick(string? game)
    {
        var now = Environment.TickCount64;

        if (game != null)
        {
            if (_gameSince == 0 || now - _lastGame > GapMs) _gameSince = now;
            _lastGame = now;
            if (Active) Game = game;
        }

        if (!settings.AutoGamingMode)
        {
            if (Active) Exit();
            return;
        }

        if (!Active && game != null && now - _gameSince >= EnterAfterMs) Enter(game);
        else if (Active && now - _lastGame >= ExitAfterMs) Exit();
    }

    private void Enter(string game)
    {
        Active = true;
        Game = game;

        var current = PowerMode.Current();
        if (current != PowerMode.BestPerformance)
        {
            settings.GamingRestorePowerMode = current.ToString();
            settings.Save();
            try { PowerMode.Set(PowerMode.BestPerformance); }
            catch { settings.GamingRestorePowerMode = null; }
        }
        boost.SetEnabled(true); // temporary: the saved Game Boost setting is left alone
    }

    public void Exit()
    {
        if (!Active) return;
        Active = false;
        Game = null;
        RestorePowerMode();
        boost.SetEnabled(settings.GameBoost);
    }

    /// <summary>
    /// Puts the power mode back. Also called at startup, in case the app was closed or crashed mid-game.
    /// Leaves it alone if you changed it yourself while playing.
    /// </summary>
    public void RestorePowerMode()
    {
        if (settings.GamingRestorePowerMode is not { } saved) return;
        try
        {
            if (PowerMode.Current() == PowerMode.BestPerformance && Guid.TryParse(saved, out var original))
                PowerMode.Set(original);
        }
        catch { /* keep current mode */ }
        settings.GamingRestorePowerMode = null;
        settings.Save();
    }
}
