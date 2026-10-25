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
