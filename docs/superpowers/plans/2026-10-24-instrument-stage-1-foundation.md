# Instrument Redesign, Stage 1 (Foundation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the NVIDIA-green palette with the Instrument color tokens, make light and dark switchable while the app runs (Light / Dark / Match Windows), make the title bar follow the theme, and add the vector icon set, all on the current page layouts.

**Architecture:** Two color dictionaries (`Themes/Colors.Dark.xaml`, `Themes/Colors.Light.xaml`) with identical keys. Every view references brushes with `DynamicResource`, so a `ThemeManager` can swap the merged color dictionary at runtime. A pure `ThemeResolver` maps the `Appearance` setting (and the Windows registry value for Match Windows) to a theme. The old key names (`Accent`, `Card`, …) stay as aliases inside the color dictionaries so current layouts recolor without rewriting each page; later stages replace them.

**Tech Stack:** .NET 8 WPF (C# 12), xUnit 2.9, Win32 DWM (`dwmapi.dll`), `Microsoft.Win32.Registry` / `SystemEvents`.

**Spec:** `docs/superpowers/specs/2026-10-24-instrument-redesign-design.md` (sections 1, 2 and Build order stage 1).

## Global Constraints

- Every number in this plan's colors comes from the spec's token table, except light `Signal` = `#955C00` and light `Good` = `#47700F` (darkened per the spec's footnote; measured 4.92:1 and 5.22:1 on `Bg`).
- Readable text pairs must reach 4.5:1 contrast on `Bg` and `Panel`; `Faint` is for dividers and axis ticks only.
- No emojis anywhere; icons are vector geometry only (24-unit grid, 1.6 stroke, round caps and joins).
- Square corners, 1 px lines, no drop shadows.
- `Appearance` values: `Light`, `Dark`, `MatchWindows`; default `MatchWindows`; unreadable Windows setting → dark.
- The in-game overlay stays dark in this stage regardless of the app theme (its own theme setting arrives in stage 2).
- Existing behavior, settings files, shortcuts and safety rules are unchanged; the existing 99 tests must keep passing.
- Build: `dotnet build PCSpec.slnx`. Tests: `dotnet test PCSpec.slnx`. Shell examples use Git Bash from the repo root `C:\Users\kaede\source\PCSpec`.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## File map

| File | Status | Responsibility |
|---|---|---|
| `PCSpecOverlay/Settings.cs` | modify | `Appearance` setting, constants, validation |
| `PCSpecOverlay/Core/ThemeResolver.cs` | create | `AppTheme` enum; setting + Windows value → theme; registry read |
| `PCSpecOverlay/Core/ThemeManager.cs` | create | Swaps the color dictionary, follows Windows changes, raises `Changed` |
| `PCSpecOverlay/Core/TitleBar.cs` | create (replaces `DarkTitleBar.cs`) | Light or dark DWM title bar |
| `PCSpecOverlay/Core/Icon.cs` | create | Tiny control that draws one icon geometry |
| `PCSpecOverlay/Themes/Colors.Dark.xaml` | create | Dark tokens + legacy aliases |
| `PCSpecOverlay/Themes/Colors.Light.xaml` | create | Light tokens + legacy aliases |
| `PCSpecOverlay/Themes/Icons.xaml` | create | Icon geometries |
| `PCSpecOverlay/Theme.xaml` | rewrite | Styles using tokens via `DynamicResource`, square shapes, `Icon` style |
| `PCSpecOverlay/App.xaml`, `App.xaml.cs` | modify | Merge dictionaries; create and drive `ThemeManager` |
| `PCSpecOverlay/ControlCenterWindow.xaml(.cs)` | modify | `DynamicResource`; Appearance row; code colors from tokens; follow theme |
| `PCSpecOverlay/InstallerWindow.xaml(.cs)` | modify | `DynamicResource`; `TitleBar` |
| `PCSpecOverlay/OverlayWindow.xaml` | modify | Local dark `Accent` so the overlay stays dark |
| `PCSpecOverlay.Tests/TestSupport.cs` | modify | `Sta.Run`, `SourceFiles.Path`, `Contrast` helpers |
| `PCSpecOverlay.Tests/ThemeTests.cs` | create | All tests for this stage |

---

### Task 1: Appearance setting

**Files:**
- Modify: `PCSpecOverlay/Settings.cs:37` (constants), `:62` area (property), `Validate()`
- Test: `PCSpecOverlay.Tests/ThemeTests.cs` (create)

**Interfaces:**
- Produces: `Settings.Light`, `Settings.Dark`, `Settings.MatchWindows` (const string); `Settings.Appearance` (string, default `MatchWindows`).

- [ ] **Step 1: Write the failing tests**

Create `PCSpecOverlay.Tests/ThemeTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~AppearanceSettingTests"`
Expected: build error `'Settings' does not contain a definition for 'MatchWindows'`.

- [ ] **Step 3: Implement**

In `PCSpecOverlay/Settings.cs`, below the existing `Expanded`/`Compact` constant line:

```csharp
    public const string Light = "Light", Dark = "Dark", MatchWindows = "MatchWindows";
```

Below the `// Telemetry` block's `HistoryRetentionDays` property, add a new group:

```csharp
    // Appearance
    /// <summary>Control-center theme: Light, Dark, or MatchWindows (follows the Windows app setting).</summary>
    public string Appearance { get; set; } = MatchWindows;
```

In `Validate()`, after the `HistoryRetentionDays` clamp line:

```csharp
        if (Appearance is not (Light or Dark or MatchWindows)) Appearance = MatchWindows;
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~AppearanceSettingTests"`
Expected: 9 passed.

- [ ] **Step 5: Commit**

```bash
git add PCSpecOverlay/Settings.cs PCSpecOverlay.Tests/ThemeTests.cs
git commit -F- <<'EOF'
Add the Appearance setting (Light, Dark, Match Windows)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: Theme resolver

**Files:**
- Create: `PCSpecOverlay/Core/ThemeResolver.cs`
- Test: `PCSpecOverlay.Tests/ThemeTests.cs` (append)

**Interfaces:**
- Consumes: `Settings.Light/Dark/MatchWindows` (Task 1), `Log.Warn(string area, string message)` (existing).
- Produces: `enum AppTheme { Dark, Light }`; `ThemeResolver.Resolve(string appearance, Func<object?> readAppsUseLightTheme) : AppTheme`; `ThemeResolver.ReadAppsUseLightTheme() : object?`.

- [ ] **Step 1: Write the failing tests**

Append to `PCSpecOverlay.Tests/ThemeTests.cs` (add `using PCSpecOverlay.Core;` at the top):

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~ThemeResolverTests"`
Expected: build error `The type or namespace name 'AppTheme' could not be found`.

- [ ] **Step 3: Implement**

Create `PCSpecOverlay/Core/ThemeResolver.cs`:

```csharp
using Microsoft.Win32;

namespace PCSpecOverlay.Core;

public enum AppTheme { Dark, Light }

/// <summary>
/// Turns the Appearance setting into a theme. Match Windows follows the Windows "app mode" setting
/// (AppsUseLightTheme = 1 means light); anything missing or unreadable falls back to dark.
/// </summary>
public static class ThemeResolver
{
    public const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static AppTheme Resolve(string appearance, Func<object?> readAppsUseLightTheme) => appearance switch
    {
        Settings.Light => AppTheme.Light,
        Settings.Dark => AppTheme.Dark,
        _ => WindowsTheme(readAppsUseLightTheme),
    };

    private static AppTheme WindowsTheme(Func<object?> read)
    {
        try { return read() is int v && v == 1 ? AppTheme.Light : AppTheme.Dark; }
        catch (Exception e)
        {
            Log.Warn("Theme", $"Couldn't read the Windows app theme, using dark: {e.Message}");
            return AppTheme.Dark;
        }
    }

    public static object? ReadAppsUseLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme");
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~ThemeResolverTests"`
Expected: 8 passed.

- [ ] **Step 5: Commit**

```bash
git add PCSpecOverlay/Core/ThemeResolver.cs PCSpecOverlay.Tests/ThemeTests.cs
git commit -F- <<'EOF'
Resolve the app theme from the Appearance setting and Windows

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 3: Color dictionaries (tokens, aliases, contrast)

**Files:**
- Create: `PCSpecOverlay/Themes/Colors.Dark.xaml`, `PCSpecOverlay/Themes/Colors.Light.xaml`
- Modify: `PCSpecOverlay.Tests/TestSupport.cs` (helpers)
- Test: `PCSpecOverlay.Tests/ThemeTests.cs` (append)

**Interfaces:**
- Produces brush keys (both files): `Bg, Panel, Line, Line2, Text, TextHover, Muted, Faint, Signal, Hot, Good, Selection` and legacy aliases `Window, Sidebar, Card, CardBorder, Input, InputBorder, Accent, AccentHover, AccentSoft, Warn, Info`. Color keys `C.Bg … C.Good` are internal to each file.
- Produces test helpers: `Sta.Run(Action)`, `SourceFiles.Path(string relative) : string`, `Contrast.Ratio(Color, Color) : double`, `XamlFile.LoadDictionary(string relative) : ResourceDictionary`.

- [ ] **Step 1: Add the test helpers**

Append to `PCSpecOverlay.Tests/TestSupport.cs`:

```csharp
/// <summary>Runs WPF code on a fresh STA thread and rethrows anything it throws.</summary>
public static class Sta
{
    public static void Run(Action action)
    {
        Exception? error = null;
        var t = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}

/// <summary>Finds files in the source tree (tests run from bin, so walk up from this file's location).</summary>
public static class SourceFiles
{
    public static string Path(string relative, [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, "..", relative));
}

public static class XamlFile
{
    public static System.Windows.ResourceDictionary LoadDictionary(string relative)
    {
        using var stream = File.OpenRead(SourceFiles.Path(relative));
        return (System.Windows.ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream);
    }
}

/// <summary>WCAG 2 contrast ratio between two colors (1 to 21).</summary>
public static class Contrast
{
    public static double Ratio(System.Windows.Media.Color a, System.Windows.Media.Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(System.Windows.Media.Color c) => 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);

    private static double Lin(byte v)
    {
        var c = v / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
```

- [ ] **Step 2: Write the failing tests**

Append to `PCSpecOverlay.Tests/ThemeTests.cs` (add `using System.Windows; using System.Windows.Media;` at the top):

```csharp
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
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~ColorDictionaryTests"`
Expected: FAIL with `FileNotFoundException` for `Colors.Dark.xaml`.

- [ ] **Step 4: Create the dictionaries**

Create `PCSpecOverlay/Themes/Colors.Dark.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Instrument tokens, dark. Same keys as Colors.Light.xaml; ThemeManager swaps the two. -->
    <Color x:Key="C.Bg">#0B0B0C</Color>
    <Color x:Key="C.Panel">#111113</Color>
    <Color x:Key="C.Line">#262629</Color>
    <Color x:Key="C.Line2">#1A1A1C</Color>
    <Color x:Key="C.Text">#E9E9E6</Color>
    <Color x:Key="C.TextHover">#FFFFFF</Color>
    <Color x:Key="C.Muted">#87878A</Color>
    <Color x:Key="C.Faint">#57575B</Color>
    <Color x:Key="C.Signal">#E8A33D</Color>
    <Color x:Key="C.Hot">#E5534B</Color>
    <Color x:Key="C.Good">#9BCB58</Color>
    <Color x:Key="C.Selection">#55E9E9E6</Color>

    <SolidColorBrush x:Key="Bg" Color="{StaticResource C.Bg}" />
    <SolidColorBrush x:Key="Panel" Color="{StaticResource C.Panel}" />
    <SolidColorBrush x:Key="Line" Color="{StaticResource C.Line}" />
    <SolidColorBrush x:Key="Line2" Color="{StaticResource C.Line2}" />
    <SolidColorBrush x:Key="Text" Color="{StaticResource C.Text}" />
    <SolidColorBrush x:Key="TextHover" Color="{StaticResource C.TextHover}" />
    <SolidColorBrush x:Key="Muted" Color="{StaticResource C.Muted}" />
    <SolidColorBrush x:Key="Faint" Color="{StaticResource C.Faint}" />
    <SolidColorBrush x:Key="Signal" Color="{StaticResource C.Signal}" />
    <SolidColorBrush x:Key="Hot" Color="{StaticResource C.Hot}" />
    <SolidColorBrush x:Key="Good" Color="{StaticResource C.Good}" />
    <SolidColorBrush x:Key="Selection" Color="{StaticResource C.Selection}" />

    <!-- Names the current screens still use; later stages switch them to the tokens above -->
    <SolidColorBrush x:Key="Window" Color="{StaticResource C.Bg}" />
    <SolidColorBrush x:Key="Sidebar" Color="{StaticResource C.Panel}" />
    <SolidColorBrush x:Key="Card" Color="{StaticResource C.Panel}" />
    <SolidColorBrush x:Key="CardBorder" Color="{StaticResource C.Line}" />
    <SolidColorBrush x:Key="Input" Color="{StaticResource C.Panel}" />
    <SolidColorBrush x:Key="InputBorder" Color="{StaticResource C.Line}" />
    <SolidColorBrush x:Key="Accent" Color="{StaticResource C.Text}" />
    <SolidColorBrush x:Key="AccentHover" Color="{StaticResource C.TextHover}" />
    <SolidColorBrush x:Key="AccentSoft" Color="{StaticResource C.Panel}" />
    <SolidColorBrush x:Key="Warn" Color="{StaticResource C.Signal}" />
    <SolidColorBrush x:Key="Info" Color="{StaticResource C.Muted}" />
</ResourceDictionary>
```

Create `PCSpecOverlay/Themes/Colors.Light.xaml` with exactly the same content except the header comment says "light" and these color values:

```xml
    <Color x:Key="C.Bg">#F3F2EE</Color>
    <Color x:Key="C.Panel">#FBFAF7</Color>
    <Color x:Key="C.Line">#D9D7D0</Color>
    <Color x:Key="C.Line2">#E6E4DD</Color>
    <Color x:Key="C.Text">#141414</Color>
    <Color x:Key="C.TextHover">#000000</Color>
    <Color x:Key="C.Muted">#66665F</Color>
    <Color x:Key="C.Faint">#9C9A92</Color>
    <Color x:Key="C.Signal">#955C00</Color>
    <Color x:Key="C.Hot">#B8322B</Color>
    <Color x:Key="C.Good">#47700F</Color>
    <Color x:Key="C.Selection">#40141414</Color>
```

Add both files to the build as WPF pages (they are picked up automatically by the WPF SDK because they are `.xaml` files under the project folder; confirm in Step 5).

- [ ] **Step 5: Run to verify pass, and that the build includes them**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~ColorDictionaryTests"`
Expected: 8 passed.
Run: `dotnet build PCSpec.slnx -v n | grep -c "Colors.Dark"` (Git Bash)
Expected: a count of 1 or more (compiled as a Page).

- [ ] **Step 6: Commit**

```bash
git add PCSpecOverlay/Themes PCSpecOverlay.Tests/TestSupport.cs PCSpecOverlay.Tests/ThemeTests.cs
git commit -F- <<'EOF'
Add the Instrument color tokens for dark and light, with contrast tests

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 4: Icon set

**Files:**
- Create: `PCSpecOverlay/Themes/Icons.xaml`, `PCSpecOverlay/Core/Icon.cs`
- Test: `PCSpecOverlay.Tests/ThemeTests.cs` (append)

**Interfaces:**
- Produces geometry keys `Icon.Mark, Icon.Overview, Icon.Optimizer, Icon.Gaming, Icon.Overlay, Icon.Temperature, Icon.Settings, Icon.Diagnostics, Icon.Sun, Icon.Moon, Icon.Monitor, Icon.Check, Icon.Warning, Icon.Info, Icon.Undo, Icon.Pause, Icon.Plus, Icon.Close, Icon.Chevron, Icon.ChevronDown, Icon.Refresh, Icon.Copy, Icon.File, Icon.Folder, Icon.Pointer, Icon.Power, Icon.Grip, Icon.Window`.
- Produces `PCSpecOverlay.Core.Icon : Control` with `Geometry? Data`. Usage: `<core:Icon Data="{StaticResource Icon.Gaming}" />` (default 14×14, stroke = `Foreground`). Its style is added in Task 6.

- [ ] **Step 1: Write the failing test**

Append to `PCSpecOverlay.Tests/ThemeTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~IconTests"`
Expected: FAIL with `FileNotFoundException` for `Icons.xaml`.

- [ ] **Step 3: Create the icons and the control**

Create `PCSpecOverlay/Themes/Icons.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Outline icons on a 24-unit grid, drawn by core:Icon with a 1.6 stroke and round caps -->
    <StreamGeometry x:Key="Icon.Mark">M4,20 V13 M9,20 V8 M14,20 V11 M19,20 V4</StreamGeometry>
    <StreamGeometry x:Key="Icon.Overview">M3.5,3.5 H10.5 V10.5 H3.5 Z M13.5,3.5 H20.5 V10.5 H13.5 Z M3.5,13.5 H10.5 V20.5 H3.5 Z M13.5,13.5 H20.5 V20.5 H13.5 Z</StreamGeometry>
    <StreamGeometry x:Key="Icon.Optimizer">M4.5,16 A8,8 0 1 1 19.5,16 M12,16 L16,11 M10.6,16 A1.4,1.4 0 1 0 13.4,16 A1.4,1.4 0 1 0 10.6,16</StreamGeometry>
    <StreamGeometry x:Key="Icon.Gaming">M7,8 H17 A4,4 0 0 1 21,12.4 L20.5,16.3 A2.4,2.4 0 0 1 16.2,17.4 L14.5,15 H9.5 L7.8,17.4 A2.4,2.4 0 0 1 3.5,16.3 L3,12.4 A4,4 0 0 1 7,8 Z M8,11 V14 M6.5,12.5 H9.5</StreamGeometry>
    <StreamGeometry x:Key="Icon.Overlay">M3,4 H21 V18 H3 Z M13,6.5 H18.5 V10.5 H13 Z M8,21 H16</StreamGeometry>
    <StreamGeometry x:Key="Icon.Temperature">M14,14.5 V5 A2,2 0 0 0 10,5 V14.5 A4,4 0 1 0 14,14.5 Z M12,9 V16</StreamGeometry>
    <StreamGeometry x:Key="Icon.Settings">M4,7 H14 M18,7 H20 M4,17 H8 M12,17 H20 M14,7 A2,2 0 1 0 18,7 A2,2 0 1 0 14,7 M8,17 A2,2 0 1 0 12,17 A2,2 0 1 0 8,17</StreamGeometry>
    <StreamGeometry x:Key="Icon.Diagnostics">M3,12 H7 L9.5,6 L14.5,18 L17,12 H21</StreamGeometry>
    <StreamGeometry x:Key="Icon.Sun">M8,12 A4,4 0 1 0 16,12 A4,4 0 1 0 8,12 M12,2.5 V4.5 M12,19.5 V21.5 M2.5,12 H4.5 M19.5,12 H21.5 M5.3,5.3 L6.7,6.7 M17.3,17.3 L18.7,18.7 M5.3,18.7 L6.7,17.3 M17.3,6.7 L18.7,5.3</StreamGeometry>
    <StreamGeometry x:Key="Icon.Moon">M20,14.5 A8,8 0 1 1 9.5,4 A6.5,6.5 0 0 0 20,14.5 Z</StreamGeometry>
    <StreamGeometry x:Key="Icon.Monitor">M3,4 H21 V16 H3 Z M9,20 H15 M12,16 V20</StreamGeometry>
    <StreamGeometry x:Key="Icon.Check">M5,12.5 L9.5,17 L19,7.5</StreamGeometry>
    <StreamGeometry x:Key="Icon.Warning">M12,4 L21,20 H3 Z M12,10 V14 M12,17 V17.01</StreamGeometry>
    <StreamGeometry x:Key="Icon.Info">M3.5,12 A8.5,8.5 0 1 0 20.5,12 A8.5,8.5 0 1 0 3.5,12 M12,11 V16 M12,8 V8.01</StreamGeometry>
    <StreamGeometry x:Key="Icon.Undo">M9,14 L4,9 L9,4 M4,9 H14 A6,6 0 0 1 14,21 H11</StreamGeometry>
    <StreamGeometry x:Key="Icon.Pause">M9,6 V18 M15,6 V18</StreamGeometry>
    <StreamGeometry x:Key="Icon.Plus">M12,5 V19 M5,12 H19</StreamGeometry>
    <StreamGeometry x:Key="Icon.Close">M6,6 L18,18 M18,6 L6,18</StreamGeometry>
    <StreamGeometry x:Key="Icon.Chevron">M9,6 L15,12 L9,18</StreamGeometry>
    <StreamGeometry x:Key="Icon.ChevronDown">M7,10 L12,15 L17,10</StreamGeometry>
    <StreamGeometry x:Key="Icon.Refresh">M20,11 A8,8 0 0 0 5.5,6.5 L4,8 M4,4 V8 H8 M4,13 A8,8 0 0 0 18.5,17.5 L20,16 M20,20 V16 H16</StreamGeometry>
    <StreamGeometry x:Key="Icon.Copy">M8,8 H20 V20 H8 Z M16,8 V4 H4 V16 H8</StreamGeometry>
    <StreamGeometry x:Key="Icon.File">M6,3 H14 L18,7 V21 H6 Z M14,3 V7 H18 M9,13 H15 M9,17 H15</StreamGeometry>
    <StreamGeometry x:Key="Icon.Folder">M3,6 H10 L12,8 H21 V19 H3 Z</StreamGeometry>
    <StreamGeometry x:Key="Icon.Pointer">M6,3 L18,12 L13,13 L16,19 L14,20 L11,14 L7,18 Z</StreamGeometry>
    <StreamGeometry x:Key="Icon.Power">M12,3 V11 M6.5,7 A7.5,7.5 0 1 0 17.5,7</StreamGeometry>
    <StreamGeometry x:Key="Icon.Grip">M9,6 V6.01 M15,6 V6.01 M9,12 V12.01 M15,12 V12.01 M9,18 V18.01 M15,18 V18.01</StreamGeometry>
    <StreamGeometry x:Key="Icon.Window">M3,4 H21 V20 H3 Z M3,9 H21</StreamGeometry>
</ResourceDictionary>
```

Create `PCSpecOverlay/Core/Icon.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCSpecOverlay.Core;

/// <summary>Draws one outline icon from Themes/Icons.xaml in the current text color (style in Theme.xaml).</summary>
public sealed class Icon : Control
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(Icon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~IconTests"`
Expected: 1 passed. If a bounds assertion fails, the message names the icon; fix that path so it stays within 0–24.

- [ ] **Step 5: Commit**

```bash
git add PCSpecOverlay/Themes/Icons.xaml PCSpecOverlay/Core/Icon.cs PCSpecOverlay.Tests/ThemeTests.cs
git commit -F- <<'EOF'
Add the vector icon set and an Icon control

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 5: Theme manager

**Files:**
- Create: `PCSpecOverlay/Core/ThemeManager.cs`
- Test: `PCSpecOverlay.Tests/ThemeTests.cs` (append)

**Interfaces:**
- Consumes: `AppTheme`, `ThemeResolver.Resolve`, `ThemeResolver.ReadAppsUseLightTheme` (Task 2); `Log.Info/Warn/Error` (existing).
- Produces:
  - `ThemeManager(ResourceDictionary target, Func<AppTheme, ResourceDictionary>? load = null, Func<object?>? readWindows = null, bool listenToWindows = true)`
  - `AppTheme Current { get; }`
  - `event Action<AppTheme>? Changed`
  - `void Apply(string appearance)`
  - `void OnWindowsThemeChanged()` (internal; called by the Windows listener, and by tests)
  - `static ResourceDictionary LoadColors(AppTheme theme)` (pack URI loader)
  - `IDisposable`

- [ ] **Step 1: Write the failing tests**

Append to `PCSpecOverlay.Tests/ThemeTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~ThemeManagerTests"`
Expected: build error `The type or namespace name 'ThemeManager' could not be found`.

- [ ] **Step 3: Implement**

Create `PCSpecOverlay/Core/ThemeManager.cs`:

```csharp
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PCSpecOverlay.Core;

/// <summary>
/// Owns the app's color dictionary. Every view uses DynamicResource, so replacing the one merged
/// dictionary that holds the colors recolors open windows immediately. With Match Windows, a change
/// to the Windows app theme switches the app without a restart.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private readonly ResourceDictionary _target;
    private readonly Func<AppTheme, ResourceDictionary> _load;
    private readonly Func<object?> _readWindows;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly bool _listening;
    private ResourceDictionary? _colors;
    private string _appearance = Settings.MatchWindows;

    public ThemeManager(ResourceDictionary target, Func<AppTheme, ResourceDictionary>? load = null,
        Func<object?>? readWindows = null, bool listenToWindows = true)
    {
        _target = target;
        _load = load ?? LoadColors;
        _readWindows = readWindows ?? ThemeResolver.ReadAppsUseLightTheme;
        // App.xaml merges the dark colors first so the installer and early windows have them before Apply
        _colors = target.MergedDictionaries.FirstOrDefault(IsColors);
        _listening = listenToWindows;
        if (_listening) SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public AppTheme Current { get; private set; } = AppTheme.Dark;

    public event Action<AppTheme>? Changed;

    public void Apply(string appearance)
    {
        _appearance = appearance;
        var wanted = ThemeResolver.Resolve(appearance, _readWindows);
        if (_colors != null && wanted == Current) return;

        ResourceDictionary colors;
        try { colors = _load(wanted); }
        catch (Exception e)
        {
            Log.Error("Theme", $"Couldn't load the {wanted} theme, using dark", e);
            if (wanted == AppTheme.Dark) return;
            wanted = AppTheme.Dark;
            if (_colors != null && Current == AppTheme.Dark) return;
            try { colors = _load(AppTheme.Dark); } catch { return; }
        }

        var index = _colors is null ? -1 : _target.MergedDictionaries.IndexOf(_colors);
        if (index >= 0) _target.MergedDictionaries[index] = colors;
        else _target.MergedDictionaries.Insert(0, colors);
        _colors = colors;

        var changed = Current != wanted;
        Current = wanted;
        Log.Info("Theme", $"Using the {wanted.ToString().ToLowerInvariant()} theme ({appearance})");
        if (changed) Changed?.Invoke(wanted);
    }

    internal void OnWindowsThemeChanged()
    {
        if (_appearance == Settings.MatchWindows) Apply(_appearance);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General) _dispatcher.BeginInvoke(OnWindowsThemeChanged);
    }

    public static ResourceDictionary LoadColors(AppTheme theme) =>
        new() { Source = new Uri($"pack://application:,,,/PCSpecOverlay;component/Themes/Colors.{theme}.xaml") };

    private static bool IsColors(ResourceDictionary d) => d.Source?.OriginalString.Contains("Themes/Colors.") == true;

    public void Dispose()
    {
        if (_listening) SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
```

Note on the first `Apply`: when no colors dictionary was found in `target` (`_colors` null, as in the tests), the first call always loads and inserts; `Changed` fires only if the theme differs from the starting `Dark`. In the tests the first `Apply(Light)` therefore fires once, and `Apply(MatchWindows)` with Windows dark loads dark without an event.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~ThemeManagerTests"`
Expected: 5 passed.

- [ ] **Step 5: Commit**

```bash
git add PCSpecOverlay/Core/ThemeManager.cs PCSpecOverlay.Tests/ThemeTests.cs
git commit -F- <<'EOF'
Add ThemeManager to swap colors live and follow Windows

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 6: Restyle the shared styles and convert every view to live colors

**Files:**
- Rewrite: `PCSpecOverlay/Theme.xaml`
- Modify: `PCSpecOverlay/App.xaml`, `PCSpecOverlay/ControlCenterWindow.xaml`, `PCSpecOverlay/InstallerWindow.xaml`, `PCSpecOverlay/OverlayWindow.xaml`
- Test: `PCSpecOverlay.Tests/ThemeTests.cs` (append)

**Interfaces:**
- Consumes: color keys (Task 3), `Icon` (Task 4).
- Produces: style keys unchanged (`H1, Lead, Section, CardTitle, Body, Small, CardBox, Btn, Switch, SliderEdge, NavItem`) plus an implicit `core:Icon` style.

- [ ] **Step 1: Write the failing test (no static color lookups left in the views)**

Append to `PCSpecOverlay.Tests/ThemeTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~LiveColorTests"`
Expected: 3 failed (static references and hex colors present).

- [ ] **Step 3: Merge the dictionaries in `App.xaml`**

Replace the `MergedDictionaries` block in `PCSpecOverlay/App.xaml` with:

```xml
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Themes/Colors.Dark.xaml" />
                <ResourceDictionary Source="Themes/Icons.xaml" />
                <ResourceDictionary Source="Theme.xaml" />
            </ResourceDictionary.MergedDictionaries>
```

- [ ] **Step 4: Rewrite `Theme.xaml`**

Replace the whole of `PCSpecOverlay/Theme.xaml` with:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:core="clr-namespace:PCSpecOverlay.Core">

    <!-- Instrument styles. Colors come from Themes/Colors.*.xaml through DynamicResource so the theme can change live. -->

    <Style x:Key="H1" TargetType="TextBlock">
        <Setter Property="FontSize" Value="24" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{DynamicResource Text}" />
    </Style>
    <Style x:Key="Lead" TargetType="TextBlock">
        <Setter Property="FontSize" Value="13" />
        <Setter Property="Foreground" Value="{DynamicResource Muted}" />
        <Setter Property="TextWrapping" Value="Wrap" />
        <Setter Property="Margin" Value="0,4,0,0" />
    </Style>
    <Style x:Key="Section" TargetType="TextBlock">
        <Setter Property="FontSize" Value="10.5" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{DynamicResource Muted}" />
        <Setter Property="Margin" Value="0,24,0,10" />
    </Style>
    <Style x:Key="CardTitle" TargetType="TextBlock">
        <Setter Property="FontSize" Value="14" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{DynamicResource Text}" />
        <Setter Property="TextWrapping" Value="Wrap" />
    </Style>
    <Style x:Key="Body" TargetType="TextBlock">
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Foreground" Value="{DynamicResource Muted}" />
        <Setter Property="TextWrapping" Value="Wrap" />
        <Setter Property="Margin" Value="0,3,0,0" />
    </Style>
    <Style x:Key="Small" TargetType="TextBlock" BasedOn="{StaticResource Body}">
        <Setter Property="FontSize" Value="11.5" />
        <Setter Property="Foreground" Value="{DynamicResource Muted}" />
    </Style>
    <Style x:Key="CardBox" TargetType="Border">
        <Setter Property="Background" Value="{DynamicResource Panel}" />
        <Setter Property="BorderBrush" Value="{DynamicResource Line}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Padding" Value="16,13" />
        <Setter Property="Margin" Value="0,0,0,8" />
    </Style>

    <!-- Square button: filled with the text color when Tag="Primary", outlined otherwise -->
    <Style x:Key="Btn" TargetType="Button">
        <Setter Property="Foreground" Value="{DynamicResource Text}" />
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="BorderBrush" Value="{DynamicResource Line}" />
        <Setter Property="FontSize" Value="12" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Padding" Value="14,5" />
        <Setter Property="MinWidth" Value="64" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="B" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="1" Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="B" Property="BorderBrush" Value="{DynamicResource Muted}" />
                            <Setter TargetName="B" Property="Background" Value="{DynamicResource Panel}" />
                        </Trigger>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="B" Property="BorderBrush" Value="{DynamicResource Text}" />
                        </Trigger>
                        <Trigger Property="Tag" Value="Primary">
                            <Setter TargetName="B" Property="Background" Value="{DynamicResource Text}" />
                            <Setter TargetName="B" Property="BorderBrush" Value="{DynamicResource Text}" />
                            <Setter Property="Foreground" Value="{DynamicResource Bg}" />
                        </Trigger>
                        <Trigger Property="Tag" Value="Danger">
                            <Setter TargetName="B" Property="BorderBrush" Value="{DynamicResource Hot}" />
                            <Setter Property="Foreground" Value="{DynamicResource Hot}" />
                        </Trigger>
                        <MultiTrigger>
                            <MultiTrigger.Conditions>
                                <Condition Property="Tag" Value="Primary" />
                                <Condition Property="IsMouseOver" Value="True" />
                            </MultiTrigger.Conditions>
                            <Setter TargetName="B" Property="Background" Value="{DynamicResource TextHover}" />
                            <Setter TargetName="B" Property="BorderBrush" Value="{DynamicResource TextHover}" />
                        </MultiTrigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter TargetName="B" Property="Opacity" Value="0.35" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Square switch; the label is the CheckBox content, so screen readers announce it -->
    <Style x:Key="Switch" TargetType="CheckBox">
        <Setter Property="core:AccessibleName.FromContent" Value="True" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Foreground" Value="{DynamicResource Text}" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="CheckBox">
                    <Grid Background="Transparent">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <ContentPresenter VerticalAlignment="Center" Margin="0,0,16,0" />
                        <Grid Grid.Column="1" Width="32" Height="18" VerticalAlignment="Center">
                            <Border x:Name="Track" Background="{DynamicResource Panel}" BorderBrush="{DynamicResource Line}" BorderThickness="1" />
                            <Rectangle x:Name="Knob" Width="10" Height="10" Fill="{DynamicResource Muted}" HorizontalAlignment="Left" Margin="4,0,0,0" />
                        </Grid>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Track" Property="Background" Value="{DynamicResource Text}" />
                            <Setter TargetName="Track" Property="BorderBrush" Value="{DynamicResource Text}" />
                            <Setter TargetName="Knob" Property="Fill" Value="{DynamicResource Bg}" />
                            <Setter TargetName="Knob" Property="HorizontalAlignment" Value="Right" />
                            <Setter TargetName="Knob" Property="Margin" Value="0,0,4,0" />
                        </Trigger>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="Track" Property="BorderBrush" Value="{DynamicResource TextHover}" />
                            <Setter TargetName="Track" Property="BorderThickness" Value="2" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.4" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="TextBox">
        <Setter Property="Background" Value="{DynamicResource Panel}" />
        <Setter Property="Foreground" Value="{DynamicResource Text}" />
        <Setter Property="BorderBrush" Value="{DynamicResource Line}" />
        <Setter Property="CaretBrush" Value="{DynamicResource Text}" />
        <Setter Property="SelectionBrush" Value="{DynamicResource Selection}" />
        <Setter Property="Padding" Value="8,5" />
        <Setter Property="FontSize" Value="12.5" />
    </Style>

    <Style TargetType="ComboBoxItem">
        <Setter Property="Foreground" Value="{DynamicResource Text}" />
        <Setter Property="Padding" Value="10,6" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ComboBoxItem">
                    <Border x:Name="B" Background="Transparent" Padding="{TemplateBinding Padding}">
                        <ContentPresenter />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsHighlighted" Value="True">
                            <Setter TargetName="B" Property="Background" Value="{DynamicResource Line2}" />
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter Property="FontWeight" Value="SemiBold" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType="ComboBox">
        <Setter Property="FontSize" Value="12.5" />
        <Setter Property="MinWidth" Value="150" />
        <Setter Property="Foreground" Value="{DynamicResource Text}" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ComboBox">
                    <Grid>
                        <ToggleButton x:Name="Toggle" Focusable="False" ClickMode="Press"
                                      IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
                            <ToggleButton.Template>
                                <ControlTemplate TargetType="ToggleButton">
                                    <Border x:Name="Box" Background="{DynamicResource Panel}" BorderBrush="{DynamicResource Line}" BorderThickness="1">
                                        <Path HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,10,0"
                                              Data="M0,0 L4,4 L8,0" Stroke="{DynamicResource Muted}" StrokeThickness="1.4" />
                                    </Border>
                                    <ControlTemplate.Triggers>
                                        <Trigger Property="IsMouseOver" Value="True">
                                            <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource Muted}" />
                                        </Trigger>
                                    </ControlTemplate.Triggers>
                                </ControlTemplate>
                            </ToggleButton.Template>
                        </ToggleButton>
                        <ContentPresenter IsHitTestVisible="False" Margin="10,6,28,6" VerticalAlignment="Center"
                                          Content="{TemplateBinding SelectionBoxItem}"
                                          ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" />
                        <Popup IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True"
                               Focusable="False" PopupAnimation="Fade">
                            <Border Background="{DynamicResource Panel}" BorderBrush="{DynamicResource Line}" BorderThickness="1" Padding="2"
                                    MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" Margin="0,2,0,0">
                                <ScrollViewer MaxHeight="280" VerticalScrollBarVisibility="Auto">
                                    <ItemsPresenter />
                                </ScrollViewer>
                            </Border>
                        </Popup>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.4" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Slider: 2 px track, filled up to a narrow square handle -->
    <Style x:Key="SliderEdge" TargetType="RepeatButton">
        <Setter Property="Focusable" Value="False" />
        <Setter Property="IsTabStop" Value="False" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="RepeatButton">
                    <Border Background="Transparent">
                        <Border Height="2" Background="{TemplateBinding Background}" />
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType="Slider">
        <Setter Property="IsSnapToTickEnabled" Value="True" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Height" Value="22" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Slider">
                    <Track x:Name="PART_Track">
                        <Track.DecreaseRepeatButton>
                            <RepeatButton Style="{StaticResource SliderEdge}" Background="{DynamicResource Text}" Command="Slider.DecreaseLarge" />
                        </Track.DecreaseRepeatButton>
                        <Track.IncreaseRepeatButton>
                            <RepeatButton Style="{StaticResource SliderEdge}" Background="{DynamicResource Line}" Command="Slider.IncreaseLarge" />
                        </Track.IncreaseRepeatButton>
                        <Track.Thumb>
                            <Thumb>
                                <Thumb.Template>
                                    <ControlTemplate TargetType="Thumb">
                                        <Rectangle x:Name="Knob" Width="4" Height="14" Fill="{DynamicResource Text}" />
                                        <ControlTemplate.Triggers>
                                            <Trigger Property="IsMouseOver" Value="True">
                                                <Setter TargetName="Knob" Property="Fill" Value="{DynamicResource TextHover}" />
                                            </Trigger>
                                        </ControlTemplate.Triggers>
                                    </ControlTemplate>
                                </Thumb.Template>
                            </Thumb>
                        </Track.Thumb>
                    </Track>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="ScrollBar">
        <Setter Property="Width" Value="10" />
        <Setter Property="MinWidth" Value="10" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ScrollBar">
                    <Track x:Name="PART_Track" IsDirectionReversed="True" Margin="3,4">
                        <Track.Thumb>
                            <Thumb>
                                <Thumb.Template>
                                    <ControlTemplate TargetType="Thumb">
                                        <Border x:Name="T" Background="{DynamicResource Line}" />
                                        <ControlTemplate.Triggers>
                                            <Trigger Property="IsMouseOver" Value="True">
                                                <Setter TargetName="T" Property="Background" Value="{DynamicResource Faint}" />
                                            </Trigger>
                                        </ControlTemplate.Triggers>
                                    </ControlTemplate>
                                </Thumb.Template>
                            </Thumb>
                        </Track.Thumb>
                    </Track>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Sidebar navigation (replaced by top tabs in stage 3) -->
    <Style x:Key="NavItem" TargetType="TabItem">
        <Setter Property="Foreground" Value="{DynamicResource Muted}" />
        <Setter Property="FontSize" Value="13" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TabItem">
                    <Border x:Name="B" Background="Transparent" BorderThickness="0" Padding="12,8" Margin="8,1">
                        <Grid>
                            <Border x:Name="Bar" Width="2" Background="{DynamicResource Text}"
                                    HorizontalAlignment="Left" Margin="-12,0,0,0" Visibility="Collapsed" />
                            <ContentPresenter ContentSource="Header" />
                        </Grid>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="B" Property="Background" Value="{DynamicResource Panel}" />
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="B" Property="Background" Value="{DynamicResource Panel}" />
                            <Setter TargetName="Bar" Property="Visibility" Value="Visible" />
                            <Setter Property="Foreground" Value="{DynamicResource Text}" />
                            <Setter Property="FontWeight" Value="SemiBold" />
                        </Trigger>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="B" Property="BorderBrush" Value="{DynamicResource Text}" />
                            <Setter TargetName="B" Property="BorderThickness" Value="1" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Outline icon: <core:Icon Data="{StaticResource Icon.Gaming}" />, drawn in the current text color -->
    <Style TargetType="core:Icon">
        <Setter Property="Width" Value="14" />
        <Setter Property="Height" Value="14" />
        <Setter Property="Focusable" Value="False" />
        <Setter Property="IsTabStop" Value="False" />
        <Setter Property="Foreground" Value="{DynamicResource Text}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="core:Icon">
                    <Viewbox Stretch="Uniform">
                        <Canvas Width="24" Height="24">
                            <Path Data="{TemplateBinding Data}" Stroke="{TemplateBinding Foreground}" StrokeThickness="1.6"
                                  StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round" />
                        </Canvas>
                    </Viewbox>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
</ResourceDictionary>
```

- [ ] **Step 5: Convert the window XAML to live colors**

Run (Git Bash, repo root):

```bash
sed -i -E 's/\{StaticResource (Accent|AccentHover|AccentSoft|Window|Sidebar|Card|CardBorder|Input|InputBorder|Text|Muted|Faint|Good|Warn|Hot|Info)\}/{DynamicResource \1}/g' \
  PCSpecOverlay/ControlCenterWindow.xaml PCSpecOverlay/InstallerWindow.xaml
grep -c "DynamicResource" PCSpecOverlay/ControlCenterWindow.xaml PCSpecOverlay/InstallerWindow.xaml
```

Expected: about 41 lines in `ControlCenterWindow.xaml` and 5 in `InstallerWindow.xaml` (grep counts lines, so a line with two references counts once). The `LiveColorTests` in Step 7 are the real check.

- [ ] **Step 6: Keep the overlay dark**

The overlay inherits app resources, so in light mode its `Accent` lookups would turn dark-on-dark. In `PCSpecOverlay/OverlayWindow.xaml`, add as the first entries inside `<Window.Resources>`:

```xml
        <!-- The overlay stays dark whatever the app theme (its own theme setting comes in stage 2) -->
        <SolidColorBrush x:Key="Accent" Color="#E9E9E6" />
```

- [ ] **Step 7: Build, run the new test and the whole suite**

Run: `dotnet build PCSpec.slnx`
Expected: Build succeeded, 0 errors.
Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~LiveColorTests"`
Expected: 3 passed.
Run: `dotnet test PCSpec.slnx`
Expected: all tests pass (99 existing + this stage's).

- [ ] **Step 8: Commit**

```bash
git add PCSpecOverlay/Theme.xaml PCSpecOverlay/App.xaml PCSpecOverlay/ControlCenterWindow.xaml PCSpecOverlay/InstallerWindow.xaml PCSpecOverlay/OverlayWindow.xaml PCSpecOverlay.Tests/ThemeTests.cs
git commit -F- <<'EOF'
Restyle shared controls in Instrument and use live color references

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 7: Move code-side colors to tokens

**Files:**
- Modify: `PCSpecOverlay/ControlCenterWindow.xaml.cs` (`Temp` ~line 661, `HotLine` ~line 357, `LevelBrush` ~line 706)
- Test: `PCSpecOverlay.Tests/ThemeTests.cs` (append)

**Interfaces:**
- Consumes: brush keys `Text, Hot, Warn, Muted` (Task 3).

- [ ] **Step 1: Write the failing test**

Append to `PCSpecOverlay.Tests/ThemeTests.cs`:

```csharp
public class CodeColorTests
{
    [Fact]
    public void Control_center_code_has_no_hard_coded_colors()
    {
        var code = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/ControlCenterWindow.xaml.cs"));
        Assert.DoesNotMatch(@"Color\.From(Rgb|Argb)\(\s*0x[0-9A-Fa-f]{2},\s*0x", code);
        Assert.DoesNotContain("Brushes.White", code);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~CodeColorTests"`
Expected: FAIL (matches `Color.FromRgb(0xFF, 0x4D, 0x4F)`).

- [ ] **Step 3: Implement**

In `PCSpecOverlay/ControlCenterWindow.xaml.cs` replace the `Temp` method with:

```csharp
    private Brush Temp(double? c, double warm, double hot) =>
        (Brush)FindResource(c is null ? "Text" : c >= hot ? "Hot" : c >= warm ? "Warn" : "Text");
```

Replace the body line that adds the dashed line in `HotLine` (the `canvas.Children.Add(new Line { … 0x70, 0xFF, 0x4D, 0x4F … })` statement) with:

```csharp
        var hotColor = ((SolidColorBrush)Application.Current.FindResource("Hot")).Color;
        canvas.Children.Add(new Line { X1 = 0, X2 = w, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(hotColor) { Opacity = 0.45 }, StrokeDashArray = [4, 4], StrokeThickness = 1 });
```

Replace the `Convert` method of `LevelBrush` with:

```csharp
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Application.Current?.TryFindResource(value switch
        {
            LogLevel.Error => "Hot",
            LogLevel.Warning => "Warn",
            _ => "Muted",
        }) as Brush ?? Brushes.Gray;
```

- [ ] **Step 4: Build and test**

Run: `dotnet build PCSpec.slnx` — Expected: 0 errors (if `Temp` is reported as used from a static context, it isn't: both callers at ~lines 237 and 247 are instance methods).
Run: `dotnet test PCSpec.slnx` — Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add PCSpecOverlay/ControlCenterWindow.xaml.cs PCSpecOverlay.Tests/ThemeTests.cs
git commit -F- <<'EOF'
Take control-center code colors from the theme

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 8: Wire the theme into the app (title bar, Appearance control, live switching)

**Files:**
- Create: `PCSpecOverlay/Core/TitleBar.cs`; delete `PCSpecOverlay/Core/DarkTitleBar.cs`
- Modify: `PCSpecOverlay/App.xaml.cs`, `PCSpecOverlay/ControlCenterWindow.xaml`, `PCSpecOverlay/ControlCenterWindow.xaml.cs`, `PCSpecOverlay/InstallerWindow.xaml.cs`

**Interfaces:**
- Consumes: `ThemeManager` (Task 5), `Settings.Appearance` (Task 1).
- Produces: `TitleBar.Apply(Window window, bool dark)`; `App.Theme : ThemeManager`.

- [ ] **Step 1: Title bar**

Delete `PCSpecOverlay/Core/DarkTitleBar.cs` and create `PCSpecOverlay/Core/TitleBar.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PCSpecOverlay.Core;

/// <summary>Switches a window's Windows title bar between light and dark to match the app theme.</summary>
public static class TitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DwmwaUseImmersiveDarkMode = 20;

    public static void Apply(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }
}
```

In `PCSpecOverlay/InstallerWindow.xaml.cs` replace `DarkTitleBar.Apply(this)` with `TitleBar.Apply(this, dark: true)` (the installer follows the theme in stage 5).

- [ ] **Step 2: Create and drive the ThemeManager in `App`**

In `PCSpecOverlay/App.xaml.cs`:

Add a field next to the other private fields:

```csharp
    private ThemeManager _theme = null!;
```

Add a public property next to `public Settings Settings => _engine.Settings;`:

```csharp
    public ThemeManager Theme => _theme;
```

In `OnStartup`, directly after `_engine = new Engine();`:

```csharp
        _theme = new ThemeManager(Resources);
        _theme.Apply(Settings.Appearance);
```

At the end of `SaveSettings()` add:

```csharp
        _theme.Apply(Settings.Appearance);
```

In `OnExit`, next to the `DisplaySettingsChanged -=` line:

```csharp
        _theme?.Dispose();
```

- [ ] **Step 3: Control center follows the theme**

In `PCSpecOverlay/ControlCenterWindow.xaml.cs` constructor, replace `SourceInitialized += (_, _) => DarkTitleBar.Apply(this);` with:

```csharp
        SourceInitialized += (_, _) => TitleBar.Apply(this, app.Theme.Current == AppTheme.Dark);
        app.Theme.Changed += OnThemeChanged;
```

Change the existing `Closed += (_, _) => { _refresh.Stop(); … };` handler so it also unsubscribes, by adding `_app.Theme.Changed -= OnThemeChanged;` as its first statement.

Add this method next to `RefreshAll()`:

```csharp
    /// <summary>Brushes made in code (banners, charts, temperature colors) are rebuilt by the next refresh.</summary>
    private void OnThemeChanged(AppTheme theme)
    {
        TitleBar.Apply(this, theme == AppTheme.Dark);
        RefreshAll();
    }
```

- [ ] **Step 4: Appearance control on the General page**

In `PCSpecOverlay/ControlCenterWindow.xaml`, directly after the STARTUP card's closing `</Border>` (the card containing `StartupSwitch`), insert:

```xml
                    <TextBlock Text="APPEARANCE" Style="{StaticResource Section}" />
                    <Border Style="{StaticResource CardBox}">
                        <DockPanel>
                            <ComboBox x:Name="AppearanceBox" DockPanel.Dock="Right" VerticalAlignment="Center"
                                      SelectedValuePath="Tag" SelectedValue="{Binding Appearance}" AutomationProperties.Name="Theme">
                                <ComboBoxItem Tag="MatchWindows" Content="Match Windows" />
                                <ComboBoxItem Tag="Light" Content="Light" />
                                <ComboBoxItem Tag="Dark" Content="Dark" />
                            </ComboBox>
                            <StackPanel Margin="0,0,16,0">
                                <TextBlock Text="Theme" FontSize="13" />
                                <TextBlock Style="{StaticResource Body}" Text="Light, dark, or follow the Windows app setting. The in-game overlay stays dark." />
                            </StackPanel>
                        </DockPanel>
                    </Border>
```

The window already saves after any `Selector.SelectionChanged` (`OnEdited`), and `App.SaveSettings` now applies the theme, so no handler is needed.

- [ ] **Step 5: Build and run all tests**

Run: `dotnet build PCSpec.slnx` — Expected: 0 errors, and no remaining reference to `DarkTitleBar` (`grep -rn DarkTitleBar PCSpecOverlay --include=*.cs` prints nothing).
Run: `dotnet test PCSpec.slnx` — Expected: all pass.

- [ ] **Step 6: Verify on the real PC**

Run the development build (`dotnet run --project PCSpecOverlay`, which uses the `-Dev` data folder and won't touch the installed app), open the control center, and check each item. Record the results in `PCSpecOverlay/docs/AUDIT.md` under a new "Instrument stage 1" heading:

1. Theme set to **Dark**: every page uses the dark tokens, with no green left, and the title bar is dark.
2. Theme set to **Light**: the pages switch without restarting, the title bar turns light, and the text is readable on every page (check Optimizer, Gaming, Temperatures and Diagnostics). If the title bar only repaints after the window is clicked away and back, that is DWM's normal behavior; note it rather than adding redraw hacks.
3. Theme set to **Match Windows**: flip Windows Settings → Personalization → Colors → "Choose your app mode" between Light and Dark while the window is open. The app follows within a second each time.
4. The in-game overlay (Alt+Shift+O) stays dark in all three modes.
5. Screen-reader names: run the UI Automation name check used before (every `Switch` CheckBox has a non-empty Name) and confirm the new Theme dropdown is named "Theme".
6. Restore your previous theme choice afterwards.

- [ ] **Step 7: Commit**

```bash
git add -A PCSpecOverlay
git commit -F- <<'EOF'
Switch light and dark live: Appearance setting, title bar, Windows follow

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## After stage 1

Stage 1 ends with the current layouts in Instrument colors, live light/dark switching, and the icon set available. It is not released on its own. Deliberately left for stage 2: the overlay's own hard-coded colors (`OverlayWindow.xaml` brushes and `OverlayWindow.xaml.cs` `Frozen(...)` values) move to tokens together with the overlay theme setting; the sun/moon title-bar toggle arrives with the tab bar in stage 3. The next plan (stage 2, the overlay) is written once this stage is merged, against the code as it then stands.
