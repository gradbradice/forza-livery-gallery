using System.ComponentModel;
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>
/// The gallery for the keyboard and a screen reader: the focus stays on the list and the keys' cursor moves over the
/// cards, so the list's name tells the card under the cursor (a reader says it as it changes); the context menu opens on
/// that card from the keyboard (Shift+F10, the menu key). The cards' own buttons are out of the Tab order (CardButton)
/// </summary>
internal partial class MainWindow
{
    private void InitializeAccessibility()
    {
        AutomationProperties.SetName(GalleryScroll, Strings.GalleryAccessibleName);
        _mainViewModel.Gallery.PropertyChanged += Gallery_CursorChanged;
        GalleryScroll.ContextRequested += GalleryScroll_ContextRequested;
        // Shift+F10 and the menu key ask for the context menu of what has the focus. Asked here, before the controls,
        // so it does not depend on the platform's own keys (and is asked once)
        AddHandler(KeyUpEvent, ContextMenuKey_Up, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void ContextMenuKey_Up(object? sender, KeyEventArgs e)
    {
        bool menuKey = e.Key == Key.Apps || (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift);
        if (!menuKey || !GalleryScroll.IsKeyboardFocusWithin) return;
        e.Handled = true;
        GalleryScroll.RaiseEvent(new ContextRequestedEventArgs());
    }

    private void Gallery_CursorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GalleryViewModel.CursorEntry)) return;
        AutomationProperties.SetName(GalleryScroll, _mainViewModel.Gallery.CursorEntry is { } entry
            ? AccessibleName(entry) : Strings.GalleryAccessibleName);
    }

    /// <summary>What a screen reader says of a card: the livery, its car, its author, and its states</summary>
    internal static string AccessibleName(LiveryEntry entry)
    {
        var parts = new List<string> { entry.LiveryName };
        string car = string.Join(' ', new[] { entry.CarManufacturer, entry.CarModelName, entry.CarYear?.ToString(CultureInfo.InvariantCulture) }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
        if (car.Length > 0) parts.Add(car);
        if (!string.IsNullOrWhiteSpace(entry.AuthorDisplayText)) parts.Add(entry.AuthorDisplayText);
        if (entry.IsSelected) parts.Add(Strings.CardAccessibleSelected);
        if (entry.IsFavorite) parts.Add(Strings.CardAccessibleFavorite);
        if (entry.IsLocked) parts.Add(Strings.CardAccessibleLocked);
        return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    // From the keyboard the request starts at the focused list, not at a card: it is the cursor's card's menu
    private void GalleryScroll_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Handled || e.TryGetPosition(null, out _)) return; // a pointer: the card under it asked already
        if (_mainViewModel.Gallery.CursorEntry is not { } entry) return;
        var card = GalleryPanel.GetVisualDescendants().OfType<CardBorder>()
            .FirstOrDefault(c => c.Classes.Contains("liveryCard") && ReferenceEquals(c.DataContext, entry));
        if (card is null) return;
        Card_ContextRequested(card, e);
    }
}
