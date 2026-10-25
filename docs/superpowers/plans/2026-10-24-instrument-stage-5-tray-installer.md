# Instrument Stage 5: Tray Menu and Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle the tray's right-click menu and the installer window in the Instrument look. The tray menu becomes "Plain menu" (A): the active theme's colors, a vector icon per item, right-aligned shortcuts, and a check at the right of switched-on items. The installer becomes "One card" (A): a heading, one or two switches, and two buttons. Both follow light/dark.

**Architecture:** WinForms draws the tray menu, so a custom `ToolStripRenderer` (`TrayMenuRenderer`) paints it. It reads colors from a `TrayPalette` built from the app's active WPF color keys (`C.Bg`, `C.Panel`, …), and icons come from the same `Icon.*` geometries the app uses, rasterized by `MenuIcons.Render` (WPF `DrawingVisual` → GDI+ bitmap). The menu redraws on `App.Theme.Changed`. The installer runs before the app's `ThemeManager` exists, so it creates its own on the app resources with Match Windows, and its title bar follows that theme.

**Tech Stack:** .NET 8 WPF + WinForms (C# 12), xUnit 2.9.

**Spec:** `docs/superpowers/specs/2026-10-24-instrument-redesign-design.md`: §2 ("The tray menu gets a custom ToolStripRenderer using the same tokens. The installer window follows the theme."), §3 "Tray menu — Plain" and "Installer — One card", and the picked options in `docs/superpowers/specs/2026-10-24-instrument-redesign-mockups/tray-installer.html` (tray A, installer A). Builds on stages 1–4 and Optiframe.

## Global Constraints

- **Colors.** Tokens only. In WPF: `Bg`, `Panel`, `Line`, `Line2`, `Text`, `Muted` (`DynamicResource`). In the tray: the matching `C.*` color keys read at runtime. No hex colors in XAML. In C#, the only literal colors allowed are `TrayPalette.Dark`'s fallbacks, which copy `Colors.Dark.xaml`. No legacy aliases (`Window`, `Card`, `CardBorder`, `Accent`, `AccentSoft`, `Warn`, `Info`, …) in the files this plan touches. Buttons are never red; there is no `Danger` tag anywhere.
- **Tray menu.**
  - Same commands as today: Open, Show/Hide overlay with its shortcut, Move overlay, Automatic gaming mode, Stop/Resume optimizations, Start with Windows, Exit.
  - The top item reads "Open Optiframe" (bold). The overlay item reads "Hide overlay" or "Show overlay" by state, instead of carrying a check.
  - Hover row: `Panel` fill with a 2 px `Text` left edge. Shortcuts in `Muted` monospace. 1 px `Line` border and separators. Square corners.
  - Switched-on items (Automatic gaming mode, Start with Windows) show a check icon at the right.
- **Icons.** Drawn from the existing `Icon.*` geometries (24-unit grid, 1.6 stroke, round caps and joins) in the `Text` color at 16 px × the menu's DPI scale.
- **Installer.**
  - Same content and flow as today: heading, explanation, Start with Windows switch (install) or the two uninstall switches, Cancel and Install/Update/Uninstall, result text.
  - The main button is `Primary` in every mode.
  - Follows the Windows app theme (Match Windows), including the title bar, and switches live.
- **Behavior.** Nothing else changes: tray left-click opens the control center, balloon notifications are unchanged, and install/uninstall logic is untouched.
- **Build and tests.** `dotnet build PCSpec.slnx` must have 0 warnings, and `dotnet test PCSpec.slnx` must pass (266 tests pass before this plan). Shell examples use Git Bash from the repo root.
- **Commits** end with `Co-Authored-By: <the model that wrote it> <noreply@anthropic.com>`.

## File map

| File | Status | Responsibility |
|---|---|---|
| `PCSpecOverlay/Core/TrayDrawing.cs` | create | `TrayPalette` (colors from the active theme), `MenuIcons.Render` (geometry → bitmap) |
| `PCSpecOverlay/TrayMenuRenderer.cs` | create | Paints the tray menu |
| `PCSpecOverlay/TrayIcon.cs` | modify | Menu items with icons, state texts, theme follow |
| `PCSpecOverlay/InstallerWindow.xaml(.cs)` | modify | One-card layout, theme follow |
| `PCSpecOverlay.Tests/TrayInstallerTests.cs` | create | Tests for this plan |

---

### Task 1: Tray colors and icons

**Files:**
- Create: `PCSpecOverlay/Core/TrayDrawing.cs`
- Test: `PCSpecOverlay.Tests/TrayInstallerTests.cs` (create)

**Interfaces:**
- Produces:
  - `record TrayPalette(System.Drawing.Color Bg, Panel, Line, Text, Muted)` with `static TrayPalette Dark` and `static TrayPalette From(Func<string, System.Windows.Media.Color?> lookup)`
  - `MenuIcons.Render(System.Windows.Media.Geometry geometry, int size, System.Windows.Media.Color color) : System.Drawing.Bitmap` (call on an STA/UI thread)

- [ ] **Step 1: Write the failing tests**

Create `PCSpecOverlay.Tests/TrayInstallerTests.cs`:

```csharp
using PCSpecOverlay.Core;
using DColor = System.Drawing.Color;
using MColor = System.Windows.Media.Color;

namespace PCSpecOverlay.Tests;

public class TrayDrawingTests
{
    [Fact]
    public void The_palette_reads_the_theme_keys_and_falls_back_to_dark()
    {
        var colors = new Dictionary<string, MColor>
        {
            ["C.Bg"] = MColor.FromRgb(0xF3, 0xF2, 0xEE),
            ["C.Text"] = MColor.FromRgb(0x14, 0x14, 0x14),
        };
        var p = TrayPalette.From(k => colors.TryGetValue(k, out var c) ? c : null);

        Assert.Equal(DColor.FromArgb(0xF3, 0xF2, 0xEE).ToArgb(), p.Bg.ToArgb());
        Assert.Equal(DColor.FromArgb(0x14, 0x14, 0x14).ToArgb(), p.Text.ToArgb());
        Assert.Equal(TrayPalette.Dark.Line.ToArgb(), p.Line.ToArgb());
    }

    [Fact]
    public void The_dark_fallback_matches_the_dark_color_dictionary()
    {
        var xaml = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/Themes/Colors.Dark.xaml"));
        foreach (var (key, color) in new[] { ("C.Bg", TrayPalette.Dark.Bg), ("C.Panel", TrayPalette.Dark.Panel),
                     ("C.Line", TrayPalette.Dark.Line), ("C.Text", TrayPalette.Dark.Text), ("C.Muted", TrayPalette.Dark.Muted) })
            Assert.Contains($"<Color x:Key=\"{key}\">#{color.R:X2}{color.G:X2}{color.B:X2}</Color>", xaml);
    }

    [Fact]
    public void An_icon_draws_its_strokes_and_leaves_the_rest_clear()
    {
        System.Drawing.Bitmap? bmp = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { bmp = MenuIcons.Render(System.Windows.Media.Geometry.Parse("M2,12 H22"), 24, System.Windows.Media.Colors.White); }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        using (bmp)
        {
            Assert.Equal(24, bmp!.Width);
            Assert.True(bmp.GetPixel(12, 12).A > 200, "the stroke across the middle");
            Assert.Equal(0, bmp.GetPixel(12, 3).A);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test PCSpec.slnx --filter "FullyQualifiedName~TrayDrawingTests"`. Expected: build error `The name 'TrayPalette' does not exist`.

- [ ] **Step 3: Implement**

Create `PCSpecOverlay/Core/TrayDrawing.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bitmap = System.Drawing.Bitmap;
using DColor = System.Drawing.Color;
using DPixelFormat = System.Drawing.Imaging.PixelFormat;
using MColor = System.Windows.Media.Color;

namespace PCSpecOverlay.Core;

/// <summary>The tray menu's colors, read from the app's active color dictionary (Themes/Colors.*.xaml).</summary>
public sealed record TrayPalette(DColor Bg, DColor Panel, DColor Line, DColor Text, DColor Muted)
{
    /// <summary>The dark Instrument colors (same values as Colors.Dark.xaml), used when a key can't be read.</summary>
    public static readonly TrayPalette Dark = new(
        DColor.FromArgb(0x0B, 0x0B, 0x0C), DColor.FromArgb(0x11, 0x11, 0x13), DColor.FromArgb(0x26, 0x26, 0x29),
        DColor.FromArgb(0xE9, 0xE9, 0xE6), DColor.FromArgb(0x87, 0x87, 0x8A));

    public static TrayPalette From(Func<string, MColor?> lookup)
    {
        DColor Read(string key, DColor fallback) => lookup(key) is { } c ? DColor.FromArgb(c.A, c.R, c.G, c.B) : fallback;
        return new(Read("C.Bg", Dark.Bg), Read("C.Panel", Dark.Panel), Read("C.Line", Dark.Line),
            Read("C.Text", Dark.Text), Read("C.Muted", Dark.Muted));
    }
}

/// <summary>Draws an Icon.* geometry (24-unit grid, 1.6 stroke, round caps) into a bitmap for the WinForms tray menu.</summary>
public static class MenuIcons
{
    /// <remarks>Uses WPF rendering, so call it on an STA thread (the app's UI thread).</remarks>
    public static Bitmap Render(Geometry geometry, int size, MColor color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 24.0, size / 24.0));
            var pen = new Pen(new SolidColorBrush(color), 1.6)
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
            };
            dc.DrawGeometry(null, pen, geometry);
        }
        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var pixels = new byte[size * size * 4];
        target.CopyPixels(pixels, size * 4, 0);

        var bmp = new Bitmap(size, size, DPixelFormat.Format32bppPArgb);
        var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, size, size), System.Drawing.Imaging.ImageLockMode.WriteOnly,
            DPixelFormat.Format32bppPArgb);
        try
        {
            for (var y = 0; y < size; y++) Marshal.Copy(pixels, y * size * 4, data.Scan0 + y * data.Stride, size * 4);
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run the Step 2 filter. Expected: 3 passed. Then run `dotnet test PCSpec.slnx`: all pass.

- [ ] **Step 5: Commit**

```bash
git add PCSpecOverlay/Core/TrayDrawing.cs PCSpecOverlay.Tests/TrayInstallerTests.cs
git commit -F- <<'EOF'
Add tray menu colors from the active theme and icon rendering

Co-Authored-By: <model> <noreply@anthropic.com>
EOF
```

---

### Task 2: The tray menu

**Files:**
- Create: `PCSpecOverlay/TrayMenuRenderer.cs`
- Modify: `PCSpecOverlay/TrayIcon.cs`
- Test: `PCSpecOverlay.Tests/TrayInstallerTests.cs` (append)

**Interfaces:**
- Consumes: `TrayPalette`, `MenuIcons.Render` (Task 1); `App.Theme` (`ThemeManager`, with a `Changed` event); `App.TryFindResource`.

- [ ] **Step 1: Write the failing test**

```csharp
public class TrayMenuTests
{
    private static readonly string Tray = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/TrayIcon.cs"));
    private static readonly string Renderer = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/TrayMenuRenderer.cs"));

    [Fact]
    public void The_menu_is_drawn_by_the_instrument_renderer_with_icons()
    {
        Assert.Contains("new TrayMenuRenderer(", Tray);
        foreach (var icon in new[] { "Icon.Window", "Icon.Overlay", "Icon.Pointer", "Icon.Gaming", "Icon.Pause", "Icon.Monitor", "Icon.Power", "Icon.Check" })
            Assert.Contains($"\"{icon}\"", Tray);
        Assert.Contains("MenuIcons.Render(", Tray);
        Assert.Contains("Theme.Changed +=", Tray);
        Assert.Contains("Theme.Changed -=", Tray);
        Assert.Contains("\"Hide overlay\"", Tray);
        Assert.Contains("$\"Open {AppIdentity.ProductName}\"", Tray);
    }

    [Fact]
    public void The_renderer_paints_every_part_from_the_palette()
    {
        foreach (var part in new[] { "OnRenderToolStripBackground", "OnRenderToolStripBorder", "OnRenderMenuItemBackground",
                     "OnRenderItemText", "OnRenderItemCheck", "OnRenderSeparator", "OnRenderImageMargin" })
            Assert.Contains($"override void {part}(", Renderer);
        Assert.DoesNotMatch("Color\\.FromArgb\\(|Color\\.[A-Z][a-z]+[^A-Za-z(]", Renderer);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `--filter "FullyQualifiedName~TrayMenuTests"`. Expected: fail, because `TrayMenuRenderer.cs` doesn't exist.

- [ ] **Step 3: The renderer**

Create `PCSpecOverlay/TrayMenuRenderer.cs`:

```csharp
using System.Drawing;
using PCSpecOverlay.Core;
using Forms = System.Windows.Forms;

namespace PCSpecOverlay;

/// <summary>
/// Paints the tray menu in the Instrument style: Bg fill with a 1 px Line border and separators, a Panel row with a
/// 2 px Text edge under the pointer, shortcuts in Muted monospace, and a check icon at the right of switched-on items.
/// </summary>
internal sealed class TrayMenuRenderer(Func<TrayPalette> palette, Func<Image?> checkMark) : Forms.ToolStripRenderer
{
    private static readonly Font ShortcutFont = new("Cascadia Mono", 8f);

    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
    {
        using var fill = new SolidBrush(palette().Bg);
        e.Graphics.FillRectangle(fill, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(palette().Line);
        var r = e.AffectedBounds;
        e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
    }

    /// <summary>The icon column has the same fill as the rest of the menu.</summary>
    protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var p = palette();
        var r = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
        using var fill = new SolidBrush(p.Panel);
        e.Graphics.FillRectangle(fill, r);
        using var edge = new SolidBrush(p.Text);
        e.Graphics.FillRectangle(edge, r.X, r.Y, 2, r.Height);
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        var item = e.Item as Forms.ToolStripMenuItem;
        var isShortcut = item is { ShortcutKeyDisplayString.Length: > 0 } && e.Text == item.ShortcutKeyDisplayString;
        if (isShortcut && string.IsNullOrWhiteSpace(e.Text)) return; // room reserved for the check, nothing to write
        var p = palette();
        e.TextColor = isShortcut || !e.Item.Enabled ? p.Muted : p.Text;
        if (isShortcut) e.TextFont = ShortcutFont;
        base.OnRenderItemText(e);
        if (!isShortcut && item is { Checked: true } && checkMark() is { } mark)
            e.Graphics.DrawImage(mark, item.Width - mark.Width - 10, (item.Height - mark.Height) / 2, mark.Width, mark.Height);
    }

    /// <summary>A switched-on item keeps its own icon (no framed box); its check is drawn at the right.</summary>
    protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image != null) e.Graphics.DrawImage(e.Image, e.ImageRectangle);
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(palette().Line);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
    }
}
```

- [ ] **Step 4: The menu**

Rewrite the menu part of `TrayIcon.cs`, keeping the notify icon, `Notify`, `DrawIcon` and the taskbar-theme handling exactly as they are:

1. Add fields:

```csharp
    /// <summary>Reserves room at the right of a switch-like item for its check (the renderer draws nothing for it).</summary>
    private const string CheckRoom = "  ";

    private readonly Forms.ContextMenuStrip _menu;
    private readonly List<(Forms.ToolStripMenuItem Item, string Icon)> _iconItems = new();
    private TrayPalette _palette = TrayPalette.Dark;
    private Image? _check;
```

2. Replace the menu construction (from `var menu = new Forms.ContextMenuStrip …` through the `menu.Opening` handler) with:

```csharp
        _menu = new Forms.ContextMenuStrip { ShowImageMargin = true, ShowCheckMargin = false };
        _menu.Renderer = new TrayMenuRenderer(() => _palette, () => _check);

        Forms.ToolStripMenuItem Item(string text, string icon, Action click)
        {
            var item = new Forms.ToolStripMenuItem(text);
            item.Click += (_, _) => click();
            _iconItems.Add((item, icon));
            return item;
        }

        var center = Item($"Open {AppIdentity.ProductName}", "Icon.Window", _app.OpenControlCenter);
        center.Font = new Font(_menu.Font, FontStyle.Bold);
        var overlay = Item("Hide overlay", "Icon.Overlay", _app.ToggleOverlay);
        var move = Item("Move overlay", "Icon.Pointer", _app.MoveOverlay);
        var gaming = Item("Automatic gaming mode", "Icon.Gaming", () =>
        {
            _app.Settings.AutoGamingMode = !_app.Settings.AutoGamingMode;
            _app.SaveSettings();
        });
        gaming.ShortcutKeyDisplayString = CheckRoom;
        var pause = Item("Stop optimizations now", "Icon.Pause", () =>
        {
            if (_app.Engine.OptimizationsPaused) _app.ResumeOptimizations();
            else Notify(string.Join(" ", _app.StopOptimizations()), "Optimizations stopped");
        });
        Forms.ToolStripMenuItem startup = null!;
        startup = Item("Start with Windows", "Icon.Monitor", () =>
        {
            var result = StartupTask.SetEnabled(!startup.Checked);
            if (result != null) Notify(result, "Start with Windows");
        });
        startup.ShortcutKeyDisplayString = CheckRoom;
        var exit = Item("Exit", "Icon.Power", _app.Shutdown);

        _menu.Items.AddRange([center, new Forms.ToolStripSeparator(), overlay, move, gaming, pause, startup,
                              new Forms.ToolStripSeparator(), exit]);
        _menu.Opening += (_, _) =>
        {
            overlay.Text = _app.OverlayVisible ? "Hide overlay" : "Show overlay";
            overlay.ShortcutKeyDisplayString = _app.HotkeyInUse;
            gaming.Checked = _app.Settings.AutoGamingMode;
            pause.Text = _app.Engine.OptimizationsPaused ? "Resume optimizations" : "Stop optimizations now";
            startup.Checked = StartupTask.Status().Enabled;
        };
        ApplyTheme();
        _app.Theme.Changed += OnAppThemeChanged;
```

   - In the `NotifyIcon` initializer, `ContextMenuStrip = menu` becomes `ContextMenuStrip = _menu`.
   - `_app.Shutdown` has an optional parameter in WPF. If the method group doesn't convert to `Action`, use `() => _app.Shutdown()`.
3. Add:

```csharp
    /// <summary>Colors and icons follow the app's light or dark theme; redrawn whenever it changes.</summary>
    private void ApplyTheme()
    {
        _palette = TrayPalette.From(key => _app.TryFindResource(key) is System.Windows.Media.Color c ? c : null);
        var size = (int)Math.Round(16 * _menu.DeviceDpi / 96.0);
        _menu.ImageScalingSize = new Size(size, size);
        var ink = System.Windows.Media.Color.FromArgb(_palette.Text.A, _palette.Text.R, _palette.Text.G, _palette.Text.B);
        foreach (var (item, key) in _iconItems)
        {
            var old = item.Image;
            item.Image = _app.TryFindResource(key) is System.Windows.Media.Geometry g ? MenuIcons.Render(g, size, ink) : null;
            old?.Dispose();
        }
        var oldCheck = _check;
        _check = _app.TryFindResource("Icon.Check") is System.Windows.Media.Geometry check ? MenuIcons.Render(check, size, ink) : null;
        oldCheck?.Dispose();
        _menu.Invalidate();
    }

    private void OnAppThemeChanged(AppTheme theme)
    {
        if (!_disposed) ApplyTheme();
    }
```

4. In `Dispose()`, after `_disposed = true;`, add `_app.Theme.Changed -= OnAppThemeChanged;`. After `_icon.Dispose();`, add `_menu.Dispose(); _check?.Dispose();`. Item images are disposed with the menu's items; if they aren't, dispose them in a loop over `_iconItems`.
5. `App.xaml.cs` creates the tray after `_theme` (line ~95 vs ~78); keep that order.

- [ ] **Step 5: Build and test** — 0 warnings; `TrayMenuTests` and `TrayDrawingTests` pass; the full suite passes.

- [ ] **Step 6: Commit** — message: "Draw the tray menu in the Instrument style with icons, following the app theme".

---

### Task 3: The installer — One card

**Files:**
- Modify: `PCSpecOverlay/InstallerWindow.xaml`, `PCSpecOverlay/InstallerWindow.xaml.cs`
- Test: `PCSpecOverlay.Tests/TrayInstallerTests.cs` (append)

**Interfaces:**
- Consumes: `ThemeManager(ResourceDictionary)`, `.Apply(string)`, `.Current`, `.Changed`, `.Dispose()`; `TitleBar.Apply(Window, bool dark)`; styles `PageTitle`, `Body`, `LedgerRow`, `Switch`, `Btn`.

**Ruling carried into this task:** the installer runs before the app has a theme manager, and in setup mode its data folder isn't the installed copy's, so it can't read the user's saved Appearance. It follows the Windows app theme (Match Windows), which is what a fresh install defaults to.

- [ ] **Step 1: Write the failing test**

```csharp
public class InstallerWindowTests
{
    [Fact]
    public void The_installer_is_one_card_that_follows_the_theme()
    {
        var xaml = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/InstallerWindow.xaml"));
        var code = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/InstallerWindow.xaml.cs"));
        foreach (var part in new[] { "{DynamicResource Bg}", "StaticResource PageTitle", "x:Name=\"Option2Row\"", "x:Name=\"ResultBox\"",
                     "x:Name=\"Primary\"", "x:Name=\"Secondary\"" })
            Assert.Contains(part, xaml);
        foreach (var gone in new[] { "DynamicResource Window}", "CardBox", "\"H1\"", "\"Lead\"" })
            Assert.DoesNotContain(gone, xaml);
        Assert.DoesNotContain("Danger", code);
        Assert.DoesNotContain("dark: true", code);
        Assert.Contains("_theme.Apply(Settings.MatchWindows)", code);
        Assert.Contains("_theme.Changed +=", code);
        Assert.Contains("_theme.Dispose()", code);
    }
}
```

- [ ] **Step 2: Run to verify failure** — `--filter "FullyQualifiedName~InstallerWindowTests"`; expected: 1 failed.

- [ ] **Step 3: The window**

Replace `InstallerWindow.xaml`'s content (keep the `Window` start tag's attributes, except `Background="{DynamicResource Window}"`, which becomes `{DynamicResource Bg}`) with:

```xml
    <StackPanel>
        <StackPanel Margin="28,24,28,18">
            <StackPanel Orientation="Horizontal">
                <core:Icon Data="{StaticResource Icon.Mark}" Width="22" Height="22" Margin="0,0,10,0" VerticalAlignment="Center" />
                <TextBlock x:Name="Heading" Style="{StaticResource PageTitle}" FontSize="18" VerticalAlignment="Center" />
            </StackPanel>
            <TextBlock x:Name="Intro" Style="{StaticResource Body}" Margin="0,10,0,0" />
        </StackPanel>

        <StackPanel x:Name="Options">
            <Border Style="{StaticResource LedgerRow}" BorderThickness="0,1,0,0" Padding="28,10">
                <CheckBox x:Name="Option1" Style="{StaticResource Switch}" IsChecked="True" />
            </Border>
            <Border x:Name="Option2Row" Style="{StaticResource LedgerRow}" BorderThickness="0,1,0,0" Padding="28,10">
                <CheckBox x:Name="Option2" Style="{StaticResource Switch}" />
            </Border>
        </StackPanel>

        <Border x:Name="ResultBox" BorderBrush="{DynamicResource Line2}" BorderThickness="0,1,0,0" Padding="28,10" Visibility="Collapsed">
            <TextBlock x:Name="ResultText" Style="{StaticResource Body}" Margin="0" Foreground="{DynamicResource Text}" />
        </Border>

        <Border BorderBrush="{DynamicResource Line}" BorderThickness="0,1,0,0" Padding="28,14">
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button x:Name="Secondary" Content="Cancel" Style="{StaticResource Btn}" Margin="0,0,8,0" Click="OnCancel" IsCancel="True" />
                <Button x:Name="Primary" Tag="Primary" Style="{StaticResource Btn}" Click="OnPrimary" IsDefault="True" />
            </StackPanel>
        </Border>
    </StackPanel>
```

- [ ] **Step 4: Code**

In `InstallerWindow.xaml.cs`:
1. Add a field: `private readonly ThemeManager _theme = new(Application.Current.Resources);`
2. Replace `SourceInitialized += (_, _) => TitleBar.Apply(this, dark: true);` with:

```csharp
        // Setup runs before the app has a theme, and can't read the installed copy's settings: follow Windows
        _theme.Apply(Settings.MatchWindows);
        SourceInitialized += (_, _) => TitleBar.Apply(this, _theme.Current == AppTheme.Dark);
        _theme.Changed += theme => TitleBar.Apply(this, theme == AppTheme.Dark);
        Closed += (_, _) => _theme.Dispose();
```

3. Install mode: replace `Option2.Visibility = Visibility.Collapsed;` with `Option2Row.Visibility = Visibility.Collapsed;`.
4. Uninstall mode: delete `Primary.Tag = "Danger";`. The XAML's `Tag="Primary"` applies.
5. In `OnPrimary`, `Primary.Tag = "Primary";` can stay; it's harmless.

- [ ] **Step 5: Build and test** — 0 warnings; `InstallerWindowTests` passes; the full suite passes.

- [ ] **Step 6: Commit** — message: "Restyle the installer as one card that follows the Windows theme".

---

## After the tasks: on-PC verification (controller, with the user)

Release development build, beside the installed copy. Don't use the desktop screenshot tool on the app's windows: it hides other apps' windows and resizes or minimizes them. Capture windows with PrintWindow and drive them through UI Automation, as in stage 4. Record the results in `PCSpecOverlay/docs/AUDIT.md` under "Instrument stage 5".

1. **Installer:**
   - Run `PCSpecOverlay.exe --install` from the dev build output, capture it, then press Cancel through UI Automation. Repeat with `--uninstall`.
   - Compare with tray-installer.html A. Check the heading, the switch rows, Primary on the right, and the title bar in the Windows theme.
   - Nothing is installed or removed, because nothing happens before the main button.
2. **Tray menu:** the user right-clicks the dev copy's tray icon (one of two Optiframe icons) and checks:
   - Colors match the app theme.
   - Icons sit in the left column.
   - The shortcut is right-aligned in monospace.
   - Checks appear at the right for Automatic gaming mode and Start with Windows.
   - The hover row has the 2 px edge.
   - It still works after flipping the app between light and dark.
