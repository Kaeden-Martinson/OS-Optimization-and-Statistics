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

public class FrameTimeTraceTests
{
    [Fact]
    public void Keeps_only_the_newest_values()
    {
        var t = new FrameTimeTrace(capacity: 5);
        for (var i = 1; i <= 8; i++) t.Add(i);
        Assert.Equal(5, t.Count);
        Assert.Equal(new double?[] { 4, 5, 6, 7, 8 }, t.Values());
    }

    [Fact]
    public void Missing_or_invalid_values_become_gaps()
    {
        var t = new FrameTimeTrace(capacity: 4);
        t.Add(10); t.Add(null); t.Add(double.NaN); t.Add(-3);
        Assert.Equal(new double?[] { 10, null, null, null }, t.Values());
        Assert.Single(t.Plot(100, 20)); // only the one real value is plotted
    }

    [Fact]
    public void Newest_value_is_at_the_right_edge()
    {
        var t = new FrameTimeTrace(capacity: 3);
        t.Add(10); t.Add(10); t.Add(10);
        var points = t.Plot(200, 20);
        Assert.Equal(new[] { 0.0, 100.0, 200.0 }, points.Select(p => p.X));
    }

    [Fact]
    public void A_partly_filled_trace_hugs_the_right_edge()
    {
        var t = new FrameTimeTrace(capacity: 5);
        t.Add(10); t.Add(10);
        Assert.Equal(new[] { 150.0, 200.0 }, t.Plot(200, 20).Select(p => p.X));
    }

    [Fact]
    public void Small_frame_times_scale_against_the_minimum_ceiling()
    {
        var t = new FrameTimeTrace(capacity: 2);
        t.Add(16.7); t.Add(16.7);
        var y = t.Plot(100, 30)[0].Y;
        Assert.Equal(30 - 16.7 / FrameTimeTrace.MinCeilingMs * 30, y, 3);
    }

    [Fact]
    public void A_spike_is_the_highest_point_and_stays_inside_the_box()
    {
        var t = new FrameTimeTrace(capacity: 4);
        t.Add(7); t.Add(7); t.Add(120); t.Add(7);
        var points = t.Plot(90, 24);
        var spike = points[2];
        Assert.Equal(points.Min(p => p.Y), spike.Y);
        Assert.InRange(spike.Y, 0, 24);
        Assert.All(points, p => Assert.InRange(p.Y, 0, 24));
    }

    [Fact]
    public void Nothing_to_draw_without_room_or_values()
    {
        var t = new FrameTimeTrace();
        Assert.Empty(t.Plot(100, 20));
        t.Add(10);
        Assert.Empty(t.Plot(0, 20));
        Assert.Empty(t.Plot(100, 0));
    }
}

public class OverlayColorTests
{
    private static readonly string[] Keys =
        ["O.Bg", "O.Solid", "O.Line", "O.Line2", "O.Text", "O.Muted", "O.Signal", "O.Hot", "O.SignalSoft", "O.HotSoft", "O.Track"];

    private static Color C(ResourceDictionary d, string key) => ((SolidColorBrush)d[key]).Color;

    [Theory]
    [InlineData("PCSpecOverlay/Themes/Overlay.Dark.xaml")]
    [InlineData("PCSpecOverlay/Themes/Overlay.Light.xaml")]
    public void Every_key_is_a_brush_and_nothing_else_is_defined(string file) => Sta.Run(() =>
    {
        var d = XamlFile.LoadDictionary(file);
        foreach (var key in Keys) Assert.IsType<SolidColorBrush>(d[key]);
        Assert.Equal(Keys.Length, d.Keys.Count);
    });

    [Theory]
    [InlineData("PCSpecOverlay/Themes/Overlay.Dark.xaml")]
    [InlineData("PCSpecOverlay/Themes/Overlay.Light.xaml")]
    public void Solid_is_the_panel_color_without_transparency(string file) => Sta.Run(() =>
    {
        var d = XamlFile.LoadDictionary(file);
        var bg = C(d, "O.Bg");
        Assert.Equal(Color.FromRgb(bg.R, bg.G, bg.B), C(d, "O.Solid"));
        Assert.InRange(bg.A, (byte)0xD0, (byte)0xF0); // see-through enough to sit on a game, solid enough to read
    });

    [Theory]
    [InlineData("PCSpecOverlay/Themes/Overlay.Dark.xaml")]
    [InlineData("PCSpecOverlay/Themes/Overlay.Light.xaml")]
    public void Readable_overlay_text_meets_four_and_a_half_to_one(string file) => Sta.Run(() =>
    {
        var d = XamlFile.LoadDictionary(file);
        foreach (var fg in new[] { "O.Text", "O.Muted", "O.Signal", "O.Hot" })
        {
            var ratio = Contrast.Ratio(C(d, fg), C(d, "O.Solid"));
            Assert.True(ratio >= 4.5, $"{fg} is {ratio:0.00}:1 in {file}");
        }
        Assert.True(Contrast.Ratio(C(d, "O.Solid"), C(d, "O.Text")) >= 4.5, "badge text on badge");
    });
}
