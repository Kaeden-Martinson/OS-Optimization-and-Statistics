using System.Text.Json;

namespace PCSpecOverlay.Tests;

public class AppearanceSettingTests
{
    [Fact]
    public void Defaults_to_match_windows() => Assert.Equal(Settings.MatchWindows, new Settings().Appearance);

    [Theory]
    [InlineData("Light", "Light")]
    [InlineData("Dark", "Dark")]
    [InlineData("MatchWindows", "MatchWindows")]
    [InlineData("light", "MatchWindows")]
    [InlineData("Purple", "MatchWindows")]
    [InlineData("", "MatchWindows")]
    public void Validate_keeps_known_values_and_replaces_others(string stored, string expected)
    {
        var s = new Settings { Appearance = stored };
        s.Validate();
        Assert.Equal(expected, s.Appearance);
    }

    [Fact]
    public void A_settings_file_from_before_this_setting_loads_with_the_default()
    {
        var s = JsonSerializer.Deserialize<Settings>("""{"Version":2,"Layout":"Compact"}""")!;
        s.Validate();
        Assert.Equal(Settings.MatchWindows, s.Appearance);
        Assert.Equal(Settings.Compact, s.Layout);
    }

    [Fact]
    public void Null_appearance_is_repaired()
    {
        var s = JsonSerializer.Deserialize<Settings>("""{"Appearance":null}""")!;
        s.Validate();
        Assert.Equal(Settings.MatchWindows, s.Appearance);
    }
}
