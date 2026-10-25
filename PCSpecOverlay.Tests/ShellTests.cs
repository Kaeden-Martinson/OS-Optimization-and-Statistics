using System.Text.RegularExpressions;
using PCSpecOverlay.Core;

namespace PCSpecOverlay.Tests;

public class OverviewDataTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData(0.0, 0)]
    [InlineData(0.04, 0)]
    [InlineData(0.05, 1)]
    [InlineData(0.64, 6)]
    [InlineData(1.0, 10)]
    [InlineData(1.7, 10)]
    [InlineData(-1.0, 0)]
    [InlineData(double.NaN, 0)]
    public void Fills_ticks_in_tenths(double? fraction, int expected) =>
        Assert.Equal(expected, OverviewData.FilledTicks(fraction));

    [Fact]
    public void A_deep_dip_is_marked()
    {
        double?[] fps = [100, 100, 100, 50, 100, 100];
        Assert.Equal(new[] { 3 }, OverviewData.Dips(fps));
    }

    [Fact]
    public void A_shallow_dip_is_not_marked()
    {
        double?[] fps = [100, 100, 100, 80, 100, 100];
        Assert.Empty(OverviewData.Dips(fps));
    }

    [Fact]
    public void A_flat_low_stretch_is_marked_once()
    {
        double?[] fps = [100, 100, 40, 40, 100, 100];
        Assert.Equal(new[] { 2 }, OverviewData.Dips(fps));
    }

    [Fact]
    public void A_dip_at_the_newest_point_is_marked()
    {
        double?[] fps = [100, 100, 100, 40];
        Assert.Equal(new[] { 3 }, OverviewData.Dips(fps));
    }

    [Fact]
    public void Gaps_are_skipped_and_too_few_values_mark_nothing()
    {
        double?[] withGaps = [100, null, 100, 30, null, 100];
        Assert.Equal(new[] { 3 }, OverviewData.Dips(withGaps));
        double?[] tooFew = [100, 30];
        Assert.Empty(OverviewData.Dips(tooFew));
    }

    private static LogEntry E(int second, LogLevel level, string area, string message) =>
        new(new DateTime(2026, 10, 24, 20, 0, second), level, area, message);

    [Fact]
    public void Events_keep_actions_and_problems_newest_first()
    {
        var log = new List<LogEntry>
        {
            E(1, LogLevel.Info, "Startup", "GPU monitor ready"),
            E(2, LogLevel.Info, "Gaming", "Gaming mode on for Starve.io"),
            E(3, LogLevel.Info, "FPS", "Frame monitoring started"),
            E(4, LogLevel.Warning, "Hotkey", "Alt+Shift+O is already used"),
            E(5, LogLevel.Info, "GameBoost", "Raised msedge.exe"),
            E(6, LogLevel.Error, "GPU", "Driver not responding"),
        };
        var events = OverviewData.Events(log, 10);
        Assert.Equal(new[] { "Driver not responding", "Raised msedge.exe", "Alt+Shift+O is already used", "Gaming mode on for Starve.io" },
            events.Select(e => e.Message));
    }

    [Fact]
    public void Events_are_capped()
    {
        var log = Enumerable.Range(0, 20).Select(i => E(i, LogLevel.Info, "Changes", $"change {i}")).ToList();
        var events = OverviewData.Events(log, 8);
        Assert.Equal(8, events.Count);
        Assert.Equal("change 19", events[0].Message);
    }
}

public class ShellSourceTests
{
    private static string Xaml => File.ReadAllText(SourceFiles.Path("PCSpecOverlay/ControlCenterWindow.xaml"));

    [Fact]
    public void Seven_top_tabs_with_icons_in_order()
    {
        var tabs = Regex.Matches(Xaml, "<TabItem Header=\"([^\"]+)\" Tag=\"\\{StaticResource Icon\\.(\\w+)\\}\" Style=\"\\{StaticResource TopTab\\}\"")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();
        Assert.Equal(new[]
        {
            ("Overview", "Overview"), ("Optimizer", "Optimizer"), ("Gaming", "Gaming"), ("Overlay", "Overlay"),
            ("Alerts", "Temperature"), ("Settings", "Settings"), ("Diagnostics", "Diagnostics"),
        }, tabs);
        Assert.Equal(7, Regex.Matches(Xaml, "<TabItem ").Count);
    }

    [Fact]
    public void Sidebar_is_gone_and_the_switch_is_named()
    {
        Assert.DoesNotContain("NavItem", Xaml);
        Assert.DoesNotContain("NavItem", File.ReadAllText(SourceFiles.Path("PCSpecOverlay/Theme.xaml")));
        Assert.Contains("AutomationProperties.Name=\"Light theme\"", Xaml);
        Assert.Contains("AutomationProperties.Name=\"Dark theme\"", Xaml);
        Assert.Contains("MinWidth=\"900\"", Xaml);
    }
}
