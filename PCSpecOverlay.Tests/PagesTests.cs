using PCSpecOverlay.Core;
using PCSpecOverlay.Optimizer;

namespace PCSpecOverlay.Tests;

public class TempScaleTests
{
    [Theory]
    [InlineData(30.0, 0.0)]
    [InlineData(65.0, 0.5)]
    [InlineData(100.0, 1.0)]
    [InlineData(10.0, 0.0)]
    [InlineData(120.0, 1.0)]
    public void Celsius_maps_onto_the_scale(double c, double fraction) =>
        Assert.Equal(fraction, TempScale.Fraction(c), 6);

    [Theory]
    [InlineData(0.0, 30)]
    [InlineData(0.5, 65)]
    [InlineData(1.0, 100)]
    [InlineData(-1.0, 30)]
    [InlineData(double.NaN, 30)]
    public void A_position_reads_back_in_whole_degrees(double fraction, int c) =>
        Assert.Equal(c, TempScale.ValueAt(fraction));

    [Theory]
    [InlineData(30)]
    [InlineData(71)]
    [InlineData(100)]
    public void Degrees_round_trip_through_the_scale(int c) =>
        Assert.Equal(c, TempScale.ValueAt(TempScale.Fraction(c)));

    [Fact]
    public void Warning_stays_below_hot()
    {
        Assert.Equal((84, 85), TempScale.MoveWarn(75, 85, 92));
        Assert.Equal((30, 85), TempScale.MoveWarn(75, 85, 5));
        Assert.Equal((75, 76), TempScale.MoveHot(75, 85, 60));
        Assert.Equal((75, 100), TempScale.MoveHot(75, 85, 130));
    }

    [Fact]
    public void Saved_levels_fit_the_scale()
    {
        var s = new Settings { GpuTempWarn = 105, GpuTempHot = 110, CpuTempWarn = 10, CpuTempHot = 10 };
        s.Validate();
        Assert.Equal((99, 100), (s.GpuTempWarn, s.GpuTempHot));
        Assert.Equal((30, 31), (s.CpuTempWarn, s.CpuTempHot));
    }
}

public class JournalRowTests
{
    [Fact]
    public void Rows_read_newest_first_in_words()
    {
        var now = new DateTime(2026, 10, 24, 20, 0, 0, DateTimeKind.Local);
        var entries = new[]
        {
            new JournalEntry
            {
                Id = "bg-recording", Original = "1", Applied = "0", Owners = new List<string> { "optimizer" },
                AppliedUtc = new DateTime(2026, 10, 23, 14, 2, 0, DateTimeKind.Local).ToUniversalTime(),
            },
            new JournalEntry
            {
                Id = "game-mode", Original = "0", Applied = "1", Owners = new List<string> { "gaming", "optimizer" },
                AppliedUtc = new DateTime(2026, 10, 24, 19, 38, 0, DateTimeKind.Local).ToUniversalTime(),
            },
        };

        var rows = JournalRows.Build(entries, id => SettingCatalog.Resolve(id)?.Name, SettingCatalog.DescribeValue, now);

        Assert.Equal(new JournalRow("game-mode", "Today 19:38", "Game Mode", "Off → On", "Gaming mode, Optimizer"), rows[0]);
        Assert.Equal(new JournalRow("bg-recording", "Oct 23 14:02", "Background recording", "On → Off", "Optimizer"), rows[1]);
    }

    [Fact]
    public void An_unknown_setting_falls_back_to_its_id_and_raw_values()
    {
        var row = JournalRows.Build(
            new[] { new JournalEntry { Id = "x", Original = "a", Applied = "b", Owners = new List<string> { "lab" } } },
            _ => null, SettingCatalog.DescribeValue, DateTime.Now)[0];
        Assert.Equal(("x", "a → b", "lab"), (row.Setting, row.Change, row.Owners));
    }
}

public class OverlayPreviewTests
{
    [Fact]
    public void The_strip_follows_the_metric_switches()
    {
        var s = new Settings { ShowFrameTime = false, ShowGpu = true, ShowCpu = false };
        Assert.Equal("144 FPS  GPU 71°", OverlayPreview.Strip(s, 144, 6.9, 71, 84));
        var all = new Settings { ShowFrameTime = true, ShowGpu = true, ShowCpu = true };
        Assert.Equal("— FPS  — ms  GPU —°  CPU —°", OverlayPreview.Strip(all, null, null, null, null));
    }

    [Fact]
    public void The_panel_lists_one_reading_per_line()
    {
        var s = new Settings { ShowFrameTime = true, ShowGpu = true, ShowCpu = true, ShowRam = false };
        Assert.Equal("OPTIFRAME\n144 FPS\n6.9 ms\nGPU  71°\nCPU  84°", OverlayPreview.Panel(s, 144, 6.9, 71, 84, 31.2));
    }
}

public class HealthTests
{
    [Fact]
    public void The_summary_counts_what_needs_a_look()
    {
        Assert.Equal("All 3 checks look fine.", Health.Summary(new[] { DiagLevel.Ok, DiagLevel.Info, DiagLevel.Ok }));
        Assert.Equal("7 of 8 checks look fine; 1 needs a look.",
            Health.Summary(new[] { DiagLevel.Ok, DiagLevel.Ok, DiagLevel.Ok, DiagLevel.Ok, DiagLevel.Ok, DiagLevel.Ok, DiagLevel.Info, DiagLevel.Warn }));
        Assert.Equal("0 of 2 checks look fine; 2 need a look.", Health.Summary(new[] { DiagLevel.Warn, DiagLevel.Error }));
    }

    [Fact]
    public void Each_level_has_a_word() =>
        Assert.Equal(new[] { "OK", "Note", "Check", "Problem" },
            new[] { DiagLevel.Ok, DiagLevel.Info, DiagLevel.Warn, DiagLevel.Error }.Select(Health.Word));
}

public class RecommendedValueTests : TempDataTest
{
    [Fact]
    public void A_tweak_shows_now_and_recommended_side_by_side()
    {
        var journal = new ChangeJournal(new AppState(), TimeProvider.System);
        var t = new Tweak(new FakeSetting(), "fast", "Speed", "Goes faster.", v => v == "fast" ? "Fast" : "Slow", journal);
        t.Refresh();
        Assert.Equal(("Slow", "Fast", ""), (t.Status, t.Recommended, t.Detail));
    }
}

public class PageStyleTests
{
    private static readonly string Theme = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/Theme.xaml"));

    [Fact]
    public void The_page_styles_exist_and_use_tokens_only()
    {
        foreach (var key in new[] { "PageTitle", "Key", "Mono", "LedgerRow", "Seg", "LayoutCard", "CornerTarget", "ScaleHandle" })
            Assert.Contains($"x:Key=\"{key}\"", Theme);
        var added = Theme[Theme.IndexOf("Page parts (stage 4)", StringComparison.Ordinal)..];
        Assert.DoesNotMatch("#[0-9A-Fa-f]{6}", added);
    }

    [Fact]
    public void The_scale_control_drags_and_steps_through_the_tested_math()
    {
        var code = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/Controls/TempScaleControl.cs"));
        foreach (var call in new[] { "TempScale.MoveWarn(", "TempScale.MoveHot(", "TempScale.ValueAt(", "TempScale.Fraction(", "Key.PageUp", "AutomationProperties.SetName(" })
            Assert.Contains(call, code);
    }
}

public class OptimizerPageTests
{
    private static string Page => PageXaml.Tab("Optimizer");

    [Fact]
    public void The_optimizer_is_one_ledger_with_now_and_recommended_columns()
    {
        foreach (var part in new[] { "Text=\"SETTING\"", "Text=\"NOW\"", "Text=\"RECOMMENDED\"", "Text=\"EFFECT AND DRAWBACK\"",
                     "{Binding Recommended}", "x:Name=\"Edge\"", "x:Name=\"Mark\"", "x:Name=\"FixAllButton\"", "x:Name=\"AppList\"",
                     "StaticResource LedgerRow", "StaticResource PageTitle" })
            Assert.Contains(part, Page);
        foreach (var gone in new[] { "CardBox", "\"H1\"", "\"Lead\"", "AccentSoft", "Ellipse" })
            Assert.DoesNotContain(gone, Page);
    }
}

/// <summary>Reads one page (TabItem) out of the control center's XAML.</summary>
internal static class PageXaml
{
    public static string Tab(string header)
    {
        var xaml = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/ControlCenterWindow.xaml"));
        var start = xaml.IndexOf($"<TabItem Header=\"{header}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No {header} page");
        var end = xaml.IndexOf("</TabItem>", start, StringComparison.Ordinal);
        return xaml[start..end];
    }
}

public class GamingPageTests
{
    [Fact]
    public void Gaming_shows_the_four_steps_and_a_status_bar()
    {
        var page = PageXaml.Tab("Gaming");
        foreach (var part in new[] { "Text=\"01\"", "Text=\"Detect\"", "Text=\"02\"", "Text=\"Power\"", "Text=\"03\"", "Text=\"Boost\"",
                     "Text=\"04\"", "Text=\"Restore\"", "x:Name=\"GamingBar\"", "x:Name=\"GamingBarText\"", "x:Name=\"GamingPause\"",
                     "x:Name=\"GamingResume\"", "x:Name=\"RuleList\"", "x:Name=\"RecentList\"", "x:Name=\"EnterSlider\"", "x:Name=\"ExitSlider\"",
                     "{Binding AutoGamingMode}", "{Binding GamingPowerMode}", "{Binding GamingProcessPriority}", "{Binding GameBoost}",
                     "{Binding DetectFullscreenGames}", "{Binding OptimizeFullscreenGames}" })
            Assert.Contains(part, page);
        foreach (var gone in new[] { "CardBox", "GamingStatusLine", "\"H1\"" })
            Assert.DoesNotContain(gone, page);
    }
}

public class OverlayPageTests
{
    [Fact]
    public void The_overlay_page_leads_with_a_live_preview()
    {
        var page = PageXaml.Tab("Overlay");
        foreach (var part in new[] { "x:Name=\"PreviewScreen\"", "x:Name=\"PreviewOverlay\"", "x:Name=\"PreviewText\"", "x:Name=\"PreviewCaption\"",
                     "x:Name=\"CornerTopLeft\"", "x:Name=\"CornerTopRight\"", "x:Name=\"CornerBottomLeft\"", "x:Name=\"CornerBottomRight\"",
                     "Click=\"OnCornerTarget\"", "x:Name=\"StripCard\"", "x:Name=\"PanelCard\"", "Click=\"OnLayoutCard\"", "Click=\"OnMoveOverlay\"",
                     "x:Name=\"DisplayBox\"", "x:Name=\"ShowOverlay\"", "{Binding OverlayTheme}", "{Binding GpuRendering}" })
            Assert.Contains(part, page);
        foreach (var gone in new[] { "PositionBox", "CardBox", "\"H1\"" })
            Assert.DoesNotContain(gone, page);
        var code = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/ControlCenterWindow.xaml.cs"));
        Assert.Contains("OverlayPreview.Strip(", code);
        Assert.Contains("OverlayPlacement.Place(", code);
    }
}

public class AlertsPageTests
{
    [Fact]
    public void Alerts_has_a_scale_per_sensor_and_ledger_notifications()
    {
        var page = PageXaml.Tab("Alerts");
        foreach (var part in new[] { "controls:TempScaleControl x:Name=\"GpuScale\"", "controls:TempScaleControl x:Name=\"CpuScale\"",
                     "x:Name=\"GpuNow\"", "x:Name=\"CpuNow\"", "x:Name=\"GpuSource\"", "x:Name=\"CpuSource\"",
                     "{Binding Alerts}", "x:Name=\"AfterSlider\"", "x:Name=\"CooldownSlider\"" })
            Assert.Contains(part, page);
        foreach (var gone in new[] { "x:Name=\"GpuWarn\"", "x:Name=\"GpuHot\"", "x:Name=\"CpuWarn\"", "x:Name=\"CpuHot\"", "CardBox" })
            Assert.DoesNotContain(gone, page);
        var code = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/ControlCenterWindow.xaml.cs"));
        Assert.Contains("GpuScale.LevelsChanged +=", code);
        Assert.Contains("CpuScale.SetCurrent(", code);
    }
}
