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

    /// <summary>
    /// Where the move-mode window goes so the panel stays exactly at (panelX, panelY) and the handle bar stays on
    /// screen: the handle sits above the panel unless that would cross the work area's top, then below it; a handle
    /// wider than the panel extends to the right unless that would cross the work area's right edge, then to the left.
    /// </summary>
    public static (int Left, int Top, bool HandleBelow, bool PanelRight) MoveWindow(int panelX, int panelY,
        (int Left, int Top, int Right, int Bottom) work, int panelWidth, int windowWidth, int handle)
    {
        var below = panelY - handle < work.Top;
        var extra = Math.Max(0, windowWidth - panelWidth);
        var right = extra > 0 && panelX + windowWidth > work.Right;
        return (right ? panelX - extra : panelX, below ? panelY : panelY - handle, below, right);
    }

    /// <summary>
    /// Moves a window of the given size the least distance that puts it fully inside the work area; a window larger
    /// than the work area starts at its left or top edge.
    /// </summary>
    public static (int Left, int Top) Clamp(int left, int top, int width, int height,
        (int Left, int Top, int Right, int Bottom) work) =>
        (Math.Max(work.Left, Math.Min(left, work.Right - width)),
         Math.Max(work.Top, Math.Min(top, work.Bottom - height)));

    /// <summary>
    /// Where the overlay window goes for its current size, in that size's pixels: the panel at its corner or pinned
    /// spot (so right and bottom placements keep their right or bottom edge as the size changes), the move handle
    /// beside it (<paramref name="handle"/> is 0 when pinned), and the whole window kept inside the work area.
    /// While pinned the window is all panel, so a panel width that disagrees with the window's is ignored.
    /// </summary>
    public static (int Left, int Top, bool HandleBelow, bool PanelRight) Arrange(string position, double customX, double customY,
        (int Left, int Top, int Right, int Bottom) work, int windowWidth, int windowHeight, int panelWidth, int handle, int margin)
    {
        if (handle <= 0 || panelWidth <= 0 || panelWidth > windowWidth) panelWidth = windowWidth;
        handle = Math.Max(0, handle);
        var (x, y) = Place(position, customX, customY, work, panelWidth, windowHeight - handle, margin);
        var (left, top, below, right) = MoveWindow(x, y, work, panelWidth, windowWidth, handle);
        (left, top) = Clamp(left, top, windowWidth, windowHeight, work);
        return (left, top, below, right);
    }

    /// <summary>
    /// Where a window placed at <paramref name="last"/> goes when only its size changed: it keeps the edges it is anchored
    /// to (right for right corners and pins at 0.5 or more across, bottom likewise, otherwise left and top), clamped to
    /// the work area. Recomputing from the pin's share of the free space instead would nudge every edge by a pixel or
    /// two each time a row comes and goes.
    /// </summary>
    public static (int Left, int Top) KeepEdges(string position, double customX, double customY,
        (int Left, int Top, int Right, int Bottom) last, int width, int height,
        (int Left, int Top, int Right, int Bottom) work)
    {
        var custom = position == Settings.Custom;
        var right = custom ? customX >= 0.5 : position.EndsWith("Right");
        var bottom = custom ? customY >= 0.5 : position.StartsWith("Bottom");
        return Clamp(right ? last.Right - width : last.Left, bottom ? last.Bottom - height : last.Top, width, height, work);
    }
}
