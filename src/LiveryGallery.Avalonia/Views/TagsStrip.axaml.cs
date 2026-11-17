using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>
/// The strip of tags over the gallery: a click on a tag shows only its liveries, a right click hides them. With many
/// tags it took half the window (a user with some 200, after 1.7.1), so its height is the user's: the line under it is
/// dragged up or down, a row at a time (never a row cut in half), and a double click on it shows every tag or one row.
/// What it shows is kept for each game (<see cref="RowsChanged"/>); the tags beyond it scroll, the marked ones come
/// first (<see cref="TagsBarViewModel"/>), and from 30 tags on a search narrows them
/// </summary>
internal sealed partial class TagsStrip : UserControl
{
    /// <summary>A chip's height with its gap, before one is laid out</summary>
    private const double FallbackStep = 36;

    private int? _rows;
    private int _fullRows;
    // Where each row laid out begins, top to bottom: N rows are what lies above the (N+1)th's top — exact even when the
    // first row (with the search) is a pixel higher than a row of tags
    private List<double> _tops = [];
    private Point? _dragFrom;
    private double _dragHeight;
    private int? _dragRows;

    // What the strip lays out: the search (when offered) and then the tags, kept in step with the model's tags
    private readonly ObservableCollection<object> _flow = [];
    private TagsBarViewModel? _model;

    public TagsStrip()
    {
        InitializeComponent();
        Items.ItemsSource = _flow;
        Items.SizeChanged += (_, _) => MeasureRows();
    }

    /// <summary>The user changed the rows shown (null: every tag): to be kept</summary>
    public event Action<int?>? RowsChanged;

    /// <summary>The rows of tags shown, the rest scroll; null: every tag</summary>
    public int? Rows
    {
        get => _rows;
        set
        {
            _rows = value is int r ? Math.Max(1, r) : null;
            Apply();
        }
    }

    /// <summary>The rows every tag takes (measured with no search)</summary>
    internal int FullRows => _fullRows;

    /// <summary>A row's height on average (a chip and the gap under it): how far a drag goes for a row</summary>
    internal double RowStep => _tops.Count > 0 && Items.Bounds.Height > 0 ? Items.Bounds.Height / _tops.Count : FallbackStep;

    /// <summary>The strip's height for this many rows: down to the next row's top, or all of the tags</summary>
    internal double HeightOf(int rows) =>
        _tops.Count == 0 ? rows * FallbackStep
        : rows < _tops.Count ? _tops[Math.Max(1, rows)] : Items.Bounds.Height;

    /// <summary>Fewer rows than every tag needs are shown</summary>
    internal bool IsLimited => _rows is int r && r < _fullRows;

    private TagsBarViewModel? Model => DataContext as TagsBarViewModel;

    /// <summary>The texts in the interface's language</summary>
    public void ShowTexts()
    {
        LabelText.Text = Strings.TagsFilterLabel;
        NothingFoundText.Text = Strings.QuickFilterPickerEmpty;
        ToolTip.SetTip(Grip, Strings.TagsGripTooltip);
    }

    /// <summary>The rows a strip this high shows: whole rows, at least one, at most every tag's</summary>
    internal static int RowsFor(double height, double step, int fullRows) =>
        Math.Clamp((int)Math.Round(height / step, MidpointRounding.AwayFromZero), 1, Math.Max(1, fullRows));

    /// <summary>What a double click on the line does: a strip that shows fewer rows than its tags take shows them all; one that shows them all, one row</summary>
    internal static int? Toggled(int? rows, int fullRows) => rows is int r && r < fullRows ? null : 1;

    /// <summary>The rows the user chose: shown and kept. As many as every tag takes is "every tag" (null): new tags show too</summary>
    internal void Choose(int? rows)
    {
        int? kept = rows is int r && r < _fullRows ? Math.Max(1, r) : null;
        _rows = kept;
        Apply();
        RowsChanged?.Invoke(kept);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.AllTags.CollectionChanged -= Tags_CollectionChanged;
            _model.PropertyChanged -= Model_PropertyChanged;
        }
        _model = DataContext as TagsBarViewModel;
        if (_model is not null)
        {
            _model.AllTags.CollectionChanged += Tags_CollectionChanged;
            _model.PropertyChanged += Model_PropertyChanged;
        }
        RebuildFlow();
    }

    // The search's place: first, while it is offered
    private int Offset => _flow.Count > 0 && _flow[0] is TagsSearchItem ? 1 : 0;

    private void RebuildFlow()
    {
        _flow.Clear();
        if (_model is null) return;
        if (_model.ShowsSearch) _flow.Add(_model.SearchItem);
        foreach (var chip in _model.AllTags) _flow.Add(chip);
    }

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TagsBarViewModel.ShowsSearch) || _model is null) return;
        if (_model.ShowsSearch && Offset == 0) _flow.Insert(0, _model.SearchItem);
        else if (!_model.ShowsSearch && Offset == 1) _flow.RemoveAt(0);
    }

    // The tags' changes, one for one; the search before them stays (a field being typed in keeps its focus)
    private void Tags_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        int offset = Offset;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is { } added:
                for (int i = 0; i < added.Count; i++) _flow.Insert(offset + e.NewStartingIndex + i, added[i]!);
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is { } removed:
                for (int i = 0; i < removed.Count; i++) _flow.RemoveAt(offset + e.OldStartingIndex);
                break;
            case NotifyCollectionChangedAction.Move:
                _flow.Move(offset + e.OldStartingIndex, offset + e.NewStartingIndex);
                break;
            case NotifyCollectionChangedAction.Replace when e.NewItems is { } replaced:
                for (int i = 0; i < replaced.Count; i++) _flow[offset + e.NewStartingIndex + i] = replaced[i]!;
                break;
            default:
                while (_flow.Count > offset) _flow.RemoveAt(_flow.Count - 1);
                if (_model is not null) foreach (var chip in _model.AllTags) _flow.Add(chip);
                break;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // While the pointer is over the strip the tags stay where they are; leaving it, the marked ones move first
        if (change.Property == IsPointerOverProperty && Model is { } model) model.HoldOrder = IsPointerOver;
    }

    private void Apply()
    {
        Scroll.MaxHeight = _rows is int r ? HeightOf(r) : double.PositiveInfinity;
        Grip.IsVisible = _fullRows > 1;
    }

    // The rows laid out, from where the tags (and the search) were placed
    private void MeasureRows()
    {
        var tops = new SortedSet<double>();
        foreach (var container in Items.GetRealizedContainers())
        {
            var bounds = container.Bounds;
            if (bounds.Height <= 0 || bounds.Width <= 0) continue;     // a tag the search hides
            tops.Add(Math.Round(bounds.Y, 2));
        }
        _tops = [.. tops];
        // Every tag's rows: only with no search (a search shows fewer); with none laid out yet, nothing to tell
        if (Model is not { SearchText.Length: > 0 } && tops.Count > 0) _fullRows = tops.Count;
        Apply();
    }

    private void Grip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Grip).Properties.IsLeftButtonPressed) return;
        _dragFrom = e.GetPosition(TopLevel.GetTopLevel(this));
        _dragHeight = Scroll.Bounds.Height;
        _dragRows = null;
        e.Pointer.Capture(Grip);
        e.Handled = true;
    }

    private void Grip_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragFrom is not { } from) return;
        double height = _dragHeight + e.GetPosition(TopLevel.GetTopLevel(this)).Y - from.Y;
        int rows = RowsFor(height, RowStep, _fullRows);
        if (rows == (_dragRows ?? -1)) return;
        _dragRows = rows;
        Scroll.MaxHeight = HeightOf(rows);
    }

    private void Grip_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndDrag();
        e.Pointer.Capture(null);
    }

    private void Grip_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (_dragFrom is null) return;
        _dragFrom = null;
        // A press with no move (half of a double click) changes nothing
        if (_dragRows is int rows) Choose(rows);
        _dragRows = null;
    }

    private void Grip_DoubleTapped(object? sender, TappedEventArgs e)
    {
        Choose(Toggled(_rows, _fullRows));
        e.Handled = true;
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        // Esc empties the search first (every tag again)
        if (e.Key == Key.Escape && Model is { SearchText.Length: > 0 } model)
        {
            model.SearchText = "";
            e.Handled = true;
        }
    }

    // A right click on a tag hides its liveries (or stops hiding them)
    private void Chip_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right) return;
        if (sender is not StyledElement { DataContext: TagChipViewModel chip }) return;
        chip.ToggleHideCommand.Execute(null);
        e.Handled = true;
    }
}
