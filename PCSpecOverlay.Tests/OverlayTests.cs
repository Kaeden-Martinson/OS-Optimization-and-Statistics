using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
        Assert.Equal(Settings.AlwaysDark, s.OverlayTheme); // missing in the file → default
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

    /// <summary>A translucent color painted over an opaque one, as the screen shows it.</summary>
    private static Color Over(Color top, Color bottom)
    {
        double a = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round(t * a + b * (1 - a));
        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    [Theory]
    [InlineData("PCSpecOverlay/Themes/Overlay.Dark.xaml")]
    [InlineData("PCSpecOverlay/Themes/Overlay.Light.xaml")]
    public void Warning_chip_text_meets_four_and_a_half_to_one_on_its_own_fill(string file) => Sta.Run(() =>
    {
        var d = XamlFile.LoadDictionary(file);
        foreach (var key in new[] { "O.Signal", "O.Hot" })
        {
            var fill = Over(C(d, key + "Soft"), C(d, "O.Solid"));
            var ratio = Contrast.Ratio(C(d, key), fill);
            Assert.True(ratio >= 4.5, $"{key} on {key}Soft is {ratio:0.00}:1 in {file}");
        }
    });

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

public class OverlaySourceTests
{
    [Fact]
    public void Overlay_xaml_takes_every_color_from_the_overlay_tokens()
    {
        var xaml = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/OverlayWindow.xaml"));
        Assert.DoesNotMatch(@"=""#[0-9A-Fa-f]{6,8}""", xaml);
        Assert.DoesNotMatch(@"\{StaticResource O\.", xaml);
        Assert.DoesNotMatch(@"\{(Static|Dynamic)Resource (Accent|Text|Muted|Bg|Panel|Signal|Hot|Warn)\}", xaml); // never the app's colors
        Assert.DoesNotContain("CornerRadius", xaml);
        Assert.Contains("Themes/Overlay.Dark.xaml", xaml);
    }

    [Fact]
    public void Overlay_code_has_no_hard_coded_colors()
    {
        var code = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/OverlayWindow.xaml.cs"));
        Assert.DoesNotMatch(@"Color\.From(Rgb|Argb)\(", code);
        Assert.DoesNotContain("Frozen(", code);
        Assert.DoesNotContain("FindResource(\"Accent\")", code);
    }
}

public class OverlayChoiceTests
{
    [Fact]
    public void Control_center_offers_strip_panel_and_the_overlay_theme()
    {
        var xaml = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/ControlCenterWindow.xaml"));
        Assert.Contains("Text=\"Strip\"", xaml);
        Assert.Contains("Tag=\"Compact\"", xaml);
        Assert.Contains("Tag=\"Expanded\"", xaml);
        Assert.Contains("SelectedValue=\"{Binding OverlayTheme}\"", xaml);
        foreach (var tag in new[] { "SameAsApp", "AlwaysDark", "AlwaysLight" }) Assert.Contains($"Tag=\"{tag}\"", xaml);
        Assert.DoesNotContain("Content=\"Expanded\"", xaml);
        Assert.DoesNotContain("Content=\"Compact\"", xaml);
    }
}

/// <summary>
/// The overlay's width depends on its layout and switches, never on the readings: 2.1.0 grew and shrank
/// every tick ("2" → "144" FPS, "30W" → "115W", a slowdown chip coming and going) and walked off its display.
/// </summary>
public class OverlayWidthTests
{
    private const double Gb = 1024.0 * 1024 * 1024;

    // Every reading at its narrowest (or missing)...
    private static readonly Telemetry Narrow = new()
    {
        Fps = new FpsSample(2, 9.9, 1, 9.9),
        FpsSource = "DXGI",
        Gpu = new GpuSample(GpuStatus.Ok, 0, 9, (ulong)(0.1 * Gb), (ulong)(63.7 * Gb), 0, 0),
        CpuLoad = 0, CpuTempC = 9, CpuClockGhz = null,
        Ram = (0.1, 63.7),
        Limits = Limit.None,
    };

    // ...and at its widest
    private static readonly Telemetry Wide = new()
    {
        Fps = new FpsSample(9999, 999.9, 9999, 999.9),
        FpsSource = "KERNEL",
        Gpu = new GpuSample(GpuStatus.Ok, 100, 105, (ulong)(63.7 * Gb), (ulong)(63.7 * Gb), 3105, 450),
        CpuLoad = 100, CpuTempC = 105, CpuClockGhz = 5.8,
        Ram = (63.7, 63.7),
        Limits = Limit.GpuThermal | Limit.CpuThermal | Limit.GpuPower | Limit.CpuPower,
    };

    // The Strip holds its width up to 999 FPS and 99.9 ms; beyond that (rare) a cell may grow
    private static readonly Telemetry StripWide = Wide with { Fps = new FpsSample(999, 99.9, 999, 99.9) };

    private static double Width(string layout, Telemetry t) => Lay(layout, t).DesiredSize.Width;

    private static UIElement Lay(string layout, Telemetry t, OverlayWindow? w = null)
    {
        w ??= new OverlayWindow();
        w.SetGpuName("NVIDIA GeForce RTX 5060 Laptop GPU");
        w.ApplySettings(new Settings { Layout = layout });
        w.ShowStatus(new OverlayStatus(false, false, false));
        w.ShowFps(t);
        w.ShowStats(t);
        var root = (UIElement)w.Content;
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
        return root;
    }

    [Theory]
    [InlineData(Settings.Expanded)]
    [InlineData(Settings.Compact)]
    public void Readings_never_change_the_width(string layout) => Sta.Run(() =>
    {
        var wide = layout == Settings.Compact ? StripWide : Wide;
        var narrow = Width(layout, Narrow);
        Assert.Equal(narrow, Width(layout, wide));
        Assert.Equal(narrow, Width(layout, wide with { Limits = Limit.GpuPower })); // "POWER" rather than "HEAT"
        if (layout == Settings.Compact) // the Strip says "sleeping" in the GPU cell's own width
            Assert.Equal(narrow, Width(layout, wide with { Gpu = wide.Gpu with { Status = GpuStatus.Sleeping } }));
    });

    /// <summary>
    /// The Strip's cells: content starts at the cell's 10 DIP padding (any slack at its right end), and the gaps are
    /// the same in every cell: label to value 6, number to unit 3, between values 8 (° and % join the number).
    /// </summary>
    [Fact]
    public void Strip_cells_are_evenly_spaced() => Sta.Run(() =>
    {
        var w = new OverlayWindow();
        var t = Narrow with
        {
            Fps = new FpsSample(20, 49.7, 15, 60),
            Gpu = Narrow.Gpu with { TempC = 64, Load = 4 },
            CpuTempC = 94, CpuLoad = 16,
        };
        var root = Lay(Settings.Compact, t, w);
        var strip = (StackPanel)w.FindName("CompactLayout");

        Rect Box(TextElement e) => new(e.ContentStart.GetCharacterRect(LogicalDirection.Forward).TopLeft,
                                       e.ContentEnd.GetCharacterRect(LogicalDirection.Backward).BottomRight);
        Run R(string name) => (Run)w.FindName(name);
        // Each cell's text lines (by run name or literal text, in order)
        TextBlock Line(string name) => (TextBlock)w.FindName(name);
        IEnumerable<Inline> Runs(TextBlock b) => b.Inlines.Where(i => i is Run run && run.Text.Length > 0 && run.Text != "​");
        double Gap(Inline a, Inline b) => Box(b).Left - Box(a).Right;
        // Layout rounds to whole device pixels (0.8 DIP at 125% scaling)
        void Near(double expected, double actual) => Assert.InRange(actual, expected - 0.5, expected + 0.5);

        foreach (var cell in strip.Children.OfType<Border>().Where(c => c.Visibility == Visibility.Visible && c.Child is Grid))
        {
            Assert.Equal(new Thickness(10, 4, 10, 4), cell.Padding);
            var line = ((Grid)cell.Child).Children.OfType<TextBlock>().Single(b => b.Visibility == Visibility.Visible);
            Near(cell.BorderThickness.Left + 10, line.TranslatePoint(new Point(), cell).X);
            Near(0, Box(Runs(line).First()).Left);
        }
        // The divider comes before every cell but the first; the last element has no trailing margin of its own
        Assert.Equal(0, ((Border)strip.Children[0]).BorderThickness.Left);

        var fps = Runs(Line("CFpsText")).ToList();
        Near(3, Gap(fps[0], fps[1]));                        // 20 FPS
        var ms = Runs(Line("CFrameTimeText")).ToList();
        Near(3, Gap(ms[0], ms[1]));                          // 49.7 ms
        foreach (var p in new[] { "CGpu", "CCpu" })
        {
            var label = Runs(Line(p + "Text")).First();
            Near(6, Gap(label, R(p + "Temp")));              // GPU 64
            Assert.Equal("°", R(p + "Temp").Text[^1..]);              // 64° and 4%: the glyphs join the number
            Assert.Equal("%", R(p + "Load").Text[^1..]);
            Near(8, Gap(R(p + "Temp"), R(p + "Load")));       // 64° 4%
        }

        // One baseline: every cell's line has the same height and sits at the same height
        var lines = new[] { "CFpsText", "CFrameTimeText", "CGpuText", "CCpuText" }.Select(Line).ToList();
        foreach (var l in lines)
        {
            Near(lines[0].ActualHeight, l.ActualHeight);
            Near(lines[0].TranslatePoint(new Point(), root).Y, l.TranslatePoint(new Point(), root).Y);
        }
    });

    [Fact]
    public void Switching_a_row_off_may_change_the_width() => Sta.Run(() =>
    {
        double Strip(bool cpu)
        {
            var w = new OverlayWindow();
            w.ApplySettings(new Settings { Layout = Settings.Compact, ShowCpu = cpu });
            w.ShowStats(Wide);
            var root = (UIElement)w.Content;
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return root.DesiredSize.Width;
        }
        Assert.True(Strip(false) < Strip(true));
    });
}
