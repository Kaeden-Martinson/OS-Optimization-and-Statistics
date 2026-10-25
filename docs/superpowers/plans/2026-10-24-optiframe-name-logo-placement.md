# Optiframe: Name, Logo and Move-and-Pin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the app to Optiframe wherever it's visible, give it the Viewfinder logo (app icon, tray icon, in-app mark), and let the user drag the overlay anywhere and pin it there, with the four corners kept as presets.

**Architecture:** Placement becomes pure math (`OverlayPlacement`: corner or pinned spot ↔ pixels) that the overlay window calls; a pinned spot is stored as 0–1 fractions of the display's free space so it survives resolution and scaling changes. The overlay gains a transient move mode (handle bar with Pin, dashed outline, clicks not passing through). One GDI+ painter (`LogoPainter`) draws the tray icon and writes `Optiframe.ico` from the same point list as the XAML `Icon.Mark`. Visible strings use `AppIdentity.ProductName`; internal identifiers (exe, folders, task, mutex) stay unchanged.

**Tech Stack:** .NET 8 WPF + WinForms tray (C# 12), System.Drawing (GDI+), xUnit 2.9.

**Spec:** `docs/superpowers/specs/2026-10-24-optiframe-name-logo-placement-design.md`. Builds on the merged Instrument stages 1–3 (tokens, `core:Icon`, `Icons.xaml`, overlay Strip/Panel with O.* colors, top tab bar).

## Global Constraints

- Visible name: **Optiframe** (window titles "Optiframe", "Optiframe Setup", overlay window "Optiframe overlay"; Panel header "OPTIFRAME"). Internal names unchanged: `PCSpecOverlay.exe`, assembly/namespaces, `%LocalAppData%\Programs\PCSpecOverlay`, `%AppData%\PCSpecOverlay`, task `\PCSpecOverlay`, mutex/events/ETW session, uninstall key, settings keys.
- Logo geometry (24 units): brackets `M3,8 V3 H8 M16,3 H21 V8 M21,16 V21 H16 M8,21 H3 V16`, trace `M6,14 H9.5 L11.5,8.5 L13.5,15.5 L15,12 H18`; round caps and joins; stroke 2 units (2.4 at 16 px).
- App icon: mark `#E9E9E6` on tile `#0B0B0C`, sizes 16, 20, 24, 32, 40, 48, 64, 256. Tray: no tile, `#141414` on a light taskbar, `#E9E9E6` otherwise.
- `Position` values: `TopRight`, `TopLeft`, `BottomRight`, `BottomLeft`, `Custom`. `CustomX`/`CustomY` in 0–1 (defaults 1 and 0). `ClickThrough` removed (old files still load).
- Move mode is never saved; pinned always means click-through. No snapping when dragging; the panel always stays fully inside the display's work area.
- Colors only from tokens (O.* in the overlay); no emojis; square corners.
- Build: `dotnet build PCSpec.slnx` (0 warnings). Tests: `dotnet test PCSpec.slnx` (190 passing before this plan). Shell examples use Git Bash from the repo root.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## File map

| File | Status | Responsibility |
|---|---|---|
| `PCSpecOverlay/Core/OverlayPlacement.cs` | create | Corner / pinned spot ↔ pixel math |
| `PCSpecOverlay/Settings.cs` | modify | `Custom`, `CustomX`, `CustomY`; remove `ClickThrough` (Task 4) |
| `PCSpecOverlay/Core/LogoPainter.cs` | create | Draw the mark (GDI+); write the .ico |
| `PCSpecOverlay/Core/ThemeResolver.cs` | modify | Taskbar light/dark |
| `PCSpecOverlay/Optiframe.ico` | create (generated) | App icon |
| `PCSpecOverlay/Themes/Icons.xaml` | modify | `Icon.Mark` = Viewfinder |
| `PCSpecOverlay/TrayIcon.cs` | modify | Logo tray icon; name; "Move overlay" |
| `PCSpecOverlay/PCSpecOverlay.csproj` | modify | Product, AssemblyTitle, ApplicationIcon, icon resource |
| `PCSpecOverlay/Core/AppIdentity.cs`, `Installer.cs`, `InstallerWindow.xaml(.cs)`, `StartupTask.cs`, `Core/Updates.cs`, `ControlCenterWindow.xaml(.cs)`, `OverlayWindow.xaml(.cs)`, `App.xaml.cs`, `build-release.ps1`, `PCSpecOverlay/README.md` | modify | Name, icon, move mode |
| `PCSpecOverlay.Tests/OptiframeTests.cs` | create | All tests for this plan |

---

### Task 1: Placement math and settings

**Files:**
- Create: `PCSpecOverlay/Core/OverlayPlacement.cs`
- Modify: `PCSpecOverlay/Settings.cs`
- Test: `PCSpecOverlay.Tests/OptiframeTests.cs` (create)

**Interfaces:**
- Produces: `Settings.Custom` ("Custom"); `Settings.CustomX`, `Settings.CustomY` (double, defaults 1 and 0); `OverlayPlacement.Place(string position, double customX, double customY, (int Left, int Top, int Right, int Bottom) work, int width, int height, int margin) : (int X, int Y)`; `OverlayPlacement.ToFractions(int left, int top, (int Left, int Top, int Right, int Bottom) work, int width, int height) : (double X, double Y)`.

- [ ] **Step 1: Write the failing tests**

Create `PCSpecOverlay.Tests/OptiframeTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~OverlayPlacementTests|FullyQualifiedName~PlacementSettingTests"`
Expected: build error `'Settings' does not contain a definition for 'Custom'`.

- [ ] **Step 3: Implement**

Create `PCSpecOverlay/Core/OverlayPlacement.cs`:

```csharp
namespace PCSpecOverlay.Core;

/// <summary>
/// Where the overlay's panel goes on a display, in physical pixels: a corner preset (with an edge margin) or a
/// pinned spot, stored as a share (0–1) of the display's free space so it survives resolution and scaling changes.
/// </summary>
public static class OverlayPlacement
{
    public static (int X, int Y) Place(string position, double customX, double customY,
        (int Left, int Top, int Right, int Bottom) work, int width, int height, int margin)
    {
        if (position == Settings.Custom)
        {
            var freeX = Math.Max(0, work.Right - work.Left - width);
            var freeY = Math.Max(0, work.Bottom - work.Top - height);
            return (work.Left + (int)Math.Round(Math.Clamp(customX, 0, 1) * freeX),
                    work.Top + (int)Math.Round(Math.Clamp(customY, 0, 1) * freeY));
        }
        var x = position.EndsWith("Left") ? work.Left + margin : work.Right - width - margin;
        var y = position.StartsWith("Bottom") ? work.Bottom - height - margin : work.Top + margin;
        return (x, y);
    }

    /// <summary>The pinned spot for a panel whose top-left corner is at (left, top), clamped to 0–1.</summary>
    public static (double X, double Y) ToFractions(int left, int top,
        (int Left, int Top, int Right, int Bottom) work, int width, int height)
    {
        var freeX = work.Right - work.Left - width;
        var freeY = work.Bottom - work.Top - height;
        return (freeX <= 0 ? 0 : Math.Clamp((left - work.Left) / (double)freeX, 0, 1),
                freeY <= 0 ? 0 : Math.Clamp((top - work.Top) / (double)freeY, 0, 1));
    }
}
```

In `PCSpecOverlay/Settings.cs`:
- Below the `Corners` array add `public const string Custom = "Custom";`.
- After the `Position` property add:

```csharp
    /// <summary>Pinned spot within the display's free space (0 = left/top edge, 1 = right/bottom); used when Position is Custom.</summary>
    public double CustomX { get; set; } = 1;
    public double CustomY { get; set; } = 0;
```

- In `Validate()`, replace `if (!Corners.Contains(Position)) Position = "TopRight";` with:

```csharp
        if (!Corners.Contains(Position) && Position != Custom) Position = "TopRight";
        CustomX = Clamp(CustomX, 0, 1, 1);
        CustomY = Clamp(CustomY, 0, 1, 0);
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~OverlayPlacementTests|FullyQualifiedName~PlacementSettingTests"`
Expected: 17 passed.

- [ ] **Step 5: Commit**

```bash
git add PCSpecOverlay/Core/OverlayPlacement.cs PCSpecOverlay/Settings.cs PCSpecOverlay.Tests/OptiframeTests.cs
git commit -F- <<'EOF'
Add placement math and a pinned-spot setting for the overlay

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: The Viewfinder logo

**Files:**
- Create: `PCSpecOverlay/Core/LogoPainter.cs`, `PCSpecOverlay/Optiframe.ico` (generated by a test)
- Modify: `PCSpecOverlay/Core/ThemeResolver.cs`, `PCSpecOverlay/Themes/Icons.xaml`, `PCSpecOverlay/TrayIcon.cs`, `PCSpecOverlay/PCSpecOverlay.csproj`, `PCSpecOverlay/ControlCenterWindow.xaml`, `PCSpecOverlay/InstallerWindow.xaml`
- Test: `PCSpecOverlay.Tests/OptiframeTests.cs` (append)

**Interfaces:**
- Produces: `LogoPainter.Draw(int size, System.Drawing.Color mark, System.Drawing.Color? tile = null) : Bitmap`; `LogoPainter.IconSizes`; `LogoPainter.WriteIco(Stream output)`; `ThemeResolver.TaskbarIsLight(Func<object?> readSystemUsesLightTheme) : bool`; `ThemeResolver.ReadSystemUsesLightTheme() : object?`.

- [ ] **Step 1: Write the failing tests**

Append to `PCSpecOverlay.Tests/OptiframeTests.cs` (add `using System.Drawing;` at the top — the test project removes the global System.Drawing using):

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~LogoTests|FullyQualifiedName~LogoUseTests"`
Expected: build error `The name 'LogoPainter' does not exist in the current context`.

- [ ] **Step 3: The painter and taskbar theme**

Create `PCSpecOverlay/Core/LogoPainter.cs`:

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace PCSpecOverlay.Core;

/// <summary>
/// The Optiframe mark (Viewfinder: corner brackets around a frame-time line) drawn with GDI+ for the tray icon and
/// the app icon. Same 24-unit geometry as Icon.Mark in Themes/Icons.xaml.
/// </summary>
public static class LogoPainter
{
    private static readonly PointF[][] Strokes =
    [
        [new(3, 8), new(3, 3), new(8, 3)],
        [new(16, 3), new(21, 3), new(21, 8)],
        [new(21, 16), new(21, 21), new(16, 21)],
        [new(8, 21), new(3, 21), new(3, 16)],
        [new(6, 14), new(9.5f, 14), new(11.5f, 8.5f), new(13.5f, 15.5f), new(15, 12), new(18, 12)],
    ];

    public static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    /// <param name="tile">Square background for the app icon; null leaves it transparent (tray).</param>
    public static Bitmap Draw(int size, Color mark, Color? tile = null)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(tile ?? Color.Transparent);
        var inset = tile is null ? 0f : size * 0.17f; // the app icon keeps the mark inside its tile
        var scale = (size - 2 * inset) / 24f;
        var width = Math.Max(1.5f, (size <= 16 ? 2.4f : 2f) * scale);
        using var pen = new Pen(mark, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        foreach (var stroke in Strokes)
            g.DrawLines(pen, stroke.Select(p => new PointF(inset + p.X * scale, inset + p.Y * scale)).ToArray());
        return bmp;
    }

    /// <summary>Writes Optiframe.ico: the mark on its tile at every size, as PNG-compressed entries.</summary>
    public static void WriteIco(Stream output)
    {
        var mark = Color.FromArgb(0xE9, 0xE9, 0xE6);
        var tile = Color.FromArgb(0x0B, 0x0B, 0x0C);
        var images = IconSizes.Select(s =>
        {
            using var bmp = Draw(s, mark, tile);
            using var png = new MemoryStream();
            bmp.Save(png, ImageFormat.Png);
            return png.ToArray();
        }).ToList();

        using var w = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write((short)0); w.Write((short)1); w.Write((short)images.Count);
        var offset = 6 + 16 * images.Count;
        for (var i = 0; i < images.Count; i++)
        {
            var s = IconSizes[i] >= 256 ? 0 : IconSizes[i];
            w.Write((byte)s); w.Write((byte)s); w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32); w.Write(images[i].Length); w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var image in images) w.Write(image);
    }
}
```

In `PCSpecOverlay/Core/ThemeResolver.cs` add inside `ThemeResolver`:

```csharp
    /// <summary>Whether the Windows taskbar is light (SystemUsesLightTheme = 1); anything else counts as dark.</summary>
    public static bool TaskbarIsLight(Func<object?> readSystemUsesLightTheme)
    {
        try { return readSystemUsesLightTheme() is int v && v == 1; }
        catch { return false; }
    }

    public static object? ReadSystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("SystemUsesLightTheme");
    }
```

- [ ] **Step 4: Generate the icon and wire it in**

1. Run: `OPTIFRAME_WRITE_ICON=1 dotnet test PCSpec.slnx --filter "FullyQualifiedName~LogoTests"` — this writes `PCSpecOverlay/Optiframe.ico`. Then run the same filter without the variable; all LogoTests must pass.
2. `PCSpecOverlay/PCSpecOverlay.csproj`: in the PropertyGroup with `<Product>`, add `<ApplicationIcon>Optiframe.ico</ApplicationIcon>`; add an ItemGroup `<ItemGroup><Resource Include="Optiframe.ico" /></ItemGroup>`.
3. `ControlCenterWindow.xaml` and `InstallerWindow.xaml`: add `Icon="Optiframe.ico"` to the `<Window …>` start tag.
4. `InstallerWindow.xaml`: add `xmlns:core="clr-namespace:PCSpecOverlay.Core"` to the Window tag and replace the three-rectangle `<Canvas Width="26" Height="26" …> … </Canvas>` with:

```xml
            <core:Icon Data="{StaticResource Icon.Mark}" Width="26" Height="26" Margin="0,0,12,0" VerticalAlignment="Center" />
```

5. `Themes/Icons.xaml`: replace the `Icon.Mark` geometry content with
`M3,8 V3 H8 M16,3 H21 V8 M21,16 V21 H16 M8,21 H3 V16 M6,14 H9.5 L11.5,8.5 L13.5,15.5 L15,12 H18`
and update the file's comment line above it if it describes the mark.
6. `TrayIcon.cs`:
   - Add `using Microsoft.Win32;`.
   - Change `private readonly Icon _image;` to `private Icon _image;`.
   - Replace the `DrawIcon` method with:

```csharp
    /// <summary>The Optiframe mark at the tray size: dark on a light taskbar, light on a dark one.</summary>
    private static Icon DrawIcon()
    {
        var size = Forms.SystemInformation.SmallIconSize.Width;
        var light = ThemeResolver.TaskbarIsLight(ThemeResolver.ReadSystemUsesLightTheme);
        using var bmp = LogoPainter.Draw(size, light ? Color.FromArgb(0x14, 0x14, 0x14) : Color.FromArgb(0xE9, 0xE9, 0xE6));
        return Icon.FromHandle(bmp.GetHicon());
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        _app.Dispatcher.BeginInvoke(() =>
        {
            var old = _image;
            _image = DrawIcon();
            _icon.Icon = _image;
            old.Dispose();
        });
    }
```

   - At the end of the constructor add `SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;`, and as the first statement of `Dispose()` add `SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;`.

- [ ] **Step 5: Build and test**

Run: `dotnet build PCSpec.slnx` — Expected: 0 errors, 0 warnings.
Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~LogoTests|FullyQualifiedName~LogoUseTests|FullyQualifiedName~IconTests"` — Expected: all pass (IconTests is the stage-1 bounds test, which must still pass with the new Mark).
Run: `dotnet test PCSpec.slnx` — Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add PCSpecOverlay/Core/LogoPainter.cs PCSpecOverlay/Core/ThemeResolver.cs PCSpecOverlay/Optiframe.ico PCSpecOverlay/Themes/Icons.xaml PCSpecOverlay/TrayIcon.cs PCSpecOverlay/PCSpecOverlay.csproj PCSpecOverlay/ControlCenterWindow.xaml PCSpecOverlay/InstallerWindow.xaml PCSpecOverlay.Tests/OptiframeTests.cs
git commit -F- <<'EOF'
Add the Optiframe Viewfinder logo: app icon, tray icon and in-app mark

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 3: Rename to Optiframe

**Files:**
- Modify: `PCSpecOverlay/Core/AppIdentity.cs`, `PCSpecOverlay/Installer.cs`, `PCSpecOverlay/InstallerWindow.xaml(.cs)`, `PCSpecOverlay/ControlCenterWindow.xaml`, `PCSpecOverlay/OverlayWindow.xaml`, `PCSpecOverlay/TrayIcon.cs`, `PCSpecOverlay/StartupTask.cs`, `PCSpecOverlay/Core/Updates.cs`, `PCSpecOverlay/PCSpecOverlay.csproj`, `build-release.ps1`, `PCSpecOverlay/README.md`
- Test: `PCSpecOverlay.Tests/OptiframeTests.cs` (append)

**Interfaces:**
- Produces: `AppIdentity.ProductName` = "Optiframe"; `AppIdentity.LegacyProductName` = "PC Spec Overlay"; `Installer.DeleteLegacyShortcut()` (private).

- [ ] **Step 1: Write the failing test**

Append to `PCSpecOverlay.Tests/OptiframeTests.cs` (add `using System.Text.RegularExpressions;` at the top):

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~NameTests"` — Expected: 2 failed (the first lists every old-name line).

- [ ] **Step 3: Rename**

1. `Core/AppIdentity.cs`: set `public const string ProductName = "Optiframe";` and add below it:
   `public const string LegacyProductName = "PC Spec Overlay"; // the Start-menu shortcut's name before 2.1.0`
2. `Installer.cs`: directly after `CreateShortcut(target);` in Install add `DeleteLegacyShortcut();`; directly after `try { File.Delete(ShortcutPath); } catch { /* already gone */ }` in Uninstall add `DeleteLegacyShortcut();`; and add:

```csharp
    /// <summary>Versions before 2.1.0 named the Start-menu shortcut after the old product name.</summary>
    private static void DeleteLegacyShortcut()
    {
        try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{AppIdentity.LegacyProductName}.lnk")); }
        catch { /* not there */ }
    }
```

3. `InstallerWindow.xaml`: `Title="Optiframe Setup"`. `InstallerWindow.xaml.cs`: the three strings become `$"Install {AppIdentity.ProductName}"` / `$"Update {AppIdentity.ProductName}"`, `$"Uninstall {AppIdentity.ProductName}"`, and `$"Installed. {AppIdentity.ProductName} is running — find it in the tray and in the Start menu."` (add `using PCSpecOverlay.Core;` if missing).
4. `ControlCenterWindow.xaml`: `Title="Optiframe"`; in the tab-bar name StackPanel change `<TextBlock Text="PC SPEC" FontSize="12" …` to `<TextBlock Text="Optiframe" FontSize="13" …` (keep its other attributes).
5. `OverlayWindow.xaml`: `Title="Optiframe overlay"`; the Panel header `<TextBlock Text="PC SPEC" …` becomes `<TextBlock Text="OPTIFRAME" …`.
6. `TrayIcon.cs`: `Text = AppIdentity.ProductName,` and the Notify signature `public void Notify(string message, string title = AppIdentity.ProductName, bool important = false)`.
7. `StartupTask.cs`: `<Description>{AppIdentity.ProductName} - performance overlay</Description>` (the string is already interpolated).
8. `Core/Updates.cs`: the download file name becomes `$"Optiframe-Setup-{release.Version}.exe"`.
9. `PCSpecOverlay.csproj`: `<Product>Optiframe</Product>` and add `<AssemblyTitle>Optiframe</AssemblyTitle>` next to it.
10. `build-release.ps1`: every "PC Spec Overlay" becomes "Optiframe", and every `PCSpecOverlay-Setup` (the setup file name, the Move-Item target, the release notes and the help text) becomes `Optiframe-Setup`. The published file `"$out\publish\PCSpecOverlay.exe"` keeps its name.
11. `PCSpecOverlay/README.md`: replace the product name "PC Spec Overlay" with "Optiframe" in prose and headings; leave file and folder names such as `PCSpecOverlay.exe`, `%AppData%\PCSpecOverlay` and `PCSpecOverlay-Dev` unchanged; change `PCSpecOverlay-Setup` file names to `Optiframe-Setup`.

- [ ] **Step 4: Build and test**

Run: `dotnet build PCSpec.slnx` — Expected: 0 errors, 0 warnings.
Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~NameTests"` — Expected: 2 passed.
Run: `dotnet test PCSpec.slnx` — Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add PCSpecOverlay build-release.ps1 PCSpecOverlay.Tests/OptiframeTests.cs
git commit -F- <<'EOF'
Rename the app to Optiframe wherever it's visible

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 4: Move and pin the overlay

**Files:**
- Modify: `PCSpecOverlay/OverlayWindow.xaml(.cs)`, `PCSpecOverlay/App.xaml.cs`, `PCSpecOverlay/TrayIcon.cs`, `PCSpecOverlay/ControlCenterWindow.xaml(.cs)`, `PCSpecOverlay/Settings.cs`
- Test: `PCSpecOverlay.Tests/OptiframeTests.cs` (append)

**Interfaces:**
- Consumes: `OverlayPlacement.Place/ToFractions`, `Settings.Custom/CustomX/CustomY` (Task 1).
- Produces: `OverlayWindow.BeginMove()`, `OverlayWindow.IsMoving`, `event Action? Pinned`; `App.MoveOverlay()`; `ControlCenterWindow.OverlayMoved()`.

- [ ] **Step 1: Write the failing tests**

Append to `PCSpecOverlay.Tests/OptiframeTests.cs`:

```csharp
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
        Assert.Contains("Tag=\"Custom\"", xaml);
        Assert.DoesNotContain("ClickThrough", xaml);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~MovePinTests"` — Expected: 3 failed.

- [ ] **Step 3: Remove the click-through setting**

In `PCSpecOverlay/Settings.cs` delete the line `public bool ClickThrough { get; set; } = true;`.

- [ ] **Step 4: Overlay XAML — handle, pin and outline**

In `PCSpecOverlay/OverlayWindow.xaml`, the window's content is currently the `<Border x:Name="Frame" …> … </Border>` element. Wrap it so the content becomes:

```xml
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <!-- Move mode only: a handle bar above the panel with the Pin button -->
        <Border x:Name="MoveHandle" Visibility="Collapsed" Background="{DynamicResource O.Text}" Height="22" Margin="0,0,0,4">
            <DockPanel>
                <Button x:Name="PinButton" DockPanel.Dock="Right" Click="OnPin" Focusable="False" Cursor="Hand" Margin="2"
                        AutomationProperties.Name="Pin the overlay here">
                    <Button.Template>
                        <ControlTemplate TargetType="Button">
                            <Border Background="{DynamicResource O.Solid}" Padding="7,0">
                                <StackPanel Orientation="Horizontal">
                                    <Path Data="M9,3 H15 L14,9 L18,13 H6 L10,9 Z M12,13 V21" Stroke="{DynamicResource O.Text}" StrokeThickness="1.6"
                                          StrokeLineJoin="Round" StrokeStartLineCap="Round" StrokeEndLineCap="Round"
                                          Width="11" Height="11" Stretch="Uniform" VerticalAlignment="Center" />
                                    <TextBlock Text="Pin" Margin="5,0,0,0" FontFamily="Segoe UI" FontWeight="SemiBold" FontSize="10.5"
                                               Foreground="{DynamicResource O.Text}" VerticalAlignment="Center" />
                                </StackPanel>
                            </Border>
                        </ControlTemplate>
                    </Button.Template>
                </Button>
                <StackPanel Orientation="Horizontal" Margin="8,0,0,0" VerticalAlignment="Center">
                    <Path Data="M12,3 V21 M3,12 H21 M9,6 L12,3 L15,6 M9,18 L12,21 L15,18 M6,9 L3,12 L6,15 M18,9 L21,12 L18,15"
                          Stroke="{DynamicResource O.Solid}" StrokeThickness="1.6" StrokeLineJoin="Round"
                          StrokeStartLineCap="Round" StrokeEndLineCap="Round" Width="11" Height="11" Stretch="Uniform" />
                    <TextBlock Text="Drag to move" Margin="5,0,0,0" FontFamily="Segoe UI" FontWeight="SemiBold" FontSize="10.5"
                               Foreground="{DynamicResource O.Solid}" VerticalAlignment="Center" />
                </StackPanel>
            </DockPanel>
        </Border>

        <Grid Grid.Row="1">
            <!-- existing <Border x:Name="Frame" …> … </Border> goes here unchanged -->
            <!-- Move mode only: dashed outline around the panel -->
            <Rectangle x:Name="MoveOutline" Visibility="Collapsed" Stroke="{DynamicResource O.Text}" StrokeThickness="1"
                       StrokeDashArray="3 2" IsHitTestVisible="False" />
        </Grid>
    </Grid>
```

(Move the existing Frame Border, unchanged, to the marked place inside `<Grid Grid.Row="1">`, before the Rectangle.)

- [ ] **Step 5: Overlay code — move mode, placement, drag and pin**

In `PCSpecOverlay/OverlayWindow.xaml.cs`:

1. Add fields/members next to the other fields:

```csharp
    private bool _moving;

    /// <summary>Raised when the user presses Pin after moving the overlay.</summary>
    public event Action? Pinned;

    public bool IsMoving => _moving;
```

2. In `ApplyWindowStyles`, replace the two `_settings.ClickThrough` lines with:

```csharp
        ex = _moving ? ex & ~Native.WS_EX_TRANSPARENT : ex | Native.WS_EX_TRANSPARENT;
        Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
        Cursor = _moving ? Cursors.SizeAll : null;
```

(keep the existing `var ex = …` line and remove the old SetWindowLong/Cursor lines that these replace).

3. Replace the whole `Reposition` method and the whole `OnDrag` method with:

```csharp
    /// <summary>
    /// Places the panel at its corner or pinned spot on the chosen display (the main display if that one is gone).
    /// While moving, the handle bar sits above the panel, so the window starts that much higher.
    /// </summary>
    public void Reposition()
    {
        if (Handle == IntPtr.Zero || Displays.Find(_settings.Monitor) is not { } display) return;
        if (!Native.GetWindowRect(Handle, out var r)) return;
        var handle = HandlePixels(display.Dpi);
        int w = r.Right - r.Left, h = r.Bottom - r.Top - handle;
        var m = (int)Math.Round(EdgeMarginDip * display.Dpi / 96.0);
        var work = display.WorkArea;
        var (x, y) = OverlayPlacement.Place(_settings.Position, _settings.CustomX, _settings.CustomY,
            (work.Left, work.Top, work.Right, work.Bottom), w, h, m);
        var top = y - handle; // near the top edge the handle may sit above the work area; the panel stays put
        if (x != r.Left || top != r.Top)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, x, top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>Height of the move handle and its gap in physical pixels; 0 when pinned.</summary>
    private int HandlePixels(uint dpi) =>
        _moving ? (int)Math.Round((MoveHandle.ActualHeight + MoveHandle.Margin.Bottom) * dpi / 96.0) : 0;

    /// <summary>Unlocks the overlay: outline and handle shown, clicks no longer pass through, dragging moves it.</summary>
    public void BeginMove()
    {
        if (_moving) return;
        _moving = true;
        MoveHandle.Visibility = MoveOutline.Visibility = Visibility.Visible;
        ApplyWindowStyles();
        Reposition();
    }

    private void OnPin(object sender, RoutedEventArgs e)
    {
        _moving = false;
        MoveHandle.Visibility = MoveOutline.Visibility = Visibility.Collapsed;
        ApplyWindowStyles();
        Reposition();
        Moved?.Invoke();
        Pinned?.Invoke();
    }

    /// <summary>In move mode, dragging pins the panel exactly where it's dropped, on the display under its center.</summary>
    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (!_moving) return;
        try { DragMove(); } catch (InvalidOperationException) { return; }

        if (!Native.GetWindowRect(Handle, out var r)) return;
        int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
        var display = Displays.All().FirstOrDefault(d => cx >= d.Bounds.Left && cx < d.Bounds.Right && cy >= d.Bounds.Top && cy < d.Bounds.Bottom)
                      ?? Displays.Find(_settings.Monitor);
        if (display is null) return;
        var panelTop = r.Top + HandlePixels(display.Dpi);
        var work = display.WorkArea;
        var (fx, fy) = OverlayPlacement.ToFractions(r.Left, panelTop, (work.Left, work.Top, work.Right, work.Bottom),
            r.Right - r.Left, r.Bottom - panelTop);
        _settings.Position = Settings.Custom;
        _settings.CustomX = fx;
        _settings.CustomY = fy;
        _settings.Monitor = display.Primary ? "" : display.DeviceName;
        Reposition();
        Moved?.Invoke();
    }
```

4. Update the class summary's sentence about dragging to: "It never takes focus, stays out of Alt+Tab, and is click-through except while the user moves it (tray or control center: Move overlay); Pin locks it where it was dropped."

- [ ] **Step 6: App, tray and control center**

1. `App.xaml.cs` — add next to `ToggleOverlay`:

```csharp
    /// <summary>Shows the overlay if needed and unlocks it for dragging; Pin on the overlay locks it again.</summary>
    public void MoveOverlay()
    {
        if (!_overlay.IsVisible) ToggleOverlay();
        _overlay.BeginMove();
    }
```

   and in `OnStartup`, directly after `_overlay.Moved += SaveSettings;`:

```csharp
        _overlay.Moved += () => _controlCenter?.OverlayMoved();
        _overlay.Pinned += () => _tray?.Notify("It stays here and clicks pass through to your game. Use Move overlay in the tray to move it again.", "Overlay pinned");
```

2. `TrayIcon.cs` — after the `overlay` menu item add:

```csharp
        var move = new Forms.ToolStripMenuItem("Move overlay");
        move.Click += (_, _) => _app.MoveOverlay();
```

   and put `move` right after `overlay` in the `menu.Items.AddRange([...])` list.

3. `ControlCenterWindow.xaml` (Overlay page):
   - In the "Corner" row change the label to `Text="Position"`, name the ComboBox and give it an automation name, and add the Custom item, so the row's ComboBox reads:

```xml
                                <ComboBox x:Name="PositionBox" Grid.Column="1" SelectedValuePath="Tag" SelectedValue="{Binding Position}"
                                          AutomationProperties.Name="Overlay position">
                                    <ComboBoxItem Content="Top right" Tag="TopRight" />
                                    <ComboBoxItem Content="Top left" Tag="TopLeft" />
                                    <ComboBoxItem Content="Bottom right" Tag="BottomRight" />
                                    <ComboBoxItem Content="Bottom left" Tag="BottomLeft" />
                                    <ComboBoxItem Content="Custom (pinned spot)" Tag="Custom" />
                                </ComboBox>
```

   - In the BEHAVIOR card replace the whole `<CheckBox Style="{StaticResource Switch}" IsChecked="{Binding ClickThrough}" …> … </CheckBox>` with:

```xml
                            <Grid Style="{StaticResource Row}">
                                <Grid.ColumnDefinitions><ColumnDefinition Width="*" /><ColumnDefinition Width="Auto" /></Grid.ColumnDefinitions>
                                <StackPanel Margin="0,0,16,0">
                                    <TextBlock Text="Move the overlay" FontSize="13" />
                                    <TextBlock Style="{StaticResource Small}" Text="Unlocks it so you can drag it anywhere on any screen; press Pin on the overlay to lock it there. Clicks pass through to your game whenever it's pinned." />
                                </StackPanel>
                                <Button Grid.Column="1" Content="Move overlay" Style="{StaticResource Btn}" Click="OnMoveOverlay" VerticalAlignment="Center" />
                            </Grid>
```

4. `ControlCenterWindow.xaml.cs` — add next to `ChooseTheme`:

```csharp
    private void OnMoveOverlay(object sender, RoutedEventArgs e) => _app.MoveOverlay();

    /// <summary>The overlay was dragged or pinned: show its new position choice without saving again.</summary>
    public void OverlayMoved()
    {
        _loading = true;
        PositionBox.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateTarget();
        _loading = false;
    }
```

- [ ] **Step 7: Build and test**

Run: `dotnet build PCSpec.slnx` — Expected: 0 errors, 0 warnings; `grep -rn ClickThrough PCSpecOverlay --include=*.cs --include=*.xaml` prints nothing.
Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~MovePinTests|FullyQualifiedName~OverlaySourceTests"` — Expected: all pass (OverlaySourceTests is the stage-2 check: no hex colors or CornerRadius in the overlay XAML).
Run: `dotnet test PCSpec.slnx` — Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add PCSpecOverlay PCSpecOverlay.Tests/OptiframeTests.cs
git commit -F- <<'EOF'
Move the overlay anywhere and pin it there; corners stay as presets

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## After the tasks: on-PC verification (controller, with the user)

Run a Release development build. Record results in `PCSpecOverlay/docs/AUDIT.md` under "Optiframe name, logo and placement":

1. Tray: the Viewfinder icon, crisp, in the taskbar's theme (dark mark on a light taskbar, light on a dark one); the tooltip says Optiframe.
2. Control center: window title and taskbar icon say Optiframe with the logo; the tab bar shows the mark and "Optiframe"; the Panel overlay header says OPTIFRAME.
3. Move overlay (tray and Overlay page): outline and handle appear; drag to the middle of the screen; Pin; clicks pass through; the Position choice shows "Custom (pinned spot)"; restart the dev copy and the overlay comes back at the same spot.
4. Choosing a corner preset moves it there; dragging onto a second display (if connected) pins it there.
5. The installer window shows the logo and "Optiframe Setup" (run the built setup in update mode against the dev data and cancel).
