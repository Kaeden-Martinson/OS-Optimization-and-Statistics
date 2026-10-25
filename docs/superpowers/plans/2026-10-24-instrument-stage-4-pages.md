# Instrument Stage 4: Pages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the six remaining control-center pages in the Instrument style the user picked: Optimizer (Ledger), Gaming (Pipeline), Overlay (Preview first), Temperatures & alerts (Scales), Settings (Status strip), Diagnostics (Health grid with a change journal). Also give every control a screen-reader name and settle where red is used.

**Architecture:** The pages stay in `ControlCenterWindow.xaml` (one `TabItem` each), and their logic stays in `ControlCenterWindow.xaml.cs`. New pure logic goes in `Core/PageData.cs` and is unit-tested: the temperature scale math, journal rows, the overlay preview text and the health summary. The one new control, `Controls/TempScaleControl.cs`, is a code-only `Canvas` that draws one sensor's scale. Per-change Undo goes through a new `ChangeJournal.ReleaseAll`, which uses the existing `Release` path, so the existing rule still holds: a value is restored only if it is still what the app set. New page parts (ledger rows, segmented buttons, layout cards, corner targets, scale handles) are styles in `Theme.xaml`, colored only through `DynamicResource` tokens.

**Tech Stack:** .NET 8 WPF (C# 12), xUnit 2.9.

**Spec:** `docs/superpowers/specs/2026-10-24-instrument-redesign-design.md`, section 3, plus the picked mockups in `docs/superpowers/specs/2026-10-24-instrument-redesign-mockups/` (`optimizer.html` A, `gaming.html` B, `overlay-settings.html` A, `alerts.html` B, `settings.html` C, `diagnostics.html` B). Builds on stages 1–3 and the Optiframe change: tokens, `core:Icon`, `Icons.xaml`, the top tab bar, the Overview, and move-and-pin with `Position` "Custom".

## Global Constraints

- **Colors.** Only the tokens `Bg`, `Panel`, `Line`, `Line2`, `Text`, `TextHover`, `Muted`, `Faint`, `Signal`, `Hot`, `Good` and `Selection`, through `DynamicResource` in XAML and `SetResourceReference` or `FindResource` in code. No hex colors. The legacy aliases (`Window`, `Sidebar`, `Card`, `CardBorder`, `Input`, `InputBorder`, `Accent`, `AccentHover`, `AccentSoft`, `Warn`, `Info`) are not used by the control center after this stage.
- **What each color means.** `Signal` marks a warning state only. `Hot` marks hot readings and errors only, and is the only red. `Good` is for status lights and check marks only. `Faint` is never used for readable text. Buttons are never red.
- **Shape.** Square corners and 1 px ruled lines. A row that needs action gets a 2 px `Signal` left edge. Primary button: `Text` fill with `Bg` lettering. No emojis; icons come from `Icon.*` geometries.
- **Type.** Every number uses Cascadia Mono (falling back to Consolas). Labels are 10.5 px uppercase in `Muted`, page titles 16 px.
- **Behavior.** Every existing setting, check, confirmation and safety rule keeps working. The only new behavior is in the spec: preview corner targets, the temperature scales, and per-change Undo.
- **Temperature scale.** 30–100 °C in 1 °C steps, with Warning always below Hot. Arrow keys move the focused handle by 1 °C and Page Up/Down by 5.
- **Undo.** Undo releases a change for every owner through `ChangeJournal.Release`. The value is restored only if it still matches what the app applied; otherwise the result is "changed elsewhere" and the user's value is kept.
- **Screen readers.** Every Slider, ComboBox, TextBox, RadioButton and ToggleButton, every Button without a text `Content`, and every CheckBox without content has `AutomationProperties.Name`.
- **Build and tests.** `dotnet build PCSpec.slnx` must have 0 warnings, and `dotnet test PCSpec.slnx` must pass (224 tests pass before this plan). Shell examples use Git Bash from the repo root.
- **Commits** end with `Co-Authored-By: <the model that wrote it> <noreply@anthropic.com>`.

## File map

| File | Status | Responsibility |
|---|---|---|
| `PCSpecOverlay/Core/PageData.cs` | create | `TempScale`, `JournalRow`/`JournalRows`, `OverlayPreview`, `Health` |
| `PCSpecOverlay/Core/ChangeJournal.cs` | modify | `ReleaseAll` |
| `PCSpecOverlay/Core/Engine.cs` | modify | `UndoChange`; `UndoEverything` uses `ReleaseAll` |
| `PCSpecOverlay/Core/DiagnosticReport.cs` | modify | `ChangesCheck` constant |
| `PCSpecOverlay/Optimizer/Checks.cs` | modify | `Check.Recommended` |
| `PCSpecOverlay/Optimizer/WindowsSettings.cs` | modify | `SettingCatalog.DescribeValue` |
| `PCSpecOverlay/Settings.cs` | modify | Temperature levels fit 30–100 with Warning below Hot |
| `PCSpecOverlay/Controls/TempScaleControl.cs` | create | One sensor's scale with two handles |
| `PCSpecOverlay/Theme.xaml` | modify | Page styles; drop the red `Danger` button |
| `PCSpecOverlay/ControlCenterWindow.xaml(.cs)` | modify | The six pages |
| `PCSpecOverlay.Tests/PagesTests.cs` | create | Tests for this plan |
| `PCSpecOverlay.Tests/ChangeJournalTests.cs`, `OptiframeTests.cs` | modify | `ReleaseAll` tests; the position test now targets the preview |

---

### Task 1: Page logic

**Files:**
- Create: `PCSpecOverlay/Core/PageData.cs`
- Modify: `PCSpecOverlay/Core/ChangeJournal.cs`, `PCSpecOverlay/Core/Engine.cs`, `PCSpecOverlay/Core/DiagnosticReport.cs`, `PCSpecOverlay/Optimizer/Checks.cs`, `PCSpecOverlay/Optimizer/WindowsSettings.cs`, `PCSpecOverlay/Settings.cs`
- Test: `PCSpecOverlay.Tests/PagesTests.cs` (create), `PCSpecOverlay.Tests/ChangeJournalTests.cs` (append inside the class)

**Interfaces:**
- Produces:
  - `TempScale.Min` (30), `TempScale.Max` (100)
  - `TempScale.Fraction(double c) : double`, `TempScale.ValueAt(double fraction) : int`
  - `TempScale.MoveWarn(int warn, int hot, int to) : (int Warn, int Hot)`, `TempScale.MoveHot(...)` with the same shape
  - `record JournalRow(string Id, string When, string Setting, string Change, string Owners)`
  - `JournalRows.OwnerLabel(string) : string`
  - `JournalRows.Build(IEnumerable<JournalEntry>, Func<string,string?> name, Func<string,string?,string> describe, DateTime nowLocal) : List<JournalRow>`
  - `OverlayPreview.Strip(Settings, double? fps, double? frameMs, double? gpuC, double? cpuC) : string`
  - `OverlayPreview.Panel(Settings, double? fps, double? frameMs, double? gpuC, double? cpuC, double? ramGb) : string`
  - `Health.Summary(IReadOnlyCollection<DiagLevel>) : string`, `Health.Word(DiagLevel) : string`
  - `ChangeJournal.ReleaseAll(IReversibleSetting) : List<ChangeResult<ReleaseOutcome>>`, `Engine.UndoChange(string id) : List<string>`
  - `DiagnosticReport.ChangesCheck`, `Check.Recommended`, `SettingCatalog.DescribeValue(string id, string? value) : string`

- [ ] **Step 1: Write the failing tests**

Create `PCSpecOverlay.Tests/PagesTests.cs`:

```csharp
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
```

Append inside `ChangeJournalTests` in `PCSpecOverlay.Tests/ChangeJournalTests.cs`:

```csharp
    [Fact]
    public void Release_all_lets_go_for_every_owner()
    {
        var s = new FakeSetting();
        _journal.Apply(s, "fast", "optimizer");
        _journal.Apply(s, "fast", "gaming");

        var results = _journal.ReleaseAll(s);

        Assert.Equal(new[] { ReleaseOutcome.StillNeeded, ReleaseOutcome.Restored }, results.Select(r => r.Outcome));
        Assert.Equal("original", s.Value);
        Assert.Null(_journal.Entry(s.Id));
    }

    [Fact]
    public void Release_all_keeps_a_newer_user_choice()
    {
        var s = new FakeSetting();
        _journal.Apply(s, "fast", "gaming");
        s.Value = "user-picked";

        Assert.Equal(new[] { ReleaseOutcome.ChangedElsewhere }, _journal.ReleaseAll(s).Select(r => r.Outcome));
        Assert.Equal("user-picked", s.Value);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~TempScaleTests|FullyQualifiedName~JournalRowTests|FullyQualifiedName~OverlayPreviewTests|FullyQualifiedName~HealthTests|FullyQualifiedName~RecommendedValueTests|FullyQualifiedName~ChangeJournalTests"`
Expected: build error `The name 'TempScale' does not exist in the current context`.

- [ ] **Step 3: Implement**

Create `PCSpecOverlay/Core/PageData.cs`:

```csharp
using System.Globalization;
using PCSpecOverlay.Optimizer;

namespace PCSpecOverlay.Core;

/// <summary>The temperature scale on the Alerts page: 30–100 °C in whole degrees, Warning always below Hot.</summary>
public static class TempScale
{
    public const int Min = 30, Max = 100;

    public static double Fraction(double celsius) => Math.Clamp((celsius - Min) / (Max - Min), 0, 1);

    public static int ValueAt(double fraction) =>
        (int)Math.Round(Min + Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1) * (Max - Min));

    public static (int Warn, int Hot) MoveWarn(int warn, int hot, int to) => (Math.Clamp(to, Min, Math.Max(Min, hot - 1)), hot);

    public static (int Warn, int Hot) MoveHot(int warn, int hot, int to) => (warn, Math.Clamp(to, Math.Min(Max, warn + 1), Max));
}

/// <summary>One change still in effect, as the Diagnostics journal lists it.</summary>
public sealed record JournalRow(string Id, string When, string Setting, string Change, string Owners);

public static class JournalRows
{
    public static string OwnerLabel(string owner) => owner switch
    {
        Tweak.Owner => "Optimizer",
        GamingMode.Owner => "Gaming mode",
        _ => owner,
    };

    /// <summary>Changes the app still owns, newest first, with values in words.</summary>
    public static List<JournalRow> Build(IEnumerable<JournalEntry> entries, Func<string, string?> name,
        Func<string, string?, string> describe, DateTime nowLocal) =>
        entries.OrderByDescending(e => e.AppliedUtc).Select(e =>
        {
            var at = e.AppliedUtc.ToLocalTime();
            var when = at.Date == nowLocal.Date
                ? "Today " + at.ToString("HH:mm", CultureInfo.InvariantCulture)
                : at.ToString("MMM d HH:mm", CultureInfo.InvariantCulture);
            return new JournalRow(e.Id, when, name(e.Id) ?? e.Id,
                $"{describe(e.Id, e.Original)} → {describe(e.Id, e.Applied)}", string.Join(", ", e.Owners.Select(OwnerLabel)));
        }).ToList();
}

/// <summary>The text the Overlay page's preview draws, from the same switches the overlay uses.</summary>
public static class OverlayPreview
{
    public static string Strip(Settings s, double? fps, double? frameMs, double? gpuC, double? cpuC)
    {
        var parts = new List<string> { $"{Whole(fps)} FPS" };
        if (s.ShowFrameTime) parts.Add($"{Tenths(frameMs)} ms");
        if (s.ShowGpu) parts.Add($"GPU {Whole(gpuC)}°");
        if (s.ShowCpu) parts.Add($"CPU {Whole(cpuC)}°");
        return string.Join("  ", parts);
    }

    public static string Panel(Settings s, double? fps, double? frameMs, double? gpuC, double? cpuC, double? ramGb)
    {
        var lines = new List<string> { AppIdentity.ProductName.ToUpperInvariant(), $"{Whole(fps)} FPS" };
        if (s.ShowFrameTime) lines.Add($"{Tenths(frameMs)} ms");
        if (s.ShowGpu) lines.Add($"GPU  {Whole(gpuC)}°");
        if (s.ShowCpu) lines.Add($"CPU  {Whole(cpuC)}°");
        if (s.ShowRam) lines.Add($"RAM  {Tenths(ramGb)} GB");
        return string.Join("\n", lines);
    }

    private static string Whole(double? v) => v is { } x ? x.ToString("0", CultureInfo.InvariantCulture) : "—";
    private static string Tenths(double? v) => v is { } x ? x.ToString("0.0", CultureInfo.InvariantCulture) : "—";
}

/// <summary>Wording for the Diagnostics health grid.</summary>
public static class Health
{
    public static string Summary(IReadOnlyCollection<DiagLevel> levels)
    {
        var problems = levels.Count(l => l is DiagLevel.Warn or DiagLevel.Error);
        return problems == 0
            ? $"All {levels.Count} checks look fine."
            : $"{levels.Count - problems} of {levels.Count} checks look fine; {problems} {(problems == 1 ? "needs" : "need")} a look.";
    }

    public static string Word(DiagLevel level) => level switch
    {
        DiagLevel.Ok => "OK",
        DiagLevel.Warn => "Check",
        DiagLevel.Error => "Problem",
        _ => "Note",
    };
}
```

`Core/ChangeJournal.cs` — add after `Release`:

```csharp
    /// <summary>Lets go of a change for every owner (the Diagnostics journal's Undo); each release follows the rules above.</summary>
    public List<ChangeResult<ReleaseOutcome>> ReleaseAll(IReversibleSetting setting) =>
        (Entry(setting.Id)?.Owners.ToList() ?? new List<string>()).Select(owner => Release(setting, owner)).ToList();
```

`Core/Engine.cs`:
- In `UndoEverything`, replace the inner `foreach (var owner …) report.Add(Journal.Release(setting, owner).Message);` with `report.AddRange(Journal.ReleaseAll(setting).Select(r => r.Message));`.
- Add after it:

```csharp
    /// <summary>Undoes one change for all its owners (Diagnostics journal); restored only if it's still what the app set.</summary>
    public List<string> UndoChange(string id) =>
        SettingCatalog.Resolve(id) is { } setting
            ? Journal.ReleaseAll(setting).Select(r => r.Message).ToList()
            : new List<string> { "This change can't be undone from here (its setting isn't available on this PC)." };
```

`Core/DiagnosticReport.cs`: add `public const string ChangesCheck = "Windows changes by the app";` to the class, and use it as the name in the existing `list.Add(new("Windows changes by the app", …))`.

`Optimizer/Checks.cs`:
- In `Check`, after `Detail`, add `public string Recommended { get; protected set; } = "—";`.
- In `Tweak.Read`, in the unsupported branch set `Recommended = "—";`, and replace `Detail = current == optimal ? "" : $"Recommended: {describe(optimal)}";` with:

```csharp
        Detail = "";
        Recommended = describe(optimal);
```

- In `ControlCenterWindow.xaml.cs` `OnFixAll`, change `{t.Detail.Replace("Recommended: ", "")}` to `{t.Recommended}`. This is the only other place that reads the old detail text.

`Optimizer/WindowsSettings.cs` — in `SettingCatalog`, add:

```csharp
    /// <summary>A stored journal value in words, e.g. "Best performance" or "Off".</summary>
    public static string DescribeValue(string id, string? value)
    {
        if (id == Power.Id) return PowerMode.Name(Guid.TryParse(value, out var g) ? g : null);
        if (id == GameMode.Id) return value is null or "1" ? "On" : "Off";
        if (id == BackgroundRecording.Id) return value is null or "0" ? "Off" : "On";
        if (id.StartsWith("gpu-pref:", StringComparison.Ordinal)) return GpuPreferenceSetting.Describe(value);
        return value ?? "default";
    }
```

`Settings.cs` `Validate()` — replace the four temperature lines with the following (add `using PCSpecOverlay.Core;` if missing):

```csharp
        GpuTempWarn = Math.Clamp(GpuTempWarn, TempScale.Min, TempScale.Max - 1);
        GpuTempHot = Math.Clamp(GpuTempHot, GpuTempWarn + 1, TempScale.Max);
        CpuTempWarn = Math.Clamp(CpuTempWarn, TempScale.Min, TempScale.Max - 1);
        CpuTempHot = Math.Clamp(CpuTempHot, CpuTempWarn + 1, TempScale.Max);
```

- [ ] **Step 4: Run to verify pass**

Run the Step 2 filter. Expected: all pass. Then `dotnet test PCSpec.slnx` (expected: all pass; `LogicTests` line ~202 still holds, since Hot ≥ Warn).

- [ ] **Step 5: Commit**

```bash
git add PCSpecOverlay PCSpecOverlay.Tests
git commit -F- <<'EOF'
Add page logic: temperature scale, change journal rows, overlay preview text, health summary

Co-Authored-By: <model> <noreply@anthropic.com>
EOF
```

---

### Task 2: Page styles and the temperature scale control

**Files:**
- Modify: `PCSpecOverlay/Theme.xaml`
- Create: `PCSpecOverlay/Controls/TempScaleControl.cs`
- Test: `PCSpecOverlay.Tests/PagesTests.cs` (append)

**Interfaces:**
- Consumes: `TempScale` (Task 1).
- Produces:
  - Styles: `PageTitle`, `Key`, `Mono`, `LedgerRow`, `Seg` (RadioButton), `LayoutCard` (RadioButton), `CornerTarget` (Button), `ScaleHandle` (Thumb)
  - `TempScaleControl` (namespace `PCSpecOverlay.Controls`) with `Sensor` (string), `Warn` and `Hot` (int, read-only)
  - `TempScaleControl.SetLevels(int warn, int hot)`, `TempScaleControl.SetCurrent(double? celsius)`, and `event Action<int,int> LevelsChanged`

- [ ] **Step 1: Write the failing test**

Append to `PCSpecOverlay.Tests/PagesTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~PageStyleTests"` — Expected: 2 failed.

- [ ] **Step 3: Styles**

Append to `PCSpecOverlay/Theme.xaml`, before `</ResourceDictionary>`:

```xml
    <!-- ===== Page parts (stage 4) ===== -->

    <Style x:Key="PageTitle" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="Segoe UI Variable Display, Segoe UI" />
        <Setter Property="FontSize" Value="16" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{DynamicResource Text}" />
    </Style>

    <!-- Uppercase column or section label -->
    <Style x:Key="Key" TargetType="TextBlock">
        <Setter Property="FontSize" Value="10.5" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{DynamicResource Muted}" />
        <Setter Property="VerticalAlignment" Value="Center" />
    </Style>

    <!-- A number or value in a ledger cell -->
    <Style x:Key="Mono" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="Cascadia Mono, Consolas" />
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Foreground" Value="{DynamicResource Text}" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="TextTrimming" Value="CharacterEllipsis" />
    </Style>

    <!-- One ruled row of a ledger table -->
    <Style x:Key="LedgerRow" TargetType="Border">
        <Setter Property="BorderBrush" Value="{DynamicResource Line2}" />
        <Setter Property="BorderThickness" Value="0,0,0,1" />
        <Setter Property="Padding" Value="24,9" />
    </Style>

    <!-- One segment of a segmented control (wrap the group in a Border with BorderThickness="1,1,0,1") -->
    <Style x:Key="Seg" TargetType="RadioButton">
        <Setter Property="Foreground" Value="{DynamicResource Muted}" />
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="RadioButton">
                    <Border x:Name="B" Background="Transparent" BorderBrush="{DynamicResource Line}" BorderThickness="0,0,1,0" Padding="10,4">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="B" Property="Background" Value="{DynamicResource Line2}" />
                        </Trigger>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="B" Property="Background" Value="{DynamicResource Text}" />
                            <Setter Property="Foreground" Value="{DynamicResource Bg}" />
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

    <!-- A choice card (Strip / Panel on the Overlay page) -->
    <Style x:Key="LayoutCard" TargetType="RadioButton">
        <Setter Property="Foreground" Value="{DynamicResource Text}" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="RadioButton">
                    <Border x:Name="B" BorderBrush="{DynamicResource Line}" BorderThickness="1" Background="Transparent" Padding="14,10">
                        <ContentPresenter />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="B" Property="Background" Value="{DynamicResource Panel}" />
                        </Trigger>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="B" Property="BorderBrush" Value="{DynamicResource Text}" />
                            <Setter TargetName="B" Property="Background" Value="{DynamicResource Panel}" />
                        </Trigger>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="B" Property="BorderThickness" Value="2" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- A dashed corner on the Overlay preview; Tag="On" marks the corner in use -->
    <Style x:Key="CornerTarget" TargetType="Button">
        <Setter Property="Width" Value="64" />
        <Setter Property="Height" Value="16" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Rectangle x:Name="R" Fill="Transparent" Stroke="{DynamicResource Muted}" StrokeThickness="1" StrokeDashArray="3 2" />
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="R" Property="Stroke" Value="{DynamicResource Text}" />
                        </Trigger>
                        <Trigger Property="Tag" Value="On">
                            <Setter TargetName="R" Property="Stroke" Value="{DynamicResource Text}" />
                            <Setter TargetName="R" Property="StrokeDashArray" Value="{x:Null}" />
                        </Trigger>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="R" Property="Stroke" Value="{DynamicResource Text}" />
                            <Setter TargetName="R" Property="StrokeThickness" Value="2" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- A handle on an Alerts temperature scale -->
    <Style x:Key="ScaleHandle" TargetType="Thumb">
        <Setter Property="Width" Value="10" />
        <Setter Property="Height" Value="22" />
        <Setter Property="Cursor" Value="SizeWE" />
        <Setter Property="Focusable" Value="True" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Thumb">
                    <Border x:Name="B" Background="{DynamicResource Text}" BorderBrush="{DynamicResource Bg}" BorderThickness="1" />
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="B" Property="BorderBrush" Value="{DynamicResource Muted}" />
                            <Setter TargetName="B" Property="BorderThickness" Value="2" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
```

- [ ] **Step 4: The control**

Create `PCSpecOverlay/Controls/TempScaleControl.cs`:

```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PCSpecOverlay.Core;

namespace PCSpecOverlay.Controls;

/// <summary>
/// One sensor's 30–100 °C scale on the Alerts page: the warning band (Signal) and hot band (Hot) shaded, a line at
/// the current reading, and two handles that drag or step with the keyboard. Warning always stays below Hot.
/// </summary>
public sealed class TempScaleControl : Canvas
{
    private const double TrackTop = 22, TrackHeight = 8, AxisTop = 40, HandleWidth = 10, HandleHeight = 22;
    private static readonly int[] AxisMarks = [30, 50, 70, 90, 100];

    private readonly Rectangle _track = new() { Height = TrackHeight, StrokeThickness = 1 };
    private readonly Rectangle _warnBand = new() { Height = TrackHeight, Opacity = 0.35 };
    private readonly Rectangle _hotBand = new() { Height = TrackHeight, Opacity = 0.35 };
    private readonly Rectangle _current = new() { Width = 2, Height = TrackHeight + 8, Visibility = Visibility.Collapsed };
    private readonly Thumb _warnHandle = new(), _hotHandle = new();
    private readonly TextBlock _warnLabel = Label("Signal"), _hotLabel = Label("Hot");
    private readonly List<(TextBlock Text, int Celsius)> _axis;
    private double? _currentC;
    private double _dragX;

    public TempScaleControl()
    {
        Height = 56;
        _track.SetResourceReference(Shape.FillProperty, "Panel");
        _track.SetResourceReference(Shape.StrokeProperty, "Line");
        _warnBand.SetResourceReference(Shape.FillProperty, "Signal");
        _hotBand.SetResourceReference(Shape.FillProperty, "Hot");
        _current.SetResourceReference(Shape.FillProperty, "Text");
        _axis = AxisMarks.Select(c => (Label("Muted", c == TempScale.Max ? $"{c} °C" : $"{c}"), c)).ToList();

        foreach (var e in new UIElement[] { _track, _warnBand, _hotBand, _current, _warnLabel, _hotLabel }) Children.Add(e);
        foreach (var (text, _) in _axis) Children.Add(text);
        foreach (var (handle, warn) in new[] { (_warnHandle, true), (_hotHandle, false) })
        {
            handle.SetResourceReference(StyleProperty, "ScaleHandle");
            handle.DragStarted += (_, _) => _dragX = GetLeft(handle) + HandleWidth / 2;
            handle.DragDelta += (_, e) =>
            {
                _dragX += e.HorizontalChange;
                Move(warn, TempScale.ValueAt(_dragX / Math.Max(1, ActualWidth)));
            };
            handle.PreviewKeyDown += (_, e) => OnKey(warn, e);
            AutomationProperties.SetHelpText(handle, "Drag, or use the arrow keys to change it by 1 °C (Page Up and Page Down by 5).");
            Children.Add(handle);
        }
        SizeChanged += (_, _) => Place();
    }

    /// <summary>Sensor name used in the handles' screen-reader names, e.g. "GPU".</summary>
    public string Sensor { get; set; } = "";

    public int Warn { get; private set; } = 75;
    public int Hot { get; private set; } = 85;

    /// <summary>Raised after the user moves a handle, with the new warning and hot levels.</summary>
    public event Action<int, int>? LevelsChanged;

    public void SetLevels(int warn, int hot)
    {
        (Warn, Hot) = (warn, hot);
        Place();
    }

    public void SetCurrent(double? celsius)
    {
        _currentC = celsius;
        Place();
    }

    private double X(double celsius) => TempScale.Fraction(celsius) * ActualWidth;

    private void Place()
    {
        var w = ActualWidth;
        if (w <= 0) return;
        _track.Width = w;
        SetTop(_track, TrackTop);

        double xw = X(Warn), xh = X(Hot);
        SetLeft(_warnBand, xw); SetTop(_warnBand, TrackTop); _warnBand.Width = Math.Max(0, xh - xw);
        SetLeft(_hotBand, xh); SetTop(_hotBand, TrackTop); _hotBand.Width = Math.Max(0, w - xh);
        foreach (var (handle, x) in new[] { (_warnHandle, xw), (_hotHandle, xh) })
        {
            SetLeft(handle, x - HandleWidth / 2);
            SetTop(handle, TrackTop + TrackHeight / 2 - HandleHeight / 2);
        }

        _warnLabel.Text = $"warn {Warn}";
        _hotLabel.Text = $"hot {Hot}";
        var warnWidth = Measure(_warnLabel);
        var hotWidth = Measure(_hotLabel);
        var warnLeft = Math.Clamp(xw - HandleWidth / 2, 0, Math.Max(0, w - warnWidth));
        SetLeft(_warnLabel, warnLeft);
        SetLeft(_hotLabel, Math.Clamp(Math.Max(xh - HandleWidth / 2, warnLeft + warnWidth + 8), 0, Math.Max(0, w - hotWidth)));

        _current.Visibility = _currentC is null ? Visibility.Collapsed : Visibility.Visible;
        if (_currentC is { } c) { SetLeft(_current, X(c) - 1); SetTop(_current, TrackTop - 4); }

        foreach (var (text, celsius) in _axis)
        {
            var width = Measure(text);
            SetLeft(text, Math.Clamp(X(celsius) - width / 2, 0, Math.Max(0, w - width)));
            SetTop(text, AxisTop);
        }

        AutomationProperties.SetName(_warnHandle, $"{Sensor} warning level, {Warn} °C");
        AutomationProperties.SetName(_hotHandle, $"{Sensor} hot level, {Hot} °C");
    }

    private void OnKey(bool warn, KeyEventArgs e)
    {
        var step = e.Key switch
        {
            Key.Left or Key.Down => -1,
            Key.Right or Key.Up => 1,
            Key.PageDown => -5,
            Key.PageUp => 5,
            _ => 0,
        };
        if (step == 0) return;
        e.Handled = true;
        Move(warn, (warn ? Warn : Hot) + step);
    }

    private void Move(bool warn, int to)
    {
        var (w, h) = warn ? TempScale.MoveWarn(Warn, Hot, to) : TempScale.MoveHot(Warn, Hot, to);
        if (w == Warn && h == Hot) return;
        (Warn, Hot) = (w, h);
        Place();
        LevelsChanged?.Invoke(w, h);
    }

    private static double Measure(TextBlock t)
    {
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return t.DesiredSize.Width;
    }

    private static TextBlock Label(string brush, string text = "")
    {
        var t = new TextBlock { Text = text, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 10.5 };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }
}
```

- [ ] **Step 5: Build and test**

Run: `dotnet build PCSpec.slnx` — Expected: 0 warnings.
Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~PageStyleTests"` — Expected: 2 passed. Then the full suite passes.

- [ ] **Step 6: Commit**

```bash
git add PCSpecOverlay/Theme.xaml PCSpecOverlay/Controls PCSpecOverlay.Tests/PagesTests.cs
git commit -F- <<'EOF'
Add page styles and the temperature scale control

Co-Authored-By: <model> <noreply@anthropic.com>
EOF
```

---

### Task 3: Optimizer — Ledger

**Files:**
- Modify: `PCSpecOverlay/ControlCenterWindow.xaml` (the OPTIMIZER `TabItem`), `PCSpecOverlay/ControlCenterWindow.xaml.cs`
- Test: `PCSpecOverlay.Tests/PagesTests.cs` (append)

**Interfaces:**
- Consumes: `Check.Recommended` (Task 1); `PageTitle`, `Key`, `Mono` and `LedgerRow` (Task 2).

- [ ] **Step 1: Write the failing test**

Append:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~OptimizerPageTests"` — Expected: 1 failed.

- [ ] **Step 3: Replace the page**

Replace everything inside the OPTIMIZER `<TabItem …>` (keep the `TabItem` start tag) with:

```xml
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <StackPanel>
                    <DockPanel Margin="24,18,24,14">
                        <Button x:Name="FixAllButton" DockPanel.Dock="Right" Tag="Primary" Style="{StaticResource Btn}"
                                Click="OnFixAll" VerticalAlignment="Center" Content="Apply recommendations" />
                        <StackPanel Margin="0,0,16,0">
                            <TextBlock Text="Optimizer" Style="{StaticResource PageTitle}" />
                            <TextBlock Style="{StaticResource Body}"
                                       Text="Nothing changes until you press a button. Every change is recorded and can be undone." />
                        </StackPanel>
                    </DockPanel>

                    <Border x:Name="RestartBanner" Visibility="Collapsed" Background="{DynamicResource Panel}"
                            BorderBrush="{DynamicResource Line}" BorderThickness="0,1,0,0">
                        <Border BorderBrush="{DynamicResource Signal}" BorderThickness="2,0,0,0" Padding="22,10,24,10">
                            <DockPanel>
                                <Button x:Name="RestartButton" DockPanel.Dock="Right" Tag="Primary" Style="{StaticResource Btn}"
                                        VerticalAlignment="Center" Click="OnRestartBrowser" />
                                <StackPanel Margin="0,0,16,0">
                                    <TextBlock x:Name="RestartTitle" FontSize="13" FontWeight="SemiBold" TextWrapping="Wrap" />
                                    <TextBlock x:Name="RestartDetail" Style="{StaticResource Body}" />
                                </StackPanel>
                            </DockPanel>
                        </Border>
                    </Border>

                    <!-- One ruled table: mark, setting, now, recommended, effect, action -->
                    <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,1,0,1" Padding="24,7">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="26" />
                                <ColumnDefinition Width="1.3*" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="2.4*" />
                                <ColumnDefinition Width="110" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column="1" Text="SETTING" Style="{StaticResource Key}" />
                            <TextBlock Grid.Column="2" Text="NOW" Style="{StaticResource Key}" />
                            <TextBlock Grid.Column="3" Text="RECOMMENDED" Style="{StaticResource Key}" />
                            <TextBlock Grid.Column="4" Text="EFFECT AND DRAWBACK" Style="{StaticResource Key}" />
                        </Grid>
                    </Border>
                    <ItemsControl x:Name="CheckList">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border Style="{StaticResource LedgerRow}" Padding="0">
                                    <Border x:Name="Edge" BorderThickness="2,0,0,0" BorderBrush="Transparent" Padding="22,9,24,9">
                                        <Grid>
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="26" />
                                                <ColumnDefinition Width="1.3*" />
                                                <ColumnDefinition Width="*" />
                                                <ColumnDefinition Width="*" />
                                                <ColumnDefinition Width="2.4*" />
                                                <ColumnDefinition Width="110" />
                                            </Grid.ColumnDefinitions>
                                            <core:Icon x:Name="Mark" Data="{StaticResource Icon.Check}" Foreground="{DynamicResource Good}"
                                                       Width="14" Height="14" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="0,2,0,0" />
                                            <StackPanel Grid.Column="1" Margin="0,0,12,0">
                                                <TextBlock Text="{Binding Title}" FontSize="12.5" FontWeight="SemiBold" TextWrapping="Wrap" />
                                                <TextBlock x:Name="DetailText" Text="{Binding Detail}" Style="{StaticResource Small}" Visibility="Collapsed" />
                                            </StackPanel>
                                            <TextBlock Grid.Column="2" Text="{Binding Status}" Style="{StaticResource Mono}" VerticalAlignment="Top" Margin="0,1,12,0" />
                                            <TextBlock Grid.Column="3" Text="{Binding Recommended}" Style="{StaticResource Mono}"
                                                       Foreground="{DynamicResource Muted}" VerticalAlignment="Top" Margin="0,1,12,0" />
                                            <TextBlock Grid.Column="4" Text="{Binding Description}" Style="{StaticResource Body}" Margin="0,0,12,0" />
                                            <Button x:Name="ActionButton" Grid.Column="5" Content="{Binding ActionLabel}" Style="{StaticResource Btn}"
                                                    HorizontalAlignment="Right" VerticalAlignment="Top" MinWidth="0" Padding="10,3"
                                                    Click="OnCheckAction" Visibility="Collapsed"
                                                    AutomationProperties.Name="{Binding Title, StringFormat='{}{0} action'}" />
                                        </Grid>
                                    </Border>
                                </Border>
                                <DataTemplate.Triggers>
                                    <DataTrigger Binding="{Binding State}" Value="Warn">
                                        <Setter TargetName="Mark" Property="Data" Value="{StaticResource Icon.Warning}" />
                                        <Setter TargetName="Mark" Property="Foreground" Value="{DynamicResource Signal}" />
                                        <Setter TargetName="Edge" Property="BorderBrush" Value="{DynamicResource Signal}" />
                                    </DataTrigger>
                                    <DataTrigger Binding="{Binding State}" Value="Info">
                                        <Setter TargetName="Mark" Property="Data" Value="{StaticResource Icon.Info}" />
                                        <Setter TargetName="Mark" Property="Foreground" Value="{DynamicResource Muted}" />
                                    </DataTrigger>
                                    <DataTrigger Binding="{Binding HasAction}" Value="True">
                                        <Setter TargetName="ActionButton" Property="Visibility" Value="Visible" />
                                    </DataTrigger>
                                    <DataTrigger Binding="{Binding HasDetail}" Value="True">
                                        <Setter TargetName="DetailText" Property="Visibility" Value="Visible" />
                                    </DataTrigger>
                                    <DataTrigger Binding="{Binding ActionLabel}" Value="Fix">
                                        <Setter TargetName="ActionButton" Property="Tag" Value="Primary" />
                                    </DataTrigger>
                                </DataTemplate.Triggers>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <!-- What's using the PC -->
                    <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,1,0,1" Padding="24,7" Margin="0,22,0,0">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="70" />
                                <ColumnDefinition Width="70" />
                                <ColumnDefinition Width="84" />
                                <ColumnDefinition Width="160" />
                                <ColumnDefinition Width="70" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="WHAT'S USING YOUR PC" Style="{StaticResource Key}" />
                            <TextBlock Grid.Column="1" Text="CPU" Style="{StaticResource Key}" HorizontalAlignment="Right" />
                            <TextBlock Grid.Column="2" Text="GPU" Style="{StaticResource Key}" HorizontalAlignment="Right" />
                            <TextBlock Grid.Column="3" Text="MEMORY" Style="{StaticResource Key}" HorizontalAlignment="Right" />
                            <TextBlock Grid.Column="4" Text="LOWER DURING GAMES" Style="{StaticResource Key}" Margin="22,0,0,0" />
                        </Grid>
                    </Border>
                    <ItemsControl x:Name="AppList">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border Style="{StaticResource LedgerRow}" Padding="24,6">
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="*" />
                                            <ColumnDefinition Width="70" />
                                            <ColumnDefinition Width="70" />
                                            <ColumnDefinition Width="84" />
                                            <ColumnDefinition Width="160" />
                                            <ColumnDefinition Width="70" />
                                        </Grid.ColumnDefinitions>
                                        <TextBlock Text="{Binding Name}" FontSize="12.5" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                                        <TextBlock Grid.Column="1" Text="{Binding Cpu}" Style="{StaticResource Mono}" HorizontalAlignment="Right" />
                                        <TextBlock Grid.Column="2" Text="{Binding Gpu}" Style="{StaticResource Mono}" HorizontalAlignment="Right" />
                                        <TextBlock Grid.Column="3" Text="{Binding Ram}" Style="{StaticResource Mono}" HorizontalAlignment="Right"
                                                   Foreground="{DynamicResource Muted}" />
                                        <CheckBox Grid.Column="4" Style="{StaticResource Switch}" Margin="22,0,0,0" HorizontalAlignment="Left"
                                                  IsChecked="{Binding IsBoostTarget, Mode=TwoWay}" IsEnabled="{Binding CanLower}"
                                                  AutomationProperties.Name="{Binding Name, StringFormat='Lower {0} during games'}" />
                                        <Button Grid.Column="5" Content="End" Style="{StaticResource Btn}" MinWidth="0" Padding="12,3"
                                                HorizontalAlignment="Right" Click="OnEndApp" IsEnabled="{Binding CanEnd}"
                                                AutomationProperties.Name="{Binding Name, StringFormat='End {0}'}" />
                                    </Grid>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <TextBlock Style="{StaticResource Small}" Margin="24,8,24,24"
                               Text="Apps are grouped by name (Edge's many processes count as one) and limited to your account. Windows system processes can't be ended or lowered. Ending an app asks first and closes it without saving." />
                </StackPanel>
            </ScrollViewer>
```

- [ ] **Step 4: Code**

In `RefreshOptimizer`, replace `FixAllButton.IsEnabled = _checks.OfType<Tweak>().Any(t => t.NeedsFix);` with:

```csharp
        var pending = _checks.OfType<Tweak>().Count(t => t.NeedsFix);
        FixAllButton.IsEnabled = pending > 0;
        FixAllButton.Content = pending switch
        {
            0 => "Nothing to apply",
            1 => "Apply 1 recommendation",
            _ => $"Apply {pending} recommendations",
        };
```

- [ ] **Step 5: Build and test**

Run: `dotnet build PCSpec.slnx` (0 warnings), `dotnet test PCSpec.slnx --filter "FullyQualifiedName~OptimizerPageTests"` (passes), then the full suite.

- [ ] **Step 6: Commit**

```bash
git add PCSpecOverlay PCSpecOverlay.Tests/PagesTests.cs
git commit -F- <<'EOF'
Rebuild the Optimizer page as one ledger

Co-Authored-By: <model> <noreply@anthropic.com>
EOF
```

---

### Task 4: Gaming — Pipeline

**Files:**
- Modify: `PCSpecOverlay/ControlCenterWindow.xaml` (GAMING `TabItem`), `PCSpecOverlay/ControlCenterWindow.xaml.cs`
- Test: `PCSpecOverlay.Tests/PagesTests.cs` (append)

- [ ] **Step 1: Write the failing test**

```csharp
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
```

- [ ] **Step 2: Run to verify failure** — `--filter "FullyQualifiedName~GamingPageTests"`; expected: 1 failed.

- [ ] **Step 3: Replace the page**

Replace everything inside the GAMING `<TabItem …>` with:

```xml
            <DockPanel>
                <!-- Shown while gaming mode is on or paused -->
                <Border x:Name="GamingBar" DockPanel.Dock="Top" Visibility="Collapsed" Background="{DynamicResource Panel}"
                        BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1" Padding="24,7">
                    <DockPanel>
                        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                            <Button x:Name="GamingPause" Style="{StaticResource Btn}" MinWidth="0" Padding="10,2" Click="OnStopOptimizations"
                                    AutomationProperties.Name="Pause gaming mode">
                                <StackPanel Orientation="Horizontal">
                                    <core:Icon Data="{StaticResource Icon.Pause}" Width="12" Height="12" Margin="0,0,6,0" VerticalAlignment="Center" />
                                    <TextBlock Text="Pause" />
                                </StackPanel>
                            </Button>
                            <Button x:Name="GamingResume" Content="Resume" Style="{StaticResource Btn}" Tag="Primary" Padding="10,2"
                                    Click="OnResume" Visibility="Collapsed" />
                        </StackPanel>
                        <Rectangle x:Name="GamingLed" Width="6" Height="6" Fill="{DynamicResource Good}" Margin="0,0,8,0" VerticalAlignment="Center" />
                        <TextBlock x:Name="GamingBarText" FontSize="12" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                    </DockPanel>
                </Border>

                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <StackPanel>
                        <StackPanel Margin="24,18,24,14">
                            <TextBlock Text="Gaming" Style="{StaticResource PageTitle}" />
                            <TextBlock Style="{StaticResource Body}"
                                       Text="While a game is in front, gaming mode takes these steps and puts everything back afterwards. If you change a setting yourself during a game, your choice is kept." />
                        </StackPanel>

                        <!-- The four steps, left to right -->
                        <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,1,0,1">
                            <UniformGrid Columns="4">
                                <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,0,1,0" Padding="16,12,16,14">
                                    <StackPanel>
                                        <DockPanel>
                                            <CheckBox DockPanel.Dock="Right" Style="{StaticResource Switch}" IsChecked="{Binding AutoGamingMode}"
                                                      AutomationProperties.Name="Turn gaming mode on automatically" />
                                            <TextBlock Text="01" Style="{StaticResource Mono}" Foreground="{DynamicResource Muted}" Margin="0,0,8,0" />
                                            <TextBlock Text="Detect" FontSize="13" FontWeight="SemiBold" VerticalAlignment="Center" />
                                        </DockPanel>
                                        <TextBlock Style="{StaticResource Body}" Margin="0,8,0,0" Text="A game from your list renders at 30+ FPS in front." />
                                        <DockPanel Margin="0,10,0,0">
                                            <TextBlock DockPanel.Dock="Right" Style="{StaticResource Mono}" Margin="8,0,0,0"
                                                       Text="{Binding Value, ElementName=EnterSlider, StringFormat='after {0:0} s'}" />
                                            <Slider x:Name="EnterSlider" Minimum="2" Maximum="30" TickFrequency="1" Value="{Binding GamingEnterSeconds}"
                                                    AutomationProperties.Name="Switch on after this many seconds" />
                                        </DockPanel>
                                    </StackPanel>
                                </Border>
                                <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,0,1,0" Padding="16,12,16,14">
                                    <StackPanel>
                                        <DockPanel>
                                            <CheckBox DockPanel.Dock="Right" Style="{StaticResource Switch}" IsChecked="{Binding GamingPowerMode}"
                                                      AutomationProperties.Name="Switch power mode to Best performance" />
                                            <TextBlock Text="02" Style="{StaticResource Mono}" Foreground="{DynamicResource Muted}" Margin="0,0,8,0" />
                                            <TextBlock Text="Power" FontSize="13" FontWeight="SemiBold" VerticalAlignment="Center" />
                                        </DockPanel>
                                        <TextBlock Style="{StaticResource Body}" Margin="0,8,0,0"
                                                   Text="Switch to Best performance for a higher CPU boost. Your power mode is remembered and put back." />
                                    </StackPanel>
                                </Border>
                                <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,0,1,0" Padding="16,12,16,14">
                                    <StackPanel>
                                        <DockPanel>
                                            <CheckBox DockPanel.Dock="Right" Style="{StaticResource Switch}" IsChecked="{Binding GamingProcessPriority}"
                                                      AutomationProperties.Name="Prioritize the game (Game Boost)" />
                                            <TextBlock Text="03" Style="{StaticResource Mono}" Foreground="{DynamicResource Muted}" Margin="0,0,8,0" />
                                            <TextBlock Text="Boost" FontSize="13" FontWeight="SemiBold" VerticalAlignment="Center" />
                                        </DockPanel>
                                        <TextBlock Style="{StaticResource Body}" Margin="0,8,0,0"
                                                   Text="The game one step up (Above normal), apps you choose one step down. Never touches Windows system processes." />
                                        <TextBlock x:Name="BoostTargetsText" Style="{StaticResource Small}" Margin="0,6,0,0" />
                                    </StackPanel>
                                </Border>
                                <Border Padding="16,12,16,14">
                                    <StackPanel>
                                        <DockPanel>
                                            <TextBlock Text="04" Style="{StaticResource Mono}" Foreground="{DynamicResource Muted}" Margin="0,0,8,0" />
                                            <TextBlock Text="Restore" FontSize="13" FontWeight="SemiBold" VerticalAlignment="Center" />
                                        </DockPanel>
                                        <TextBlock Style="{StaticResource Body}" Margin="0,8,0,0" Text="Everything is put back once no game is in front (Alt+Tab grace)." />
                                        <DockPanel Margin="0,10,0,0">
                                            <TextBlock DockPanel.Dock="Right" Style="{StaticResource Mono}" Margin="8,0,0,0"
                                                       Text="{Binding Value, ElementName=ExitSlider, StringFormat='after {0:0} s'}" />
                                            <Slider x:Name="ExitSlider" Minimum="5" Maximum="120" TickFrequency="5" Value="{Binding GamingExitSeconds}"
                                                    AutomationProperties.Name="Switch back after this many seconds" />
                                        </DockPanel>
                                    </StackPanel>
                                </Border>
                            </UniformGrid>
                        </Border>

                        <!-- Games -->
                        <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1" Padding="24,7" Margin="0,22,0,0">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="148" />
                                    <ColumnDefinition Width="132" />
                                    <ColumnDefinition Width="96" />
                                    <ColumnDefinition Width="40" />
                                </Grid.ColumnDefinitions>
                                <TextBlock Text="GAME" Style="{StaticResource Key}" />
                                <TextBlock Grid.Column="1" Text="MATCH BY" Style="{StaticResource Key}" />
                                <TextBlock Grid.Column="2" Text="OVERLAY" Style="{StaticResource Key}" />
                                <TextBlock Grid.Column="3" Text="OPTIMIZE" Style="{StaticResource Key}" />
                            </Grid>
                        </Border>
                        <ItemsControl x:Name="RuleList">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Border Style="{StaticResource LedgerRow}" Padding="24,6">
                                        <Grid>
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="*" />
                                                <ColumnDefinition Width="148" />
                                                <ColumnDefinition Width="132" />
                                                <ColumnDefinition Width="96" />
                                                <ColumnDefinition Width="40" />
                                            </Grid.ColumnDefinitions>
                                            <TextBox Text="{Binding Match, UpdateSourceTrigger=LostFocus}" Margin="0,0,8,0" VerticalContentAlignment="Center"
                                                     AutomationProperties.Name="Game match" />
                                            <ComboBox Grid.Column="1" Margin="0,0,8,0" MinWidth="0" SelectedValuePath="Tag"
                                                      SelectedValue="{Binding Kind}" AutomationProperties.Name="Match by">
                                                <ComboBoxItem Content="Window title" Tag="{x:Static local:GameMatchKind.WindowTitle}" />
                                                <ComboBoxItem Content="App name" Tag="{x:Static local:GameMatchKind.ProcessName}" />
                                            </ComboBox>
                                            <ComboBox Grid.Column="2" Margin="0,0,8,0" MinWidth="0" SelectedValuePath="Tag"
                                                      SelectedValue="{Binding LayoutChoice}" AutomationProperties.Name="Overlay layout for this game">
                                                <ComboBoxItem Content="Default" Tag="Default" />
                                                <ComboBoxItem Content="Strip" Tag="Compact" />
                                                <ComboBoxItem Content="Panel" Tag="Expanded" />
                                            </ComboBox>
                                            <CheckBox Grid.Column="3" Style="{StaticResource Switch}" IsChecked="{Binding Optimize}" HorizontalAlignment="Left"
                                                      AutomationProperties.Name="{Binding Match, StringFormat='Optimize while {0} is in front'}" />
                                            <Button Grid.Column="4" Style="{StaticResource Btn}" MinWidth="0" Padding="6,4" HorizontalAlignment="Right"
                                                    Click="OnRemoveRule" AutomationProperties.Name="{Binding Match, StringFormat='Remove {0}'}">
                                                <core:Icon Data="{StaticResource Icon.Close}" Width="12" Height="12" />
                                            </Button>
                                        </Grid>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                        <Border Style="{StaticResource LedgerRow}" Padding="24,8">
                            <DockPanel>
                                <Button DockPanel.Dock="Right" Style="{StaticResource Btn}" Click="OnAddRule" AutomationProperties.Name="Add game">
                                    <StackPanel Orientation="Horizontal">
                                        <core:Icon Data="{StaticResource Icon.Plus}" Width="12" Height="12" Margin="0,0,6,0" VerticalAlignment="Center" />
                                        <TextBlock Text="Add game" />
                                    </StackPanel>
                                </Button>
                                <TextBox x:Name="NewRule" Margin="0,0,8,0" AutomationProperties.Name="New game window title" KeyDown="OnNewRuleKey" />
                            </DockPanel>
                        </Border>
                        <TextBlock Style="{StaticResource Small}" Margin="24,6,24,0"
                                   Text="A window counts as a game when it renders at 30+ FPS and matches a rule. Match browser games by part of the window title (e.g. Starve.io) and other games by app name. Turn off Optimize to detect a game without changing anything." />

                        <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1" Padding="24,7" Margin="0,22,0,0">
                            <TextBlock Text="RECENTLY SEEN" Style="{StaticResource Key}" />
                        </Border>
                        <ItemsControl x:Name="RecentList">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Border Style="{StaticResource LedgerRow}" Padding="24,6">
                                        <DockPanel>
                                            <Button DockPanel.Dock="Right" Content="Add" Style="{StaticResource Btn}" MinWidth="0" Padding="10,2"
                                                    Click="OnAddRecent" IsEnabled="{Binding CanAdd}" />
                                            <TextBlock Text="{Binding Text}" FontSize="12" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                                        </DockPanel>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                        <TextBlock x:Name="RecentEmpty" Style="{StaticResource Small}" Margin="24,8,24,0"
                                   Text="Nothing yet. Games appear here while they render in front." />

                        <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1" Padding="24,7" Margin="0,22,0,0">
                            <TextBlock Text="OPTIONS" Style="{StaticResource Key}" />
                        </Border>
                        <Border Style="{StaticResource LedgerRow}">
                            <CheckBox Style="{StaticResource Switch}" IsChecked="{Binding DetectFullscreenGames}">
                                <StackPanel>
                                    <TextBlock Text="Detect fullscreen games" FontSize="13" />
                                    <TextBlock Style="{StaticResource Body}" Text="Shows fullscreen apps that render steadily as games, even if they aren't listed." />
                                </StackPanel>
                            </CheckBox>
                        </Border>
                        <Border Style="{StaticResource LedgerRow}">
                            <CheckBox Style="{StaticResource Switch}" IsChecked="{Binding OptimizeFullscreenGames}">
                                <StackPanel>
                                    <TextBlock Text="Also optimize unlisted fullscreen apps" FontSize="13" />
                                    <TextBlock Style="{StaticResource Body}" Text="Off by default: a fullscreen video would also count. Adding the game to the list is the safer choice." />
                                </StackPanel>
                            </CheckBox>
                        </Border>
                        <Border Style="{StaticResource LedgerRow}" Margin="0,0,0,24">
                            <CheckBox Style="{StaticResource Switch}" IsChecked="{Binding GameBoost}">
                                <StackPanel>
                                    <TextBlock Text="Always prioritize games (even without gaming mode)" FontSize="13" />
                                    <TextBlock Style="{StaticResource Body}" Text="Applies the same one-step priority changes whenever a listed game is in front." />
                                </StackPanel>
                            </CheckBox>
                        </Border>
                    </StackPanel>
                </ScrollViewer>
            </DockPanel>
```

- [ ] **Step 4: Code**

Replace `RefreshGaming`'s first statement (the `GamingStatusLine.Text = …` assignment) with:

```csharp
        var paused = _engine.OptimizationsPaused;
        GamingBar.Visibility = gm.Active || paused ? Visibility.Visible : Visibility.Collapsed;
        GamingBarText.Text = paused ? "Optimizations are paused. Gaming mode won't change anything until you resume."
            : gm.Active ? $"Gaming mode is on for {string.Join(", ", gm.ActiveGames)}: {string.Join("; ", gm.Optimizations)}"
            : "";
        GamingLed.SetResourceReference(Shape.FillProperty, paused ? "Signal" : "Good");
        GamingPause.Visibility = paused ? Visibility.Collapsed : Visibility.Visible;
        GamingResume.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
```

`OnStopOptimizations` and `OnResume` already exist. They refresh the Overview; add a `RefreshGaming();` call at the end of each so the bar updates at once.

- [ ] **Step 5: Build and test** — 0 warnings; `GamingPageTests` passes; full suite passes.

- [ ] **Step 6: Commit** — message: "Rebuild the Gaming page as a four-step pipeline".

---

### Task 5: Overlay — Preview first

**Files:**
- Modify: `PCSpecOverlay/ControlCenterWindow.xaml` (OVERLAY `TabItem`), `PCSpecOverlay/ControlCenterWindow.xaml.cs`, `PCSpecOverlay.Tests/OptiframeTests.cs`
- Test: `PCSpecOverlay.Tests/PagesTests.cs` (append)

**Interfaces:**
- Consumes:
  - `OverlayPreview.Strip/Panel` (Task 1)
  - `Seg`, `LayoutCard` and `CornerTarget` (Task 2)
  - `OverlayPlacement.Place` and `App.MoveOverlay()` (Optiframe)
- Produces: `ControlCenterWindow.Quietly(Action)` (private helper; Task 7 reuses it), and `OverlayMoved()` now redraws the preview.

**Rulings carried into this task:**
- The preview's corner targets replace the Position dropdown, as the spec says. "Custom" can't be picked from a target; it comes only from dragging with Move overlay, and the preview shows it at its pinned spot.
- The Optiframe test `Move_overlay_is_offered_in_the_tray_and_the_control_center` asserted `Tag="Custom"`. It now asserts the corner targets instead.
- The spec's "Click-through" row isn't added back: Optiframe removed that setting, and pinned always means click-through.

- [ ] **Step 1: Write the failing tests**

Append to `PagesTests.cs`:

```csharp
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
```

In `OptiframeTests.cs` `Move_overlay_is_offered_in_the_tray_and_the_control_center`, replace `Assert.Contains("Tag=\"Custom\"", xaml);` with `Assert.Contains("Click=\"OnCornerTarget\"", xaml);`.

- [ ] **Step 2: Run to verify failure** — `--filter "FullyQualifiedName~OverlayPageTests|FullyQualifiedName~MovePinTests"`; expected: 2 failed.

- [ ] **Step 3: Replace the page**

Replace everything inside the OVERLAY `<TabItem …>` with:

```xml
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="360" />
                    </Grid.ColumnDefinitions>

                    <!-- Left: where and how it appears -->
                    <StackPanel Margin="24,18,24,24">
                        <TextBlock Text="Overlay" Style="{StaticResource PageTitle}" />
                        <TextBlock Style="{StaticResource Body}" Text="What you see in games. Changes apply immediately." Margin="0,3,0,14" />

                        <Border BorderBrush="{DynamicResource Line}" BorderThickness="1">
                            <Viewbox Stretch="Uniform">
                                <Canvas x:Name="PreviewScreen" Width="320" Height="180" Background="{DynamicResource Panel}" ClipToBounds="True">
                                    <Button x:Name="CornerTopLeft" Style="{StaticResource CornerTarget}" CommandParameter="TopLeft"
                                            Canvas.Left="6" Canvas.Top="6" Click="OnCornerTarget" AutomationProperties.Name="Move the overlay to the top left" />
                                    <Button x:Name="CornerTopRight" Style="{StaticResource CornerTarget}" CommandParameter="TopRight"
                                            Canvas.Left="250" Canvas.Top="6" Click="OnCornerTarget" AutomationProperties.Name="Move the overlay to the top right" />
                                    <Button x:Name="CornerBottomLeft" Style="{StaticResource CornerTarget}" CommandParameter="BottomLeft"
                                            Canvas.Left="6" Canvas.Top="158" Click="OnCornerTarget" AutomationProperties.Name="Move the overlay to the bottom left" />
                                    <Button x:Name="CornerBottomRight" Style="{StaticResource CornerTarget}" CommandParameter="BottomRight"
                                            Canvas.Left="250" Canvas.Top="158" Click="OnCornerTarget" AutomationProperties.Name="Move the overlay to the bottom right" />
                                    <Border x:Name="PreviewOverlay" IsHitTestVisible="False" Background="{DynamicResource Bg}"
                                            BorderBrush="{DynamicResource Line}" BorderThickness="0.5" Padding="4,2">
                                        <TextBlock x:Name="PreviewText" FontFamily="Cascadia Mono, Consolas" FontSize="7" Foreground="{DynamicResource Text}" />
                                    </Border>
                                </Canvas>
                            </Viewbox>
                        </Border>
                        <DockPanel Margin="0,6,0,0">
                            <TextBlock x:Name="PreviewHotkey" DockPanel.Dock="Right" Style="{StaticResource Mono}" Foreground="{DynamicResource Muted}" FontSize="11" />
                            <TextBlock x:Name="PreviewCaption" Style="{StaticResource Small}" Margin="0" />
                        </DockPanel>

                        <Grid Margin="0,14,0,0">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <ComboBox x:Name="DisplayBox" SelectedValuePath="Tag" SelectedValue="{Binding Monitor}" AutomationProperties.Name="Display"
                                      HorizontalAlignment="Left" MinWidth="220" />
                            <Button Grid.Column="1" Style="{StaticResource Btn}" Click="OnMoveOverlay" AutomationProperties.Name="Move overlay">
                                <StackPanel Orientation="Horizontal">
                                    <core:Icon Data="{StaticResource Icon.Pointer}" Width="12" Height="12" Margin="0,0,6,0" VerticalAlignment="Center" />
                                    <TextBlock Text="Move overlay" />
                                </StackPanel>
                            </Button>
                        </Grid>
                        <TextBlock Style="{StaticResource Small}" Margin="0,6,0,0"
                                   Text="Move overlay unlocks it so you can drag it anywhere on any screen; press Pin on the overlay to lock it there. Clicks pass through to your game whenever it's pinned." />

                        <UniformGrid Columns="2" Margin="0,16,0,0">
                            <RadioButton x:Name="StripCard" GroupName="Layout" Style="{StaticResource LayoutCard}" Tag="Compact" Click="OnLayoutCard"
                                         Margin="0,0,6,0" AutomationProperties.Name="Strip layout">
                                <StackPanel>
                                    <TextBlock Text="Strip" FontSize="13" FontWeight="SemiBold" />
                                    <TextBlock Style="{StaticResource Small}" Text="One ruled line. The default." />
                                </StackPanel>
                            </RadioButton>
                            <RadioButton x:Name="PanelCard" GroupName="Layout" Style="{StaticResource LayoutCard}" Tag="Expanded" Click="OnLayoutCard"
                                         Margin="6,0,0,0" AutomationProperties.Name="Panel layout">
                                <StackPanel>
                                    <TextBlock Text="Panel" FontSize="13" FontWeight="SemiBold" />
                                    <TextBlock Style="{StaticResource Small}" Text="Adds the frame-time trace and a row per reading." />
                                </StackPanel>
                            </RadioButton>
                        </UniformGrid>
                    </StackPanel>

                    <!-- Right: the other settings -->
                    <Border Grid.Column="1" BorderBrush="{DynamicResource Line}" BorderThickness="1,0,0,0">
                        <StackPanel>
                            <Border Style="{StaticResource LedgerRow}" Padding="20,12">
                                <CheckBox x:Name="ShowOverlay" Style="{StaticResource Switch}" Click="OnShowOverlay">
                                    <StackPanel>
                                        <TextBlock Text="Show the overlay" FontSize="13" />
                                        <TextBlock x:Name="HotkeyHint" Style="{StaticResource Body}" />
                                    </StackPanel>
                                </CheckBox>
                            </Border>
                            <Border Style="{StaticResource LedgerRow}" Padding="20,10">
                                <DockPanel>
                                    <TextBlock DockPanel.Dock="Right" Style="{StaticResource Mono}" MinWidth="40" TextAlignment="Right"
                                               Text="{Binding Value, ElementName=OpacitySlider, StringFormat='{}{0:P0}'}" />
                                    <TextBlock Text="Opacity" FontSize="13" Width="86" VerticalAlignment="Center" />
                                    <Slider x:Name="OpacitySlider" Minimum="0.3" Maximum="1" TickFrequency="0.05" Value="{Binding Opacity}"
                                            Margin="0,0,10,0" AutomationProperties.Name="Overlay opacity" />
                                </DockPanel>
                            </Border>
                            <Border Style="{StaticResource LedgerRow}" Padding="20,10">
                                <DockPanel>
                                    <TextBlock DockPanel.Dock="Right" Style="{StaticResource Mono}" MinWidth="40" TextAlignment="Right"
                                               Text="{Binding Value, ElementName=ScaleSlider, StringFormat='{}{0:P0}'}" />
                                    <TextBlock Text="Text size" FontSize="13" Width="86" VerticalAlignment="Center" />
                                    <Slider x:Name="ScaleSlider" Minimum="0.75" Maximum="1.75" TickFrequency="0.05" Value="{Binding Scale}"
                                            Margin="0,0,10,0" AutomationProperties.Name="Overlay text size" />
                                </DockPanel>
                            </Border>
                            <Border Style="{StaticResource LedgerRow}" Padding="20,10">
                                <StackPanel>
                                    <TextBlock Text="SHOW" Style="{StaticResource Key}" Margin="0,0,0,8" />
                                    <UniformGrid Columns="2">
                                        <CheckBox Style="{StaticResource Switch}" IsChecked="{Binding ShowFrameTime}" Margin="0,0,14,8" Content="Frame time" />
                                        <CheckBox Style="{StaticResource Switch}" IsChecked="{Binding ShowOnePercentLow}" Margin="0,0,0,8" Content="1% low" />
                                        <CheckBox Style="{StaticResource Switch}" IsChecked="{Binding ShowGpu}" Margin="0,0,14,8" Content="GPU" />
                                        <CheckBox Style="{StaticResource Switch}" IsChecked="{Binding ShowVram}" Margin="0,0,0,8" Content="VRAM" />
                                        <CheckBox Style="{StaticResource Switch}" IsChecked="{Binding ShowCpu}" Margin="0,0,14,8" Content="CPU" />
                                        <CheckBox Style="{StaticResource Switch}" IsChecked="{Binding ShowRam}" Margin="0,0,0,8" Content="RAM" />
                                        <CheckBox Style="{StaticResource Switch}" IsChecked="{Binding ShowSlowdowns}" Margin="0,0,14,0" Content="Slowdowns" />
                                    </UniformGrid>
                                </StackPanel>
                            </Border>
                            <Border Style="{StaticResource LedgerRow}" Padding="20,10">
                                <DockPanel>
                                    <ComboBox x:Name="FpsRefreshBox" DockPanel.Dock="Right" SelectedValuePath="Tag" SelectedValue="{Binding FpsRefreshMs}"
                                              AutomationProperties.Name="FPS refresh" />
                                    <StackPanel Margin="0,0,12,0">
                                        <TextBlock Text="FPS refresh" FontSize="13" />
                                        <TextBlock Style="{StaticResource Small}" Text="Once a second uses the least CPU." />
                                    </StackPanel>
                                </DockPanel>
                            </Border>
                            <Border Style="{StaticResource LedgerRow}" Padding="20,10">
                                <DockPanel>
                                    <ComboBox x:Name="StatsRefreshBox" DockPanel.Dock="Right" SelectedValuePath="Tag" SelectedValue="{Binding StatsRefreshMs}"
                                              AutomationProperties.Name="Sensor refresh" />
                                    <StackPanel Margin="0,0,12,0">
                                        <TextBlock Text="Sensor refresh" FontSize="13" />
                                        <TextBlock Style="{StaticResource Small}" Text="Slower uses less power." />
                                    </StackPanel>
                                </DockPanel>
                            </Border>
                            <Border Style="{StaticResource LedgerRow}" Padding="20,10">
                                <DockPanel>
                                    <ComboBox DockPanel.Dock="Right" SelectedValuePath="Tag" SelectedValue="{Binding OverlayTheme}" AutomationProperties.Name="Overlay theme">
                                        <ComboBoxItem Content="Always dark" Tag="AlwaysDark" />
                                        <ComboBoxItem Content="Always light" Tag="AlwaysLight" />
                                        <ComboBoxItem Content="Same as app" Tag="SameAsApp" />
                                    </ComboBox>
                                    <StackPanel Margin="0,0,12,0">
                                        <TextBlock Text="Overlay theme" FontSize="13" />
                                        <TextBlock Style="{StaticResource Small}" Text="Dark reads best over most games." />
                                    </StackPanel>
                                </DockPanel>
                            </Border>
                            <Border Style="{StaticResource LedgerRow}" Padding="20,12">
                                <StackPanel>
                                    <CheckBox Style="{StaticResource Switch}" IsChecked="{Binding GpuRendering}">
                                        <StackPanel>
                                            <TextBlock Text="Draw with the graphics chip" FontSize="13" />
                                            <TextBlock Style="{StaticResource Body}" Text="Off by default. Measured on this laptop: about 0.3% of one CPU core less, but about 145 MB more memory. Takes effect after the app restarts." />
                                        </StackPanel>
                                    </CheckBox>
                                    <Button x:Name="RestartAppButton" Content="Restart the app now" Style="{StaticResource Btn}" Tag="Primary"
                                            HorizontalAlignment="Left" Margin="0,10,0,0" Visibility="Collapsed" Click="OnRestartApp" />
                                </StackPanel>
                            </Border>
                        </StackPanel>
                    </Border>
                </Grid>
            </ScrollViewer>
```

- [ ] **Step 4: Code**

1. Add a constant next to `AppRows`: `private const int PreviewMargin = 6;`.
2. In `OnEdited`, after the theme-button line, add:
   `if (e.OriginalSource is RadioButton) return; // layout cards and appearance segments save through their own clicks`
3. Add a helper after `OnEdited`:

```csharp
    /// <summary>Sets controls from settings without that counting as an edit.</summary>
    private void Quietly(Action set)
    {
        var was = _loading;
        _loading = true;
        try { set(); }
        finally { _loading = was; }
    }
```

4. Replace `OverlayMoved()`'s body with:

```csharp
        Quietly(() => DisplayBox.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateTarget());
        RefreshPreview();
```

   Update its summary to: "The overlay was dragged or pinned: show its new display and spot without saving again."
5. Add the page code (in the `// ---- overlay / general ----` region):

```csharp
    /// <summary>A dashed corner on the preview: puts the overlay in that corner.</summary>
    private void OnCornerTarget(object sender, RoutedEventArgs e)
    {
        _settings.Position = (string)((Button)sender).CommandParameter;
        Save();
    }

    private void OnLayoutCard(object sender, RoutedEventArgs e)
    {
        _settings.Layout = (string)((FrameworkElement)sender).Tag;
        Save();
    }

    /// <summary>Draws the overlay on the preview screen where and how it will appear, with live readings.</summary>
    private void RefreshPreview()
    {
        var t = _engine.Latest;
        PreviewText.Text = _settings.Layout == Settings.Compact
            ? OverlayPreview.Strip(_settings, t.Fps?.Fps, t.Fps?.FrameTimeMs, t.Gpu.TempC, t.CpuTempC)
            : OverlayPreview.Panel(_settings, t.Fps?.Fps, t.Fps?.FrameTimeMs, t.Gpu.TempC, t.CpuTempC, t.Ram?.UsedGb);
        PreviewText.FontSize = 7 * _settings.Scale;
        PreviewOverlay.Opacity = _settings.Opacity;
        PreviewOverlay.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = PreviewOverlay.DesiredSize;
        var (x, y) = OverlayPlacement.Place(_settings.Position, _settings.CustomX, _settings.CustomY,
            (0, 0, (int)PreviewScreen.Width, (int)PreviewScreen.Height), (int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), PreviewMargin);
        Canvas.SetLeft(PreviewOverlay, x);
        Canvas.SetTop(PreviewOverlay, y);
        foreach (var target in new[] { CornerTopLeft, CornerTopRight, CornerBottomLeft, CornerBottomRight })
            target.Tag = (string)target.CommandParameter == _settings.Position ? "On" : null;

        var display = Displays.Find(_settings.Monitor);
        PreviewCaption.Text = (_settings.Position == Settings.Custom ? "Pinned spot. Use Move overlay to drag it" : "Click a corner to move it there") +
                              (display is null ? "" : $" · {display.Label}");
        PreviewHotkey.Text = _app.HotkeyInUse ?? "";
    }
```

6. In `Save()`, add `RefreshPreview();` as the last line.
7. In `Refresh()`, add `case 3: RefreshPreview(); break;`.
8. In `RefreshSlow()`, after `ShowOverlay.IsChecked = …`, add:

```csharp
        Quietly(() =>
        {
            StripCard.IsChecked = _settings.Layout == Settings.Compact;
            PanelCard.IsChecked = _settings.Layout == Settings.Expanded;
        });
        RefreshPreview();
```

- [ ] **Step 5: Build and test** — 0 warnings; `OverlayPageTests` and `MovePinTests` pass; full suite passes.

- [ ] **Step 6: Commit** — message: "Rebuild the Overlay page around a live preview with corner targets".

---

### Task 6: Temperatures & alerts — Scales

**Files:**
- Modify: `PCSpecOverlay/ControlCenterWindow.xaml` (ALERTS `TabItem`, plus `xmlns:controls` on the Window), `PCSpecOverlay/ControlCenterWindow.xaml.cs`
- Test: `PCSpecOverlay.Tests/PagesTests.cs` (append)

**Interfaces:**
- Consumes: `TempScaleControl` (Task 2).

- [ ] **Step 1: Write the failing test**

```csharp
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
```

- [ ] **Step 2: Run to verify failure** — expected: 1 failed.

- [ ] **Step 3: Replace the page**

Add `xmlns:controls="clr-namespace:PCSpecOverlay.Controls"` to the `<Window …>` start tag. Replace everything inside the ALERTS `<TabItem …>` with:

```xml
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <StackPanel>
                    <StackPanel Margin="24,18,24,14">
                        <TextBlock Text="Temperatures &amp; alerts" Style="{StaticResource PageTitle}" />
                        <TextBlock Style="{StaticResource Body}"
                                   Text="Drag a handle, or focus it and use the arrow keys, to change a level. The white line is the current reading. Warning levels color the overlay amber; hot levels color it red." />
                    </StackPanel>

                    <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,1,0,1" Padding="24,14,24,10">
                        <StackPanel>
                            <DockPanel>
                                <TextBlock x:Name="GpuNow" DockPanel.Dock="Right" Style="{StaticResource Mono}" FontSize="14" />
                                <TextBlock Text="GPU" FontSize="13" FontWeight="SemiBold" VerticalAlignment="Center" />
                                <TextBlock x:Name="GpuSource" Style="{StaticResource Small}" Margin="10,0,0,0" VerticalAlignment="Center" />
                            </DockPanel>
                            <controls:TempScaleControl x:Name="GpuScale" Sensor="GPU" Margin="0,10,0,0" />
                        </StackPanel>
                    </Border>
                    <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1" Padding="24,14,24,10">
                        <StackPanel>
                            <DockPanel>
                                <TextBlock x:Name="CpuNow" DockPanel.Dock="Right" Style="{StaticResource Mono}" FontSize="14" />
                                <TextBlock Text="CPU" FontSize="13" FontWeight="SemiBold" VerticalAlignment="Center" />
                                <TextBlock x:Name="CpuSource" Style="{StaticResource Small}" Margin="10,0,0,0" VerticalAlignment="Center" />
                            </DockPanel>
                            <controls:TempScaleControl x:Name="CpuScale" Sensor="CPU" Margin="0,10,0,0" />
                        </StackPanel>
                    </Border>

                    <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1" Padding="24,7" Margin="0,22,0,0">
                        <TextBlock Text="NOTIFICATIONS" Style="{StaticResource Key}" />
                    </Border>
                    <Border Style="{StaticResource LedgerRow}">
                        <CheckBox Style="{StaticResource Switch}" IsChecked="{Binding Alerts}">
                            <StackPanel>
                                <TextBlock Text="Notify me about slowdowns and high temperatures" FontSize="13" />
                                <TextBlock Style="{StaticResource Body}" Text="Confirmed slowdowns (the driver or Windows reports the hardware slowing itself down) and temperatures above your hot levels, which isn't a fault on its own." />
                            </StackPanel>
                        </CheckBox>
                    </Border>
                    <Border Style="{StaticResource LedgerRow}">
                        <DockPanel>
                            <TextBlock DockPanel.Dock="Right" Style="{StaticResource Mono}" MinWidth="56" TextAlignment="Right"
                                       Text="{Binding Value, ElementName=AfterSlider, StringFormat='{}{0:0} s'}" />
                            <Slider x:Name="AfterSlider" DockPanel.Dock="Right" Width="220" Minimum="5" Maximum="120" TickFrequency="5"
                                    Value="{Binding AlertAfterSeconds}" Margin="12,0,10,0" AutomationProperties.Name="Only notify if it lasts this many seconds" />
                            <StackPanel>
                                <TextBlock Text="Only if it lasts" FontSize="13" />
                                <TextBlock Style="{StaticResource Small}" Text="Brief spikes are ignored." />
                            </StackPanel>
                        </DockPanel>
                    </Border>
                    <Border Style="{StaticResource LedgerRow}" Margin="0,0,0,24">
                        <DockPanel>
                            <TextBlock DockPanel.Dock="Right" Style="{StaticResource Mono}" MinWidth="56" TextAlignment="Right"
                                       Text="{Binding Value, ElementName=CooldownSlider, StringFormat='{}{0:0} min'}" />
                            <Slider x:Name="CooldownSlider" DockPanel.Dock="Right" Width="220" Minimum="1" Maximum="120" TickFrequency="1"
                                    Value="{Binding AlertCooldownMinutes}" Margin="12,0,10,0" AutomationProperties.Name="Notify at most once every this many minutes" />
                            <StackPanel>
                                <TextBlock Text="At most once every" FontSize="13" />
                                <TextBlock Style="{StaticResource Small}" Text="Per kind of alert. If the same problem keeps going, reminders slow to hourly after the third." />
                            </StackPanel>
                        </DockPanel>
                    </Border>
                </StackPanel>
            </ScrollViewer>
```

- [ ] **Step 4: Code**

1. In the constructor, after `RuleList.ItemsSource = _rules;`:

```csharp
        GpuScale.LevelsChanged += (warn, hot) => { _settings.GpuTempWarn = warn; _settings.GpuTempHot = hot; ScheduleSave(); };
        CpuScale.LevelsChanged += (warn, hot) => { _settings.CpuTempWarn = warn; _settings.CpuTempHot = hot; ScheduleSave(); };
```

2. Extract the two lines at the end of `OnEdited` into `private void ScheduleSave() { if (_loading) return; _saveSoon.Stop(); _saveSoon.Start(); }`. `OnEdited` then ends with `ScheduleSave();`.
3. Add `case 4: RefreshAlerts(); break;` to `Refresh()`, and:

```csharp
    // ---- alerts ----

    private void RefreshAlerts()
    {
        var t = _engine.Latest;
        GpuScale.SetCurrent(t.Gpu.TempC);
        CpuScale.SetCurrent(t.CpuTempC);
        GpuNow.Text = t.Gpu.TempC is { } g ? $"{g:0} °C" : t.Gpu.Status == GpuStatus.Sleeping ? "Sleeping" : "—";
        GpuNow.Foreground = Temp(t.Gpu.TempC, _settings.GpuTempWarn, _settings.GpuTempHot);
        CpuNow.Text = t.CpuTempC is { } c ? $"{c:0} °C" : "—";
        CpuNow.Foreground = Temp(t.CpuTempC, _settings.CpuTempWarn, _settings.CpuTempHot);
    }
```

4. In `RefreshSlow()`, add:

```csharp
        GpuScale.SetLevels(_settings.GpuTempWarn, _settings.GpuTempHot);
        CpuScale.SetLevels(_settings.CpuTempWarn, _settings.CpuTempHot);
        GpuSource.Text = _engine.Gpu is { } gpu ? $"{gpu.Name.Replace("NVIDIA ", "").Replace("GeForce ", "")} · NVIDIA driver" : "No NVIDIA GPU found";
        CpuSource.Text = _engine.System is { Ready: true, ThermalZones: 0 } ? "No thermal zones exposed by this PC" : "Hottest ACPI thermal zone (package, not per core)";
        RefreshAlerts();
```

- [ ] **Step 5: Build and test** — 0 warnings; `AlertsPageTests` passes; full suite passes.

- [ ] **Step 6: Commit** — message: "Rebuild Temperatures & alerts with draggable scales".

---

### Task 7: Settings — Status strip

**Files:**
- Modify: `PCSpecOverlay/ControlCenterWindow.xaml` (Settings `TabItem`, its comment says GENERAL), `PCSpecOverlay/ControlCenterWindow.xaml.cs`
- Test: `PCSpecOverlay.Tests/PagesTests.cs` (append)

**Interfaces:**
- Consumes: `Seg` (Task 2), `Quietly` (Task 5).

- [ ] **Step 1: Write the failing test**

```csharp
public class SettingsPageTests
{
    [Fact]
    public void Settings_opens_with_a_status_strip()
    {
        var page = PageXaml.Tab("Settings");
        foreach (var part in new[] { "Text=\"VERSION\"", "Text=\"STARTUP\"", "Text=\"APPEARANCE\"", "x:Name=\"VersionValue\"", "x:Name=\"UpdateStatus\"",
                     "x:Name=\"CheckUpdatesButton\"", "x:Name=\"InstallUpdateButton\"", "x:Name=\"StartupSwitch\"", "x:Name=\"StartupValue\"",
                     "x:Name=\"AppearanceLight\"", "x:Name=\"AppearanceDark\"", "x:Name=\"AppearanceMatch\"", "x:Name=\"AppearanceValue\"",
                     "Click=\"OnAppearance\"", "x:Name=\"HotkeyBox\"", "{Binding CheckForUpdates}", "x:Name=\"SleepSwitch\"",
                     "x:Name=\"RetentionSlider\"", "x:Name=\"SessionList\"", "Click=\"OnResetSettings\"" })
            Assert.Contains(part, page);
        foreach (var gone in new[] { "AppearanceBox", "UpdateVersion", "CardBox", "\"H1\"" })
            Assert.DoesNotContain(gone, page);
    }
}
```

- [ ] **Step 2: Run to verify failure** — expected: 1 failed.

- [ ] **Step 3: Replace the page**

Replace everything inside the Settings `<TabItem …>` with:

```xml
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <StackPanel>
                    <StackPanel Margin="24,18,24,14">
                        <TextBlock Text="Settings" Style="{StaticResource PageTitle}" />
                        <TextBlock Style="{StaticResource Body}" Text="Version, startup and appearance, then the shortcut, power use and history." />
                    </StackPanel>

                    <!-- Status strip -->
                    <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,1,0,1">
                        <UniformGrid Columns="3">
                            <Border Style="{StaticResource ReadingCell}">
                                <StackPanel>
                                    <TextBlock Text="VERSION" Style="{StaticResource ReadingKey}" />
                                    <TextBlock x:Name="VersionValue" Style="{StaticResource ReadingValue}" FontSize="22" />
                                    <TextBlock x:Name="UpdateStatus" Style="{StaticResource Small}" />
                                    <WrapPanel Margin="0,10,0,0">
                                        <Button x:Name="CheckUpdatesButton" Style="{StaticResource Btn}" Click="OnCheckUpdates" Margin="0,0,8,6"
                                                AutomationProperties.Name="Check for updates now">
                                            <StackPanel Orientation="Horizontal">
                                                <core:Icon Data="{StaticResource Icon.Refresh}" Width="12" Height="12" Margin="0,0,6,0" VerticalAlignment="Center" />
                                                <TextBlock Text="Check now" />
                                            </StackPanel>
                                        </Button>
                                        <Button x:Name="InstallUpdateButton" Content="Download and install" Style="{StaticResource Btn}" Tag="Primary"
                                                Click="OnInstallUpdate" Visibility="Collapsed" Margin="0,0,8,6" />
                                        <Button x:Name="ReleasePageButton" Content="What's new" Style="{StaticResource Btn}"
                                                Click="OnReleasePage" Visibility="Collapsed" Margin="0,0,8,6" />
                                    </WrapPanel>
                                </StackPanel>
                            </Border>
                            <Border Style="{StaticResource ReadingCell}">
                                <StackPanel>
                                    <TextBlock Text="STARTUP" Style="{StaticResource ReadingKey}" />
                                    <TextBlock x:Name="StartupValue" FontSize="16" FontWeight="SemiBold" Margin="0,8,0,0" />
                                    <TextBlock x:Name="StartupStatus" Style="{StaticResource Small}" />
                                    <CheckBox x:Name="StartupSwitch" Style="{StaticResource Switch}" Click="OnStartupToggle" HorizontalAlignment="Left"
                                              Margin="0,10,0,0" AutomationProperties.Name="Start with Windows" />
                                </StackPanel>
                            </Border>
                            <Border Style="{StaticResource ReadingCell}" BorderThickness="0">
                                <StackPanel>
                                    <TextBlock Text="APPEARANCE" Style="{StaticResource ReadingKey}" />
                                    <TextBlock x:Name="AppearanceValue" FontSize="16" FontWeight="SemiBold" Margin="0,8,0,0" />
                                    <TextBlock Style="{StaticResource Small}" Text="This window. The overlay has its own theme on the Overlay page." />
                                    <Border BorderBrush="{DynamicResource Line}" BorderThickness="1,1,0,1" HorizontalAlignment="Left" Margin="0,10,0,0">
                                        <StackPanel Orientation="Horizontal">
                                            <RadioButton x:Name="AppearanceLight" GroupName="Appearance" Style="{StaticResource Seg}" Tag="Light"
                                                         Click="OnAppearance" AutomationProperties.Name="Light" ToolTip="Light">
                                                <core:Icon Data="{StaticResource Icon.Sun}" Width="14" Height="14" />
                                            </RadioButton>
                                            <RadioButton x:Name="AppearanceDark" GroupName="Appearance" Style="{StaticResource Seg}" Tag="Dark"
                                                         Click="OnAppearance" AutomationProperties.Name="Dark" ToolTip="Dark">
                                                <core:Icon Data="{StaticResource Icon.Moon}" Width="14" Height="14" />
                                            </RadioButton>
                                            <RadioButton x:Name="AppearanceMatch" GroupName="Appearance" Style="{StaticResource Seg}" Tag="MatchWindows"
                                                         Click="OnAppearance" AutomationProperties.Name="Match Windows" ToolTip="Match Windows">
                                                <core:Icon Data="{StaticResource Icon.Monitor}" Width="14" Height="14" />
                                            </RadioButton>
                                        </StackPanel>
                                    </Border>
                                </StackPanel>
                            </Border>
                        </UniformGrid>
                    </Border>

                    <!-- Two columns -->
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,0,1,0">
                            <StackPanel>
                                <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1" Padding="24,7" Margin="0,18,0,0">
                                    <TextBlock Text="SHORTCUT AND UPDATES" Style="{StaticResource Key}" />
                                </Border>
                                <Border Style="{StaticResource LedgerRow}">
                                    <StackPanel>
                                        <TextBlock Text="Show or hide the overlay" FontSize="13" />
                                        <DockPanel Margin="0,8,0,0">
                                            <Button DockPanel.Dock="Right" Content="Use this shortcut" Style="{StaticResource Btn}" Margin="8,0,0,0" Click="OnApplyHotkey" />
                                            <TextBox x:Name="HotkeyBox" IsReadOnly="True" PreviewKeyDown="OnHotkeyKey" AutomationProperties.Name="Overlay shortcut"
                                                     ToolTip="Click here and press the new shortcut" FontFamily="Cascadia Mono, Consolas" />
                                        </DockPanel>
                                        <TextBlock x:Name="HotkeyResult" Style="{StaticResource Body}" />
                                        <TextBlock Style="{StaticResource Small}" Text="Click the box, press the keys you want (e.g. Ctrl+Alt+P), then choose Use this shortcut. If another app already uses it, your current shortcut stays." />
                                    </StackPanel>
                                </Border>
                                <Border Style="{StaticResource LedgerRow}">
                                    <CheckBox Style="{StaticResource Switch}" IsChecked="{Binding CheckForUpdates}">
                                        <StackPanel>
                                            <TextBlock Text="Check for updates once a day" FontSize="13" />
                                            <TextBlock Style="{StaticResource Body}" Text="Asks GitHub for the latest release; nothing personal is sent. Updates install only when you choose, after the download's checksum is verified." />
                                        </StackPanel>
                                    </CheckBox>
                                </Border>
                            </StackPanel>
                        </Border>
                        <StackPanel Grid.Column="1">
                            <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1" Padding="24,7" Margin="0,18,0,0">
                                <TextBlock Text="POWER AND HISTORY" Style="{StaticResource Key}" />
                            </Border>
                            <Border Style="{StaticResource LedgerRow}">
                                <CheckBox x:Name="SleepSwitch" Style="{StaticResource Switch}" IsChecked="{Binding LetGpuSleep}">
                                    <StackPanel>
                                        <TextBlock Text="Let the laptop GPU sleep" FontSize="13" />
                                        <TextBlock Style="{StaticResource Body}" Text="When the NVIDIA GPU is powered down, don't wake it just to read its sensors." />
                                    </StackPanel>
                                </CheckBox>
                            </Border>
                            <Border Style="{StaticResource LedgerRow}">
                                <CheckBox Style="{StaticResource Switch}" IsChecked="{Binding HistoryEnabled}">
                                    <StackPanel>
                                        <TextBlock Text="Keep performance history" FontSize="13" />
                                        <TextBlock Style="{StaticResource Body}" Text="The last 10 minutes stay in memory for the graphs. A short summary of each gaming session is saved on this PC; nothing is uploaded." />
                                    </StackPanel>
                                </CheckBox>
                            </Border>
                            <Border Style="{StaticResource LedgerRow}">
                                <DockPanel>
                                    <TextBlock DockPanel.Dock="Right" Style="{StaticResource Mono}" MinWidth="64" TextAlignment="Right"
                                               Text="{Binding Value, ElementName=RetentionSlider, StringFormat='{}{0:0} days'}" />
                                    <Slider x:Name="RetentionSlider" DockPanel.Dock="Right" Width="160" Minimum="1" Maximum="90" TickFrequency="1"
                                            Value="{Binding HistoryRetentionDays}" Margin="12,0,10,0" AutomationProperties.Name="Keep session summaries for this many days" />
                                    <TextBlock Text="Keep session summaries for" FontSize="13" VerticalAlignment="Center" TextWrapping="Wrap" />
                                </DockPanel>
                            </Border>
                            <Border Style="{StaticResource LedgerRow}">
                                <StackPanel>
                                    <TextBlock Text="RECENT SESSIONS" Style="{StaticResource Key}" Margin="0,0,0,6" />
                                    <ItemsControl x:Name="SessionList">
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate>
                                                <TextBlock Text="{Binding}" FontFamily="Cascadia Mono, Consolas" FontSize="11" Foreground="{DynamicResource Text}"
                                                           TextWrapping="Wrap" Margin="0,0,0,5" />
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                    <TextBlock x:Name="SessionsEmpty" Style="{StaticResource Small}" Text="No sessions saved yet." />
                                </StackPanel>
                            </Border>
                        </StackPanel>
                    </Grid>

                    <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,1,0,0" Padding="24,14" Margin="0,18,0,0">
                        <DockPanel>
                            <Button DockPanel.Dock="Right" Content="Restore defaults" Style="{StaticResource Btn}" Click="OnResetSettings" VerticalAlignment="Center" />
                            <StackPanel Margin="0,0,16,0">
                                <TextBlock Text="Restore default settings" FontSize="13" />
                                <TextBlock Style="{StaticResource Body}" Text="Resets preferences on every page. Your games list and lowered apps are kept, and Windows settings the app changed aren't touched (undo those on the Diagnostics page)." />
                            </StackPanel>
                        </DockPanel>
                    </Border>
                </StackPanel>
            </ScrollViewer>
```

- [ ] **Step 4: Code**

1. `ShowUpdate`: replace the `UpdateVersion.Text = …` line with `VersionValue.Text = AppIdentity.Version + (AppIdentity.IsDevBuild ? " dev" : "");`.
2. Add `private void OnAppearance(object sender, RoutedEventArgs e) => ChooseTheme((string)((FrameworkElement)sender).Tag);` next to `OnLightTheme`.
3. `ChooseTheme`: delete the three lines that update `AppearanceBox` (`_loading = true;`, the `UpdateTarget`, `_loading = false;`); keep `_settings.Appearance = appearance; _app.SaveSettings(); SyncThemeButtons();`. Update its summary to: "The tab-bar switch and the Appearance segments both land here; the switch picks Light or Dark outright."
4. `SyncThemeButtons`: append

```csharp
        Quietly(() =>
        {
            AppearanceLight.IsChecked = _settings.Appearance == Settings.Light;
            AppearanceDark.IsChecked = _settings.Appearance == Settings.Dark;
            AppearanceMatch.IsChecked = _settings.Appearance == Settings.MatchWindows;
        });
        AppearanceValue.Text = _settings.Appearance == Settings.MatchWindows
            ? $"Match Windows ({(_app.Theme.Current == AppTheme.Dark ? "dark" : "light")})"
            : _settings.Appearance;
```

5. `OnEdited` needs no change: the Appearance segments are RadioButtons, which Task 5's guard already skips, and the existing guard for the tab-bar `LightButton`/`DarkButton` ToggleButtons stays.
6. `RefreshSlow`: after `StartupSwitch.IsChecked = startup.Enabled;`, add `StartupValue.Text = startup.Enabled ? "With Windows" : "Off";`.
7. `OnResetSettings`: after `RefreshAll();`, add `SyncThemeButtons();`.

- [ ] **Step 5: Build and test** — 0 warnings; `SettingsPageTests` passes; full suite passes.

- [ ] **Step 6: Commit** — message: "Rebuild the Settings page with a status strip".

---

### Task 8: Diagnostics — Health grid and change journal

**Files:**
- Modify: `PCSpecOverlay/ControlCenterWindow.xaml` (DIAGNOSTICS `TabItem`), `PCSpecOverlay/ControlCenterWindow.xaml.cs`
- Test: `PCSpecOverlay.Tests/PagesTests.cs` (append)

**Interfaces:**
- Consumes: `Health`, `JournalRows`, `JournalRow`, `SettingCatalog.DescribeValue`, `DiagnosticReport.ChangesCheck` and `Engine.UndoChange` (Task 1).

**Rulings carried into this task:**
- The grid shows every existing `DiagnosticReport.Status` entry except the "Windows changes" one, which the journal replaces. No new probes are invented to match the mockup's eight cells.
- The "Recent events" list moves out to Open log, as the picked mockup says; the Overview keeps its event panel. `LevelBrush` goes with it.
- The journal lists `ChangeJournal` entries only, as the spec says. Game Boost priority changes stay in the grid's "Process priorities changed" cell.

- [ ] **Step 1: Write the failing test**

```csharp
public class DiagnosticsPageTests
{
    [Fact]
    public void Diagnostics_is_a_health_grid_and_a_change_journal()
    {
        var page = PageXaml.Tab("Diagnostics");
        foreach (var part in new[] { "x:Name=\"StatusGrid\"", "x:Name=\"HealthText\"", "x:Name=\"JournalList\"", "x:Name=\"JournalEmpty\"",
                     "Click=\"OnUndoChange\"", "Click=\"OnCopyReport\"", "Click=\"OnOpenLog\"", "Click=\"OnOpenData\"", "Click=\"OnUndoEverything\"",
                     "x:Name=\"ActionResult\"", "Text=\"CHANGE JOURNAL\"" })
            Assert.Contains(part, page);
        foreach (var gone in new[] { "LogList", "StatusList", "LevelBrush", "CardBox", "\"H1\"" })
            Assert.DoesNotContain(gone, page);
        var code = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/ControlCenterWindow.xaml.cs"));
        foreach (var call in new[] { "JournalRows.Build(", "Health.Summary(", "_engine.UndoChange(", "DiagnosticReport.ChangesCheck" })
            Assert.Contains(call, code);
        Assert.DoesNotContain("class LevelBrush", code);
    }
}
```

- [ ] **Step 2: Run to verify failure** — expected: 1 failed.

- [ ] **Step 3: Replace the page**

Replace everything inside the DIAGNOSTICS `<TabItem …>` with:

```xml
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <StackPanel>
                    <DockPanel Margin="24,18,24,10">
                        <WrapPanel DockPanel.Dock="Right" VerticalAlignment="Center">
                            <Button Style="{StaticResource Btn}" Click="OnCopyReport" Margin="8,0,0,6" AutomationProperties.Name="Copy diagnostic report">
                                <StackPanel Orientation="Horizontal">
                                    <core:Icon Data="{StaticResource Icon.Copy}" Width="12" Height="12" Margin="0,0,6,0" VerticalAlignment="Center" />
                                    <TextBlock Text="Copy report" />
                                </StackPanel>
                            </Button>
                            <Button Style="{StaticResource Btn}" Click="OnOpenLog" Margin="8,0,0,6" AutomationProperties.Name="Open log">
                                <StackPanel Orientation="Horizontal">
                                    <core:Icon Data="{StaticResource Icon.File}" Width="12" Height="12" Margin="0,0,6,0" VerticalAlignment="Center" />
                                    <TextBlock Text="Open log" />
                                </StackPanel>
                            </Button>
                            <Button Style="{StaticResource Btn}" Click="OnOpenData" Margin="8,0,0,6" AutomationProperties.Name="Open data folder">
                                <StackPanel Orientation="Horizontal">
                                    <core:Icon Data="{StaticResource Icon.Folder}" Width="12" Height="12" Margin="0,0,6,0" VerticalAlignment="Center" />
                                    <TextBlock Text="Data folder" />
                                </StackPanel>
                            </Button>
                            <Button Style="{StaticResource Btn}" Click="OnUndoEverything" Margin="8,0,0,6" AutomationProperties.Name="Undo every change the app made">
                                <StackPanel Orientation="Horizontal">
                                    <core:Icon Data="{StaticResource Icon.Undo}" Width="12" Height="12" Margin="0,0,6,0" VerticalAlignment="Center" />
                                    <TextBlock Text="Undo every change" />
                                </StackPanel>
                            </Button>
                        </WrapPanel>
                        <StackPanel Margin="0,0,16,0">
                            <TextBlock Text="Diagnostics" Style="{StaticResource PageTitle}" />
                            <TextBlock x:Name="HealthText" Style="{StaticResource Body}" />
                        </StackPanel>
                    </DockPanel>
                    <TextBlock x:Name="ActionResult" Style="{StaticResource Body}" Foreground="{DynamicResource Text}" Margin="24,0,24,10" />

                    <!-- Health grid -->
                    <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,1,0,0">
                        <ItemsControl x:Name="StatusGrid">
                            <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate>
                                    <UniformGrid Columns="4" />
                                </ItemsPanelTemplate>
                            </ItemsControl.ItemsPanel>
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,0,1,1" Padding="16,12,16,14" ToolTip="{Binding Detail}"
                                            AutomationProperties.Name="{Binding Name}" AutomationProperties.HelpText="{Binding Detail}">
                                        <StackPanel>
                                            <TextBlock Text="{Binding Name}" Style="{StaticResource Key}" TextTrimming="CharacterEllipsis" />
                                            <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                                                <core:Icon x:Name="Mark" Data="{StaticResource Icon.Info}" Foreground="{DynamicResource Muted}"
                                                           Width="14" Height="14" VerticalAlignment="Center" Margin="0,0,6,0" />
                                                <TextBlock Text="{Binding Word}" FontSize="14" FontWeight="SemiBold" VerticalAlignment="Center" />
                                            </StackPanel>
                                            <TextBlock Text="{Binding Detail}" Style="{StaticResource Small}" TextTrimming="CharacterEllipsis" MaxHeight="32" Margin="0,6,0,0" />
                                        </StackPanel>
                                    </Border>
                                    <DataTemplate.Triggers>
                                        <DataTrigger Binding="{Binding Level}" Value="Ok">
                                            <Setter TargetName="Mark" Property="Data" Value="{StaticResource Icon.Check}" />
                                            <Setter TargetName="Mark" Property="Foreground" Value="{DynamicResource Good}" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding Level}" Value="Warn">
                                            <Setter TargetName="Mark" Property="Data" Value="{StaticResource Icon.Warning}" />
                                            <Setter TargetName="Mark" Property="Foreground" Value="{DynamicResource Signal}" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding Level}" Value="Error">
                                            <Setter TargetName="Mark" Property="Data" Value="{StaticResource Icon.Warning}" />
                                            <Setter TargetName="Mark" Property="Foreground" Value="{DynamicResource Hot}" />
                                        </DataTrigger>
                                    </DataTemplate.Triggers>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </Border>

                    <!-- Change journal -->
                    <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1" Padding="24,7" Margin="0,22,0,0">
                        <DockPanel>
                            <TextBlock DockPanel.Dock="Right" Text="Restored only if it's still what the app set" Style="{StaticResource Small}" Margin="0" VerticalAlignment="Center" />
                            <TextBlock Text="CHANGE JOURNAL" Style="{StaticResource Key}" />
                        </DockPanel>
                    </Border>
                    <ItemsControl x:Name="JournalList">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border Style="{StaticResource LedgerRow}" Padding="24,8">
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="120" />
                                            <ColumnDefinition Width="*" />
                                            <ColumnDefinition Width="160" />
                                            <ColumnDefinition Width="Auto" />
                                        </Grid.ColumnDefinitions>
                                        <TextBlock Text="{Binding When}" Style="{StaticResource Mono}" Foreground="{DynamicResource Muted}" />
                                        <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">
                                            <TextBlock Text="{Binding Setting}" FontSize="12.5" Margin="0,0,10,0" />
                                            <TextBlock Text="{Binding Change}" Style="{StaticResource Mono}" Foreground="{DynamicResource Muted}" />
                                        </StackPanel>
                                        <TextBlock Grid.Column="2" Text="{Binding Owners}" FontSize="12" Foreground="{DynamicResource Muted}" VerticalAlignment="Center" />
                                        <Button Grid.Column="3" Content="Undo" Style="{StaticResource Btn}" MinWidth="0" Padding="10,3" Click="OnUndoChange"
                                                AutomationProperties.Name="{Binding Setting, StringFormat='Undo {0}'}" />
                                    </Grid>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <TextBlock x:Name="JournalEmpty" Style="{StaticResource Small}" Margin="24,8,24,0"
                               Text="No Windows settings are changed by the app right now." />
                    <TextBlock Style="{StaticResource Small}" Margin="24,12,24,24"
                               Text="Copy report includes the version, settings, sensor availability and recent events, with your user name and profile paths replaced. Every event is in the log." />
                </StackPanel>
            </ScrollViewer>
```

- [ ] **Step 4: Code**

1. Replace `RefreshDiagnostics` and the `StatusRow` record with:

```csharp
    private void RefreshDiagnostics()
    {
        var status = DiagnosticReport.Status(_engine).Where(s => s.Name != DiagnosticReport.ChangesCheck).ToList();
        StatusGrid.ItemsSource = status.Select(s => new StatusCell(s.Name, s.Detail, s.Level, Health.Word(s.Level))).ToList();
        HealthText.Text = Health.Summary(status.Select(s => s.Level).ToList());

        var rows = JournalRows.Build(_engine.Journal.Entries, id => SettingCatalog.Resolve(id)?.Name, SettingCatalog.DescribeValue, DateTime.Now);
        JournalList.ItemsSource = rows;
        JournalEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed record StatusCell(string Name, string Detail, DiagLevel Level, string Word);

    private void OnUndoChange(object sender, RoutedEventArgs e)
    {
        var row = (JournalRow)((FrameworkElement)sender).DataContext;
        if (!Confirm($"Undo {row.Setting} ({row.Change})?\n\nIt goes back only if it's still what the app set. If you changed it yourself since, your choice is kept.", "Undo change"))
            return;
        ActionResult.Text = string.Join("\n", _engine.UndoChange(row.Id));
        foreach (var c in _checks) c.Refresh();
        RefreshDiagnostics();
    }
```

2. Delete the `LevelBrush` class at the end of the file.

- [ ] **Step 5: Build and test** — 0 warnings; `DiagnosticsPageTests` passes; full suite passes.

- [ ] **Step 6: Commit** — message: "Rebuild Diagnostics as a health grid with a change journal and per-change Undo".

---

### Task 9: Screen-reader names, tokens and red

**Files:**
- Modify: `PCSpecOverlay/ControlCenterWindow.xaml(.cs)`, `PCSpecOverlay/Theme.xaml`
- Test: `PCSpecOverlay.Tests/PagesTests.cs` (append)

- [ ] **Step 1: Write the failing tests**

Add `using System.Xml.Linq;` at the top of `PagesTests.cs`, then append:

```csharp
public class PageSweepTests
{
    private static readonly string Xaml = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/ControlCenterWindow.xaml"));
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Every_input_has_a_screen_reader_name()
    {
        var unnamed = XDocument.Parse(Xaml).Descendants()
            .Where(e => e.Name.LocalName is "Slider" or "ComboBox" or "TextBox" or "RadioButton" or "ToggleButton"
                        || (e.Name.LocalName == "Button" && e.Attribute("Content") is null)
                        || (e.Name.LocalName == "CheckBox" && e.Attribute("Content") is null && !e.Elements().Any()))
            .Where(e => e.Attribute("AutomationProperties.Name") is null)
            .Select(e => $"{e.Name.LocalName} {e.Attribute(X + "Name")?.Value}")
            .ToList();
        Assert.Empty(unnamed);
    }

    [Fact]
    public void Pages_use_only_the_instrument_tokens()
    {
        var code = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/ControlCenterWindow.xaml.cs"));
        foreach (var key in new[] { "Window", "Sidebar", "Card", "CardBorder", "Input", "InputBorder", "Accent", "AccentHover", "AccentSoft", "Warn", "Info" })
        {
            Assert.DoesNotContain($"{{DynamicResource {key}}}", Xaml);
            Assert.DoesNotContain($"FindResource(\"{key}\")", code);
            Assert.DoesNotContain($"\"{key}\" :", code);
            Assert.DoesNotContain($": \"{key}\"", code);
        }
    }

    [Fact]
    public void Red_is_only_for_hot_readings_and_errors()
    {
        Assert.DoesNotContain("Danger", Xaml);
        Assert.DoesNotContain("Danger", File.ReadAllText(SourceFiles.Path("PCSpecOverlay/Theme.xaml")));
    }
}
```

- [ ] **Step 2: Run to verify failure** — `--filter "FullyQualifiedName~PageSweepTests"`; expected: up to 3 failed. The first lists any control still missing a name.

- [ ] **Step 3: Sweep**

1. `ControlCenterWindow.xaml`:
   - The Window's `Background="{DynamicResource Window}"` becomes `{DynamicResource Bg}`.
   - Delete `Tag="Danger"` from `StopButton`.
   - Add `AutomationProperties.Name` to every control the first test lists, named after its visible label, e.g. "Use this shortcut", "Remove {0}". For `HotkeyBox`, `NewRule` and others that already have one, nothing changes.
   - Delete the window-level styles `TileLabel`, `Row` and `ValueLabel` if nothing references them any more (grep first).
2. `Theme.xaml`: delete the whole `<Trigger Property="Tag" Value="Danger"> … </Trigger>` from `Btn`, and update the comment above `Btn` if it mentions Danger. Buttons are never red.
3. `ControlCenterWindow.xaml.cs`:
   - In `Banner`, `FindResource(warn ? "Warn" : "Accent")` becomes `FindResource(warn ? "Signal" : "Text")`.
   - In `Temp`, `"Warn"` becomes `"Signal"`.
   - In `OnApplyHotkey`, `"Warn"` becomes `"Signal"`.
   - Anything else the second test flags gets the same treatment, mapping `Accent` to `Text` and `Info` to `Muted`.
4. `InstallerWindow.xaml` is stage 5's; if it uses `Tag="Danger"`, leave it (it now renders as a plain button) and note it in the report.

- [ ] **Step 4: Build and test** — 0 warnings; `PageSweepTests` passes; full suite passes.

- [ ] **Step 5: Commit** — message: "Name every control for screen readers and keep red for hot readings and errors".

---

## After the tasks: on-PC verification (controller, with the user)

Run a Release development build beside the installed copy. Record the results in `PCSpecOverlay/docs/AUDIT.md` under "Instrument stage 4". Don't drive clicks on screen yourself: the user interrupted that before. Screenshots and UI Automation reads are fine. Hiding the overlay window during a screenshot resizes it (see the Optiframe audit note), so restart the dev copy after screenshots if the overlay looks clipped.

1. Each of the six pages in dark and light (the tab-bar switch): compare it with its picked mockup.
2. Optimizer: amber left edge on rows that need action; Now and Recommended columns filled; the button reads "Apply N recommendations".
3. Gaming: four steps with their switches; the status bar appears while a game is detected (Starve.io); Pause and Resume work.
4. Overlay: the preview shows the overlay at its corner or pinned spot with live numbers; clicking a corner moves the real overlay; the Strip and Panel cards switch the layout.
5. Alerts: drag a handle and step it with the arrow keys; Warning never passes Hot; the levels persist after a restart.
6. Settings: the Version, Startup and Appearance cells; the Appearance segments and the tab-bar switch stay in step.
7. Diagnostics: the grid's summary line; the journal lists current changes. Undo a single change (e.g. one the Optimizer made) with the user's go-ahead.
8. UI Automation: every control on each page has a name, and the scale handles announce their value.
