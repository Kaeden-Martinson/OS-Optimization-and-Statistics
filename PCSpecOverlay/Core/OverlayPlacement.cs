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
