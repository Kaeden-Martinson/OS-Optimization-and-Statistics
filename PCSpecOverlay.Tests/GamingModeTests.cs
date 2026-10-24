using Microsoft.Extensions.Time.Testing;
using PCSpecOverlay.Core;
using PCSpecOverlay.Optimizer;

namespace PCSpecOverlay.Tests;

public class GamingModeTests : TempDataTest
{
    private readonly FakeTimeProvider _time = new();
    private readonly Settings _settings = new() { AutoGamingMode = true, GamingEnterSeconds = 5, GamingExitSeconds = 20, GamingProcessPriority = false };
    private readonly AppState _state = new();
    private readonly FakeSetting _power = new("power-mode", "balanced");
    private readonly ChangeJournal _journal;
    private readonly GamingMode _gaming;

    private static readonly GameDetection Starve = new("msedge", "Starve.io", new GameRule { Match = "Starve.io" }, Optimize: true);
    private static readonly GameDetection Other = new("terraria", "terraria", new GameRule { Match = "terraria", Kind = GameMatchKind.ProcessName }, Optimize: true);

    public GamingModeTests()
    {
        _journal = new ChangeJournal(_state, _time);
        _gaming = new GamingMode(_settings, _journal, new GameBoost(_state), _power, _time);
    }

    private static string Best => PowerMode.BestPerformance.ToString();

    /// <summary>Ticks every 2 s for the given duration, like the app's timer.</summary>
    private void Play(GameDetection? game, int seconds)
    {
        for (var t = 0; t < seconds; t += 2)
        {
            _gaming.Tick(game);
            _time.Advance(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public void Switches_on_after_the_enter_delay_and_back_after_the_grace_period()
    {
        Play(Starve, 4);
        Assert.False(_gaming.Active);
        Play(Starve, 4);
        Assert.True(_gaming.Active);
        Assert.Equal(Best, _power.Value);

        Play(null, 16); // within the grace period
        Assert.True(_gaming.Active);
        Play(null, 8);
        Assert.False(_gaming.Active);
        Assert.Equal("balanced", _power.Value);
    }

    [Fact]
    public void Quick_alt_tab_doesnt_switch_back()
    {
        Play(Starve, 10);
        Play(null, 6);
        Play(Starve, 4);
        Play(null, 16);
        Assert.True(_gaming.Active);
    }

    [Fact]
    public void Keeps_the_users_power_choice_made_during_the_game()
    {
        Play(Starve, 10);
        _power.Value = "efficiency"; // user changed it mid-game
        Play(Starve, 4);
        Assert.Contains(_gaming.Notices, n => n.Contains("changed the power mode"));

        Play(null, 30);
        Assert.False(_gaming.Active);
        Assert.Equal("efficiency", _power.Value);
    }

    [Fact]
    public void Second_game_keeps_optimizations_until_both_are_gone()
    {
        Play(Starve, 10);
        Play(Other, 10);
        Play(null, 16);
        Assert.True(_gaming.Active);
        Play(null, 10);
        Assert.False(_gaming.Active);
        Assert.Equal("balanced", _power.Value);
    }

    [Fact]
    public void Detect_only_games_never_trigger_it()
    {
        var unconfirmed = Starve with { Rule = null, Optimize = false };
        Play(unconfirmed, 30);
        Assert.False(_gaming.Active);
        Assert.Equal(0, _power.Writes);
    }

    [Fact]
    public void Power_toggle_off_leaves_power_mode_alone()
    {
        _settings.GamingPowerMode = false;
        Play(Starve, 10);
        Assert.True(_gaming.Active);
        Assert.Equal("balanced", _power.Value);
        Assert.Equal(0, _power.Writes);
    }

    [Fact]
    public void Turning_the_feature_off_mid_game_restores_immediately()
    {
        Play(Starve, 10);
        _settings.AutoGamingMode = false;
        Play(Starve, 2);
        Assert.False(_gaming.Active);
        Assert.Equal("balanced", _power.Value);
    }

    [Fact]
    public void Unsupported_power_mode_is_reported_and_skipped()
    {
        _power.Unsupported = "no power modes on this plan";
        Play(Starve, 10);
        Assert.True(_gaming.Active);
        Assert.Contains(_gaming.Notices, n => n.Contains("no power modes"));
        Assert.Equal(0, _power.Writes);
    }

    [Fact]
    public void Already_best_performance_isnt_restored_to_anything()
    {
        _power.Value = Best;
        Play(Starve, 10);
        Play(null, 30);
        Assert.Equal(Best, _power.Value);
        Assert.Equal(0, _power.Writes);
    }

    [Fact]
    public void A_crash_mid_game_is_recovered_on_the_next_start()
    {
        Play(Starve, 10);
        Assert.Equal(Best, _power.Value);

        // No Exit(): the app died. Next start recovers from state.json.
        var journal = new ChangeJournal(AppState.Load(), _time);
        journal.RecoverTemporary([GamingMode.Owner], id => id == _power.Id ? _power : null);
        Assert.Equal("balanced", _power.Value);
    }
}
