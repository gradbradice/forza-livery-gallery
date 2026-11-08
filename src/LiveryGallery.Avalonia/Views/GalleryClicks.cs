using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace LiveryGallery.Views;

/// <summary>
/// What a left click in the gallery hits: a card, or the empty space between and around the cards (and below the last
/// row). A click on the empty space clears the selection, like in Explorer; a card, a group header, a button, the scroll
/// bar and a text field keep it
/// </summary>
internal static class GalleryClicks
{
    /// <summary>The class of a livery card (the card template in MainWindow.axaml)</summary>
    public const string CardClass = "liveryCard";

    /// <summary>Whether a click on <paramref name="source"/> (the element under the pointer) is on the empty space</summary>
    public static bool IsEmptySpace(Visual? source)
    {
        if (source is null) return false;
        foreach (var visual in source.GetSelfAndVisualAncestors())
        {
            if (visual is Border border && border.Classes.Contains(CardClass)) return false;
            if (visual is Button or ToggleButton or ScrollBar or TextBox or MenuItem) return false;
        }
        return true;
    }

    /// <summary>
    /// Whether a press on a card changes the selection: the first press of a click does, the second of a double click
    /// does not (it opens the livery window), so Ctrl with a double click does not take the card out again, as in Explorer
    /// </summary>
    public static bool SelectsOnPress(int clickCount) => clickCount <= 1;
}
