using System.Windows;
using System.Windows.Media;
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

public class ColorDictionaryTests
{
    private static readonly string[] Keys =
    [
        "Bg", "Panel", "Line", "Line2", "Text", "TextHover", "Muted", "Faint", "Signal", "Hot", "Good", "Selection",
        "Window", "Sidebar", "Card", "CardBorder", "Input", "InputBorder", "Accent", "AccentHover", "AccentSoft", "Warn", "Info",
    ];

    private static Color C(ResourceDictionary d, string key) => ((SolidColorBrush)d[key]).Color;

    [Theory]
    [InlineData("PCSpecOverlay/Themes/Colors.Dark.xaml")]
    [InlineData("PCSpecOverlay/Themes/Colors.Light.xaml")]
    public void Every_key_is_a_brush(string file) => Sta.Run(() =>
    {
        var d = XamlFile.LoadDictionary(file);
        foreach (var key in Keys) Assert.IsType<SolidColorBrush>(d[key]);
    });

    [Theory]
    [InlineData("PCSpecOverlay/Themes/Colors.Dark.xaml", "#0B0B0C", "#E9E9E6")]
    [InlineData("PCSpecOverlay/Themes/Colors.Light.xaml", "#F3F2EE", "#141414")]
    public void Uses_the_spec_values(string file, string bg, string text) => Sta.Run(() =>
    {
        var d = XamlFile.LoadDictionary(file);
        Assert.Equal((Color)ColorConverter.ConvertFromString(bg), C(d, "Bg"));
        Assert.Equal((Color)ColorConverter.ConvertFromString(text), C(d, "Text"));
    });

    [Theory]
    [InlineData("PCSpecOverlay/Themes/Colors.Dark.xaml")]
    [InlineData("PCSpecOverlay/Themes/Colors.Light.xaml")]
    public void Readable_text_meets_four_and_a_half_to_one(string file) => Sta.Run(() =>
    {
        var d = XamlFile.LoadDictionary(file);
        foreach (var fg in new[] { "Text", "Muted", "Signal", "Hot", "Good" })
            foreach (var bg in new[] { "Bg", "Panel" })
                Assert.True(Contrast.Ratio(C(d, fg), C(d, bg)) >= 4.5, $"{fg} on {bg} is {Contrast.Ratio(C(d, fg), C(d, bg)):0.00}:1 in {file}");
    });

    [Theory]
    [InlineData("PCSpecOverlay/Themes/Colors.Dark.xaml")]
    [InlineData("PCSpecOverlay/Themes/Colors.Light.xaml")]
    public void Old_names_alias_the_new_tokens(string file) => Sta.Run(() =>
    {
        var d = XamlFile.LoadDictionary(file);
        Assert.Equal(C(d, "Bg"), C(d, "Window"));
        Assert.Equal(C(d, "Panel"), C(d, "Card"));
        Assert.Equal(C(d, "Line"), C(d, "CardBorder"));
        Assert.Equal(C(d, "Text"), C(d, "Accent"));
        Assert.Equal(C(d, "Signal"), C(d, "Warn"));
        Assert.Equal(C(d, "Muted"), C(d, "Info"));
    });
}

public class IconTests
{
    private static readonly string[] Names =
    [
        "Mark", "Overview", "Optimizer", "Gaming", "Overlay", "Temperature", "Settings", "Diagnostics", "Sun", "Moon",
        "Monitor", "Check", "Warning", "Info", "Undo", "Pause", "Plus", "Close", "Chevron", "ChevronDown", "Refresh",
        "Copy", "File", "Folder", "Pointer", "Power", "Grip", "Window",
    ];

    [Fact]
    public void Every_icon_is_a_geometry_inside_the_24_unit_grid() => Sta.Run(() =>
    {
        var d = XamlFile.LoadDictionary("PCSpecOverlay/Themes/Icons.xaml");
        foreach (var name in Names)
        {
            var g = Assert.IsAssignableFrom<Geometry>(d["Icon." + name]);
            var b = g.Bounds;
            Assert.False(b.IsEmpty, name);
            Assert.True(b.Left >= 0 && b.Top >= 0 && b.Right <= 24 && b.Bottom <= 24, $"{name} bounds {b}");
        }
        Assert.Equal(Names.Length, d.Keys.Count);
    });
}

public class ThemeManagerTests : TempDataTest // logs theme changes; keep them out of the real log
{
    /// <summary>A stand-in color dictionary that records which theme it is.</summary>
    private static ResourceDictionary Palette(AppTheme t) =>
        new() { ["Bg"] = new SolidColorBrush(t == AppTheme.Dark ? Colors.Black : Colors.White), ["Theme"] = t };

    private static (ResourceDictionary target, ThemeManager manager, List<AppTheme> events) Make(Func<object?>? windows = null, Func<AppTheme, ResourceDictionary>? load = null)
    {
        var target = new ResourceDictionary();
        target.MergedDictionaries.Add(new ResourceDictionary()); // stands in for Icons.xaml / Theme.xaml
        var m = new ThemeManager(target, load ?? Palette, windows ?? (() => 0), listenToWindows: false);
        var events = new List<AppTheme>();
        m.Changed += events.Add;
        return (target, m, events);
    }

    [Fact]
    public void Apply_light_swaps_in_the_light_colors() => Sta.Run(() =>
    {
        var (target, m, events) = Make();
        m.Apply(Settings.Light);
        Assert.Equal(AppTheme.Light, m.Current);
        Assert.Equal(AppTheme.Light, target["Theme"]);
        Assert.Equal([AppTheme.Light], events);
        Assert.Equal(2, target.MergedDictionaries.Count); // replaced, not stacked
    });

    [Fact]
    public void Applying_the_same_theme_again_does_nothing() => Sta.Run(() =>
    {
        var (_, m, events) = Make();
        m.Apply(Settings.Light);
        m.Apply(Settings.Light);
        Assert.Single(events);
    });

    [Fact]
    public void Match_windows_follows_a_windows_change() => Sta.Run(() =>
    {
        var windows = 0;
        var (target, m, events) = Make(() => windows);
        m.Apply(Settings.MatchWindows);
        Assert.Equal(AppTheme.Dark, m.Current);
        windows = 1;
        m.OnWindowsThemeChanged();
        Assert.Equal(AppTheme.Light, target["Theme"]);
    });

    [Fact]
    public void A_windows_change_is_ignored_when_the_choice_is_explicit() => Sta.Run(() =>
    {
        var windows = 0;
        var (_, m, _) = Make(() => windows);
        m.Apply(Settings.Dark);
        windows = 1;
        m.OnWindowsThemeChanged();
        Assert.Equal(AppTheme.Dark, m.Current);
    });

    [Fact]
    public void A_theme_that_fails_to_load_falls_back_to_dark() => Sta.Run(() =>
    {
        var (target, m, _) = Make(load: t => t == AppTheme.Light ? throw new IOException("missing") : Palette(t));
        m.Apply(Settings.Light);
        Assert.Equal(AppTheme.Dark, m.Current);
        Assert.Equal(AppTheme.Dark, target["Theme"]);
    });
}

public class LiveColorTests
{
    private static readonly string[] ColorKeys =
    [
        "Bg", "Panel", "Line", "Line2", "Text", "TextHover", "Muted", "Faint", "Signal", "Hot", "Good", "Selection",
        "Window", "Sidebar", "Card", "CardBorder", "Input", "InputBorder", "Accent", "AccentHover", "AccentSoft", "Warn", "Info",
    ];

    [Theory]
    [InlineData("PCSpecOverlay/Theme.xaml")]
    [InlineData("PCSpecOverlay/ControlCenterWindow.xaml")]
    [InlineData("PCSpecOverlay/InstallerWindow.xaml")]
    public void Views_use_dynamic_color_references(string file)
    {
        var xaml = File.ReadAllText(SourceFiles.Path(file));
        foreach (var key in ColorKeys)
            Assert.DoesNotContain("{StaticResource " + key + "}", xaml);
        Assert.DoesNotMatch(@"(Color|Fill|Stroke|Background|Foreground|BorderBrush|CaretBrush|SelectionBrush)=""#[0-9A-Fa-f]{6,8}""", xaml);
    }
}
