# Instrument Redesign, Stage 2 (Overlay) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the in-game overlay in the Instrument style: Strip (one ruled line, the new default) and Panel (full readout with a live frame-time trace), with its own theme setting (Same as app / Always dark / Always light, default Always dark) and every overlay color taken from theme tokens.

**Architecture:** The overlay gets its own pair of color dictionaries (`Themes/Overlay.Dark.xaml`, `Themes/Overlay.Light.xaml`) merged into the overlay window only, so the app theme never recolors it unless the user picks "Same as app". The window swaps that one dictionary when the resolved overlay theme changes. Colors set from code use `SetResourceReference`, so a swap recolors them too. A small `FrameTimeTrace` ring buffer records the worst frame time of each FPS update (new `FpsSample.WorstFrameMs`) and turns it into polyline points. Stored layout values stay `Compact`/`Expanded`; only the names shown change (Strip/Panel).

**Tech Stack:** .NET 8 WPF (C# 12), xUnit 2.9.

**Spec:** `docs/superpowers/specs/2026-10-24-instrument-redesign-design.md` (sections 1, 2 "overlay setting", 3 "In-game overlay", and Build order stage 2). Stage 1 (merged) provides `AppTheme`, `ThemeResolver`, `ThemeManager` (`Current`, `Changed`), `Settings.Light/Dark/MatchWindows`, and the test helpers `Sta.Run`, `SourceFiles.Path`, `XamlFile.LoadDictionary`, `Contrast.Ratio`.

## Global Constraints

- Layout values stored in settings stay `Compact` (shown as **Strip**) and `Expanded` (shown as **Panel**). Strip is the default for fresh installs; a saved `Expanded` is kept (never overwrite a saved choice).
- `OverlayTheme` values: `SameAsApp`, `AlwaysDark`, `AlwaysLight`; default `AlwaysDark`.
- Warning values in `Signal` (amber), hot values in `Hot` (red); otherwise monochrome. No brand color, no green.
- Numbers in Cascadia Mono (fallback Consolas); labels in Segoe UI. Square corners, 1 px lines, no shadows. No emojis.
- Every readable overlay text color reaches 4.5:1 on the overlay's opaque background color.
- Existing behavior is unchanged: metric switches, opacity, size, corner/monitor, click-through drag-and-snap, per-game layout override, slowdown chips, gaming-mode/pause/boost badge, GPU-sleep message, hint when FPS is unavailable.
- Build: `dotnet build PCSpec.slnx`. Tests: `dotnet test PCSpec.slnx` (135 passing before this stage). Shell examples use Git Bash from the repo root.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## File map

| File | Status | Responsibility |
|---|---|---|
| `PCSpecOverlay/Settings.cs` | modify | `OverlayTheme` setting; Strip (`Compact`) default; invalid layout → `Compact` |
| `PCSpecOverlay/Core/ThemeResolver.cs` | modify | `ResolveOverlay(string overlayTheme, AppTheme appTheme)` |
| `PCSpecOverlay/Monitors/FrameStats.cs` | modify | `FpsSample.WorstFrameMs` |
| `PCSpecOverlay/Core/Engine.cs` | modify | `Telemetry.FpsSource` ("DXGI" / "KERNEL") |
| `PCSpecOverlay/Monitors/FrameTimeTrace.cs` | create | Fixed-size ring buffer of frame times → polyline points |
| `PCSpecOverlay/Themes/Overlay.Dark.xaml`, `Overlay.Light.xaml` | create | Overlay color tokens |
| `PCSpecOverlay/OverlayWindow.xaml`, `OverlayWindow.xaml.cs` | rewrite | Strip and Panel layouts, token colors, theme swap, trace |
| `PCSpecOverlay/ControlCenterWindow.xaml` | modify | Strip/Panel names; Overlay theme dropdown |
| `PCSpecOverlay/App.xaml.cs` | modify | Tell the overlay the app theme |
| `PCSpecOverlay.Tests/LogicTests.cs` | modify | Invalid layout now falls back to Strip |
| `PCSpecOverlay.Tests/OverlayTests.cs` | create | All tests for this stage |

---

### Task 1: Overlay settings and theme resolution

**Files:**
- Modify: `PCSpecOverlay/Settings.cs`, `PCSpecOverlay/Core/ThemeResolver.cs`, `PCSpecOverlay.Tests/LogicTests.cs:209`
- Test: `PCSpecOverlay.Tests/OverlayTests.cs` (create)

**Interfaces:**
- Consumes: `AppTheme` (stage 1).
- Produces: `Settings.SameAsApp`, `Settings.AlwaysDark`, `Settings.AlwaysLight` (const string); `Settings.OverlayTheme` (string, default `AlwaysDark`); `Settings.Layout` default `Compact`; `ThemeResolver.ResolveOverlay(string overlayTheme, AppTheme appTheme) : AppTheme`.

- [ ] **Step 1: Write the failing tests**

Create `PCSpecOverlay.Tests/OverlayTests.cs`:

```csharp
using System.Text.Json;
using System.Windows;
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
```

In `PCSpecOverlay.Tests/LogicTests.cs`, in `SettingsTests.Out_of_range_values_are_clamped`, change the last assertion to:

```csharp
        Assert.Equal(Settings.Compact, s.Layout); // an unknown layout falls back to the default, Strip
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~OverlaySettingTests|FullyQualifiedName~SettingsTests"`
Expected: build error `'Settings' does not contain a definition for 'AlwaysDark'`.

- [ ] **Step 3: Implement**

In `PCSpecOverlay/Settings.cs`:

Below the `Light`/`Dark`/`MatchWindows` constant line add:

```csharp
    public const string SameAsApp = "SameAsApp", AlwaysDark = "AlwaysDark", AlwaysLight = "AlwaysLight";
```

Change the layout property to:

```csharp
    /// <summary>Compact is shown as "Strip" (one line, the default); Expanded as "Panel".</summary>
    public string Layout { get; set; } = Compact;
```

In the `// Appearance` group, after `Appearance`, add:

```csharp
    /// <summary>In-game overlay theme: SameAsApp, AlwaysDark (default; reads best over games) or AlwaysLight.</summary>
    public string OverlayTheme { get; set; } = AlwaysDark;
```

In `Validate()`, replace `if (Layout != Compact) Layout = Expanded;` with:

```csharp
        if (Layout is not (Compact or Expanded)) Layout = Compact;
```

and after the `Appearance` validation line add:

```csharp
        if (OverlayTheme is not (SameAsApp or AlwaysDark or AlwaysLight)) OverlayTheme = AlwaysDark;
```

In `PCSpecOverlay/Core/ThemeResolver.cs`, add inside `ThemeResolver` after `Resolve`:

```csharp
    /// <summary>The overlay's theme: its own choice, or the app's when set to "Same as app".</summary>
    public static AppTheme ResolveOverlay(string overlayTheme, AppTheme appTheme) => overlayTheme switch
    {
        Settings.AlwaysLight => AppTheme.Light,
        Settings.SameAsApp => appTheme,
        _ => AppTheme.Dark,
    };
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~OverlaySettingTests|FullyQualifiedName~SettingsTests"`
Expected: all pass (13 in `OverlaySettingTests` plus the existing `SettingsTests`).

- [ ] **Step 5: Commit**

```bash
git add PCSpecOverlay/Settings.cs PCSpecOverlay/Core/ThemeResolver.cs PCSpecOverlay.Tests/OverlayTests.cs PCSpecOverlay.Tests/LogicTests.cs
git commit -F- <<'EOF'
Add the overlay theme setting and make Strip the default layout

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: Worst frame time and FPS source

**Files:**
- Modify: `PCSpecOverlay/Monitors/FrameStats.cs`, `PCSpecOverlay/Core/Engine.cs`
- Test: `PCSpecOverlay.Tests/OverlayTests.cs` (append)

**Interfaces:**
- Produces: `FpsSample(double Fps, double FrameTimeMs, double OnePercentLow, double WorstFrameMs = 0)` — `WorstFrameMs` is the longest frame interval inside the 1 s FPS window; `Telemetry.FpsSource : string?` ("DXGI", "KERNEL", or null).

- [ ] **Step 1: Write the failing tests**

Append to `PCSpecOverlay.Tests/OverlayTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~WorstFrameTests"`
Expected: build error `'FpsSample' does not contain a definition for 'WorstFrameMs'`.

- [ ] **Step 3: Implement**

In `PCSpecOverlay/Monitors/FrameStats.cs` replace the record line with:

```csharp
public readonly record struct FpsSample(double Fps, double FrameTimeMs, double OnePercentLow, double WorstFrameMs = 0);
```

Add a bullet to the class summary after the 1% low item:

```csharp
/// <item><b>Worst frame</b>: the longest interval among those frames — a stutter shows up here even when the average hides it.</item>
```

In `Compute`, directly before `scratch.Clear();`, add:

```csharp
        var worst = 0.0;
        for (var i = first + 1; i < presents.Count; i++) worst = Math.Max(worst, presents[i] - presents[i - 1]);
```

and change the return to:

```csharp
        return new FpsSample(fps, span / frames, low, worst);
```

In `PCSpecOverlay/Core/Engine.cs`, add to `Telemetry` after `FpsUnavailable`:

```csharp
    /// <summary>Where the frame rate comes from: "DXGI" (DirectX) or "KERNEL" (graphics kernel: Vulkan, OpenGL).</summary>
    public string? FpsSource { get; init; }
```

In `Engine.SampleFps`, change `Latest = Latest with { Fps = null, FpsUnavailable = "Needs admin or Performance Log Users" };` to also clear the source:

```csharp
            Latest = Latest with { Fps = null, FpsSource = null, FpsUnavailable = "Needs admin or Performance Log Users" };
```

and change `Latest = Latest with { Fps = sample, FpsUnavailable = null };` to:

```csharp
        var source = sample is null ? null : Fps.LastSource switch
        {
            FpsMonitor.Source.DirectX => "DXGI",
            FpsMonitor.Source.Kernel => "KERNEL",
            _ => null,
        };
        Latest = Latest with { Fps = sample, FpsSource = source, FpsUnavailable = null };
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~WorstFrameTests|FullyQualifiedName~FrameStatsTests"`
Expected: all pass (3 new + existing FrameStats tests).

- [ ] **Step 5: Commit**

```bash
git add PCSpecOverlay/Monitors/FrameStats.cs PCSpecOverlay/Core/Engine.cs PCSpecOverlay.Tests/OverlayTests.cs
git commit -F- <<'EOF'
Measure the worst frame per second and report the FPS source

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 3: Frame-time trace

**Files:**
- Create: `PCSpecOverlay/Monitors/FrameTimeTrace.cs`
- Test: `PCSpecOverlay.Tests/OverlayTests.cs` (append)

**Interfaces:**
- Produces: `FrameTimeTrace(int capacity = 60)` (capacity ≥ 2); `int Capacity`; `int Count`; `void Add(double? ms)`; `IReadOnlyList<double?> Values()` (oldest first); `List<Point> Plot(double width, double height)`; `const double MinCeilingMs = 33.4`.

- [ ] **Step 1: Write the failing tests**

Append to `PCSpecOverlay.Tests/OverlayTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~FrameTimeTraceTests"`
Expected: build error `The type or namespace name 'FrameTimeTrace' could not be found`.

- [ ] **Step 3: Implement**

Create `PCSpecOverlay/Monitors/FrameTimeTrace.cs`:

```csharp
using System.Windows;

namespace PCSpecOverlay.Monitors;

/// <summary>
/// The overlay's frame-time line: the worst frame of each FPS update, newest on the right. Fixed size,
/// so it never grows. The scale stretches to fit the biggest spike but never zooms in past 33.4 ms
/// (30 FPS), so a smooth game draws a calm, low line and a stutter stands out.
/// </summary>
public sealed class FrameTimeTrace
{
    public const double MinCeilingMs = 33.4;
    private const double Headroom = 1.15;

    private readonly double?[] _values;
    private int _next;

    public FrameTimeTrace(int capacity = 60)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        _values = new double?[capacity];
    }

    public int Capacity => _values.Length;
    public int Count { get; private set; }

    /// <param name="ms">Frame time in milliseconds; null, NaN, infinite or non-positive leave a gap.</param>
    public void Add(double? ms)
    {
        _values[_next] = ms is { } v && double.IsFinite(v) && v > 0 ? v : null;
        _next = (_next + 1) % Capacity;
        if (Count < Capacity) Count++;
    }

    /// <summary>The stored values, oldest first.</summary>
    public IReadOnlyList<double?> Values()
    {
        var list = new List<double?>(Count);
        for (var i = 0; i < Count; i++) list.Add(_values[(_next - Count + i + Capacity) % Capacity]);
        return list;
    }

    /// <summary>Points for a polyline in a width × height box (y grows downward); gaps are skipped.</summary>
    public List<Point> Plot(double width, double height)
    {
        var points = new List<Point>();
        if (width <= 0 || height <= 0) return points;
        var values = Values();
        var max = values.Max() ?? 0;
        if (max <= 0) return points;
        var ceiling = Math.Max(MinCeilingMs, max * Headroom);
        var step = width / (Capacity - 1);
        var offset = Capacity - values.Count; // a partly filled trace starts part-way across, ending at the right edge
        for (var i = 0; i < values.Count; i++)
            if (values[i] is { } v)
                points.Add(new Point((offset + i) * step, height - Math.Min(v / ceiling, 1) * height));
        return points;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~FrameTimeTraceTests"`
Expected: 7 passed.

- [ ] **Step 5: Commit**

```bash
git add PCSpecOverlay/Monitors/FrameTimeTrace.cs PCSpecOverlay.Tests/OverlayTests.cs
git commit -F- <<'EOF'
Add a fixed-size frame-time trace for the overlay

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 4: Overlay color dictionaries

**Files:**
- Create: `PCSpecOverlay/Themes/Overlay.Dark.xaml`, `PCSpecOverlay/Themes/Overlay.Light.xaml`
- Test: `PCSpecOverlay.Tests/OverlayTests.cs` (append)

**Interfaces:**
- Produces brush keys (both files): `O.Bg` (translucent panel), `O.Solid` (the same color, opaque), `O.Line`, `O.Line2`, `O.Text`, `O.Muted`, `O.Signal`, `O.Hot`, `O.SignalSoft`, `O.HotSoft`, `O.Track`.

- [ ] **Step 1: Write the failing tests**

Append to `PCSpecOverlay.Tests/OverlayTests.cs`:

```csharp
public class OverlayColorTests
{
    private static readonly string[] Keys =
        ["O.Bg", "O.Solid", "O.Line", "O.Line2", "O.Text", "O.Muted", "O.Signal", "O.Hot", "O.SignalSoft", "O.HotSoft", "O.Track"];

    private static Color C(ResourceDictionary d, string key) => ((SolidColorBrush)d[key]).Color;

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
        Assert.InRange(bg.A, 0xD0, 0xF0); // see-through enough to sit on a game, solid enough to read
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~OverlayColorTests"`
Expected: FAIL with `FileNotFoundException` for `Overlay.Dark.xaml`.

- [ ] **Step 3: Create the dictionaries**

Create `PCSpecOverlay/Themes/Overlay.Dark.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- In-game overlay, dark. Merged into the overlay window only; same keys as Overlay.Light.xaml. -->
    <SolidColorBrush x:Key="O.Bg" Color="#DB0B0B0C" />
    <SolidColorBrush x:Key="O.Solid" Color="#0B0B0C" />
    <SolidColorBrush x:Key="O.Line" Color="#2EFFFFFF" />
    <SolidColorBrush x:Key="O.Line2" Color="#17FFFFFF" />
    <SolidColorBrush x:Key="O.Text" Color="#ECECE8" />
    <SolidColorBrush x:Key="O.Muted" Color="#9A9A9C" />
    <SolidColorBrush x:Key="O.Signal" Color="#E8A33D" />
    <SolidColorBrush x:Key="O.Hot" Color="#E5534B" />
    <SolidColorBrush x:Key="O.SignalSoft" Color="#33E8A33D" />
    <SolidColorBrush x:Key="O.HotSoft" Color="#33E5534B" />
    <SolidColorBrush x:Key="O.Track" Color="#24FFFFFF" />
</ResourceDictionary>
```

Create `PCSpecOverlay/Themes/Overlay.Light.xaml` with the same structure (comment says "light") and these values:

```xml
    <SolidColorBrush x:Key="O.Bg" Color="#EBFAF9F6" />
    <SolidColorBrush x:Key="O.Solid" Color="#FAF9F6" />
    <SolidColorBrush x:Key="O.Line" Color="#29000000" />
    <SolidColorBrush x:Key="O.Line2" Color="#14000000" />
    <SolidColorBrush x:Key="O.Text" Color="#141414" />
    <SolidColorBrush x:Key="O.Muted" Color="#5F5F59" />
    <SolidColorBrush x:Key="O.Signal" Color="#955C00" />
    <SolidColorBrush x:Key="O.Hot" Color="#B8322B" />
    <SolidColorBrush x:Key="O.SignalSoft" Color="#2E955C00" />
    <SolidColorBrush x:Key="O.HotSoft" Color="#2EB8322B" />
    <SolidColorBrush x:Key="O.Track" Color="#1F000000" />
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~OverlayColorTests"`
Expected: 6 passed. If a contrast assertion fails, report the pair and ratio; do not change values.

- [ ] **Step 5: Commit**

```bash
git add PCSpecOverlay/Themes/Overlay.Dark.xaml PCSpecOverlay/Themes/Overlay.Light.xaml PCSpecOverlay.Tests/OverlayTests.cs
git commit -F- <<'EOF'
Add dark and light color tokens for the in-game overlay

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 5: Rebuild the overlay window (Strip and Panel)

**Files:**
- Rewrite: `PCSpecOverlay/OverlayWindow.xaml`, `PCSpecOverlay/OverlayWindow.xaml.cs`
- Test: `PCSpecOverlay.Tests/OverlayTests.cs` (append)

**Interfaces:**
- Consumes: `Settings.OverlayTheme`, `Settings.Compact/Expanded` (Task 1); `ThemeResolver.ResolveOverlay` (Task 1); `Telemetry.FpsSource`, `FpsSample.WorstFrameMs` (Task 2); `FrameTimeTrace` (Task 3); `O.*` keys (Task 4).
- Produces: unchanged public surface (`Handle`, `Moved`, `EnsureHandle`, `ApplySettings(Settings, string?)`, `Reposition`, `KeepOnTop`, `SetGpuName`, `ShowStatus`, `ShowFps`, `ShowStats`) plus `void SetAppTheme(AppTheme theme)`.

- [ ] **Step 1: Write the failing tests (static checks on the source)**

Append to `PCSpecOverlay.Tests/OverlayTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~OverlaySourceTests"`
Expected: 2 failed (hex colors, CornerRadius, `Frozen(` present).

- [ ] **Step 3: Replace `OverlayWindow.xaml`**

Replace the whole file with:

```xml
<Window x:Class="PCSpecOverlay.OverlayWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="PC Spec Overlay"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ShowActivated="False" ResizeMode="NoResize"
        SizeToContent="WidthAndHeight" Focusable="False"
        UseLayoutRounding="True" SnapsToDevicePixels="True"
        TextOptions.TextFormattingMode="Display" TextOptions.TextRenderingMode="Grayscale">

    <Window.Resources>
        <ResourceDictionary>
            <!-- The overlay's own colors (O.*), swapped by OverlayWindow.ApplyTheme; never the app's colors -->
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Themes/Overlay.Dark.xaml" />
            </ResourceDictionary.MergedDictionaries>

            <Style x:Key="Label" TargetType="TextBlock">
                <Setter Property="FontFamily" Value="Segoe UI" />
                <Setter Property="FontWeight" Value="SemiBold" />
                <Setter Property="FontSize" Value="9.5" />
                <Setter Property="Foreground" Value="{DynamicResource O.Muted}" />
                <Setter Property="VerticalAlignment" Value="Center" />
                <Setter Property="Width" Value="34" />
            </Style>
            <Style x:Key="Num" TargetType="TextBlock">
                <Setter Property="FontFamily" Value="Cascadia Mono, Consolas" />
                <Setter Property="FontSize" Value="13" />
                <Setter Property="Foreground" Value="{DynamicResource O.Text}" />
                <Setter Property="TextAlignment" Value="Right" />
                <Setter Property="Text" Value="—" />
            </Style>
            <Style x:Key="Unit" TargetType="TextBlock">
                <Setter Property="FontFamily" Value="Segoe UI" />
                <Setter Property="FontSize" Value="9.5" />
                <Setter Property="Foreground" Value="{DynamicResource O.Muted}" />
                <Setter Property="Margin" Value="2,0,9,1" />
                <Setter Property="VerticalAlignment" Value="Bottom" />
            </Style>
            <Style x:Key="State" TargetType="TextBlock">
                <Setter Property="FontFamily" Value="Segoe UI" />
                <Setter Property="FontSize" Value="11" />
                <Setter Property="FontStyle" Value="Italic" />
                <Setter Property="Foreground" Value="{DynamicResource O.Muted}" />
                <Setter Property="Visibility" Value="Collapsed" />
            </Style>
            <Style x:Key="BarTrack" TargetType="Border">
                <Setter Property="Height" Value="2" />
                <Setter Property="Background" Value="{DynamicResource O.Track}" />
                <Setter Property="Margin" Value="0,4,0,0" />
            </Style>
            <Style x:Key="BarFill" TargetType="Border">
                <Setter Property="Background" Value="{DynamicResource O.Text}" />
                <Setter Property="RenderTransformOrigin" Value="0,0.5" />
            </Style>
            <Style x:Key="Cell" TargetType="Border">
                <Setter Property="BorderBrush" Value="{DynamicResource O.Line2}" />
                <Setter Property="BorderThickness" Value="0,0,1,0" />
                <Setter Property="Padding" Value="9,4,9,5" />
            </Style>
            <Style x:Key="Badge" TargetType="Border">
                <Setter Property="Background" Value="{DynamicResource O.Text}" />
                <Setter Property="Padding" Value="5,0,5,1" />
                <Setter Property="VerticalAlignment" Value="Center" />
                <Setter Property="Visibility" Value="Collapsed" />
            </Style>
            <Style x:Key="BadgeText" TargetType="TextBlock">
                <Setter Property="FontFamily" Value="Segoe UI" />
                <Setter Property="FontWeight" Value="SemiBold" />
                <Setter Property="FontSize" Value="8.5" />
                <Setter Property="Foreground" Value="{DynamicResource O.Solid}" />
            </Style>
        </ResourceDictionary>
    </Window.Resources>

    <Border x:Name="Frame" Background="{DynamicResource O.Bg}" BorderBrush="{DynamicResource O.Line}" BorderThickness="1">
        <Border.LayoutTransform>
            <ScaleTransform x:Name="PanelScale" ScaleX="1" ScaleY="1" />
        </Border.LayoutTransform>
        <Grid>
            <!-- ============ Panel: the full readout (stored as "Expanded") ============ -->
            <StackPanel x:Name="ExpandedLayout" MinWidth="220" Visibility="Collapsed">
                <DockPanel Margin="10,5,10,5">
                    <Border x:Name="Badge" DockPanel.Dock="Right" Style="{StaticResource Badge}" Margin="8,0,0,0">
                        <TextBlock x:Name="BadgeText" Style="{StaticResource BadgeText}" />
                    </Border>
                    <TextBlock x:Name="FpsSource" DockPanel.Dock="Right" Style="{StaticResource Label}" Width="Auto"
                               FontFamily="Cascadia Mono, Consolas" FontWeight="Normal" Margin="8,0,0,0" />
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="PC SPEC" Style="{StaticResource Label}" Width="Auto" />
                        <TextBlock x:Name="GpuName" Style="{StaticResource Label}" Width="Auto" FontWeight="Normal" Margin="8,0,0,0" />
                    </StackPanel>
                </DockPanel>
                <Border Height="1" Background="{DynamicResource O.Line}" />

                <Grid Margin="10,7,10,5">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock x:Name="FpsValue" Style="{StaticResource Num}" FontSize="32" MinWidth="58" TextAlignment="Left" />
                        <TextBlock Text="FPS" Style="{StaticResource Unit}" Margin="5,0,0,5" />
                    </StackPanel>
                    <StackPanel Grid.Column="1" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="12,0,0,0">
                        <StackPanel x:Name="FrameTimeRow" Orientation="Horizontal" HorizontalAlignment="Right">
                            <TextBlock x:Name="FrameTime" Style="{StaticResource Num}" FontSize="11.5" />
                            <TextBlock Text="ms" Style="{StaticResource Unit}" Margin="3,0,0,1" />
                        </StackPanel>
                        <StackPanel x:Name="LowRow" Orientation="Horizontal" HorizontalAlignment="Right">
                            <TextBlock Text="1% " Style="{StaticResource Unit}" Margin="0,0,0,1" />
                            <TextBlock x:Name="LowFps" Style="{StaticResource Num}" FontSize="11.5" />
                        </StackPanel>
                    </StackPanel>
                </Grid>
                <TextBlock x:Name="FpsHint" Visibility="Collapsed" FontFamily="Segoe UI" FontSize="9.5" TextWrapping="Wrap"
                           MaxWidth="220" Foreground="{DynamicResource O.Muted}" Margin="10,0,10,5" />

                <!-- Worst frame of each update, newest on the right: spikes are stutters -->
                <Border x:Name="TraceArea" Height="24" BorderBrush="{DynamicResource O.Line2}" BorderThickness="0,1,0,1">
                    <Canvas x:Name="TraceCanvas" ClipToBounds="True">
                        <Polyline x:Name="TraceLine" Stroke="{DynamicResource O.Text}" StrokeThickness="1" StrokeLineJoin="Round" />
                    </Canvas>
                </Border>

                <StackPanel Margin="10,6,10,8">
                    <DockPanel x:Name="GpuRow" Margin="0,0,0,5">
                        <TextBlock Text="GPU" Style="{StaticResource Label}" VerticalAlignment="Top" Margin="0,1,8,0" />
                        <StackPanel>
                            <StackPanel x:Name="GpuValues" Orientation="Horizontal">
                                <TextBlock x:Name="GpuTemp" Style="{StaticResource Num}" MinWidth="22" />
                                <TextBlock Text="°C" Style="{StaticResource Unit}" />
                                <TextBlock x:Name="GpuLoad" Style="{StaticResource Num}" MinWidth="22" />
                                <TextBlock Text="%" Style="{StaticResource Unit}" />
                                <TextBlock x:Name="GpuClock" Style="{StaticResource Num}" MinWidth="34" />
                                <TextBlock Text="MHz" Style="{StaticResource Unit}" />
                                <TextBlock x:Name="GpuPower" Style="{StaticResource Num}" MinWidth="18" />
                                <TextBlock Text="W" Style="{StaticResource Unit}" Margin="2,0,0,1" />
                            </StackPanel>
                            <TextBlock x:Name="GpuState" Style="{StaticResource State}" />
                            <Border x:Name="GpuTrack" Style="{StaticResource BarTrack}">
                                <Border x:Name="GpuBar" Style="{StaticResource BarFill}">
                                    <Border.RenderTransform><ScaleTransform ScaleX="0" /></Border.RenderTransform>
                                </Border>
                            </Border>
                        </StackPanel>
                    </DockPanel>

                    <DockPanel x:Name="VramRow" Margin="0,0,0,5">
                        <TextBlock Text="VRAM" Style="{StaticResource Label}" Margin="0,0,8,0" />
                        <StackPanel Orientation="Horizontal">
                            <TextBlock x:Name="VramUsed" Style="{StaticResource Num}" MinWidth="26" />
                            <TextBlock x:Name="VramTotal" Style="{StaticResource Unit}" Text="/ — GB" Margin="4,0,0,1" />
                        </StackPanel>
                    </DockPanel>

                    <DockPanel x:Name="CpuRow" Margin="0,0,0,5">
                        <TextBlock Text="CPU" Style="{StaticResource Label}" VerticalAlignment="Top" Margin="0,1,8,0" />
                        <StackPanel>
                            <StackPanel Orientation="Horizontal">
                                <TextBlock x:Name="CpuTemp" Style="{StaticResource Num}" MinWidth="22" />
                                <TextBlock Text="°C" Style="{StaticResource Unit}" />
                                <TextBlock x:Name="CpuLoad" Style="{StaticResource Num}" MinWidth="22" />
                                <TextBlock Text="%" Style="{StaticResource Unit}" />
                                <TextBlock x:Name="CpuClock" Style="{StaticResource Num}" MinWidth="26" />
                                <TextBlock Text="GHz" Style="{StaticResource Unit}" Margin="2,0,0,1" />
                            </StackPanel>
                            <Border Style="{StaticResource BarTrack}">
                                <Border x:Name="CpuBar" Style="{StaticResource BarFill}">
                                    <Border.RenderTransform><ScaleTransform ScaleX="0" /></Border.RenderTransform>
                                </Border>
                            </Border>
                        </StackPanel>
                    </DockPanel>

                    <DockPanel x:Name="RamRow">
                        <TextBlock Text="RAM" Style="{StaticResource Label}" Margin="0,0,8,0" />
                        <StackPanel Orientation="Horizontal">
                            <TextBlock x:Name="RamUsed" Style="{StaticResource Num}" MinWidth="26" />
                            <TextBlock x:Name="RamTotal" Style="{StaticResource Unit}" Text="/ — GB" Margin="4,0,0,1" />
                        </StackPanel>
                    </DockPanel>

                    <!-- Why the hardware is slowing down; only shown while it is -->
                    <WrapPanel x:Name="LimitsRow" Visibility="Collapsed" Margin="0,8,0,-4" />
                </StackPanel>
            </StackPanel>

            <!-- ============ Strip: one ruled line (stored as "Compact"; the default) ============ -->
            <StackPanel x:Name="CompactLayout" Orientation="Horizontal">
                <Border Style="{StaticResource Cell}">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock x:Name="CFps" Style="{StaticResource Num}" FontSize="17" MinWidth="30" VerticalAlignment="Bottom" />
                        <TextBlock Text="FPS" Style="{StaticResource Unit}" Margin="4,0,0,2" />
                    </StackPanel>
                </Border>
                <Border x:Name="CFrameTimeBox" Style="{StaticResource Cell}">
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Bottom">
                        <TextBlock x:Name="CFrameTime" Style="{StaticResource Num}" FontSize="12" />
                        <TextBlock Text="ms" Style="{StaticResource Unit}" Margin="3,0,0,1" />
                    </StackPanel>
                </Border>
                <Border x:Name="CGpuBox" Style="{StaticResource Cell}">
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Bottom">
                        <TextBlock Text="GPU" Style="{StaticResource Label}" Width="Auto" Margin="0,0,6,1" VerticalAlignment="Bottom" />
                        <TextBlock x:Name="CGpu" Style="{StaticResource Num}" FontSize="12" />
                    </StackPanel>
                </Border>
                <Border x:Name="CCpuBox" Style="{StaticResource Cell}">
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Bottom">
                        <TextBlock Text="CPU" Style="{StaticResource Label}" Width="Auto" Margin="0,0,6,1" VerticalAlignment="Bottom" />
                        <TextBlock x:Name="CCpu" Style="{StaticResource Num}" FontSize="12" />
                    </StackPanel>
                </Border>
                <Border x:Name="CLimit" Visibility="Collapsed" Padding="6,0,6,1" Margin="6,0,0,0" VerticalAlignment="Center">
                    <TextBlock x:Name="CLimitText" FontFamily="Segoe UI" FontWeight="SemiBold" FontSize="9" />
                </Border>
                <Border x:Name="CBadge" Style="{StaticResource Badge}" Margin="6,0,6,0">
                    <TextBlock x:Name="CBadgeText" Style="{StaticResource BadgeText}" />
                </Border>
            </StackPanel>
        </Grid>
    </Border>
</Window>
```

- [ ] **Step 4: Replace `OverlayWindow.xaml.cs`**

Replace the whole file with:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PCSpecOverlay.Core;
using PCSpecOverlay.Monitors;

namespace PCSpecOverlay;

/// <summary>What the overlay shows about gaming mode, separate from raw telemetry.</summary>
public readonly record struct OverlayStatus(bool GamingMode, bool Boost, bool Paused);

/// <summary>
/// The always-on-top performance panel. It only displays; all configuration lives in the control
/// center. It never takes focus, stays out of Alt+Tab, and is click-through unless the user turns
/// that off to drag it (it then snaps to the nearest corner of whichever monitor it's dropped on).
/// Positioning works in physical pixels per monitor, so mixed display scaling can't misplace it.
/// Its colors come from Themes/Overlay.*.xaml merged into this window only, so the app theme never
/// recolors it unless the overlay theme is "Same as app".
/// </summary>
public partial class OverlayWindow : Window
{
    private const double EdgeMarginDip = 14;

    private Settings _settings = new();
    private string _layout = Settings.Compact;
    private Limit _shownLimits = (Limit)(-1);
    private AppTheme _appTheme = AppTheme.Dark;
    private AppTheme _shownTheme = AppTheme.Dark; // OverlayWindow.xaml merges the dark colors
    private readonly FrameTimeTrace _trace = new();

    public IntPtr Handle { get; private set; }

    /// <summary>Raised after the user drags the overlay to a new corner or monitor.</summary>
    public event Action? Moved;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            ApplyWindowStyles();
        };
        SizeChanged += (_, _) => Reposition();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Reposition);
        MouseLeftButtonDown += OnDrag;
        TraceCanvas.SizeChanged += (_, _) => DrawTrace();
    }

    public void EnsureHandle() => Handle = new WindowInteropHelper(this).EnsureHandle();

    /// <param name="layoutOverride">A per-game layout, or null for the user's normal one.</param>
    public void ApplySettings(Settings s, string? layoutOverride = null)
    {
        _settings = s;
        _layout = layoutOverride ?? s.Layout;
        Opacity = s.Opacity;
        PanelScale.ScaleX = PanelScale.ScaleY = s.Scale;

        var panel = _layout == Settings.Expanded;
        ExpandedLayout.Visibility = Show(panel);
        CompactLayout.Visibility = Show(!panel);
        FrameTimeRow.Visibility = CFrameTimeBox.Visibility = TraceArea.Visibility = Show(s.ShowFrameTime);
        LowRow.Visibility = Show(s.ShowOnePercentLow);
        CpuRow.Visibility = CCpuBox.Visibility = Show(s.ShowCpu);
        RamRow.Visibility = Show(s.ShowRam);
        _shownLimits = (Limit)(-1); // re-render limit chips

        ApplyTheme();
        ApplyWindowStyles();
        Reposition();
        DrawTrace();
    }

    /// <summary>The control center's theme; used when the overlay theme is "Same as app".</summary>
    public void SetAppTheme(AppTheme theme)
    {
        _appTheme = theme;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var theme = ThemeResolver.ResolveOverlay(_settings.OverlayTheme, _appTheme);
        if (theme == _shownTheme) return;
        try
        {
            var colors = new ResourceDictionary { Source = new Uri($"pack://application:,,,/PCSpecOverlay;component/Themes/Overlay.{theme}.xaml") };
            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(colors);
            _shownTheme = theme;
        }
        catch (Exception e)
        {
            Log.Error("Overlay", $"Couldn't load the {theme.ToString().ToLowerInvariant()} overlay colors", e);
        }
    }

    private static Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    private void ApplyWindowStyles()
    {
        if (Handle == IntPtr.Zero) return;
        var ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE) | Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
        ex = _settings.ClickThrough ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
        Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
        Cursor = _settings.ClickThrough ? null : Cursors.SizeAll;
    }

    /// <summary>Places the overlay in its corner of the chosen monitor (primary if that one is gone).</summary>
    public void Reposition()
    {
        if (Handle == IntPtr.Zero || Displays.Find(_settings.Monitor) is not { } display) return;
        if (!Native.GetWindowRect(Handle, out var r)) return;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        var m = (int)Math.Round(EdgeMarginDip * display.Dpi / 96.0);
        var work = display.WorkArea;
        var x = _settings.Position.EndsWith("Left") ? work.Left + m : work.Right - w - m;
        var y = _settings.Position.StartsWith("Bottom") ? work.Bottom - h - m : work.Top + m;
        if (x != r.Left || y != r.Top)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (_settings.ClickThrough) return;
        try { DragMove(); } catch (InvalidOperationException) { return; }

        // Snap to the nearest corner of the monitor the overlay was dropped on
        if (!Native.GetWindowRect(Handle, out var r)) return;
        var cx = (r.Left + r.Right) / 2;
        var cy = (r.Top + r.Bottom) / 2;
        var display = Displays.All().FirstOrDefault(d => cx >= d.Bounds.Left && cx < d.Bounds.Right && cy >= d.Bounds.Top && cy < d.Bounds.Bottom);
        if (display is null) { Reposition(); return; }
        var left = cx < (display.WorkArea.Left + display.WorkArea.Right) / 2;
        var top = cy < (display.WorkArea.Top + display.WorkArea.Bottom) / 2;
        _settings.Position = (top ? "Top" : "Bottom") + (left ? "Left" : "Right");
        _settings.Monitor = display.Primary ? "" : display.DeviceName;
        Reposition();
        Moved?.Invoke();
    }

    /// <summary>Fullscreen/borderless apps can push themselves above us; re-assert z-order.</summary>
    public void KeepOnTop()
    {
        if (Handle != IntPtr.Zero && IsVisible)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public void SetGpuName(string? name) =>
        GpuName.Text = name?.Replace("NVIDIA ", "").Replace("GeForce ", "").Replace(" Laptop GPU", " Laptop") ?? "";

    // ---- rendering ----

    public void ShowStatus(OverlayStatus status)
    {
        var text = status.GamingMode ? "GAMING MODE" : status.Paused ? "PAUSED" : status.Boost ? "BOOST" : null;
        BadgeText.Text = text ?? "";
        CBadgeText.Text = status.GamingMode ? "GAME" : text ?? "";
        Badge.Visibility = CBadge.Visibility = Show(text is not null);
        var fill = status.Paused ? "O.Muted" : "O.Text";
        Badge.SetResourceReference(Border.BackgroundProperty, fill);
        CBadge.SetResourceReference(Border.BackgroundProperty, fill);
    }

    public void ShowFps(Telemetry t)
    {
        FpsHint.Text = t.FpsUnavailable;
        FpsHint.Visibility = Show(t.FpsUnavailable is not null);
        FpsSource.Text = t.FpsSource ?? "";

        if (t.Fps is { } s)
        {
            FpsValue.Text = CFps.Text = Math.Round(s.Fps).ToString("0");
            FrameTime.Text = CFrameTime.Text = s.FrameTimeMs.ToString("0.0");
            LowFps.Text = Math.Round(s.OnePercentLow).ToString("0");
        }
        else
        {
            FpsValue.Text = CFps.Text = FrameTime.Text = CFrameTime.Text = LowFps.Text = "—";
        }
        var color = t.Fps is null ? "O.Muted" : "O.Text";
        FpsValue.SetResourceReference(TextBlock.ForegroundProperty, color);
        CFps.SetResourceReference(TextBlock.ForegroundProperty, color);

        _trace.Add(t.Fps?.WorstFrameMs);
        DrawTrace();
    }

    private void DrawTrace()
    {
        if (ExpandedLayout.Visibility != Visibility.Visible || TraceArea.Visibility != Visibility.Visible) return;
        TraceLine.Points = new PointCollection(_trace.Plot(TraceCanvas.ActualWidth, TraceCanvas.ActualHeight));
    }

    public void ShowStats(Telemetry t)
    {
        var s = _settings;
        var g = t.Gpu;

        // GPU: hide entirely without an NVIDIA GPU; say why when there's no reading
        var hasGpu = g.Status != GpuStatus.NotPresent;
        GpuRow.Visibility = Show(hasGpu && s.ShowGpu);
        VramRow.Visibility = Show(hasGpu && s.ShowVram);
        CGpuBox.Visibility = Show(hasGpu && s.ShowGpu);
        var ok = g.Status == GpuStatus.Ok;
        GpuValues.Visibility = GpuTrack.Visibility = Show(ok);
        GpuState.Visibility = Show(!ok);
        GpuState.Text = g.Status switch
        {
            GpuStatus.Sleeping => "Sleeping (saving power)",
            GpuStatus.Unavailable => "Driver not responding",
            _ => "",
        };

        Set(GpuLoad, g.Load, "0");
        Set(GpuTemp, g.TempC, "0");
        Set(GpuClock, g.ClockMhz, "0");
        Set(GpuPower, g.PowerW, "0");
        SetTemp(GpuTemp, g.TempC, s.GpuTempWarn, s.GpuTempHot);
        SetBar(GpuBar, g.Load);
        const double gb = 1024.0 * 1024 * 1024;
        Set(VramUsed, g.VramUsed / gb, "0.0");
        VramTotal.Text = g.VramTotal is { } total ? $"/ {total / gb:0.0} GB" : "/ — GB";

        Set(CpuLoad, t.CpuLoad, "0");
        Set(CpuTemp, t.CpuTempC, "0");
        Set(CpuClock, t.CpuClockGhz, "0.0");
        SetTemp(CpuTemp, t.CpuTempC, s.CpuTempWarn, s.CpuTempHot);
        SetBar(CpuBar, t.CpuLoad);

        Set(RamUsed, t.Ram?.UsedGb, "0.0");
        RamTotal.Text = t.Ram is { } r ? $"/ {r.TotalGb:0.0} GB" : "/ — GB";

        // Strip: temperature first, then load
        CGpu.Text = ok ? $"{Num(g.TempC, "0")}° {Num(g.Load, "0")}%" : g.Status == GpuStatus.Sleeping ? "sleeping" : "—";
        SetTemp(CGpu, g.TempC, s.GpuTempWarn, s.GpuTempHot);
        CCpu.Text = $"{Num(t.CpuTempC, "0")}° {Num(t.CpuLoad, "0")}%";
        SetTemp(CCpu, t.CpuTempC, s.CpuTempWarn, s.CpuTempHot);

        ShowLimits(s.ShowSlowdowns ? t.Limits : Limit.None);
    }

    private void ShowLimits(Limit limits)
    {
        if (limits == _shownLimits) return;
        _shownLimits = limits;

        LimitsRow.Children.Clear();
        foreach (var (flag, text, hot) in new[]
                 {
                     (Limit.GpuThermal, "GPU · SLOWING DOWN (HEAT)", true),
                     (Limit.CpuThermal, "CPU · SLOWING DOWN (HEAT)", true),
                     (Limit.GpuPower, "GPU · AT POWER LIMIT", false),
                     (Limit.CpuPower, "CPU · AT POWER LIMIT", false),
                 })
        {
            if (limits.HasFlag(flag)) LimitsRow.Children.Add(Chip(text, hot ? "O.Hot" : "O.Signal"));
        }
        LimitsRow.Visibility = Show(limits != Limit.None);

        var thermal = (limits & (Limit.GpuThermal | Limit.CpuThermal)) != 0;
        CLimit.Visibility = Show(limits != Limit.None);
        CLimitText.Text = thermal ? "HEAT" : "POWER";
        var key = thermal ? "O.Hot" : "O.Signal";
        CLimitText.SetResourceReference(TextBlock.ForegroundProperty, key);
        CLimit.SetResourceReference(Border.BackgroundProperty, key + "Soft");
    }

    /// <summary>A square warning chip in the given color token (its "Soft" variant fills it).</summary>
    private static Border Chip(string text, string key)
    {
        var label = new TextBlock { Text = text, FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold, FontSize = 9.5 };
        label.SetResourceReference(TextBlock.ForegroundProperty, key);
        var chip = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(0, 0, 6, 4), Child = label };
        chip.SetResourceReference(Border.BackgroundProperty, key + "Soft");
        chip.SetResourceReference(Border.BorderBrushProperty, key);
        return chip;
    }

    private static string Num(double? v, string format) => v is { } x ? x.ToString(format) : "—";

    private static void Set(TextBlock block, double? value, string format) => block.Text = Num(value, format);

    /// <summary>Amber at the warning level, red at the hot level, otherwise the normal text color.</summary>
    private static void SetTemp(TextBlock block, double? c, double warm, double hot) =>
        block.SetResourceReference(TextBlock.ForegroundProperty, c is null ? "O.Text" : c >= hot ? "O.Hot" : c >= warm ? "O.Signal" : "O.Text");

    /// <summary>
    /// Bars ease only on noticeable changes (≥10 points), briefly and at a low frame rate; small
    /// changes jump. The overlay renders in software, so every animation frame repaints the panel.
    /// </summary>
    private static void SetBar(FrameworkElement fill, double? percent)
    {
        var scale = (ScaleTransform)fill.RenderTransform;
        var target = Math.Clamp((percent ?? 0) / 100.0, 0, 1);
        var change = Math.Abs(scale.ScaleX - target);
        if (change < 0.01) return;
        if (change < 0.10)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.ScaleX = target;
            return;
        }
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuadraticEase() };
        Timeline.SetDesiredFrameRate(anim, 20);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
    }
}
```

- [ ] **Step 5: Build, run the new tests and the suite**

Run: `dotnet build PCSpec.slnx` — Expected: 0 errors, 0 warnings.
Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~OverlaySourceTests"` — Expected: 2 passed.
Run: `dotnet test PCSpec.slnx` — Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add PCSpecOverlay/OverlayWindow.xaml PCSpecOverlay/OverlayWindow.xaml.cs PCSpecOverlay.Tests/OverlayTests.cs
git commit -F- <<'EOF'
Rebuild the overlay as Strip and Panel with its own theme colors and a frame-time trace

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 6: Control-center choices and app wiring

**Files:**
- Modify: `PCSpecOverlay/ControlCenterWindow.xaml` (Overlay page Layout row ~line 526; games table layout dropdown ~line 439), `PCSpecOverlay/App.xaml.cs`
- Test: `PCSpecOverlay.Tests/OverlayTests.cs` (append)

**Interfaces:**
- Consumes: `OverlayWindow.SetAppTheme(AppTheme)` (Task 5); `App.Theme` / `ThemeManager.Current` / `ThemeManager.Changed` (stage 1); `Settings.OverlayTheme` and constants (Task 1).

- [ ] **Step 1: Write the failing test**

Append to `PCSpecOverlay.Tests/OverlayTests.cs`:

```csharp
public class OverlayChoiceTests
{
    [Fact]
    public void Control_center_offers_strip_panel_and_the_overlay_theme()
    {
        var xaml = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/ControlCenterWindow.xaml"));
        Assert.Contains("Content=\"Strip (one line)\" Tag=\"Compact\"", xaml);
        Assert.Contains("Content=\"Panel\" Tag=\"Expanded\"", xaml);
        Assert.Contains("SelectedValue=\"{Binding OverlayTheme}\"", xaml);
        foreach (var tag in new[] { "SameAsApp", "AlwaysDark", "AlwaysLight" }) Assert.Contains($"Tag=\"{tag}\"", xaml);
        Assert.DoesNotContain("Content=\"Expanded\"", xaml);
        Assert.DoesNotContain("Content=\"Compact\"", xaml);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~OverlayChoiceTests"` — Expected: FAIL.

- [ ] **Step 3: Rename the layout choices**

In `PCSpecOverlay/ControlCenterWindow.xaml`, on the Overlay page, replace the two items of the `Layout` ComboBox (the one with `SelectedValue="{Binding Layout}"`) with:

```xml
                                    <ComboBoxItem Content="Strip (one line)" Tag="Compact" />
                                    <ComboBoxItem Content="Panel" Tag="Expanded" />
```

In the games table, replace the per-game layout ComboBox (the one with `SelectedValue="{Binding LayoutChoice}"`, currently `SelectedValuePath="Content"`) with:

```xml
                                            <ComboBox Grid.Column="2" Margin="0,0,12,0" MinWidth="0" SelectedValuePath="Tag"
                                                      SelectedValue="{Binding LayoutChoice}" AutomationProperties.Name="Overlay layout for this game">
                                                <ComboBoxItem Content="Default" Tag="Default" />
                                                <ComboBoxItem Content="Strip" Tag="Compact" />
                                                <ComboBoxItem Content="Panel" Tag="Expanded" />
                                            </ComboBox>
```

(`GameRule.LayoutChoice` already maps "Default" ↔ null and otherwise stores the value, so the stored values stay `Compact`/`Expanded`.)

- [ ] **Step 4: Add the Overlay theme row**

Directly after the `Layout` row's closing `</Grid>` on the Overlay page, insert a row in the same pattern:

```xml
                            <Grid Style="{StaticResource Row}">
                                <Grid.ColumnDefinitions><ColumnDefinition Width="*" /><ColumnDefinition Width="220" /></Grid.ColumnDefinitions>
                                <StackPanel Margin="0,0,16,0">
                                    <TextBlock Text="Overlay theme" FontSize="13" />
                                    <TextBlock Style="{StaticResource Body}" Text="Dark reads best over most games." />
                                </StackPanel>
                                <ComboBox Grid.Column="1" VerticalAlignment="Center" SelectedValuePath="Tag" SelectedValue="{Binding OverlayTheme}"
                                          AutomationProperties.Name="Overlay theme">
                                    <ComboBoxItem Content="Always dark" Tag="AlwaysDark" />
                                    <ComboBoxItem Content="Always light" Tag="AlwaysLight" />
                                    <ComboBoxItem Content="Same as app" Tag="SameAsApp" />
                                </ComboBox>
                            </Grid>
```

- [ ] **Step 5: Tell the overlay the app theme**

In `PCSpecOverlay/App.xaml.cs` `OnStartup`, directly after `_overlay.ApplySettings(Settings);`, add:

```csharp
        _overlay.SetAppTheme(_theme.Current);
        _theme.Changed += _overlay.SetAppTheme;
```

(The overlay already re-reads `OverlayTheme` on every `ApplySettings`, which `SaveSettings` calls.)

- [ ] **Step 6: Build, run the new test and the suite**

Run: `dotnet build PCSpec.slnx` — Expected: 0 errors.
Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~OverlayChoiceTests"` — Expected: 1 passed.
Run: `dotnet test PCSpec.slnx` — Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add PCSpecOverlay/ControlCenterWindow.xaml PCSpecOverlay/App.xaml.cs PCSpecOverlay.Tests/OverlayTests.cs
git commit -F- <<'EOF'
Offer Strip and Panel and the overlay theme in the control center

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## After the tasks: on-PC verification (controller, with the user)

Run the development build (`PCSpecOverlay/bin/Debug/net8.0-windows/PCSpecOverlay.exe`; it uses `%AppData%\PCSpecOverlay-Dev` and the installed copy keeps its shortcut). Record results in `PCSpecOverlay/docs/AUDIT.md` under "Instrument stage 2":

1. Strip and Panel both render as in the picked mockups (screenshot each), with no green and square corners.
2. Overlay theme: Always dark, Always light, and Same as app (flip the app's Theme between Light and Dark; the overlay follows only in Same as app).
3. In a game (Starve.io in Edge, or the FPS test windows): FPS source shows DXGI or KERNEL in Panel; the frame-time trace moves and shows a spike when the game stutters (e.g. while switching tabs); warning/hot colors appear at the configured levels.
4. Per-game layout override still switches Strip/Panel when that game is in front.
5. Click-through off: drag and snap still work in both layouts.
6. CPU use of the overlay, measured against the installed 2.0.13 alternately under the same load as in the earlier audit: no increase beyond noise.
7. Screen-reader names: the new Overlay theme dropdown is named.

The user's installed copy has `Expanded` saved; switching it to Strip happens through the Layout setting after 2.1.0 is installed (never by overwriting the saved choice).
