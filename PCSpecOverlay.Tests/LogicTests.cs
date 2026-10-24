using System.Windows.Input;
using Microsoft.Extensions.Time.Testing;
using PCSpecOverlay.Core;
using PCSpecOverlay.Monitors;
using PCSpecOverlay.Optimizer;

namespace PCSpecOverlay.Tests;

public class FrameStatsTests
{
    private static List<double> Steady(double fps, double seconds) =>
        Enumerable.Range(0, (int)(fps * seconds)).Select(i => i * 1000.0 / fps).ToList();

    [Fact]
    public void Steady_frame_rate()
    {
        var s = FrameStats.Compute(Steady(60, 5), new())!.Value;
        Assert.Equal(60, s.Fps, 1);
        Assert.Equal(16.67, s.FrameTimeMs, 1);
        Assert.Equal(60, s.OnePercentLow, 1);
    }

    [Fact]
    public void One_percent_low_reflects_stutters()
    {
        var presents = Steady(100, 5);
        for (var i = 50; i < presents.Count; i += 50) // a 50 ms hitch on 2% of frames
            for (var j = i; j < presents.Count; j++) presents[j] += 40;
        var s = FrameStats.Compute(presents, new())!.Value;
        Assert.InRange(s.OnePercentLow, 19, 21); // 1000 / 50 ms
        Assert.True(s.Fps > 70);
    }

    [Fact]
    public void Rare_hitches_below_one_percent_dont_drag_the_1_percent_low()
    {
        var presents = Steady(100, 5);
        for (var j = 250; j < presents.Count; j++) presents[j] += 40; // a single hitch in 500 frames
        Assert.True(FrameStats.Compute(presents, new())!.Value.OnePercentLow > 90);
    }

    [Fact]
    public void Too_few_frames_is_unavailable_not_zero()
    {
        Assert.Null(FrameStats.Compute([], new()));
        Assert.Null(FrameStats.Compute([5.0], new()));
    }

    [Fact]
    public void Busiest_counts_only_the_last_second() =>
        Assert.Equal(61, FrameStats.RecentCount(Steady(60, 5)));
}

public class GameDetectorTests
{
    private readonly Settings _s = new() { GameRules = [new GameRule { Match = "Starve.io" }, new GameRule { Match = "Terraria", Kind = GameMatchKind.ProcessName, Optimize = false }] };

    [Fact]
    public void Matches_window_title_rules()
    {
        var g = GameDetector.Detect(new ForegroundWindow("msedge", "Starve.io and 1 more page - Edge", false, 144), _s);
        Assert.NotNull(g);
        Assert.Equal("Starve.io", g!.Label);
        Assert.True(g.Optimize);
        Assert.True(g.Confirmed);
    }

    [Fact]
    public void Matches_app_name_rules_with_or_without_exe() =>
        Assert.NotNull(GameDetector.Detect(new ForegroundWindow("terraria", "Terraria: Dig Peon", false, 60), _s));

    [Fact]
    public void Respects_per_game_optimize_off() =>
        Assert.False(GameDetector.Detect(new ForegroundWindow("Terraria", "x", false, 60), _s)!.Optimize);

    [Fact]
    public void Needs_steady_rendering()
    {
        Assert.Null(GameDetector.Detect(new ForegroundWindow("msedge", "Starve.io", false, 12), _s));
        Assert.Null(GameDetector.Detect(new ForegroundWindow("msedge", "Starve.io", false, null), _s));
    }

    [Fact]
    public void Unlisted_fullscreen_apps_are_detect_only_by_default()
    {
        var g = GameDetector.Detect(new ForegroundWindow("vlc", "movie.mkv", true, 60), _s);
        Assert.NotNull(g);
        Assert.False(g!.Optimize);
        Assert.False(g.Confirmed);

        _s.OptimizeFullscreenGames = true;
        Assert.True(GameDetector.Detect(new ForegroundWindow("vlc", "movie.mkv", true, 60), _s)!.Optimize);
    }

    [Fact]
    public void Ignores_system_processes_and_disabled_rules()
    {
        Assert.Null(GameDetector.Detect(new ForegroundWindow("explorer", "Starve.io", true, 60), _s));
        _s.GameRules[0].Enabled = false;
        Assert.Null(GameDetector.Detect(new ForegroundWindow("msedge", "Starve.io", false, 144), _s));
    }
}

public class RestartTrackerTests : TempDataTest
{
    private const string Edge = @"C:\edge\msedge.exe";
    private readonly AppState _state = new();
    private static readonly DateTime T0 = new(2026, 10, 23, 22, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Change_while_running_needs_a_restart()
    {
        RestartTracker.Changed(_state, Edge, null, "GpuPreference=2;", T0);
        Assert.True(RestartTracker.NeedsRestart(_state, Edge, T0.AddHours(-1)));
    }

    [Fact]
    public void Undo_before_restarting_needs_no_restart()
    {
        RestartTracker.Changed(_state, Edge, null, "GpuPreference=2;", T0);
        RestartTracker.Changed(_state, Edge, "GpuPreference=2;", null, T0.AddMinutes(1));
        Assert.False(RestartTracker.NeedsRestart(_state, Edge, T0.AddHours(-1)));
    }

    [Fact]
    public void Restarted_browser_clears_the_warning_permanently()
    {
        RestartTracker.Changed(_state, Edge, null, "GpuPreference=2;", T0);
        Assert.False(RestartTracker.NeedsRestart(_state, Edge, T0.AddMinutes(5)));
        Assert.False(RestartTracker.NeedsRestart(_state, Edge, T0.AddHours(-1))); // cleared, even if asked with an old time
    }

    [Fact]
    public void Browser_not_running_needs_no_restart_but_keeps_tracking()
    {
        RestartTracker.Changed(_state, Edge, null, "GpuPreference=2;", T0);
        Assert.False(RestartTracker.NeedsRestart(_state, Edge, null));
        Assert.True(_state.BrowserRestarts.ContainsKey(Edge));
    }

    [Fact]
    public void Survives_an_app_restart()
    {
        RestartTracker.Changed(_state, Edge, null, "GpuPreference=2;", T0);
        Assert.True(RestartTracker.NeedsRestart(AppState.Load(), Edge, T0.AddHours(-1)));
    }
}

public class SettingsTests : TempDataTest
{
    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        var s = new Settings { Opacity = 5, Scale = double.NaN, FpsRefreshMs = 1, GpuTempWarn = 90, GpuTempHot = 60, Position = "Middle", Layout = "Huge" };
        s.Validate();
        Assert.Equal(1.0, s.Opacity);
        Assert.Equal(1.0, s.Scale);
        Assert.Equal(250, s.FpsRefreshMs);
        Assert.True(s.GpuTempHot >= s.GpuTempWarn);
        Assert.Equal("TopRight", s.Position);
        Assert.Equal(Settings.Expanded, s.Layout);
    }

    [Fact]
    public void Version_1_games_list_becomes_rules()
    {
        File.WriteAllText(AppPaths.SettingsFile, """{ "Games": ["Starve.io", " ", "starve.io"], "AutoGamingMode": true }""");
        var s = Settings.Load();
        Assert.Single(s.GameRules);
        Assert.Equal("Starve.io", s.GameRules[0].Match);
        Assert.Null(s.Games);
        Assert.True(s.AutoGamingMode);
    }

    [Fact]
    public void Corrupt_file_is_kept_aside_and_defaults_used()
    {
        File.WriteAllText(AppPaths.SettingsFile, "{ not json");
        var s = Settings.Load();
        Assert.Equal("Alt+Shift+O", s.Hotkey);
        Assert.Single(Directory.GetFiles(Dir, "settings.json.corrupt-*"));
    }

    [Fact]
    public void Duplicate_and_empty_rules_are_removed()
    {
        var s = new Settings { GameRules = [new() { Match = "A" }, new() { Match = "a" }, new() { Match = "  " }, new() { Match = "a", Kind = GameMatchKind.ProcessName }] };
        s.Validate();
        Assert.Equal(2, s.GameRules.Count);
    }

    [Fact]
    public void Migration_moves_undo_records_into_the_journal()
    {
        var setting = new FakeSetting("game-mode", "1");
        var settings = new Settings { TweakBackups = new() { ["game-mode"] = null, ["gone"] = "x" } };
        var state = new AppState();
        Migration.Run(settings, state, id => id == "game-mode" ? setting : null);

        var entry = Assert.Single(state.Changes);
        Assert.Equal("game-mode", entry.Id);
        Assert.Null(entry.Original);
        Assert.Equal("1", entry.Applied);
        Assert.Null(settings.TweakBackups);
    }
}

public class LimitTrackerTests
{
    [Fact]
    public void Flickering_limit_stays_visible_and_accumulates()
    {
        var time = new FakeTimeProvider();
        var t = new LimitTracker(time);
        for (var i = 0; i < 10; i++)
        {
            t.Update(i % 2 == 0 ? Limit.GpuThermal : Limit.None);
            time.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.Equal(Limit.GpuThermal, t.Update(Limit.None));
        Assert.True(t.ActiveFor(Limit.GpuThermal) >= TimeSpan.FromSeconds(9));

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(Limit.None, t.Update(Limit.None));
        Assert.Equal(TimeSpan.Zero, t.ActiveFor(Limit.GpuThermal));
    }

    [Fact]
    public void Sustained_threshold_ignores_missing_readings_and_resets_below()
    {
        var time = new FakeTimeProvider();
        var s = new SustainedThreshold(time);
        s.Update(90, 85);
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(10), s.Update(null, 85));
        Assert.Equal(TimeSpan.Zero, s.Update(70, 85));
    }
}

public class HistoryTests : TempDataTest
{
    private static Telemetry Sample(double fps, double gpuTemp) => new()
    {
        Fps = new FpsSample(fps, 1000 / fps, fps * 0.8),
        Gpu = new GpuSample(GpuStatus.Ok, 50, (uint)gpuTemp, null, null, 1500, 30),
        CpuLoad = 40, CpuTempC = 80,
    };

    [Fact]
    public void Session_is_summarized_after_the_game_has_been_gone_a_minute()
    {
        var time = new FakeTimeProvider();
        var h = new HistoryRecorder(new Settings(), time);
        for (var i = 0; i < 30; i++) { h.Record(Sample(120 + i % 10, 70 + i % 5), "Starve.io"); time.Advance(TimeSpan.FromSeconds(1)); }
        for (var i = 0; i < 70; i++) { h.Record(Sample(60, 60), null); time.Advance(TimeSpan.FromSeconds(1)); }

        var s = Assert.Single(HistoryRecorder.LoadSessions());
        Assert.Equal("Starve.io", s.Game);
        Assert.InRange(s.AvgFps!.Value, 120, 130);
        Assert.Equal(74, s.MaxGpuTempC);
        Assert.Equal(30, s.Samples);
    }

    [Fact]
    public void Very_short_sessions_arent_saved()
    {
        var h = new HistoryRecorder(new Settings());
        h.Record(Sample(120, 70), "Starve.io");
        h.Flush();
        Assert.Empty(HistoryRecorder.LoadSessions());
    }

    [Fact]
    public void In_memory_history_is_bounded_to_ten_minutes()
    {
        var time = new FakeTimeProvider();
        var h = new HistoryRecorder(new Settings(), time);
        for (var i = 0; i < 1200; i++) { h.Record(Sample(60, 60), null); time.Advance(TimeSpan.FromSeconds(1)); }
        Assert.InRange(h.Points.Count, 595, 602);
    }

    [Fact]
    public void Disabled_history_records_nothing()
    {
        var h = new HistoryRecorder(new Settings { HistoryEnabled = false });
        h.Record(Sample(60, 60), "x");
        Assert.Empty(h.Points);
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Alt+Shift+O", "Alt+Shift+O")]
    [InlineData("shift + alt + o", "Alt+Shift+O")]
    [InlineData("Ctrl+F12", "Ctrl+F12")]
    [InlineData("F10", "F10")]
    public void Valid_shortcuts_round_trip(string input, string expected)
    {
        Assert.True(Hotkeys.TryParse(input, out var mods, out var key));
        Assert.Equal(expected, Hotkeys.Format(mods, key));
    }

    [Theory]
    [InlineData("O")]        // would steal normal typing
    [InlineData("Alt+")]
    [InlineData("Alt+O+P")]
    [InlineData("Alt+Banana")]
    [InlineData("")]
    public void Invalid_shortcuts_are_rejected(string input) =>
        Assert.False(Hotkeys.TryParse(input, out _, out _));
}
