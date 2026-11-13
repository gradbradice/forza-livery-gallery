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

    /// <summary>The class of the folder's name under a card, shown when the folder names are (the card template)</summary>
    public const string FolderNameClass = "cardFolderName";

    /// <summary>
    /// Whether <paramref name="source"/> is text of a card that is selected with the mouse (the folder's name): a press
    /// and a double click there belong to the text, not to the card
    /// </summary>
    public static bool IsSelectableText(Visual? source) =>
        source is not null && source.GetSelfAndVisualAncestors().Any(v => v is SelectableTextBlock);

    /// <summary>
    /// The selection in a folder's name goes when a press lands anywhere else: the name keeps the focus (a card takes
    /// none), so without this the highlight would stay under a card clicked long ago
    /// </summary>
    public static void DropTextSelection(object? focused, Visual? pressed)
    {
        if (focused is SelectableTextBlock { SelectionStart: var start, SelectionEnd: var end } text && start != end
            && text.Classes.Contains(FolderNameClass)
            && (pressed is null || !pressed.GetSelfAndVisualAncestors().Contains(text)))
            text.ClearSelection();
    }

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
