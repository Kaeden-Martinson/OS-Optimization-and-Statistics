using System.Text.Json;
using PCSpecOverlay.Core;

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

public class ThemeResolverTests : TempDataTest // logs a warning; keep it out of the real log
{
    [Theory]
    [InlineData("Light", 0, AppTheme.Light)]
    [InlineData("Dark", 1, AppTheme.Dark)]
    [InlineData("MatchWindows", 1, AppTheme.Light)]
    [InlineData("MatchWindows", 0, AppTheme.Dark)]
    public void Resolves_the_setting(string appearance, int windows, AppTheme expected) =>
        Assert.Equal(expected, ThemeResolver.Resolve(appearance, () => windows));

    [Fact]
    public void Missing_windows_value_means_dark() =>
        Assert.Equal(AppTheme.Dark, ThemeResolver.Resolve(Settings.MatchWindows, () => null));

    [Fact]
    public void Wrong_value_type_means_dark() =>
        Assert.Equal(AppTheme.Dark, ThemeResolver.Resolve(Settings.MatchWindows, () => "1"));

    [Fact]
    public void Unreadable_windows_value_means_dark() =>
        Assert.Equal(AppTheme.Dark, ThemeResolver.Resolve(Settings.MatchWindows, () => throw new UnauthorizedAccessException()));

    [Fact]
    public void Explicit_choice_never_reads_windows()
    {
        var reads = 0;
        ThemeResolver.Resolve(Settings.Light, () => { reads++; return 0; });
        Assert.Equal(0, reads);
    }
}
