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
        bmp.SetResolution(96, 96); // fixed, so the PNG bytes (pHYs chunk) never depend on the process DPI state
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
