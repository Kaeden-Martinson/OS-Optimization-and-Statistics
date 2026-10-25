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
