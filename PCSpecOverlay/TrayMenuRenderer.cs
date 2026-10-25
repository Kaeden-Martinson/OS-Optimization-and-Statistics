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
    /// <summary>Cascadia Mono, or Consolas where it isn't installed (GDI+ would otherwise fall back to Microsoft Sans Serif).</summary>
    private static readonly Font ShortcutFont = new(MonoFamily(), 8f);

    private static string MonoFamily()
    {
        using var fonts = new System.Drawing.Text.InstalledFontCollection();
        return fonts.Families.Any(f => f.Name == "Cascadia Mono") ? "Cascadia Mono" : "Consolas";
    }

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
        var r = new Rectangle(0, 0, e.Item.Width, e.Item.Height);
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
        e.TextColor = isShortcut ? p.Muted : p.Text;
        if (isShortcut) e.TextFont = ShortcutFont;
        base.OnRenderItemText(e);
        if (!isShortcut && item is { Checked: true } && e.Text == item.Text && checkMark() is { } mark)
            e.Graphics.DrawImage(mark, item.Width - mark.Width - 10, (item.Height - mark.Height) / 2, mark.Width, mark.Height);
    }

    /// <summary>Draws nothing: the item's own icon comes from OnRenderItemImage and the check is drawn at the right in OnRenderItemText.</summary>
    protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
    {
        // WinForms would paint its built-in black check here, under the item icon.
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(palette().Line);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
    }
}
