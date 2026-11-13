using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace LiveryGallery.Views;

public sealed class VirtualGalleryPanel : Panel
{
    public static readonly StyledProperty<IDataTemplate?> HeaderTemplateProperty =
        AvaloniaProperty.Register<VirtualGalleryPanel, IDataTemplate?>(nameof(HeaderTemplate));

    public static readonly StyledProperty<IDataTemplate?> RowTemplateProperty =
        AvaloniaProperty.Register<VirtualGalleryPanel, IDataTemplate?>(nameof(RowTemplate));

    public IDataTemplate? HeaderTemplate
    {
        get => GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    public IDataTemplate? RowTemplate
    {
        get => GetValue(RowTemplateProperty);
        set => SetValue(RowTemplateProperty, value);
    }

    public Func<object, IEnumerable<string>>? AnchorKeySelector { get; set; }

    private const double DefaultHeaderHeight = 56;
    private const double DefaultRowHeight = 340;
    private const double MinBuffer = 300;
    private const int MaxPooledPerKind = 12;
    // Once realized, the viewport may move this far inside the realized area before a new
    // measure pass is needed
    private const double RemeasureMargin = 120;
    private const int MaxAnchorCandidates = 8;
    // Items past the estimated end of the realized area a kept row may still be for (heights not measured yet)
    private const int ReuseLookahead = 8;

    private IReadOnlyList<object> _items = [];
    private bool[] _isHeader = [];
    private double[] _heights = []; // NaN = not measured yet at the current width
    private double[] _tops = [0]; // _tops[i] = top of item i; _tops[n] = total height
    private bool _topsDirty = true;
    private double _coveredTop, _coveredBottom = -1; // area realized by the last measure pass

    private readonly Dictionary<object, double> _knownHeights = new(ReferenceEqualityComparer.Instance);
    private double _measureWidth = double.NaN;

    private double _headerHeightSum, _rowHeightSum;
    private int _headerHeightCount, _rowHeightCount;

    private readonly Dictionary<int, Control> _realized = [];
    private readonly Dictionary<object, Control> _reusable = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Control, bool> _controlIsHeader = [];
    private readonly Stack<Control> _headerPool = new();
    private readonly Stack<Control> _rowPool = new();

    private Rect _viewport;
    private double _pendingScrollDelta;
    private sealed record AnchorSnapshot(List<(string[] Keys, double RelativeTop)> Candidates, int HintIndex);

    private AnchorSnapshot? _pendingAnchor;
    private bool _pendingTop;

    // A place in the list to come back to: the rows that were in view (found again by their liveries), or the top
    private sealed record Place(AnchorSnapshot? Anchor);

    /// <summary>
    /// Where the list is now, to come back to with <see cref="RestorePlace"/> after it showed something else (the
    /// liveries a filter left, then all of them again). Taken before the items change
    /// </summary>
    public object CapturePlace() => new Place(CaptureAnchor());

    /// <summary>
    /// Scrolls back to a place taken with <see cref="CapturePlace"/>: the rows that were in view then, where they were,
    /// as far as their liveries are still shown; the list stays where it is when none of them is
    /// </summary>
    public void RestorePlace(object? place)
    {
        if (place is not Place taken) return;
        _pendingAnchor = taken.Anchor;
        _pendingTop = taken.Anchor is null;
        InvalidateMeasure();
    }

    private ScrollViewer? _scrollViewer;

    public VirtualGalleryPanel()
    {
        EffectiveViewportChanged += OnEffectiveViewportChanged;
        LayoutUpdated += OnLayoutUpdated;
    }

    public void SetItems(IReadOnlyList<object> items, bool preservePosition)
    {
        _pendingAnchor = preservePosition ? CaptureAnchor() : null;
        foreach (var (index, control) in _realized)
        {
            if (!_reusable.TryAdd(_items[index], control)) Recycle(control);
        }
        _realized.Clear();

        _items = items;
        int n = items.Count;
        _isHeader = new bool[n];
        _heights = new double[n];

        var stillKnown = new Dictionary<object, double>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < n; i++)
        {
            var item = items[i];
            _isHeader[i] = HeaderTemplate?.Match(item) == true;
            if (_knownHeights.TryGetValue(item, out double height))
            {
                _heights[i] = height;
                stillKnown[item] = height;
            }
            else
            {
                _heights[i] = double.NaN;
            }
        }

        _knownHeights.Clear();
        foreach (var (item, height) in stillKnown) _knownHeights[item] = height;

        _topsDirty = true;
        RecomputeTops();
        _coveredBottom = -1;
        InvalidateMeasure();
    }

    /// <summary>
    /// Scrolls so <paramref name="item"/> is in view, with its group's header when it is the group's first row; the list
    /// does not move when it already is. Unmeasured rows are of the average height, so it looks again once laid out
    /// </summary>
    public void BringItemIntoView(object item)
    {
        BringIntoViewOnce(item);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => BringIntoViewOnce(item), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void BringIntoViewOnce(object item)
    {
        if (_scrollViewer is not { } scrollViewer || _viewport.Height <= 0) return;
        int index = -1;
        for (int i = 0; i < _items.Count; i++)
            if (ReferenceEquals(_items[i], item)) { index = i; break; }
        if (index < 0) return;
        RecomputeTops();
        double top = index > 0 && _isHeader[index - 1] ? _tops[index - 1] : _tops[index];
        double bottom = _tops[index + 1];
        double delta = top < _viewport.Y ? top - _viewport.Y
            : bottom > _viewport.Bottom ? Math.Min(bottom - _viewport.Bottom, top - _viewport.Y)
            : 0;
        if (Math.Abs(delta) < 0.5) return;
        double maxY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, Math.Clamp(scrollViewer.Offset.Y + delta, 0, maxY));
    }

    /// <summary>The first row (not a header) at the top of the view or below it; null when there is none</summary>
    public object? FirstVisibleRow()
    {
        if (_items.Count == 0) return null;
        RecomputeTops();
        for (int i = FindIndexAt(Math.Max(0, _viewport.Y)); i < _items.Count; i++)
            if (!_isHeader[i] && _tops[i + 1] > _viewport.Y + 1) return _items[i];
        return null;
    }

    /// <summary>How many rows a page of the view holds (Page Up, Page Down), at least one</summary>
    public int RowsPerPage()
    {
        double row = _rowHeightCount > 0 ? _rowHeightSum / _rowHeightCount : DefaultRowHeight;
        double height = _viewport.Height > 0 ? _viewport.Height : _scrollViewer?.Viewport.Height ?? 0;
        return Math.Max(1, (int)(height / Math.Max(1, row)));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scrollViewer = this.FindAncestorOfType<ScrollViewer>();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _scrollViewer = null;
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var previous = _viewport;
        if (e.EffectiveViewport == previous) return;
        _viewport = e.EffectiveViewport;

        if (IsCoveredByRealizedArea(previous, _viewport)) return;
        InvalidateMeasure();
    }

    private bool IsCoveredByRealizedArea(Rect previous, Rect viewport)
    {
        if (_pendingAnchor is not null || _pendingTop || _pendingScrollDelta != 0 || _coveredBottom < 0) return false;
        if (viewport.Height <= 0 || viewport.Width != previous.Width || viewport.Height != previous.Height) return false;

        double total = _tops[^1];
        bool topCovered = viewport.Y - RemeasureMargin >= _coveredTop || _coveredTop <= 0.5;
        bool bottomCovered = viewport.Bottom + RemeasureMargin <= _coveredBottom || _coveredBottom >= total - 0.5;
        return topCovered && bottomCovered;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width)
            ? availableSize.Width
            : _scrollViewer?.Viewport.Width ?? 0;

        if (width != _measureWidth)
        {
            _measureWidth = width;
            if (_knownHeights.Count > 0)
            {
                _knownHeights.Clear();
                Array.Fill(_heights, double.NaN);
                _topsDirty = true;
            }
        }

        int n = _items.Count;
        if (n == 0)
        {
            foreach (var control in _realized.Values) Recycle(control);
            _realized.Clear();
            foreach (var control in _reusable.Values) Recycle(control);
            _reusable.Clear();
            _pendingAnchor = null;
            _topsDirty = true;
            RecomputeTops();
            _coveredBottom = -1;
            return default;
        }

        RecomputeTops();

        var viewport = CurrentViewport(width);

        if (_pendingTop)
        {
            _pendingTop = false;
            _pendingScrollDelta += -viewport.Y;
            viewport = new Rect(viewport.X, 0, viewport.Width, viewport.Height);
        }
        if (_pendingAnchor is { } anchor)
        {
            _pendingAnchor = null;
            if (TryResolveAnchor(anchor, out int anchorIndex, out double relativeTop))
            {
                double targetY = Math.Max(0, _tops[anchorIndex] - relativeTop);
                _pendingScrollDelta += targetY - viewport.Y;
                viewport = new Rect(viewport.X, targetY, viewport.Width, viewport.Height);
            }
        }

        double buffer = Math.Max(MinBuffer, viewport.Height * 0.5);
        double from = Math.Max(0, viewport.Y - buffer);
        double to = viewport.Bottom + buffer;

        int visibleFirst = FindIndexAt(Math.Max(0, viewport.Y));
        double visibleFirstTopBefore = _tops[visibleFirst];

        // The rows that stay in the realized area are kept for their items; those that left it go to the pool first,
        // so the rows coming in take them instead of building new cards (and the pool stays small)
        int first = FindIndexAt(from);
        foreach (var (index, control) in _realized)
        {
            bool stays = index >= first && _tops[index] < to;
            if (!stays || !_reusable.TryAdd(_items[index], control)) Recycle(control);
        }
        _realized.Clear();
        // Kept for their items (SetItems) but not coming back: to the pool now as well. A new list of rows (another
        // sort, a filter) would else build new rows and cards for all it shows while the old ones wait to be pooled
        if (_reusable.Count > 0)
        {
            int last = Math.Min(n - 1, FindIndexAt(to) + ReuseLookahead);
            var coming = new HashSet<object>(ReferenceEqualityComparer.Instance);
            for (int i = first; i <= last; i++) coming.Add(_items[i]);
            foreach (var (item, control) in _reusable.ToList())
            {
                if (coming.Contains(item)) continue;
                _reusable.Remove(item);
                Recycle(control);
            }
        }

        var measureSize = new Size(width, double.PositiveInfinity);
        double y = _tops[first];
        for (int i = first; i < n && y < to; i++)
        {
            var control = Realize(i);
            if (control is null)
            {
                y += EffectiveHeight(i);
                continue;
            }

            control.Measure(measureSize);
            RecordHeight(i, control.DesiredSize.Height);
            y += _heights[i];
        }

        foreach (var control in _reusable.Values) Recycle(control);
        _reusable.Clear();

        RecomputeTops();
        _coveredTop = _tops[first];
        _coveredBottom = y;
        double correction = _tops[visibleFirst] - visibleFirstTopBefore;
        if (Math.Abs(correction) > 0.01) _pendingScrollDelta += correction;

        return new Size(0, _tops[n]);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (index, control) in _realized)
        {
            double height = double.IsNaN(_heights[index]) ? control.DesiredSize.Height : _heights[index];
            control.Arrange(new Rect(0, _tops[index], finalSize.Width, height));
        }
        return finalSize;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_pendingScrollDelta == 0 || _scrollViewer is not { } scrollViewer) return;

        double delta = _pendingScrollDelta;
        _pendingScrollDelta = 0;

        double maxY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        double newY = Math.Clamp(scrollViewer.Offset.Y + delta, 0, maxY);
        if (Math.Abs(newY - scrollViewer.Offset.Y) > 0.01)
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, newY);
    }

    private Control? Realize(int index)
    {
        var item = _items[index];
        if (_reusable.Remove(item, out var existing))
        {
            _realized[index] = existing;
            return existing;
        }

        bool isHeader = _isHeader[index];
        var pool = isHeader ? _headerPool : _rowPool;

        Control? control;
        if (pool.Count > 0)
        {
            control = pool.Pop();
            // A row's cards keep their liveries in the pool: the row's own again shows them as they were, another
            // changes only the cards whose livery differs (GalleryRowPresenter)
            if (ReferenceEquals(control.DataContext, item)) Unpark(control);
            else control.DataContext = item;
            control.IsVisible = true;
        }
        else
        {
            control = (isHeader ? HeaderTemplate : RowTemplate)?.Build(item);
            if (control is null) return null;
            _controlIsHeader[control] = isHeader;
            control.DataContext = item;
            Children.Add(control);
        }

        _realized[index] = control;
        return control;
    }

    // Into the pool with its data kept: setting it to nothing and to the next item would run every binding of every
    // card twice, the first time into errors (most of the cost of a scroll). Its cards' previews are let go
    private void Recycle(Control control)
    {
        bool isHeader = _controlIsHeader.TryGetValue(control, out bool header) && header;
        var pool = isHeader ? _headerPool : _rowPool;
        if (pool.Count < MaxPooledPerKind)
        {
            control.IsVisible = false;
            if (!isHeader) RowOf(control)?.ParkAll();
            pool.Push(control);
        }
        else
        {
            Children.Remove(control);
            _controlIsHeader.Remove(control);
            _rowOf.Remove(control);
        }
    }

    private void Unpark(Control control)
    {
        if (!(_controlIsHeader.TryGetValue(control, out bool header) && header)) RowOf(control)?.UnparkShown();
    }

    // The cards of a row control (its template's GalleryRowPresenter), found once: a walk of all its visuals on every
    // recycle would go through every card's tree
    private readonly Dictionary<Control, GalleryRowPresenter?> _rowOf = [];

    /// <summary>
    /// The liveries whose cards a rectangle touches (<see cref="GalleryBand"/>), in the panel's coordinates, top to
    /// bottom: of the rows on screen and of those that are not built now (scrolled away under the rectangle). A row not
    /// built is taken to be laid out as the ones that are: the same left edge, the same card width
    /// </summary>
    internal List<ViewModels.LiveryEntry> EntriesIn(Rect band)
    {
        var found = new List<ViewModels.LiveryEntry>();
        int n = _items.Count;
        if (n == 0 || band.Width < 0 || band.Height < 0) return found;
        RecomputeTops();

        // The cards' slots, from a row that is built: every row starts at the same place and has cards of one width
        double left = 0, pitch = 286, margin = 8;
        foreach (var control in _realized.Values)
        {
            if (RowOf(control) is not { Children.Count: > 0 } presenter || presenter.Children[0] is not { IsVisible: true } card) continue;
            if (card.DesiredSize.Width <= 0 || presenter.TranslatePoint(default, this) is not { } origin) continue;
            left = origin.X;
            pitch = card.DesiredSize.Width;
            margin = card.Margin.Left;
            break;
        }

        int from = FindIndexAt(Math.Max(0, band.Y)), to = FindIndexAt(Math.Max(0, band.Bottom));
        for (int i = from; i <= to && i < n; i++)
        {
            if (_isHeader[i] || _items[i] is not Models.GalleryRow row) continue;
            if (GalleryBand.Columns(band, _tops[i], _tops[i + 1], left, pitch, margin, row.Items.Count) is not var (first, last)) continue;
            for (int c = first; c <= last; c++) found.Add(row.Items[c]);
        }
        return found;
    }

    private GalleryRowPresenter? RowOf(Control control)
    {
        if (_rowOf.TryGetValue(control, out var known) && known is not null) return known;
        var found = FindRow(control);
        _rowOf[control] = found;
        return found;
    }

    private static GalleryRowPresenter? FindRow(Visual visual)
    {
        if (visual is GalleryRowPresenter row) return row;
        foreach (var child in visual.GetVisualChildren())
            if (FindRow(child) is { } found) return found;
        return null;
    }

    private void RecordHeight(int index, double height)
    {
        double old = _heights[index];
        if (old == height) return;

        if (_isHeader[index])
        {
            if (double.IsNaN(old)) _headerHeightCount++; else _headerHeightSum -= old;
            _headerHeightSum += height;
        }
        else
        {
            if (double.IsNaN(old)) _rowHeightCount++; else _rowHeightSum -= old;
            _rowHeightSum += height;
        }

        _heights[index] = height;
        _knownHeights[_items[index]] = height;
        _topsDirty = true;
    }

    private double EffectiveHeight(int index)
    {
        double height = _heights[index];
        if (!double.IsNaN(height)) return height;

        return _isHeader[index]
            ? (_headerHeightCount > 0 ? _headerHeightSum / _headerHeightCount : DefaultHeaderHeight)
            : (_rowHeightCount > 0 ? _rowHeightSum / _rowHeightCount : DefaultRowHeight);
    }

    private void RecomputeTops()
    {
        int n = _items.Count;
        if (!_topsDirty && _tops.Length == n + 1) return;
        _topsDirty = false;
        if (_tops.Length != n + 1) _tops = new double[n + 1];

        double y = 0;
        for (int i = 0; i < n; i++)
        {
            _tops[i] = y;
            y += EffectiveHeight(i);
        }
        _tops[n] = y;
    }

    private int FindIndexAt(double y)
    {
        int n = _items.Count;
        if (n == 0) return 0;

        int lo = 0, hi = n - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_tops[mid] <= y) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    private Rect CurrentViewport(double width)
    {
        var viewport = _viewport;
        if (viewport.Height <= 0)
        {
            double fallbackHeight = _scrollViewer is { Viewport.Height: > 0 } scrollViewer ? scrollViewer.Viewport.Height : 1000;
            viewport = new Rect(0, Math.Max(0, viewport.Y), width, fallbackHeight);
        }
        return new Rect(viewport.X, Math.Max(0, viewport.Y + _pendingScrollDelta), viewport.Width, viewport.Height);
    }

    private AnchorSnapshot? CaptureAnchor()
    {
        if (_items.Count == 0 || AnchorKeySelector is null || _tops.Length != _items.Count + 1) return null;

        var viewport = CurrentViewport(_measureWidth);

        if (_viewport.Height <= 0 || viewport.Y <= 0.5) return null;

        int firstVisible = FindIndexAt(viewport.Y);
        var candidates = new List<(string[] Keys, double RelativeTop)>();
        for (int i = firstVisible; i < _items.Count && _tops[i] < viewport.Bottom; i++)
        {
            string[] keys = [.. AnchorKeySelector(_items[i])];
            if (keys.Length > 0) candidates.Add((keys, _tops[i] - viewport.Y));
            if (candidates.Count >= MaxAnchorCandidates) break;
        }
        return candidates.Count > 0 ? new AnchorSnapshot(candidates, firstVisible) : null;
    }

    private bool TryResolveAnchor(AnchorSnapshot anchor, out int index, out double relativeTop)
    {
        index = -1;
        relativeTop = 0;
        int count = _items.Count;
        if (AnchorKeySelector is null || count == 0) return false;

        var rankByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int rank = 0; rank < anchor.Candidates.Count; rank++)
            foreach (string key in anchor.Candidates[rank].Keys)
                rankByKey.TryAdd(key, rank);

        int bestRank = int.MaxValue;
        int bestIndex = -1;
        int stopDistance = int.MaxValue;
        int hint = Math.Clamp(anchor.HintIndex, 0, count - 1);
        int maxDistance = Math.Max(hint, count - 1 - hint);

        for (int distance = 0; distance <= maxDistance && distance <= stopDistance; distance++)
        {
            CheckItem(hint - distance);
            if (distance > 0) CheckItem(hint + distance);
            if (bestRank == 0) break;
        }

        if (bestIndex < 0) return false;
        index = bestIndex;
        relativeTop = anchor.Candidates[bestRank].RelativeTop;
        return true;

        void CheckItem(int i)
        {
            if (i < 0 || i >= count) return;
            foreach (string key in AnchorKeySelector!(_items[i]))
            {
                if (!rankByKey.TryGetValue(key, out int rank) || rank >= bestRank) continue;
                bestRank = rank;
                bestIndex = i;
                if (stopDistance == int.MaxValue)
                    stopDistance = Math.Abs(i - hint) + 2 * MaxAnchorCandidates;
            }
        }
    }
}
