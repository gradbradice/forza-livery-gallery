using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LiveryGallery.ViewModels.Collections;

namespace LiveryGallery.Views.Collections;

/// <summary>
/// The side panel of a collection (the albums of the screenshots, later the folders of the storage). The owner fills
/// <see cref="Items"/> and hears a row picked, "+ New", renaming, deleting and something dropped on a user's row
/// </summary>
internal partial class CollectionSidebar : UserControl
{
    // A user's row dragged to a new place, inside this panel only
    private static readonly DataFormat<string> RowDragFormat = DataFormat.CreateInProcessFormat<string>("LiveryGallery.CollectionRow");

    private (PointerPressedEventArgs Args, CollectionSidebarItem Item, Point At)? _rowPress;
    private bool _dragged;

    public CollectionSidebar()
    {
        InitializeComponent();
        ItemsList.ItemsSource = Items;
        AddHandler(DragDrop.DragOverEvent, Row_DragOver);
        AddHandler(DragDrop.DragLeaveEvent, Row_DragLeave);
        AddHandler(DragDrop.DropEvent, Row_Drop);
        // A row is a button, which takes the press: heard here even so, to drag the row to a new place
        ItemsList.AddHandler(PointerPressedEvent, Row_PointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        ItemsList.AddHandler(PointerMovedEvent, Row_PointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        ItemsList.AddHandler(PointerReleasedEvent, (_, _) => _rowPress = null, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public ObservableCollection<CollectionSidebarItem> Items { get; } = [];

    public static readonly StyledProperty<string?> ActionTextProperty = AvaloniaProperty.Register<CollectionSidebar, string?>(nameof(ActionText));

    /// <summary>The owner's action at the top of a user's row's menu (<see cref="ActionRequested"/>); none when null</summary>
    public string? ActionText
    {
        get => GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    /// <summary>The owner's action of a user's row was chosen</summary>
    public event Action<CollectionSidebarItem>? ActionRequested;

    public static readonly StyledProperty<string?> SecondActionTextProperty = AvaloniaProperty.Register<CollectionSidebar, string?>(nameof(SecondActionText));

    /// <summary>A second action of the owner under the first (the storage: "Back up the folder"); none when null</summary>
    public string? SecondActionText
    {
        get => GetValue(SecondActionTextProperty);
        set => SetValue(SecondActionTextProperty, value);
    }

    /// <summary>The owner's second action of a user's row was chosen</summary>
    public event Action<CollectionSidebarItem>? SecondActionRequested;

    /// <summary>A line under the rows (bold); hidden when null</summary>
    public void SetFooter(string? text)
    {
        FooterText.Text = text;
        FooterText.IsVisible = !string.IsNullOrEmpty(text);
    }

    /// <summary>The row shown (<see cref="Select"/>); null when none</summary>
    public CollectionSidebarItem? SelectedItem => Items.FirstOrDefault(i => i.IsSelected);

    /// <summary>The panel's words: its heading, the "+ New" link, the hint at the bottom</summary>
    public void SetTexts(string heading, string newText, string hint)
    {
        HeadingText.Text = heading.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        NewButton.Content = "+ " + newText;
        HintText.Text = hint;
    }

    /// <summary>The row picked (the owner marks it <see cref="CollectionSidebarItem.IsSelected"/>)</summary>
    public event Action<CollectionSidebarItem>? Picked;
    public event Action? CreateRequested;
    public event Action<CollectionSidebarItem>? RenameRequested;
    public event Action<CollectionSidebarItem>? DeleteRequested;

    /// <summary>Whether what is dragged can go on a user's row; null leaves the panel no drop at all</summary>
    public Func<DragEventArgs, bool>? CanDrop { get; set; }

    /// <summary>Something was dropped on a user's row</summary>
    public event Action<CollectionSidebarItem, DragEventArgs>? Dropped;

    /// <summary>The user's rows in a new order (dragged, "Move up", "Move down"): their ids, all of them</summary>
    public event Action<IReadOnlyList<string>>? Reordered;

    /// <summary>Marks the row as the one shown; the others not</summary>
    public void Select(string id)
    {
        foreach (var item in Items) item.IsSelected = item.Id == id;
    }

    private void Row_Click(object? sender, RoutedEventArgs e)
    {
        if (_dragged) { _dragged = false; return; } // the release that ended a drag is no click
        if (sender is Control { DataContext: CollectionSidebarItem item }) Picked?.Invoke(item);
    }

    private void MoveUp_Click(object? sender, RoutedEventArgs e) => MoveBy(sender, -1);

    private void MoveDown_Click(object? sender, RoutedEventArgs e) => MoveBy(sender, +1);

    private void MoveBy(object? sender, int step)
    {
        if (sender is not Control { DataContext: CollectionSidebarItem { IsUsers: true } item }) return;
        var users = Items.Where(i => i.IsUsers).ToList();
        int from = users.IndexOf(item), to = from + step;
        if (from < 0 || to < 0 || to >= users.Count) return;
        users.RemoveAt(from);
        users.Insert(to, item);
        Reordered?.Invoke([.. users.Select(i => i.Id)]);
    }

    private void Row_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _rowPress = (e.Source as Control)?.FindAncestorOfType<Button>(includeSelf: true)?.DataContext is CollectionSidebarItem { IsUsers: true } item
            && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            ? (e, item, e.GetPosition(this))
            : null;
    }

    private async void Row_PointerMoved(object? sender, PointerEventArgs e)
    {
        try
        {
            if (_rowPress is not { } press || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var delta = e.GetPosition(this) - press.At;
            if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6) return;
            _rowPress = null;
            _dragged = true;
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(RowDragFormat, press.Item.Id));
            await DragDrop.DoDragDropAsync(press.Args, data, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Dragging a row of the side panel failed", ex);
        }
    }

    // Where a dragged row would go: before the row under the pointer when over its upper half, else after it
    private (CollectionSidebarItem Row, bool After)? InsertPlace(DragEventArgs e)
    {
        if (RowUnder(e) is not { } row || (e.Source as Control)?.FindAncestorOfType<Button>(includeSelf: true) is not { } button) return null;
        return (row, e.GetPosition(button).Y > button.Bounds.Height / 2);
    }

    private void ShowInsertPlace((CollectionSidebarItem Row, bool After)? place)
    {
        foreach (var item in Items)
        {
            item.IsInsertBefore = place is { } p && ReferenceEquals(p.Row, item) && !p.After;
            item.IsInsertAfter = place is { } q && ReferenceEquals(q.Row, item) && q.After;
        }
    }

    private void New_Click(object? sender, RoutedEventArgs e) => CreateRequested?.Invoke();

    private void Action_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CollectionSidebarItem { IsUsers: true } item }) ActionRequested?.Invoke(item);
    }

    private void SecondAction_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CollectionSidebarItem { IsUsers: true } item }) SecondActionRequested?.Invoke(item);
    }

    private void Rename_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CollectionSidebarItem { IsUsers: true } item }) RenameRequested?.Invoke(item);
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CollectionSidebarItem { IsUsers: true } item }) DeleteRequested?.Invoke(item);
    }

    // A user's row (to reorder) or one that takes a drop
    private static CollectionSidebarItem? RowUnder(DragEventArgs e, bool forDrop = false) =>
        (e.Source as Control)?.FindAncestorOfType<Button>(includeSelf: true)?.DataContext as CollectionSidebarItem is { } item
        && (forDrop ? item.AcceptsDrops : item.IsUsers) ? item : null;

    private void Row_DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(RowDragFormat))
        {
            var place = InsertPlace(e);
            ShowInsertPlace(place);
            e.DragEffects = place is null ? DragDropEffects.None : DragDropEffects.Move;
            e.Handled = true;
            return;
        }
        var row = RowUnder(e, forDrop: true);
        bool can = row is not null && CanDrop?.Invoke(e) == true;
        foreach (var item in Items) item.IsDropTarget = can && ReferenceEquals(item, row);
        // An effect the source allows: one that offers only Move (the storage's cards) would refuse a Copy, and Windows
        // shows the drop as not allowed
        e.DragEffects = !can ? DragDropEffects.None
            : e.DragEffects.HasFlag(DragDropEffects.Move) ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Row_DragLeave(object? sender, DragEventArgs e)
    {
        foreach (var item in Items) item.IsDropTarget = false;
        ShowInsertPlace(null);
    }

    private void Row_Drop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(RowDragFormat) is { } movedId)
        {
            var place = InsertPlace(e);
            ShowInsertPlace(null);
            e.Handled = true;
            var users = Items.Where(i => i.IsUsers).ToList();
            if (place is not { } p || users.FirstOrDefault(i => i.Id == movedId) is not { } moved || ReferenceEquals(moved, p.Row)) return;
            users.Remove(moved);
            users.Insert(users.IndexOf(p.Row) + (p.After ? 1 : 0), moved);
            Reordered?.Invoke([.. users.Select(i => i.Id)]);
            return;
        }
        var row = RowUnder(e, forDrop: true);
        foreach (var item in Items) item.IsDropTarget = false;
        if (row is null || CanDrop?.Invoke(e) != true) return;
        e.Handled = true;
        Dropped?.Invoke(row, e);
    }
}
