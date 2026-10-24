using PCSpecOverlay.Core;

namespace PCSpecOverlay.Optimizer;

/// <summary>
/// Automatic gaming mode. Once a game that may be optimized has been in front for a few seconds, it
/// applies the optimizations the user enabled (Best performance power mode, Game Boost priorities);
/// once no such game has been in front for the grace period, it releases them.
/// <list type="bullet">
/// <item>Changes go through <see cref="ChangeJournal"/> under the "gaming" owner: originals are recorded,
/// and nothing is restored if the user changed it in the meantime.</item>
/// <item>Several games are tracked at once; optimizations stay on until the last one has been gone for
/// the full grace period.</item>
/// <item>If an optimization can't be applied it's reported and skipped; the rest still apply.</item>
/// </list>
/// </summary>
internal sealed class GamingMode(Settings settings, ChangeJournal journal, GameBoost boost, IReversibleSetting power, TimeProvider time)
{
    public const string Owner = "gaming";
    private const long GapMs = 3_000; // FPS dips shorter than this (menus, loading) don't reset "in front since"

    private sealed class Seen { public long First, Last; public string Label = ""; }
    private readonly Dictionary<string, Seen> _games = new(StringComparer.OrdinalIgnoreCase);

    public bool Active { get; private set; }

    /// <summary>Optimizations currently in effect, in plain words.</summary>
    public List<string> Optimizations { get; } = new();

    /// <summary>Things the user should know (an optimization was skipped, a setting was changed by hand).</summary>
    public List<string> Notices { get; } = new();

    public IEnumerable<string> ActiveGames =>
        _games.Values.Where(g => Now - g.Last < settings.GamingExitSeconds * 1000L).Select(g => g.Label);

    private long Now => time.GetTimestamp() * 1000 / time.TimestampFrequency;

    /// <summary>Called every couple of seconds with the game in front (only optimizable ones count).</summary>
    public void Tick(GameDetection? game)
    {
        var now = Now;
        if (game is { Optimize: true })
        {
            if (!_games.TryGetValue(game.Process, out var seen) || now - seen.Last > GapMs)
                _games[game.Process] = seen = new Seen { First = now };
            seen.Last = now;
            seen.Label = game.Label;
        }
        foreach (var key in _games.Where(kv => now - kv.Value.Last >= settings.GamingExitSeconds * 1000L).Select(kv => kv.Key).ToList())
            _games.Remove(key);

        if (!settings.AutoGamingMode)
        {
            if (Active) Exit("gaming mode was turned off");
            return;
        }

        var ready = _games.Values.Any(g => g.Last - g.First >= settings.GamingEnterSeconds * 1000L);
        if (!Active && ready) Enter();
        else if (Active && _games.Count == 0) Exit("no game has been in front for a while");
        else if (Active) WatchForOutsideChanges();
    }

    private void Enter()
    {
        Active = true;
        Optimizations.Clear();
        Notices.Clear();
        Log.Info("Gaming", $"Gaming mode on for {string.Join(", ", ActiveGames)}");

        if (settings.GamingPowerMode)
        {
            var r = journal.Apply(power, PowerMode.BestPerformance.ToString(), Owner);
            if (r.Outcome is ApplyOutcome.Applied or ApplyOutcome.AlreadySet) Optimizations.Add("Power mode: Best performance");
            else Notices.Add($"Power mode not changed: {r.Message}");
        }

        if (settings.GamingProcessPriority)
        {
            boost.SetEnabled(true); // temporary: the saved Game Boost preference is left alone
            Optimizations.Add("Game gets higher CPU priority" +
                (settings.BoostTargets.Count > 0 ? $"; lowered: {string.Join(", ", settings.BoostTargets)}" : ""));
        }
    }

    private void WatchForOutsideChanges()
    {
        if (!journal.IsOwnedBy(power.Id, Owner) || journal.Entry(power.Id) is not { } entry) return;
        string? current;
        try { current = power.Read(); } catch { return; }
        if (current == entry.Applied) return;

        const string notice = "You changed the power mode during the game; it will be left as you set it.";
        if (!Notices.Contains(notice)) Notices.Add(notice);
        Optimizations.RemoveAll(o => o.StartsWith("Power mode"));
    }

    /// <summary>Releases every gaming optimization now (also the emergency "stop" action).</summary>
    public void Exit(string reason)
    {
        if (Active) Log.Info("Gaming", $"Gaming mode off: {reason}");
        Active = false;
        _games.Clear();
        Optimizations.Clear();

        var r = journal.Release(power, Owner);
        if (r.Outcome is ReleaseOutcome.ChangedElsewhere or ReleaseOutcome.Failed) Notices.Add(r.Message);
        boost.SetEnabled(settings.GameBoost);
    }
}
