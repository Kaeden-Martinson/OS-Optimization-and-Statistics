using System.Drawing;
using System.Text.Json;
using PCSpecOverlay.Core;

namespace PCSpecOverlay.Tests;

public class OverlayPlacementTests
{
    private static readonly (int, int, int, int) Screen = (0, 0, 1920, 1040);

    [Theory]
    [InlineData("TopRight", 1602, 18)]
    [InlineData("TopLeft", 18, 18)]
    [InlineData("BottomRight", 1602, 982)]
    [InlineData("BottomLeft", 18, 982)]
    public void Corners_sit_a_margin_from_the_edges(string corner, int x, int y) =>
        Assert.Equal((x, y), OverlayPlacement.Place(corner, 0, 0, Screen, 300, 40, 18));

    [Fact]
    public void A_pinned_spot_is_a_share_of_the_free_space() =>
        Assert.Equal((810, 500), OverlayPlacement.Place(Settings.Custom, 0.5, 0.5, Screen, 300, 40, 18));

    [Fact]
    public void A_pinned_spot_never_leaves_the_screen() =>
        Assert.Equal((1620, 0), OverlayPlacement.Place(Settings.Custom, 2, -1, Screen, 300, 40, 18));

    [Fact]
    public void A_pinned_spot_follows_a_second_display() =>
        Assert.Equal((1920, 0), OverlayPlacement.Place(Settings.Custom, 0, 0, (1920, 0, 3840, 1080), 300, 40, 18));

    [Fact]
    public void A_panel_wider_than_the_screen_starts_at_its_edge() =>
        Assert.Equal((0, 0), OverlayPlacement.Place(Settings.Custom, 1, 0, (0, 0, 200, 1040), 300, 40, 18));

    [Fact]
    public void A_dropped_spot_becomes_fractions() =>
        Assert.Equal((0.5, 0.5), OverlayPlacement.ToFractions(810, 500, Screen, 300, 40));

    [Fact]
    public void Dropping_past_an_edge_clamps_the_fractions()
    {
        Assert.Equal((0.0, 0.0), OverlayPlacement.ToFractions(-50, -20, Screen, 300, 40));
        Assert.Equal((1.0, 1.0), OverlayPlacement.ToFractions(5000, 5000, Screen, 300, 40));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(437, 222)]
    [InlineData(1620, 1000)]
    public void A_spot_round_trips_through_its_fractions(int left, int top)
    {
        var (fx, fy) = OverlayPlacement.ToFractions(left, top, Screen, 300, 40);
        Assert.Equal((left, top), OverlayPlacement.Place(Settings.Custom, fx, fy, Screen, 300, 40, 18));
    }

    [Fact]
    public void The_move_handle_sits_above_the_panel_when_there_is_room() =>
        Assert.Equal((800, 474, false, false), OverlayPlacement.MoveWindow(800, 500, Screen, 300, 300, 26));

    [Theory]
    [InlineData(18)] // a top corner: the 14 DIP margin is less than the handle
    [InlineData(0)]  // a pinned spot at the very top
    public void The_move_handle_goes_below_the_panel_near_the_top(int top) =>
        Assert.Equal((1602, top, true, false), OverlayPlacement.MoveWindow(1602, top, Screen, 300, 300, 26));

    [Fact]
    public void The_move_handle_measures_against_the_work_area_top() =>
        Assert.True(OverlayPlacement.MoveWindow(2000, 60, (1920, 40, 3840, 1080), 300, 300, 26).HandleBelow);

    [Fact]
    public void A_handle_wider_than_the_panel_extends_away_from_the_right_edge()
    {
        Assert.Equal((100, 474, false, false), OverlayPlacement.MoveWindow(100, 500, Screen, 90, 130, 26));
        Assert.Equal((1782, 474, false, true), OverlayPlacement.MoveWindow(1822, 500, Screen, 90, 130, 26));
    }
}

public class PlacementSettingTests
{
    [Fact]
    public void Defaults_put_a_pinned_spot_at_the_top_right()
    {
        var s = new Settings();
        Assert.Equal(1.0, s.CustomX);
        Assert.Equal(0.0, s.CustomY);
    }

    [Fact]
    public void Custom_is_a_valid_position()
    {
        var s = new Settings { Position = Settings.Custom };
        s.Validate();
        Assert.Equal(Settings.Custom, s.Position);
    }

    [Fact]
    public void Bad_fractions_are_repaired()
    {
        var s = new Settings { CustomX = double.NaN, CustomY = 5 };
        s.Validate();
        Assert.Equal(1.0, s.CustomX);
        Assert.Equal(1.0, s.CustomY);
    }

    [Fact]
    public void A_settings_file_without_the_fractions_loads_with_defaults()
    {
        var s = JsonSerializer.Deserialize<Settings>("""{"Position":"Custom"}""")!;
        s.Validate();
        Assert.Equal((Settings.Custom, 1.0, 0.0), (s.Position, s.CustomX, s.CustomY));
    }
}

public class LogoTests
{
    private static readonly Color Light = Color.FromArgb(0xE9, 0xE9, 0xE6);
    private static readonly Color Tile = Color.FromArgb(0x0B, 0x0B, 0x0C);

    [Fact]
    public void The_mark_draws_its_corner_brackets_and_leaves_the_rest_clear()
    {
        using var bmp = LogoPainter.Draw(48, Light);
        Assert.True(bmp.GetPixel(6, 6).A > 200, "top-left bracket corner (3,3 at 2× scale)");
        Assert.True(bmp.GetPixel(42, 42).A > 200, "bottom-right bracket corner (21,21)");
        Assert.Equal(0, bmp.GetPixel(38, 12).A); // (19,6): inside the frame, away from the trace
        Assert.Equal(0, bmp.GetPixel(0, 0).A);
    }

    [Fact]
    public void The_app_icon_sits_on_its_tile()
    {
        using var bmp = LogoPainter.Draw(64, Light, Tile);
        Assert.Equal(Tile.ToArgb(), bmp.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void The_committed_icon_has_every_size_and_matches_the_mark()
    {
        using var fresh = new MemoryStream();
        LogoPainter.WriteIco(fresh);
        var path = SourceFiles.Path("PCSpecOverlay/Optiframe.ico");
        if (Environment.GetEnvironmentVariable("OPTIFRAME_WRITE_ICON") == "1") File.WriteAllBytes(path, fresh.ToArray());

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(LogoPainter.IconSizes.Length, BitConverter.ToInt16(bytes, 4));
        var sizes = Enumerable.Range(0, LogoPainter.IconSizes.Length).Select(i => bytes[6 + 16 * i] == 0 ? 256 : bytes[6 + 16 * i]);
        Assert.Equal(LogoPainter.IconSizes, sizes);
        Assert.True(bytes.SequenceEqual(fresh.ToArray()),
            "Optiframe.ico differs from LogoPainter's output; regenerate it with OPTIFRAME_WRITE_ICON=1 dotnet test --filter LogoTests");
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void Reads_the_taskbar_theme(int value, bool light) =>
        Assert.Equal(light, ThemeResolver.TaskbarIsLight(() => value));

    [Fact]
    public void An_unreadable_taskbar_theme_counts_as_dark() =>
        Assert.False(ThemeResolver.TaskbarIsLight(() => null));
}

public class LogoUseTests
{
    [Fact]
    public void The_app_and_its_windows_use_the_icon_and_the_new_mark()
    {
        Assert.Contains("<ApplicationIcon>Optiframe.ico</ApplicationIcon>", File.ReadAllText(SourceFiles.Path("PCSpecOverlay/PCSpecOverlay.csproj")));
        Assert.Contains("Icon=\"Optiframe.ico\"", File.ReadAllText(SourceFiles.Path("PCSpecOverlay/ControlCenterWindow.xaml")));
        var installer = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/InstallerWindow.xaml"));
        Assert.Contains("Icon=\"Optiframe.ico\"", installer);
        Assert.Contains("Icon.Mark", installer);
        Assert.Contains("M3,8 V3 H8 M16,3 H21 V8 M21,16 V21 H16 M8,21 H3 V16 M6,14 H9.5 L11.5,8.5 L13.5,15.5 L15,12 H18",
            File.ReadAllText(SourceFiles.Path("PCSpecOverlay/Themes/Icons.xaml")));
        Assert.Contains("LogoPainter.Draw(", File.ReadAllText(SourceFiles.Path("PCSpecOverlay/TrayIcon.cs")));
    }
}

public class NameTests
{
    private static IEnumerable<string> AppSources() =>
        Directory.EnumerateFiles(SourceFiles.Path("PCSpecOverlay"), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml") || f.EndsWith(".csproj"))
                        && !f.Contains(@"\obj\") && !f.Contains(@"\bin\"));

    [Fact]
    public void The_old_name_only_remains_as_the_legacy_constant()
    {
        var hits = AppSources().Concat([SourceFiles.Path("build-release.ps1")])
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
            .Where(x => x.line.Contains("PC Spec Overlay") || x.line.Contains("PC SPEC") || x.line.Contains("PCSpecOverlay-Setup"))
            .Where(x => !x.line.Contains("LegacyProductName"))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void Visible_names_say_Optiframe()
    {
        Assert.Equal("Optiframe", AppIdentity.ProductName);
        Assert.Contains("Title=\"Optiframe\"", File.ReadAllText(SourceFiles.Path("PCSpecOverlay/ControlCenterWindow.xaml")));
        Assert.Contains("Title=\"Optiframe Setup\"", File.ReadAllText(SourceFiles.Path("PCSpecOverlay/InstallerWindow.xaml")));
        Assert.Contains("Text=\"OPTIFRAME\"", File.ReadAllText(SourceFiles.Path("PCSpecOverlay/OverlayWindow.xaml")));
        var csproj = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/PCSpecOverlay.csproj"));
        Assert.Contains("<Product>Optiframe</Product>", csproj);
        Assert.Contains("<AssemblyTitle>Optiframe</AssemblyTitle>", csproj);
        Assert.Contains("Optiframe-Setup-", File.ReadAllText(SourceFiles.Path("build-release.ps1")));
        Assert.Contains("DeleteLegacyShortcut()", File.ReadAllText(SourceFiles.Path("PCSpecOverlay/Installer.cs")));
    }
}

public class MovePinTests
{
    [Fact]
    public void Click_through_is_no_longer_a_setting()
    {
        Assert.Null(typeof(Settings).GetProperty("ClickThrough"));
        var s = JsonSerializer.Deserialize<Settings>("""{"ClickThrough":false,"Position":"TopLeft"}""")!;
        s.Validate();
        Assert.Equal("TopLeft", s.Position); // an older file still loads
    }

    [Fact]
    public void The_overlay_has_a_move_handle_with_a_pin()
    {
        var xaml = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/OverlayWindow.xaml"));
        foreach (var name in new[] { "MoveHandle", "PinButton", "MoveOutline" }) Assert.Contains($"x:Name=\"{name}\"", xaml);
        Assert.Contains("Click=\"OnPin\"", xaml);
        var code = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/OverlayWindow.xaml.cs"));
        Assert.Contains("OverlayPlacement.Place(", code);
        Assert.Contains("OverlayPlacement.ToFractions(", code);
        Assert.DoesNotContain("ClickThrough", code);
    }

    [Fact]
    public void Move_overlay_is_offered_in_the_tray_and_the_control_center()
    {
        Assert.Contains("\"Move overlay\"", File.ReadAllText(SourceFiles.Path("PCSpecOverlay/TrayIcon.cs")));
        var xaml = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/ControlCenterWindow.xaml"));
        Assert.Contains("Click=\"OnMoveOverlay\"", xaml);
        Assert.Contains("Click=\"OnCornerTarget\"", xaml);
        Assert.DoesNotContain("ClickThrough", xaml);
    }
}
