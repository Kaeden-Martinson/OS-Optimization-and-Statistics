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
            Assert.True(bmp.GetPixel(12, 12).A > 128, "the stroke across the middle");
            Assert.Equal(0, bmp.GetPixel(12, 3).A);
        }
    }
}

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
        Assert.DoesNotMatch(@"Color\.FromArgb\(|Color\.[A-Z][a-z]+[^A-Za-z(]", Renderer);
    }
}

public class TrayMenuRenderingTests
{
    /// <summary>Where the rendered menu is saved for a look by eye.</summary>
    public static readonly string RenderedPng = Path.Combine(Path.GetTempPath(), "Optiframe.Tests", "tray-menu.png");

    private static readonly TrayPalette Light = new(DColor.FromArgb(0xF3, 0xF2, 0xEE), DColor.FromArgb(0xE6, 0xE5, 0xE0),
        DColor.FromArgb(0xC8, 0xC7, 0xC2), DColor.FromArgb(0x14, 0x14, 0x14), DColor.FromArgb(0x6E, 0x6E, 0x70));
    private static readonly DColor CheckColor = DColor.FromArgb(0xD0, 0x20, 0x20);
    private static readonly DColor IconColor = DColor.FromArgb(0x20, 0x70, 0xD0);

    private static System.Drawing.Bitmap Solid(DColor color)
    {
        var bmp = new System.Drawing.Bitmap(16, 16);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(color);
        return bmp;
    }

    /// <summary>Like a real menu icon: a stroke on a transparent ground, so anything painted under it would show.</summary>
    private static System.Drawing.Bitmap Stroke(DColor color)
    {
        var bmp = new System.Drawing.Bitmap(16, 16);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(DColor.Transparent);
        using var brush = new System.Drawing.SolidBrush(color);
        g.FillRectangle(brush, 1, 1, 14, 2);
        return bmp;
    }

    [Fact]
    public void The_menu_paints_with_the_palette_and_draws_the_check_at_the_right()
    {
        System.Drawing.Bitmap? shot = null;
        System.Drawing.Rectangle checkedRow = default, plainRow = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var check = Solid(CheckColor);
                using var icon = Stroke(IconColor);
                using var menu = new System.Windows.Forms.ContextMenuStrip
                {
                    ShowImageMargin = true, ShowCheckMargin = false, DropShadowEnabled = false,
                    ImageScalingSize = new System.Drawing.Size(16, 16),
                };
                menu.Renderer = new TrayMenuRenderer(() => Light, () => check);
                var on = new System.Windows.Forms.ToolStripMenuItem("Automatic gaming mode")
                {
                    Checked = true, Image = icon, ShortcutKeyDisplayString = new string(' ', 6),
                };
                var off = new System.Windows.Forms.ToolStripMenuItem("Hide overlay")
                {
                    Image = icon, ShortcutKeyDisplayString = "Alt+Shift+O",
                };
                menu.Items.AddRange([on, new System.Windows.Forms.ToolStripSeparator(), off]);
                menu.PerformLayout();
                menu.Size = menu.PreferredSize;
                menu.CreateControl();
                checkedRow = on.Bounds;
                plainRow = off.Bounds;
                shot = new System.Drawing.Bitmap(menu.Width, menu.Height);
                // WM_PRINT through the strip's own OnPaint, so each item paints itself via the renderer
                menu.DrawToBitmap(shot, new System.Drawing.Rectangle(0, 0, menu.Width, menu.Height));
            }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        using (shot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RenderedPng)!);
            shot!.Save(RenderedPng, System.Drawing.Imaging.ImageFormat.Png);

            static bool Near(DColor a, DColor b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 24;
            Assert.True(checkedRow.Height > 0 && plainRow.Height > 0, "the items were laid out");
            Assert.Equal(Light.Bg.ToArgb(), shot.GetPixel(shot.Width / 2, plainRow.Bottom - 1).ToArgb());
            Assert.Equal(Light.Line.ToArgb(), shot.GetPixel(0, shot.Height / 2).ToArgb());

            var cy = checkedRow.Top + checkedRow.Height / 2;
            // The image column: the item's icon, and no stock black WinForms check under or beside it
            var iconSeen = false;
            for (var x = checkedRow.Left + 1; x < checkedRow.Left + 28; x++)
                for (var y = checkedRow.Top; y < checkedRow.Bottom; y++)
                {
                    var c = shot.GetPixel(x, y);
                    Assert.False(c.R + c.G + c.B < 120, $"near-black pixel {c} at ({x},{y}) in the image column");
                    iconSeen |= Near(c, IconColor);
                }
            Assert.True(iconSeen, "the item's icon is drawn in the image column");

            // The check: drawn in the reserved room at the right of the checked row, and only there
            static bool HasCheck(System.Drawing.Bitmap b, System.Drawing.Rectangle row, int from, int to)
            {
                for (var x = from; x < to; x++)
                    for (var y = row.Top; y < row.Bottom; y++)
                        if (Near(b.GetPixel(x, y), CheckColor)) return true;
                return false;
            }
            Assert.True(HasCheck(shot, checkedRow, checkedRow.Right - 40, checkedRow.Right), "the check near the right edge");
            Assert.False(HasCheck(shot, plainRow, plainRow.Left, plainRow.Right), "no check on the unchecked item");
            Assert.False(HasCheck(shot, checkedRow, checkedRow.Left, checkedRow.Right - 40), "the check only at the right");
            Assert.Equal(Light.Bg.ToArgb(), shot.GetPixel(checkedRow.Right - 4, cy - checkedRow.Height / 2).ToArgb());
        }
    }
}

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
        // Setup follows Windows; uninstall (run by the installed copy) uses the saved Appearance
        Assert.Contains("_theme.Apply(mode == Mode.Uninstall ? SavedAppearance() : Settings.MatchWindows)", code);
        Assert.Contains("Settings.Load().Appearance", code);
        Assert.Contains("_theme.Changed +=", code);
        Assert.Contains("_theme.Dispose()", code);
    }

    [Fact]
    public void Switch_labels_wrap_instead_of_clipping_and_keep_their_accessible_name()
    {
        var code = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/InstallerWindow.xaml.cs"));
        Assert.Contains("TextWrapping = TextWrapping.Wrap", code);
        foreach (var option in new[] { "Option1.Content = Wrapped(", "Option2.Content = Wrapped(" })
            Assert.Contains(option, code);
        Assert.DoesNotMatch(@"Option\d\.Content = """, code);
        // the screen-reader name comes from the first TextBlock in the content
        var name = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/Core/AccessibleName.cs"));
        Assert.Contains("TextBlock { Text.Length: > 0 }", name);
    }
}
