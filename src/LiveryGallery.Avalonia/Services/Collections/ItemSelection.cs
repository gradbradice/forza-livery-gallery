namespace LiveryGallery.Services.Collections;

/// <summary>Something a grid of tiles can select (a screenshot, a livery of the storage)</summary>
internal interface ISelectableItem
{
    bool IsSelected { get; set; }

    /// <summary>The cursor of the keys is on it (a ring round the tile)</summary>
    bool IsCurrent { get; set; }
}

/// <summary>
/// The selection of a grid of tiles, as in Explorer: a click selects one, Ctrl+click adds or takes away, Shift+click
/// selects from the last one clicked (the anchor) to this one in the order on screen. The keys move a cursor
/// (<see cref="MoveTo"/>, <see cref="ToggleCursor"/>) with the same rules as the cards' (<see cref="GalleryMoveSelection"/>).
/// The items keep their own <see cref="ISelectableItem.IsSelected"/> and <see cref="ISelectableItem.IsCurrent"/>, which
/// the tiles show
/// </summary>
internal sealed class ItemSelection<T> where T : class, ISelectableItem
{
    private IReadOnlyList<T> _items = [];
    private T? _anchor;
    private T? _cursor;

    /// <summary>Where the keys are: the last tile clicked or moved to; null before either</summary>
    public T? Cursor => _cursor;

    /// <summary>The items in the order on screen</summary>
    public IReadOnlyList<T> Items => _items;

    /// <summary>The selected items, in the order on screen</summary>
    public IReadOnlyList<T> Selected => [.. _items.Where(i => i.IsSelected)];

    public bool HasSelection => _items.Any(i => i.IsSelected);

    /// <summary>A new list on screen (a filter, a regroup): what is selected and still there stays selected</summary>
    public void Reset(IReadOnlyList<T> items, Func<T, T, bool>? same = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        same ??= ReferenceEquals;
        var was = _items.Where(i => i.IsSelected).ToList();
        foreach (var item in items) item.IsSelected = was.Any(w => same(w, item));
        _anchor = _anchor is { } anchor ? items.FirstOrDefault(i => same(anchor, i)) : null;
        var cursor = _cursor is { } c ? items.FirstOrDefault(i => same(c, i)) : null;
        foreach (var item in items) item.IsCurrent = false;
        _items = items;
        SetCursor(cursor);
    }

    /// <summary>A click on a tile with the keys held</summary>
    public void Click(T item, bool control, bool shift)
    {
        ArgumentNullException.ThrowIfNull(item);
        int to = IndexOf(item);
        if (to < 0) return;
        SetCursor(item);
        if (shift && _anchor is { } anchor && IndexOf(anchor) is var from and >= 0)
        {
            if (!control) foreach (var i in _items) i.IsSelected = false;
            for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++) _items[i].IsSelected = true;
            return;
        }
        if (control) item.IsSelected = !item.IsSelected;
        else
        {
            foreach (var i in _items) i.IsSelected = ReferenceEquals(i, item);
        }
        _anchor = item;
    }

    /// <summary>A right click: a tile outside the selection becomes the selection, one inside keeps it (the menu acts on all)</summary>
    public void Point(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsSelected) return;
        Click(item, control: false, shift: false);
    }

    /// <summary>The cursor moved by a key to <paramref name="target"/>, the selection with it as the cards' does</summary>
    public void MoveTo(T target, GalleryMoveSelection selection)
    {
        ArgumentNullException.ThrowIfNull(target);
        switch (selection)
        {
            case GalleryMoveSelection.SelectOnly:
                Click(target, control: false, shift: false);
                break;
            case GalleryMoveSelection.Extend:
                if (_anchor is null) _anchor = _cursor ?? target;
                Click(target, control: false, shift: true);
                break;
            case GalleryMoveSelection.ExtendAdd:
                if (_anchor is null) _anchor = _cursor ?? target;
                Click(target, control: true, shift: true);
                break;
            default: // CursorOnly: Space then adds or takes away
                SetCursor(target);
                break;
        }
    }

    /// <summary>Space: the tile under the cursor into the selection or out of it; false when there is no cursor</summary>
    public bool ToggleCursor()
    {
        if (_cursor is not { } cursor) return false;
        cursor.IsSelected = !cursor.IsSelected;
        _anchor = cursor;
        return true;
    }

    private void SetCursor(T? item)
    {
        if (_cursor is { } old) old.IsCurrent = false;
        _cursor = item;
        if (item is not null) item.IsCurrent = true;
    }

    public void SelectAll()
    {
        foreach (var i in _items) i.IsSelected = true;
    }

    /// <summary>False when nothing was selected</summary>
    public bool Clear()
    {
        bool any = HasSelection;
        foreach (var i in _items) i.IsSelected = false;
        _anchor = null;
        return any;
    }

    private int IndexOf(T item)
    {
        for (int i = 0; i < _items.Count; i++)
            if (ReferenceEquals(_items[i], item)) return i;
        return -1;
    }
}
