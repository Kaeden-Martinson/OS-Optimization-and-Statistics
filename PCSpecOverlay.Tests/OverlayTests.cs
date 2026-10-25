using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using PCSpecOverlay.Core;
using PCSpecOverlay.Monitors;

namespace PCSpecOverlay.Tests;

public class OverlaySettingTests
{
    [Fact]
    public void Fresh_settings_use_strip_and_a_dark_overlay()
    {
        var s = new Settings();
        Assert.Equal(Settings.Compact, s.Layout);
        Assert.Equal(Settings.AlwaysDark, s.OverlayTheme);
    }

    [Fact]
    public void A_saved_panel_layout_is_kept()
    {
        var s = JsonSerializer.Deserialize<Settings>("""{"Layout":"Expanded"}""")!;
        s.Validate();
        Assert.Equal(Settings.Expanded, s.Layout);
    }

    [Theory]
    [InlineData("SameAsApp", "SameAsApp")]
    [InlineData("AlwaysDark", "AlwaysDark")]
    [InlineData("AlwaysLight", "AlwaysLight")]
    [InlineData("Light", "AlwaysDark")]
    [InlineData("", "AlwaysDark")]
    public void Validate_keeps_known_overlay_themes(string stored, string expected)
    {
        var s = new Settings { OverlayTheme = stored };
        s.Validate();
        Assert.Equal(expected, s.OverlayTheme);
    }

    [Fact]
    public void Null_overlay_theme_is_repaired()
    {
        var s = JsonSerializer.Deserialize<Settings>("""{"OverlayTheme":null}""")!;
        s.Validate();
        Assert.Equal(Settings.AlwaysDark, s.OverlayTheme);
    }

    [Theory]
    [InlineData("AlwaysDark", AppTheme.Light, AppTheme.Dark)]
    [InlineData("AlwaysLight", AppTheme.Dark, AppTheme.Light)]
    [InlineData("SameAsApp", AppTheme.Light, AppTheme.Light)]
    [InlineData("SameAsApp", AppTheme.Dark, AppTheme.Dark)]
    [InlineData("Unknown", AppTheme.Light, AppTheme.Dark)]
    public void Resolves_the_overlay_theme(string overlayTheme, AppTheme app, AppTheme expected) =>
        Assert.Equal(expected, ThemeResolver.ResolveOverlay(overlayTheme, app));
}

public class WorstFrameTests
{
    private static List<double> Steady(double fps, double seconds) =>
        Enumerable.Range(0, (int)(fps * seconds)).Select(i => i * 1000.0 / fps).ToList();

    [Fact]
    public void Steady_frames_have_a_worst_frame_equal_to_the_frame_time()
    {
        var s = FrameStats.Compute(Steady(100, 3), new())!.Value;
        Assert.Equal(10, s.WorstFrameMs, 1);
    }

    [Fact]
    public void A_hitch_in_the_last_second_shows_as_the_worst_frame()
    {
        var p = Steady(100, 3);
        for (var j = 250; j < p.Count; j++) p[j] += 40; // one 50 ms frame, 0.5 s before the end
        var s = FrameStats.Compute(p, new())!.Value;
        Assert.Equal(50, s.WorstFrameMs, 1);
    }

    [Fact]
    public void A_hitch_older_than_one_second_is_not_the_worst_frame()
    {
        var p = Steady(100, 3);
        for (var j = 50; j < p.Count; j++) p[j] += 40; // 2.5 s before the end
        var s = FrameStats.Compute(p, new())!.Value;
        Assert.Equal(10, s.WorstFrameMs, 1);
    }
}
