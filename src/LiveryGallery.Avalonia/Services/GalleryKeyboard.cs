using Avalonia.Input;

namespace LiveryGallery.Services;

/// <summary>Where a key of the arrows moves the gallery's cursor</summary>
internal enum GalleryMove
{
    Left,
    Right,
    Up,
    Down,
    PageUp,
    PageDown,
    Home,
    End,
}

/// <summary>What a key of the arrows does with the selection, as in Explorer</summary>
internal enum GalleryMoveSelection
{
    /// <summary>No modifier: the card moved to is selected alone</summary>
    SelectOnly,

    /// <summary>Shift: the cards from the anchor to the one moved to are selected</summary>
    Extend,

    /// <summary>Ctrl: only the cursor moves; Space then adds the card to the selection or takes it out</summary>
    CursorOnly,

    /// <summary>Ctrl+Shift: the cards from the anchor to the one moved to are added to the selection</summary>
    ExtendAdd,
}

/// <summary>
/// The arrows, Home, End, Page Up and Page Down of the gallery: from a card to another in the rows on screen (a folded
/// group's cards left out). ← and → go on to the row before or after, ↑ and ↓ keep the column (the last card of a
/// shorter row), Page Up and Page Down go a screen of rows, Home and End to the first and the last card
/// </summary>
internal static class GalleryKeyboard
{
    /// <summary>The move and the selection of a key; null for a key that is not one of them (or with Alt, Win)</summary>
    public static (GalleryMove Move, GalleryMoveSelection Selection)? Match(Key key, KeyModifiers modifiers)
    {
        GalleryMove? move = key switch
        {
            Key.Left => GalleryMove.Left,
            Key.Right => GalleryMove.Right,
            Key.Up => GalleryMove.Up,
            Key.Down => GalleryMove.Down,
            Key.PageUp => GalleryMove.PageUp,
            Key.PageDown => GalleryMove.PageDown,
            Key.Home => GalleryMove.Home,
            Key.End => GalleryMove.End,
            _ => null,
        };
        if (move is null) return null;
        GalleryMoveSelection? selection = modifiers switch
        {
            KeyModifiers.None => GalleryMoveSelection.SelectOnly,
            KeyModifiers.Shift => GalleryMoveSelection.Extend,
            KeyModifiers.Control => GalleryMoveSelection.CursorOnly,
            KeyModifiers.Control | KeyModifiers.Shift => GalleryMoveSelection.ExtendAdd,
            _ => null,
        };
        return selection is null ? null : (move.Value, selection.Value);
    }

    /// <summary>
    /// The card a move goes to from <paramref name="current"/>'s place (<paramref name="row"/>, <paramref name="column"/>);
    /// null when there is no card. From no place (-1), any move goes to <paramref name="start"/>'s, or to the first card
    /// (End to the last)
    /// </summary>
    /// <param name="rows">The rows on screen, top first</param>
    /// <param name="row">The row of the cursor; -1 for none</param>
    /// <param name="column">Its column</param>
    /// <param name="move">The key</param>
    /// <param name="pageRows">How many rows a page holds</param>
    /// <param name="start">Where a cursor not on screen starts: the first row in view; -1 for the top</param>
    public static (int Row, int Column)? Target(IReadOnlyList<int> rows, int row, int column, GalleryMove move, int pageRows, int start = -1)
    {
        ArgumentNullException.ThrowIfNull(rows);
        int count = rows.Count;
        if (count == 0) return null;
        int Last(int r) => Math.Max(0, rows[r] - 1);
        (int, int) At(int r, int c) => (r, Math.Min(c, Last(r)));
        if (row < 0 || row >= count)
            return move == GalleryMove.End ? (count - 1, Last(count - 1)) : (Math.Clamp(start, 0, count - 1), 0);
        column = Math.Min(column, Last(row));
        return move switch
        {
            GalleryMove.Left when column > 0 => (row, column - 1),
            GalleryMove.Left => row > 0 ? (row - 1, Last(row - 1)) : (row, column),
            GalleryMove.Right when column < Last(row) => (row, column + 1),
            GalleryMove.Right => row < count - 1 ? (row + 1, 0) : (row, column),
            GalleryMove.Up => At(Math.Max(0, row - 1), column),
            GalleryMove.Down => At(Math.Min(count - 1, row + 1), column),
            GalleryMove.PageUp => At(Math.Max(0, row - Math.Max(1, pageRows)), column),
            GalleryMove.PageDown => At(Math.Min(count - 1, row + Math.Max(1, pageRows)), column),
            GalleryMove.Home => (0, 0),
            GalleryMove.End => (count - 1, Last(count - 1)),
            _ => (row, column),
        };
    }
}
