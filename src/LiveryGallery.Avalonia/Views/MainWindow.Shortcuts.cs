using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>The gallery's keys (<see cref="GalleryShortcuts"/>)</summary>
internal partial class MainWindow
{
    /// <summary>
    /// The window sees each key before the control in focus (tunnelling): a button that was clicked keeps the focus,
    /// and the space would press it again instead of selecting the card. A dialog of the gallery is modal, so its keys
    /// never come here, and a menu or flyout that is open gets its keys in its own popup
    /// </summary>
    private void InitializeShortcuts()
    {
        AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);
        // Before anything under the pointer takes the press: text selected in a folder's name is let go
        AddHandler(PointerPressedEvent, (_, e) => GalleryClicks.DropTextSelection(FocusManager?.GetFocusedElement(), e.Source as Avalonia.Visual),
            RoutingStrategies.Tunnel);
    }

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || !_isLoaded) return;
        var focused = FocusManager?.GetFocusedElement();
        bool typing = focused is TextBox;
        if (ReferenceEquals(focused, SearchBox) && e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && SearchEnter())
        {
            e.Handled = true;
            return;
        }
        if (!typing && (_showingScreenshots ? MoveScreenshotCursor(e, focused) : MoveCursor(e, focused)))
        {
            e.Handled = true;
            return;
        }
        if (GalleryShortcuts.Match(e.Key, e.PhysicalKey, e.KeyModifiers, typing, _mainViewModel.IsStorageMode) is { } action
            && (RunModeShortcut(action) || RunCardSizeShortcut(action)
                || (_showingScreenshots ? RunScreenshotsShortcut(action, focused) : RunShortcut(action, focused))))
            e.Handled = true;
    }

    /// <summary>Does what the key asks; false when there is nothing to do, and the key goes on to the control in focus</summary>
    private bool RunShortcut(GalleryShortcut action, object? focused)
    {
        // The keys of the selection (F, L, T, Del, Ins, Enter, 3, G…): the gallery's actions, as the menu runs them
        if (_mainViewModel.Actions.RunKey(action)) return true;
        var gallery = _mainViewModel.Gallery;
        switch (action)
        {
            case GalleryShortcut.Search:
                SearchBox.Focus(NavigationMethod.Tab);
                SearchBox.SelectAll();
                return true;
            case GalleryShortcut.Escape:
                return Escape(focused);
            case GalleryShortcut.SelectAll:
                gallery.SelectAllCommand.Execute(null);
                return true;
            case GalleryShortcut.SaveFilterSet:
                RunUiTask(SaveFilterPresetAsync, "save filter preset");
                return true;
            case GalleryShortcut.Undo:
                if (_mainViewModel.UndoCommand.CanExecute(null)) _mainViewModel.UndoCommand.Execute(null);
                return true;
            case GalleryShortcut.Refresh:
                if (!_mainViewModel.Status.CanRefresh || !_mainViewModel.RefreshCommand.CanExecute(null)) return true;
                _mainViewModel.RefreshCommand.Execute(null);
                return true;

            case GalleryShortcut.ToggleSelection:
                return ToggleCursor();

            case GalleryShortcut.Settings:
                OpenSettingsMenuItem_Click(null, new RoutedEventArgs());
                return true;
            case GalleryShortcut.Archive:
                OpenArchiveMenuItem_Click(null, new RoutedEventArgs());
                return true;
            case GalleryShortcut.Backups:
                OpenBackupsMenuItem_Click(null, new RoutedEventArgs());
                return true;
            case GalleryShortcut.Statistics:
                StatsButton_Click(null, new RoutedEventArgs());
                return true;
            case GalleryShortcut.Authors:
                AuthorsButton_Click(null, new RoutedEventArgs());
                return true;
            case GalleryShortcut.ShortcutList:
                ShortcutsMenuItem_Click(null, new RoutedEventArgs());
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The arrows, Home, End, Page Up and Page Down move the cursor from card to card (<see cref="GalleryKeyboard"/>), and
    /// the selection with it, as in Explorer; the list scrolls to the card. Not in a list of choices or a slider, whose
    /// arrows are their own; false when there is no card, and the key scrolls the list as before
    /// </summary>
    private bool MoveCursor(KeyEventArgs e, object? focused)
    {
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control) return ToggleCursor();
        if (GalleryKeyboard.Match(e.Key, e.KeyModifiers) is not { } key) return false;
        if (focused is ComboBox or ListBoxItem or ListBox or Slider or MenuItem or TabItem or TreeViewItem) return false;
        var row = _mainViewModel.Gallery.MoveCursor(key.Move, key.Selection, GalleryPanel.RowsPerPage(),
            GalleryPanel.FirstVisibleRow() as Models.GalleryRow);
        if (row is null) return false;
        GalleryPanel.BringItemIntoView(row);
        return true;
    }

    // Space, and Ctrl+Space as in Explorer
    private bool ToggleCursor()
    {
        var row = _mainViewModel.Gallery.ToggleCursor(GalleryPanel.FirstVisibleRow() as Models.GalleryRow);
        if (row is null) return false;
        GalleryPanel.BringItemIntoView(row);
        return true;
    }

    /// <summary>
    /// Esc in the search: clears it, and when it is empty, leaves it. Elsewhere it clears the selection. Esc in another
    /// text field is that field's
    /// </summary>
    private bool Escape(object? focused)
    {
        if (BandDrawn)
        {
            EndBand(keep: false); // a rectangle being dragged: dropped, the selection as it was before it
            return true;
        }
        if (ReferenceEquals(focused, SearchBox))
        {
            if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = "";
            else GalleryScroll.Focus(); // the list takes the focus: the arrows and Page Down scroll it
            return true;
        }
        if (focused is TextBox || !_mainViewModel.Gallery.HasSelection) return false;
        _mainViewModel.Gallery.ClearSelectionCommand.Execute(null);
        return true;
    }

    private void ShortcutsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        RunUiTask(() => ForzaToolkit.UI.Dialogs.ShortcutsDialog.ShowAsync(this, Localisation.Strings.ShortcutsTitle, GalleryShortcuts.Groups(), GalleryShortcuts.Note()),
            "keyboard shortcuts dialog");
    }
}
