using Avalonia;

namespace LiveryGallery.Views;

/// <summary>
/// The numbers of selecting with a rectangle drawn over the cards, as in Explorer: press on the empty space of the
/// gallery, drag, and every card the rectangle touches is selected (with Ctrl or Shift, added to what was selected).
/// The rectangle is kept in the list's own coordinates, so it grows with the list scrolling under the pointer
/// </summary>
internal static class GalleryBand
{
    /// <summary>How far the pointer goes from the press before a rectangle starts: a click stays a click</summary>
    public const double DragThreshold = 4;

    /// <summary>The band near the top and the bottom of the list where the drag scrolls it, and the fastest it goes</summary>
    public const double ScrollEdge = 28, MaxScrollSpeed = 2400;

    /// <summary>The rectangle between the press and the pointer, whichever way it was dragged</summary>
    public static Rect Between(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>Whether the pointer went far enough from the press for a rectangle</summary>
    public static bool Started(Point pressed, Point now) =>
        Math.Abs(now.X - pressed.X) > DragThreshold || Math.Abs(now.Y - pressed.Y) > DragThreshold;

    /// <summary>
    /// The cards of one row the rectangle touches: the first and the last of them, or null when none. A card takes its
    /// slot without the margin around it, so the rectangle has to reach the card itself, not the gap beside it
    /// </summary>
    /// <param name="band">The rectangle, in the list's coordinates</param>
    /// <param name="rowTop">The row's top and bottom there</param>
    /// <param name="rowBottom">See <paramref name="rowTop"/></param>
    /// <param name="left">Where the row's first slot starts</param>
    /// <param name="pitch">A slot's width (the card and its margins)</param>
    /// <param name="margin">The margin around a card</param>
    /// <param name="count">The cards in the row</param>
    public static (int First, int Last)? Columns(Rect band, double rowTop, double rowBottom, double left, double pitch, double margin, int count)
    {
        if (count <= 0 || pitch <= 0 || band.Bottom < rowTop + margin || band.Y > rowBottom - margin) return null;
        // Card i lies from left + i * pitch + margin to left + (i + 1) * pitch - margin
        int first = (int)Math.Max(0, Math.Ceiling((band.X - left + margin) / pitch) - 1);
        int last = (int)Math.Min(count - 1, Math.Floor((band.Right - left - margin) / pitch));
        return first <= last ? (first, last) : null;
    }

    /// <summary>
    /// How fast the list scrolls while the rectangle is dragged at its edge or past it, in pixels a second: nothing
    /// inside, faster the farther past the edge, up for a negative value
    /// </summary>
    /// <param name="pointerY">The pointer, from the top of what is seen of the list</param>
    /// <param name="viewportHeight">How much of the list is seen</param>
    public static double ScrollSpeed(double pointerY, double viewportHeight)
    {
        if (double.IsNaN(pointerY) || viewportHeight <= 0) return 0;
        double over = pointerY < ScrollEdge ? pointerY - ScrollEdge : pointerY > viewportHeight - ScrollEdge ? pointerY - (viewportHeight - ScrollEdge) : 0;
        return Math.Clamp(over * 12, -MaxScrollSpeed, MaxScrollSpeed);
    }
}
