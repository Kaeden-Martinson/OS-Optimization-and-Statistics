# Instrument Redesign, Stage 3 (Window Shell and Overview) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the control center's left sidebar with a top tab bar (seven tabs with icons, the version and a sun/moon theme switch) and rebuild the Overview page in the Instrument style: four readings in a ruled grid, a frame-rate chart with dips marked, an event panel of what the app did, and a status line.

**Architecture:** Three small pure functions (`OverviewData`: tick-bar fill, chart dips, event filtering) carry the logic and are unit-tested. The window's root becomes a Grid: the TabControl (its template is now a top bar plus the page) with the app name drawn over the bar's left edge and the version and theme switch over its right edge, so those elements are ordinary named fields. The Overview's code-built colors use `SetResourceReference` or per-refresh lookups so they follow live theme changes.

**Tech Stack:** .NET 8 WPF (C# 12), xUnit 2.9.

**Spec:** `docs/superpowers/specs/2026-10-24-instrument-redesign-design.md` (sections 1, 2 "sun/moon button", 3 "Window shell" and "Overview", Build order stage 3). Stages 1 and 2 are merged: color tokens (`Bg, Panel, Line, Line2, Text, TextHover, Muted, Faint, Signal, Hot, Good` plus legacy aliases), `core:Icon` with `Icon.*` geometries, `ThemeManager` (`App.Theme.Current`, `Changed`), `Settings.Appearance` and its dropdown `AppearanceBox`, `Telemetry.FpsSource`, and test helpers `SourceFiles.Path`.

## Global Constraints

- Tabs, in this order: Overview, Optimizer, Gaming, Overlay, Alerts, Settings, Diagnostics (the old "Temperatures & alerts" is shown as **Alerts**, "General" as **Settings**). Page order and `Nav.SelectedIndex` values are unchanged.
- Each tab: vector icon + label; the selected tab has a 2 px underline in `Text`. No emojis.
- Sun/moon switch at the right of the tab bar: flips immediately and saves `Light` or `Dark`. Match Windows stays available on the Settings page.
- Minimum window width 900 px (spec said "about 840"; seven tabs plus the version and switch need 900 — Ruling recorded in the ledger).
- Colors only from tokens via `DynamicResource` / `SetResourceReference` / per-refresh `FindResource`; `Faint` never for text; amber (`Signal`) only for warnings, red (`Hot`) only for hot, green (`Good`) only for status lights. Numbers in Cascadia Mono (fallback Consolas). Square corners, 1 px lines.
- The event panel reads the existing in-memory log (`Log.Recent()`); nothing new is stored.
- Existing behavior is unchanged: banners (recovery mode, FPS unavailable, browser restart), Stop/Resume optimizations, every other page.
- Build: `dotnet build PCSpec.slnx` (0 warnings). Tests: `dotnet test PCSpec.slnx` (169 passing before this stage). Shell examples use Git Bash from the repo root.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## File map

| File | Status | Responsibility |
|---|---|---|
| `PCSpecOverlay/Core/OverviewData.cs` | create | Tick-bar fill, chart dips, event filtering |
| `PCSpecOverlay/Theme.xaml` | modify | `TopTab` and `IconToggle` styles; remove `NavItem` |
| `PCSpecOverlay/ControlCenterWindow.xaml` | modify | Root grid, top-bar template, tab headers/icons, Overview page |
| `PCSpecOverlay/ControlCenterWindow.xaml.cs` | modify | Version label, theme switch, Overview refresh code |
| `PCSpecOverlay.Tests/ShellTests.cs` | create | All tests for this stage |

---

### Task 1: Overview data helpers

**Files:**
- Create: `PCSpecOverlay/Core/OverviewData.cs`
- Test: `PCSpecOverlay.Tests/ShellTests.cs` (create)

**Interfaces:**
- Consumes: `LogEntry(DateTime Time, LogLevel Level, string Area, string Message)`, `LogLevel { Info, Warning, Error }` (existing, `PCSpecOverlay.Core`).
- Produces: `OverviewData.TickCount` (10); `OverviewData.FilledTicks(double? fraction) : int`; `OverviewData.Dips(IReadOnlyList<double?> values, double ratio = 0.7) : List<int>`; `OverviewData.Events(IReadOnlyList<LogEntry> log, int max) : List<LogEntry>`.

- [ ] **Step 1: Write the failing tests**

Create `PCSpecOverlay.Tests/ShellTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~OverviewDataTests"`
Expected: build error `The name 'OverviewData' does not exist in the current context`.

- [ ] **Step 3: Implement**

Create `PCSpecOverlay/Core/OverviewData.cs`:

```csharp
namespace PCSpecOverlay.Core;

/// <summary>The small calculations behind the Overview page, kept apart from the window so they can be tested.</summary>
public static class OverviewData
{
    public const int TickCount = 10;

    /// <summary>How many of the ten segments of a reading's bar are filled (fraction 0–1; anything else counts as empty or full).</summary>
    public static int FilledTicks(double? fraction) =>
        fraction is { } f && double.IsFinite(f)
            ? (int)Math.Round(Math.Clamp(f, 0, 1) * TickCount, MidpointRounding.AwayFromZero)
            : 0;

    /// <summary>
    /// Frame-rate dips worth marking on the chart: points below <paramref name="ratio"/> × the average that are
    /// lower than the point before and no higher than the point after (a flat low stretch is marked once, at its
    /// start). Gaps (null or non-positive) are skipped; with fewer than three real values nothing is marked.
    /// </summary>
    public static List<int> Dips(IReadOnlyList<double?> values, double ratio = 0.7)
    {
        var dips = new List<int>();
        var valid = values.Where(v => v is > 0).Select(v => v!.Value).ToList();
        if (valid.Count < 3) return dips;
        var limit = valid.Average() * ratio;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is not { } v || v <= 0 || v >= limit) continue;
            var before = Neighbour(values, i, -1);
            var after = Neighbour(values, i, +1);
            if ((before is null || v < before) && (after is null || v <= after)) dips.Add(i);
        }
        return dips;
    }

    /// <summary>The nearest real value in one direction, skipping gaps.</summary>
    private static double? Neighbour(IReadOnlyList<double?> values, int index, int step)
    {
        for (var i = index + step; i >= 0 && i < values.Count; i += step)
            if (values[i] is > 0) return values[i];
        return null;
    }

    /// <summary>Log areas whose entries describe something the app did (as opposed to start-up chatter).</summary>
    private static readonly HashSet<string> ActionAreas = new(StringComparer.OrdinalIgnoreCase)
        { "Gaming", "GameBoost", "Changes", "Alert", "Optimizer", "Browser", "Updates", "Recovery", "Theme" };

    /// <summary>What the app did and why, newest first: actions from those areas plus every warning and error.</summary>
    public static List<LogEntry> Events(IReadOnlyList<LogEntry> log, int max) =>
        log.Where(e => e.Level != LogLevel.Info || ActionAreas.Contains(e.Area)).Reverse().Take(max).ToList();
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~OverviewDataTests"`
Expected: 16 passed.

- [ ] **Step 5: Commit**

```bash
git add PCSpecOverlay/Core/OverviewData.cs PCSpecOverlay.Tests/ShellTests.cs
git commit -F- <<'EOF'
Add the Overview's tick-bar, dip and event calculations

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: Top tab bar and theme switch

**Files:**
- Modify: `PCSpecOverlay/Theme.xaml` (replace the `NavItem` style), `PCSpecOverlay/ControlCenterWindow.xaml` (root, template, headers, two page titles), `PCSpecOverlay/ControlCenterWindow.xaml.cs`
- Test: `PCSpecOverlay.Tests/ShellTests.cs` (append)

**Interfaces:**
- Consumes: `core:Icon`, `Icon.*` geometries, color tokens (stage 1); `App.Theme.Current`, `Settings.Light/Dark`, `AppearanceBox` (stage 1).
- Produces: styles `TopTab` (TabItem; icon from `Tag`) and `IconToggle` (ToggleButton; icon from `Tag`); window fields `VersionLabel`, `LightButton`, `DarkButton`; methods `ChooseTheme(string)`, `SyncThemeButtons()`.

- [ ] **Step 1: Write the failing test**

Append to `PCSpecOverlay.Tests/ShellTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~ShellSourceTests"` — Expected: 2 failed.

- [ ] **Step 3: Styles in `Theme.xaml`**

Replace the whole `NavItem` style (from `<!-- Sidebar navigation (replaced by top tabs in stage 3) -->` through its closing `</Style>`) with:

```xml
    <!-- Top tab: icon (from Tag) + label; the selected tab is underlined in the text color -->
    <Style x:Key="TopTab" TargetType="TabItem">
        <Setter Property="Foreground" Value="{DynamicResource Muted}" />
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TabItem">
                    <Border x:Name="B" Background="Transparent" BorderBrush="Transparent" BorderThickness="0,0,0,2" Padding="9,0" Height="40">
                        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                            <core:Icon Data="{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}"
                                       Foreground="{TemplateBinding Foreground}" Margin="0,0,7,0" VerticalAlignment="Center" />
                            <ContentPresenter ContentSource="Header" VerticalAlignment="Center" />
                        </StackPanel>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Foreground" Value="{DynamicResource Text}" />
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="B" Property="BorderBrush" Value="{DynamicResource Text}" />
                            <Setter Property="Foreground" Value="{DynamicResource Text}" />
                        </Trigger>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="B" Property="Background" Value="{DynamicResource Panel}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Small square icon toggle (the sun/moon switch); the icon comes from Tag -->
    <Style x:Key="IconToggle" TargetType="ToggleButton">
        <Setter Property="Foreground" Value="{DynamicResource Muted}" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ToggleButton">
                    <Border x:Name="B" Background="Transparent" Padding="6,4">
                        <core:Icon Data="{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}" Foreground="{TemplateBinding Foreground}" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="B" Property="Background" Value="{DynamicResource Panel}" />
                            <Setter Property="Foreground" Value="{DynamicResource Text}" />
                        </Trigger>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Foreground" Value="{DynamicResource Text}" />
                        </Trigger>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="B" Property="Background" Value="{DynamicResource Line2}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
```

- [ ] **Step 4: Window root and tab bar in `ControlCenterWindow.xaml`**

1. In the `<Window …>` start tag add `xmlns:core="clr-namespace:PCSpecOverlay.Core"` after the `xmlns:local` line, and change `MinWidth="820"` to `MinWidth="900"`.
2. Wrap the `<TabControl x:Name="Nav" …> … </TabControl>` element in a `<Grid>` (open `<Grid>` directly before `<TabControl`, close `</Grid>` directly before `</Window>`).
3. Replace the whole `<TabControl.Template> … </TabControl.Template>` block with:

```xml
        <TabControl.Template>
            <ControlTemplate TargetType="TabControl">
                <DockPanel>
                    <!-- Tab bar; the app name (left) and version + theme switch (right) are drawn over it by the window -->
                    <Border DockPanel.Dock="Top" Height="40" BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1">
                        <StackPanel IsItemsHost="True" Orientation="Horizontal" Margin="124,0,170,0" />
                    </Border>
                    <!-- This name lets screen readers (UI Automation) reach the page content -->
                    <ContentPresenter x:Name="PART_SelectedContentHost" ContentSource="SelectedContent" />
                </DockPanel>
            </ControlTemplate>
        </TabControl.Template>
```

4. Directly after `</TabControl>` (inside the new Grid) add:

```xml
    <!-- App name over the left of the tab bar -->
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Left" VerticalAlignment="Top" Height="40" Margin="16,0,0,0" IsHitTestVisible="False">
        <core:Icon Data="{StaticResource Icon.Mark}" Width="16" Height="16" VerticalAlignment="Center" />
        <TextBlock Text="PC SPEC" FontSize="12" FontWeight="SemiBold" Margin="8,0,0,0" VerticalAlignment="Center" />
    </StackPanel>
    <!-- Version and the light/dark switch over the right of the tab bar -->
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Top" Height="40" Margin="0,0,12,0">
        <TextBlock x:Name="VersionLabel" FontFamily="Cascadia Mono, Consolas" FontSize="11" Foreground="{DynamicResource Muted}"
                   VerticalAlignment="Center" Margin="0,0,12,0" />
        <Border BorderBrush="{DynamicResource Line}" BorderThickness="1" VerticalAlignment="Center">
            <StackPanel Orientation="Horizontal">
                <ToggleButton x:Name="LightButton" Style="{StaticResource IconToggle}" Tag="{StaticResource Icon.Sun}"
                              AutomationProperties.Name="Light theme" ToolTip="Light theme" Click="OnLightTheme" />
                <ToggleButton x:Name="DarkButton" Style="{StaticResource IconToggle}" Tag="{StaticResource Icon.Moon}"
                              AutomationProperties.Name="Dark theme" ToolTip="Dark theme" Click="OnDarkTheme" />
            </StackPanel>
        </Border>
    </StackPanel>
```

5. Change the seven tab headers (keep everything inside each TabItem as it is):

| From | To |
|---|---|
| `<TabItem Header="Overview" Style="{StaticResource NavItem}">` | `<TabItem Header="Overview" Tag="{StaticResource Icon.Overview}" Style="{StaticResource TopTab}">` |
| `<TabItem Header="Optimizer" Style="{StaticResource NavItem}">` | `<TabItem Header="Optimizer" Tag="{StaticResource Icon.Optimizer}" Style="{StaticResource TopTab}">` |
| `<TabItem Header="Gaming" Style="{StaticResource NavItem}">` | `<TabItem Header="Gaming" Tag="{StaticResource Icon.Gaming}" Style="{StaticResource TopTab}">` |
| `<TabItem Header="Overlay" Style="{StaticResource NavItem}">` | `<TabItem Header="Overlay" Tag="{StaticResource Icon.Overlay}" Style="{StaticResource TopTab}">` |
| `<TabItem Header="Temperatures &amp; alerts" Style="{StaticResource NavItem}">` | `<TabItem Header="Alerts" Tag="{StaticResource Icon.Temperature}" Style="{StaticResource TopTab}">` |
| `<TabItem Header="General" Style="{StaticResource NavItem}">` | `<TabItem Header="Settings" Tag="{StaticResource Icon.Settings}" Style="{StaticResource TopTab}">` |
| `<TabItem Header="Diagnostics" Style="{StaticResource NavItem}">` | `<TabItem Header="Diagnostics" Tag="{StaticResource Icon.Diagnostics}" Style="{StaticResource TopTab}">` |

6. On the Settings page (the former General page), change its title `<TextBlock Text="General" Style="{StaticResource H1}" />` to `<TextBlock Text="Settings" Style="{StaticResource H1}" />`.

- [ ] **Step 5: Code in `ControlCenterWindow.xaml.cs`**

1. Delete the property `public string VersionText => …;` (only the old sidebar used it).
2. In the constructor, directly after `InitializeComponent();`, add:

```csharp
        VersionLabel.Text = AppIdentity.Version + (AppIdentity.IsDevBuild ? " dev" : "");
```

3. Change `Loaded += (_, _) => { _loading = false; RefreshAll(); };` to:

```csharp
        Loaded += (_, _) => { _loading = false; SyncThemeButtons(); RefreshAll(); };
```

4. In `OnThemeChanged`, add `SyncThemeButtons();` as its first statement.
5. In `RefreshSlow`, change the hotkey hint text `"No shortcut is active; set one on the General page."` to `"No shortcut is active; set one on the Settings page."`.
6. Add these members next to `OnThemeChanged`:

```csharp
    private void OnLightTheme(object sender, RoutedEventArgs e) => ChooseTheme(Settings.Light);

    private void OnDarkTheme(object sender, RoutedEventArgs e) => ChooseTheme(Settings.Dark);

    /// <summary>The switch in the tab bar picks Light or Dark outright; Match Windows stays on the Settings page.</summary>
    private void ChooseTheme(string appearance)
    {
        _settings.Appearance = appearance;
        _app.SaveSettings();
        AppearanceBox.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateTarget();
        SyncThemeButtons();
    }

    /// <summary>The pressed half of the switch shows the theme in use (also when it follows Windows).</summary>
    private void SyncThemeButtons()
    {
        LightButton.IsChecked = _app.Theme.Current == AppTheme.Light;
        DarkButton.IsChecked = _app.Theme.Current == AppTheme.Dark;
    }
```

- [ ] **Step 6: Build, run the new tests and the suite**

Run: `dotnet build PCSpec.slnx` — Expected: 0 errors, 0 warnings (no remaining reference to `VersionText` or `NavItem`).
Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~ShellSourceTests"` — Expected: 2 passed.
Run: `dotnet test PCSpec.slnx` — Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add PCSpecOverlay/Theme.xaml PCSpecOverlay/ControlCenterWindow.xaml PCSpecOverlay/ControlCenterWindow.xaml.cs PCSpecOverlay.Tests/ShellTests.cs
git commit -F- <<'EOF'
Replace the sidebar with a top tab bar and a light/dark switch

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 3: Rebuild the Overview page

**Files:**
- Modify: `PCSpecOverlay/ControlCenterWindow.xaml` (Window.Resources styles; the whole Overview TabItem), `PCSpecOverlay/ControlCenterWindow.xaml.cs` (Overview refresh code)
- Test: `PCSpecOverlay.Tests/ShellTests.cs` (append)

**Interfaces:**
- Consumes: `OverviewData` (Task 1); `TopTab` (Task 2); `Telemetry.FpsSource` (stage 2); `Log.Recent()`, `LogEntry`, `LogLevel`; existing `Temp(double?, double, double)`, `Banner(...)`, `BrowsersNeedingRestart()`, `Names(...)`, `OnStopOptimizations`, `OnResume`, `_app.History.Points`, `HistoryRecorder.Window`.
- Produces: Overview elements `TFps, TFpsSource, TFpsSub, TGpu, TGpuName, TGpuUnit, TGpuSub, GpuTicks, TCpu, TCpuSub, CpuTicks, TRam, TRamTotal, TRamSub, RamTicks, FpsChart, FpsChartRange, EventList, EventsEmpty, StatusLed, StatusLine, StopButton, ResumeButton, Banners`; methods `RefreshOverview`, `SetTicks`, `DrawFpsChart`, `ChartPoint`, `RefreshEvents`, `RefreshStatus`.

- [ ] **Step 1: Write the failing test**

Append to `PCSpecOverlay.Tests/ShellTests.cs`:

```csharp
public class OverviewSourceTests
{
    [Fact]
    public void Overview_has_the_instrument_parts_and_none_of_the_old_cards()
    {
        var xaml = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/ControlCenterWindow.xaml"));
        foreach (var name in new[] { "TFpsSource", "GpuTicks", "CpuTicks", "RamTicks", "EventList", "EventsEmpty", "StatusLed", "StatusLine", "FpsChart" })
            Assert.Contains($"x:Name=\"{name}\"", xaml);
        foreach (var gone in new[] { "TempChart", "GamingPill", "GamingSummary", "GamingDetails", "PowerPlan", "OwnedChanges", "LastSession", "StaticResource Tile}", "TileValue" })
            Assert.DoesNotContain(gone, xaml);
    }

    [Fact]
    public void Overview_code_uses_the_tested_helpers()
    {
        var code = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/ControlCenterWindow.xaml.cs"));
        Assert.Contains("OverviewData.FilledTicks(", code);
        Assert.Contains("OverviewData.Dips(", code);
        Assert.Contains("OverviewData.Events(", code);
        Assert.DoesNotContain("RefreshGamingCard", code);
        Assert.DoesNotContain("HotLine(", code);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~OverviewSourceTests"` — Expected: 2 failed.

- [ ] **Step 3: Window styles**

In `ControlCenterWindow.xaml` `<Window.Resources>`, delete the `Tile` and `TileValue` styles (keep `TileLabel`: the Optimizer page still uses it) and add:

```xml
        <!-- Overview: one ruled reading cell -->
        <Style x:Key="ReadingCell" TargetType="Border">
            <Setter Property="BorderBrush" Value="{DynamicResource Line}" />
            <Setter Property="BorderThickness" Value="0,0,1,0" />
            <Setter Property="Padding" Value="16,12,16,14" />
        </Style>
        <Style x:Key="ReadingKey" TargetType="TextBlock">
            <Setter Property="FontSize" Value="10.5" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="Foreground" Value="{DynamicResource Muted}" />
        </Style>
        <Style x:Key="ReadingValue" TargetType="TextBlock">
            <Setter Property="FontFamily" Value="Cascadia Mono, Consolas" />
            <Setter Property="FontSize" Value="30" />
            <Setter Property="Margin" Value="0,6,0,0" />
            <Setter Property="Text" Value="—" />
        </Style>
        <Style x:Key="ReadingUnit" TargetType="TextBlock">
            <Setter Property="FontSize" Value="12" />
            <Setter Property="Foreground" Value="{DynamicResource Muted}" />
            <Setter Property="VerticalAlignment" Value="Bottom" />
            <Setter Property="Margin" Value="4,0,0,6" />
        </Style>
        <Style x:Key="ReadingSub" TargetType="TextBlock">
            <Setter Property="FontFamily" Value="Cascadia Mono, Consolas" />
            <Setter Property="FontSize" Value="11" />
            <Setter Property="Foreground" Value="{DynamicResource Muted}" />
            <Setter Property="Margin" Value="0,4,0,0" />
            <Setter Property="TextTrimming" Value="CharacterEllipsis" />
        </Style>
        <Style x:Key="Ticks" TargetType="UniformGrid">
            <Setter Property="Columns" Value="10" />
            <Setter Property="Height" Value="6" />
            <Setter Property="Margin" Value="0,10,0,0" />
        </Style>
```

- [ ] **Step 4: Replace the Overview page**

Replace the whole Overview TabItem (from `<!-- ===================== OVERVIEW ===================== -->` through the `</TabItem>` that closes it, just before `<!-- ===================== OPTIMIZER ===================== -->`) with:

```xml
        <!-- ===================== OVERVIEW ===================== -->
        <TabItem Header="Overview" Tag="{StaticResource Icon.Overview}" Style="{StaticResource TopTab}">
            <DockPanel>
                <!-- Status line: what is running, power, overlay; Stop/Resume when relevant -->
                <Border DockPanel.Dock="Bottom" BorderBrush="{DynamicResource Line}" BorderThickness="0,1,0,0" Padding="16,6">
                    <DockPanel>
                        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                            <Button x:Name="StopButton" Content="Stop optimizations" Style="{StaticResource Btn}" Tag="Danger"
                                    Click="OnStopOptimizations" Padding="10,2" Margin="8,0,0,0" />
                            <Button x:Name="ResumeButton" Content="Resume" Style="{StaticResource Btn}" Tag="Primary"
                                    Click="OnResume" Visibility="Collapsed" Padding="10,2" Margin="8,0,0,0" />
                        </StackPanel>
                        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                            <Rectangle x:Name="StatusLed" Width="6" Height="6" Fill="{DynamicResource Good}" Margin="0,0,8,0" VerticalAlignment="Center" />
                            <TextBlock x:Name="StatusLine" FontFamily="Cascadia Mono, Consolas" FontSize="11"
                                       Foreground="{DynamicResource Muted}" TextTrimming="CharacterEllipsis" />
                        </StackPanel>
                    </DockPanel>
                </Border>

                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <StackPanel>
                        <!-- Banners: only shown when something needs attention -->
                        <StackPanel x:Name="Banners" Margin="16,12,16,0" />

                        <!-- Four readings in a ruled grid -->
                        <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1">
                            <UniformGrid Columns="4">
                                <Border Style="{StaticResource ReadingCell}">
                                    <StackPanel>
                                        <DockPanel>
                                            <TextBlock x:Name="TFpsSource" DockPanel.Dock="Right" Style="{StaticResource ReadingKey}" FontFamily="Cascadia Mono, Consolas" />
                                            <TextBlock Text="FRAME RATE" Style="{StaticResource ReadingKey}" />
                                        </DockPanel>
                                        <StackPanel Orientation="Horizontal">
                                            <TextBlock x:Name="TFps" Style="{StaticResource ReadingValue}" />
                                            <TextBlock Text="fps" Style="{StaticResource ReadingUnit}" />
                                        </StackPanel>
                                        <TextBlock x:Name="TFpsSub" Style="{StaticResource ReadingSub}" />
                                    </StackPanel>
                                </Border>
                                <Border Style="{StaticResource ReadingCell}">
                                    <StackPanel>
                                        <DockPanel>
                                            <TextBlock x:Name="TGpuName" DockPanel.Dock="Right" Style="{StaticResource ReadingKey}" />
                                            <TextBlock Text="GPU" Style="{StaticResource ReadingKey}" />
                                        </DockPanel>
                                        <StackPanel Orientation="Horizontal">
                                            <TextBlock x:Name="TGpu" Style="{StaticResource ReadingValue}" />
                                            <TextBlock x:Name="TGpuUnit" Text="°C" Style="{StaticResource ReadingUnit}" />
                                        </StackPanel>
                                        <TextBlock x:Name="TGpuSub" Style="{StaticResource ReadingSub}" />
                                        <UniformGrid x:Name="GpuTicks" Style="{StaticResource Ticks}" />
                                    </StackPanel>
                                </Border>
                                <Border Style="{StaticResource ReadingCell}">
                                    <StackPanel>
                                        <TextBlock Text="CPU" Style="{StaticResource ReadingKey}" />
                                        <StackPanel Orientation="Horizontal">
                                            <TextBlock x:Name="TCpu" Style="{StaticResource ReadingValue}" />
                                            <TextBlock Text="°C" Style="{StaticResource ReadingUnit}" />
                                        </StackPanel>
                                        <TextBlock x:Name="TCpuSub" Style="{StaticResource ReadingSub}" />
                                        <UniformGrid x:Name="CpuTicks" Style="{StaticResource Ticks}" />
                                    </StackPanel>
                                </Border>
                                <Border Style="{StaticResource ReadingCell}" BorderThickness="0">
                                    <StackPanel>
                                        <DockPanel>
                                            <TextBlock x:Name="TRamTotal" DockPanel.Dock="Right" Style="{StaticResource ReadingKey}" FontFamily="Cascadia Mono, Consolas" />
                                            <TextBlock Text="MEMORY" Style="{StaticResource ReadingKey}" />
                                        </DockPanel>
                                        <StackPanel Orientation="Horizontal">
                                            <TextBlock x:Name="TRam" Style="{StaticResource ReadingValue}" />
                                            <TextBlock Text="GB" Style="{StaticResource ReadingUnit}" />
                                        </StackPanel>
                                        <TextBlock x:Name="TRamSub" Style="{StaticResource ReadingSub}" />
                                        <UniformGrid x:Name="RamTicks" Style="{StaticResource Ticks}" />
                                    </StackPanel>
                                </Border>
                            </UniformGrid>
                        </Border>

                        <!-- Frame-rate chart and what the app did -->
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="1.7*" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,0,1,0" Padding="16,12,16,16">
                                <StackPanel>
                                    <DockPanel>
                                        <TextBlock x:Name="FpsChartRange" DockPanel.Dock="Right" Style="{StaticResource ReadingKey}" FontFamily="Cascadia Mono, Consolas" />
                                        <TextBlock Text="FRAME RATE · LAST 10 MINUTES" Style="{StaticResource ReadingKey}" />
                                    </DockPanel>
                                    <Canvas x:Name="FpsChart" Height="170" Margin="0,10,0,0" ClipToBounds="True" />
                                </StackPanel>
                            </Border>
                            <StackPanel Grid.Column="1" Margin="16,12,16,16">
                                <TextBlock Text="EVENTS" Style="{StaticResource ReadingKey}" Margin="0,0,0,8" />
                                <TextBlock x:Name="EventsEmpty" Style="{StaticResource Body}" Visibility="Collapsed"
                                           Text="Nothing yet. Gaming mode, Game Boost, optimizer changes and alerts appear here." />
                                <ItemsControl x:Name="EventList">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <Grid Margin="0,0,0,7">
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="46" />
                                                    <ColumnDefinition Width="*" />
                                                </Grid.ColumnDefinitions>
                                                <TextBlock Text="{Binding Time, StringFormat=HH:mm}" FontFamily="Cascadia Mono, Consolas" FontSize="11"
                                                           Foreground="{DynamicResource Muted}" Margin="0,1,0,0" />
                                                <TextBlock Grid.Column="1" Text="{Binding Message}" FontSize="12" TextWrapping="Wrap">
                                                    <TextBlock.Style>
                                                        <Style TargetType="TextBlock">
                                                            <Setter Property="Foreground" Value="{DynamicResource Text}" />
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding Level}" Value="Warning">
                                                                    <Setter Property="Foreground" Value="{DynamicResource Signal}" />
                                                                </DataTrigger>
                                                                <DataTrigger Binding="{Binding Level}" Value="Error">
                                                                    <Setter Property="Foreground" Value="{DynamicResource Hot}" />
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </TextBlock.Style>
                                                </TextBlock>
                                            </Grid>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        </Grid>
                    </StackPanel>
                </ScrollViewer>
            </DockPanel>
        </TabItem>
```

- [ ] **Step 5: Overview code in `ControlCenterWindow.xaml.cs`**

1. In `RefreshSlow`, delete the two lines that set `LastSession` (`var last = …;` and `LastSession.Text = …;`). Keep `SessionList` / `SessionsEmpty` (Settings page).
2. Delete the methods `RefreshGamingCard`, `DrawCharts`, `HotLine` and `OnGoToGaming` (the Overview no longer has their elements; `OnGoToGaming` was only used by the removed card's button).
3. Replace the whole `RefreshOverview` method with:

```csharp
    private void RefreshOverview()
    {
        var t = _engine.Latest;
        var s = _settings;

        TFps.Text = t.Fps is { } f ? $"{f.Fps:0}" : "—";
        TFpsSource.Text = t.FpsSource ?? "";
        TFpsSub.Text = t.Fps is { } ff ? $"{ff.FrameTimeMs:0.0} ms · 1% low {ff.OnePercentLow:0}"
            : t.FpsUnavailable ?? "No game or app rendering in front";

        var g = t.Gpu;
        TGpuName.Text = _engine.Gpu?.Name.Replace("NVIDIA ", "").Replace("GeForce ", "").Replace(" Laptop GPU", "") ?? "";
        TGpuUnit.Visibility = g.TempC is null ? Visibility.Collapsed : Visibility.Visible;
        TGpu.Text = g.TempC is { } gt ? $"{gt:0}" : g.Status switch { GpuStatus.Sleeping => "Sleeping", GpuStatus.NotPresent => "None", _ => "—" };
        TGpu.Foreground = Temp(g.TempC, s.GpuTempWarn, s.GpuTempHot);
        TGpuSub.Text = g.Status switch
        {
            GpuStatus.Ok => $"{g.Load ?? 0}% · {g.ClockMhz} MHz · {g.PowerW:0} W",
            GpuStatus.Sleeping => "Powered down to save battery",
            GpuStatus.NotPresent => "No NVIDIA GPU or driver found",
            _ => "Driver isn't responding; retrying",
        };
        SetTicks(GpuTicks, g.Status == GpuStatus.Ok ? g.Load / 100.0 : null);

        TCpu.Text = t.CpuTempC is { } ct ? $"{ct:0}" : "—";
        TCpu.Foreground = Temp(t.CpuTempC, s.CpuTempWarn, s.CpuTempHot);
        TCpuSub.Text = $"{t.CpuLoad:0}% · {t.CpuClockGhz:0.0} GHz";
        SetTicks(CpuTicks, t.CpuLoad / 100.0);

        TRam.Text = t.Ram is { } r ? $"{r.UsedGb:0.0}" : "—";
        TRamTotal.Text = t.Ram is { } rt ? $"{rt.TotalGb:0} GB" : "";
        TRamSub.Text = g.VramUsed is { } vu && g.VramTotal is { } vt ? $"VRAM {vu / 1073741824.0:0.0} / {vt / 1073741824.0:0} GB" : "";
        SetTicks(RamTicks, t.Ram is { TotalGb: > 0 } ru ? ru.UsedGb / ru.TotalGb : null);

        DrawFpsChart();
        RefreshEvents();
        RefreshStatus();
        RefreshBanners();
    }

    /// <summary>Fills the first N of a reading's ten segments in the text color; the rest stay as divider lines.</summary>
    private static void SetTicks(UniformGrid host, double? fraction)
    {
        if (host.Children.Count == 0)
            for (var i = 0; i < OverviewData.TickCount; i++) host.Children.Add(new Rectangle { Margin = new Thickness(0, 0, 2, 0) });
        var filled = OverviewData.FilledTicks(fraction);
        for (var i = 0; i < host.Children.Count; i++)
            ((Rectangle)host.Children[i]).SetResourceReference(Shape.FillProperty, i < filled ? "Text" : "Line");
    }

    private void RefreshEvents()
    {
        var events = OverviewData.Events(Log.Recent(), 8);
        EventList.ItemsSource = events;
        EventsEmpty.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>One line at the bottom: what is running, power, and the overlay; Stop/Resume when they apply.</summary>
    private void RefreshStatus()
    {
        var gm = _engine.Gaming;
        string state, led;
        if (_engine.RecoveryMode) { state = "Recovery mode: automatic optimizations are off"; led = "Signal"; }
        else if (_engine.OptimizationsPaused) { state = "Optimizations paused"; led = "Signal"; }
        else if (gm.Active) { state = $"Gaming mode on for {string.Join(", ", gm.ActiveGames)}"; led = "Good"; }
        else if (_settings.AutoGamingMode) { state = "Gaming mode waiting for a game"; led = "Good"; }
        else { state = "Monitoring"; led = "Good"; }
        StatusLed.SetResourceReference(Shape.FillProperty, led);

        var power = PowerMode.Name(PowerMode.Current()) + (_engine.Journal.IsOwnedBy(SettingCatalog.Power.Id, GamingMode.Owner) ? " (gaming mode)" : "");
        var source = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline ? "On battery" : "Plugged in";
        var overlay = _app.OverlayVisible ? $"Overlay visible · {_app.HotkeyInUse ?? "no shortcut"}" : "Overlay hidden";
        StatusLine.Text = string.Join("  ·  ", state, source, power, overlay);

        var somethingActive = gm.Active || _engine.Boost.Active || _engine.Journal.Entries.Any(e => e.Owners.Contains(GamingMode.Owner));
        StopButton.Visibility = !_engine.OptimizationsPaused && somethingActive ? Visibility.Visible : Visibility.Collapsed;
        ResumeButton.Visibility = _engine.OptimizationsPaused || _engine.RecoveryMode ? Visibility.Visible : Visibility.Collapsed;
    }
```

4. In `RefreshBanners`, after the browser-restart banner, add the gaming-mode notices that the removed card used to list:

```csharp
        foreach (var notice in _engine.Gaming.Notices)
            Banners.Children.Add(Banner("Gaming mode", notice, null, null, warn: true));
```

5. Replace the `// ---- charts ----` section's remaining `Plot` method (and add the new chart code) so the section reads:

```csharp
    // ---- charts ----

    /// <summary>Frame rate over the history window on a light grid, with marked dips.</summary>
    private void DrawFpsChart()
    {
        var points = _app.History.Points.ToList();
        var fps = points.Select(p => p.Fps).ToList();
        var real = fps.Where(v => v is > 0).Select(v => v!.Value).ToList();
        var top = Math.Max(60, Math.Ceiling((real.Count > 0 ? real.Max() : 0) * 1.15 / 30) * 30);
        FpsChartRange.Text = real.Count > 0 ? $"avg {real.Average():0}  low {real.Min():0}" : "";

        FpsChart.Children.Clear();
        double w = FpsChart.ActualWidth, h = FpsChart.ActualHeight;
        if (w <= 0) return;
        for (var i = 1; i < 4; i++) AddGridLine(0, h * i / 4, w, h * i / 4);
        for (var i = 1; i < 8; i++) AddGridLine(w * i / 8, 0, w * i / 8, h);

        Plot(FpsChart, points, p => p.Fps, 0, top, (Brush)FindResource("Text"), clear: false);
        foreach (var i in OverviewData.Dips(fps))
        {
            var p = ChartPoint(points, i, fps[i]!.Value, 0, top, w, h);
            var dot = new Ellipse { Width = 6, Height = 6 };
            dot.SetResourceReference(Shape.FillProperty, "Signal");
            Canvas.SetLeft(dot, p.X - 3);
            Canvas.SetTop(dot, p.Y - 3);
            FpsChart.Children.Add(dot);
        }
    }

    private void AddGridLine(double x1, double y1, double x2, double y2)
    {
        var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, StrokeThickness = 1 };
        line.SetResourceReference(Shape.StrokeProperty, "Line2");
        FpsChart.Children.Add(line);
    }

    /// <summary>Where point <paramref name="index"/> sits on a chart of the history window (newest at the right).</summary>
    private static Point ChartPoint(List<HistoryPoint> points, int index, double value, double min, double max, double w, double h)
    {
        var x = w - (points[^1].Time - points[index].Time).TotalSeconds / HistoryRecorder.Window.TotalSeconds * w;
        var y = h - (Math.Clamp(value, min, max) - min) / (max - min) * h;
        return new Point(x, y);
    }

    private static void Plot(Canvas canvas, List<HistoryPoint> points, Func<HistoryPoint, double?> value, double min, double max, Brush brush, bool clear)
    {
        if (clear) canvas.Children.Clear();
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        if (w <= 0 || points.Count < 2) return;
        var line = new Polyline { Stroke = brush, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
        for (var i = 0; i < points.Count; i++)
        {
            if (value(points[i]) is not { } v) { if (line.Points.Count > 0) { canvas.Children.Add(line); line = new Polyline { Stroke = brush, StrokeThickness = 1.6 }; } continue; }
            line.Points.Add(ChartPoint(points, i, v, min, max, w, h));
        }
        if (line.Points.Count > 0) canvas.Children.Add(line);
    }
```

(The file already has `using System.Windows.Shapes;` and `using System.Windows.Controls.Primitives;`.)

- [ ] **Step 6: Build, run the new tests and the suite**

Run: `dotnet build PCSpec.slnx` — Expected: 0 errors, 0 warnings; no remaining references to the removed elements or methods (`grep -n "LastSession\|RefreshGamingCard\|DrawCharts\|HotLine\|OnGoToGaming\|TempChart" PCSpecOverlay/ControlCenterWindow.xaml*` prints nothing).
Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~OverviewSourceTests"` — Expected: 2 passed.
Run: `dotnet test PCSpec.slnx` — Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add PCSpecOverlay/ControlCenterWindow.xaml PCSpecOverlay/ControlCenterWindow.xaml.cs PCSpecOverlay.Tests/ShellTests.cs
git commit -F- <<'EOF'
Rebuild the Overview as ruled readings, a dip-marked chart, events and a status line

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## After the tasks: on-PC verification (controller, with the user)

Run a Release development build (`PCSpecOverlay/bin/Release/net8.0-windows/PCSpecOverlay.exe`; separate `-Dev` data, overlay moved to the top-left). Record results in `PCSpecOverlay/docs/AUDIT.md` under "Instrument stage 3":

1. Tab bar: seven tabs with icons, the selected one underlined; app name on the left and version + sun/moon on the right never overlap the tabs at the minimum width (900 px) or at full width.
2. Sun/moon switch: flips the theme instantly, the Settings page's Theme dropdown shows the new value, and with Match Windows chosen there the switch shows the theme in use.
3. Overview in dark and light (screenshots compared with the picked mockup): four ruled readings with tick bars, chart with grid and amber dip dots, events newest first (warnings amber), status line with the green light.
4. Banners still appear (e.g. FPS unavailable) and Stop/Resume optimizations still work from the status line while gaming mode is on.
5. Screen-reader names: the two switch buttons are named; tab names are Overview … Diagnostics.
