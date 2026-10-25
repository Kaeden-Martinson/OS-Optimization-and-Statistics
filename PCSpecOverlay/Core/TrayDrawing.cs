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
