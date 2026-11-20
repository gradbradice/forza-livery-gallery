using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>
/// The selection bar and the properties panel (docs/plans.md, «Панель свойств»). The bar shows over the gallery when
/// several liveries are selected and the panel is closed: the gallery's actions offered there (GalleryActions.Bar: the
/// star, the lock, the tags, to the storage or to the game), the panel, the card menu and the cross that clears the
/// selection. The panel on the right shows the selected livery or what the selected share; it keeps its place for the
/// next launch. Neither shows over the screenshots
/// </summary>
internal partial class MainWindow
{
    private bool _propertiesShown;

    private void InitializeSelectionBar()
    {
        _mainViewModel.PropertyChanged += SelectionBar_MainChanged;
        PropertiesPanel.CloseRequested += () => _mainViewModel.IsPropertiesOpen = false;
        UpdateSelectionPanels();
    }

    private void SelectionBar_MainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsSelectionBarVisible) or nameof(MainViewModel.IsPropertiesOpen)
            or nameof(MainViewModel.IsStorageMode))
            UpdateSelectionPanels();
    }

    /// <summary>The bar and the panel as the selection, the panel's setting and the view ask</summary>
    private void UpdateSelectionPanels()
    {
        SelectionBar.IsVisible = _mainViewModel.IsSelectionBarVisible && !_showingScreenshots;
        bool panel = _mainViewModel.IsPropertiesOpen && !_showingScreenshots;
        if (panel == _propertiesShown) return;
        _propertiesShown = panel;
        PropertiesPanel.IsVisible = panel;
        if (!panel) PropertiesPanel.CloseSuggestions();
        // The short messages stay over the list, left of the panel
        ToastHost.Margin = new Avalonia.Thickness(0, 0, panel ? PropertiesPanel.Width + 24 : 24, 20);
        if (panel)
        {
            _mainViewModel.Properties.Update();
            KeepScrollBarInReach();
            Dispatcher.UIThread.Post(KeepScrollBarInReach, DispatcherPriority.Loaded);
        }
        // The panel lies over the list: the list keeps its width, its cards their size and rows
    }

    // The panel ends left of the list's scroll bar (as wide as the bar, and a gap), so the bar stays in reach
    private void KeepScrollBarInReach()
    {
        double bar = GalleryScroll.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>()
            .FirstOrDefault(b => b.Orientation == Avalonia.Layout.Orientation.Vertical && b.IsEffectivelyVisible)?.Bounds.Width ?? 0;
        PropertiesPanel.Margin = new Avalonia.Thickness(0, 8, Math.Max(bar, 12) + 8, 8);
    }

    // «⋯»: the cards' menu for the selection, where the button is. The menu belongs to a card of the selection: the
    // cursor's when it is one of them, else the first selected, brought into view first when it is not on screen
    private void SelectionMore_Click(object? sender, RoutedEventArgs e)
    {
        var gallery = _mainViewModel.Gallery;
        var entry = gallery.CursorEntry is { IsSelected: true } cursor ? cursor : gallery.SelectedEntries.FirstOrDefault();
        if (entry is null) return;
        var card = CardOf(entry) ?? gallery.SelectedEntries.Select(CardOf).FirstOrDefault(c => c is not null);
        if (card is null)
        {
            if (gallery.RowOf(entry) is not { } row) return;
            GalleryPanel.BringItemIntoView(row);
            Dispatcher.UIThread.Post(() =>
            {
                if (CardOf(entry) is { } shown) OpenSelectionMenu(shown);
            }, DispatcherPriority.Background);
            return;
        }
        OpenSelectionMenu(card);
    }

    private CardBorder? CardOf(LiveryEntry entry) => GalleryPanel.GetVisualDescendants().OfType<CardBorder>()
        .FirstOrDefault(c => c.Classes.Contains("liveryCard") && ReferenceEquals(c.DataContext, entry) && c.IsEffectivelyVisible);

    private void OpenSelectionMenu(CardBorder card)
    {
        Card_ContextRequested(card, new ContextRequestedEventArgs());
        if (_cardMenu is { IsOpen: true } menu)
        {
            // Under the bar's button, not over the card
            menu.Close();
            var placement = menu.Placement;
            menu.PlacementTarget = SelectionMoreButton;
            menu.Placement = PlacementMode.TopEdgeAlignedRight;
            menu.Open(card);
            void Closed(object? s, RoutedEventArgs a)
            {
                menu.Closed -= Closed;
                menu.PlacementTarget = null;
                menu.Placement = placement;
            }
            menu.Closed += Closed;
        }
    }
}
