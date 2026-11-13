using Avalonia.Controls;
using Avalonia.Input;
using LiveryGallery.Services;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.ViewModels;
using LiveryGallery.Views.Collections;

namespace LiveryGallery.Views;

/// <summary>
/// The "Saved filter sets" button by the filter menu. Its menu starts with "Save the current filters…" (Ctrl+S; asks
/// for a name, and a name already there replaces that set), then has a set per line: a click shows it, a right click
/// gives "Apply the set" and "Delete a set", and the pointer resting on it shows what the set holds. The sets are the
/// game's (<see cref="FilterBarViewModel.Presets"/>). The menu is kept built, not filled as it opens: a menu filled in
/// its Opening shows up empty the first time, with nothing to say how a set is saved
/// </summary>
internal partial class MainWindow
{
    private void InitializeFilterPresets()
    {
        _mainViewModel.FilterBar.PresetsChanged += RebuildFilterPresetsMenu;
        _mainViewModel.ScanCompleted += RebuildFilterPresetsMenu; // another game has its own sets
        _mainViewModel.ScanCompleted += ApplySearchHistory;       // and its own searches
        RebuildFilterPresetsMenu();
        ApplySearchHistory();
    }

    // The game whose searches the search box offers now
    private Models.GameSettingsData? _searchHistoryOf;

    /// <summary>
    /// The search box offers the last searches of the game shown (ForzaToolkit's SearchHistoryBox: the button at the
    /// box's end, Alt+Down). Each game keeps its own, in its settings
    /// </summary>
    private void ApplySearchHistory()
    {
        var game = _settings.Game;
        if (ReferenceEquals(game, _searchHistoryOf)) return;
        _searchHistoryOf = game;
        var history = new ForzaToolkit.UI.Input.SearchHistory(game.SearchHistory);
        history.Changed += (_, _) =>
        {
            game.SearchHistory = [.. history.Items];
            Services.AppSettingsService.Save(_settings);
        };
        ForzaToolkit.UI.Input.SearchHistoryBox.SetHistory(SearchBox, history);
    }

    /// <summary>
    /// The search was used — a card was pressed, the list was turned with the wheel, Enter went to the results: it
    /// goes into the history (if it found anything; the toolkit's box knows from <see cref="RebuildGalleryItems"/>)
    /// </summary>
    private void SearchWasUsed()
    {
        if (!string.IsNullOrWhiteSpace(SearchBox.Text)) ForzaToolkit.UI.Input.SearchHistoryBox.Remember(SearchBox);
    }

    /// <summary>
    /// Enter in the search box goes to what was found: the search is shown at once (not after the typing pause), kept
    /// in the history, and the first card found becomes the current, selected one, with the keys on the cards from
    /// there (arrows, Space, Enter for the livery window). With nothing found the box keeps the focus
    /// </summary>
    private bool SearchEnter()
    {
        if (_showingScreenshots) return false;
        if (_searchDebounceTimer.IsEnabled)
        {
            _searchDebounceTimer.Stop();
            RefreshGallery();
        }
        SearchWasUsed();
        if (GalleryKeyboard.Match(Key.Home, KeyModifiers.None) is not { } home) return true;
        var row = _mainViewModel.Gallery.MoveCursor(home.Move, home.Selection, GalleryPanel.RowsPerPage(), null);
        if (row is null) return true;
        GalleryPanel.BringItemIntoView(row);
        GalleryScroll.Focus();
        return true;
    }

    /// <summary>How long the pointer rests on a set before what it holds is shown</summary>
    internal const int PresetDetailsDelayMs = 700;

    private void RebuildFilterPresetsMenu()
    {
        if (FilterPresetsButton.Flyout is not MenuFlyout menu) return;
        var bar = _mainViewModel.FilterBar;
        var items = menu.Items;
        items.Clear();

        var save = new MenuItem { Header = Strings.FilterPresetSave };
        ShortcutHint.SetAction(save, Services.GalleryShortcut.SaveFilterSet); // shows Ctrl+S beside it
        save.Click += (_, _) => RunUiTask(SaveFilterPresetAsync, "save filter preset");
        items.Add(save);
        items.Add(new Separator());

        foreach (var preset in bar.Presets) items.Add(PresetItem(menu, preset));
        if (bar.Presets.Count == 0) items.Add(new MenuItem { Header = Strings.FilterPresetsEmpty, IsEnabled = false });
    }

    // A set's line: a click shows the set, a right click offers to apply or delete it, the pointer at rest tells what it holds
    private MenuItem PresetItem(MenuFlyout menu, FilterPresetData preset)
    {
        var bar = _mainViewModel.FilterBar;
        var item = new MenuItem { Header = new TextBlock { Text = preset.Name } }; // a TextBlock: an underscore in a name is not an access key
        item.Click += (_, _) => bar.ApplyPreset(preset);

        var apply = new MenuItem { Header = Strings.FilterPresetApply };
        apply.Click += (_, _) =>
        {
            menu.Hide();
            bar.ApplyPreset(preset);
        };
        var delete = new MenuItem { Header = Strings.FilterPresetDelete };
        delete.Click += (_, _) =>
        {
            menu.Hide();
            bar.DeletePreset(preset);
        };
        item.ContextMenu = new ContextMenu { Items = { apply, delete } };

        ToolTip.SetTip(item, string.Join(Environment.NewLine, [.. FilterBarViewModel.DescribePreset(preset), "", Strings.FilterPresetHint]));
        ToolTip.SetShowDelay(item, PresetDetailsDelayMs);
        ToolTip.SetPlacement(item, PlacementMode.Right);
        return item;
    }

    private async Task SaveFilterPresetAsync()
    {
        string? name = await NameDialog.AskAsync(this, Strings.FilterPresetsTooltip, Strings.FilterPresetNameLabel, "",
            FilterBarViewModel.PresetNameProblem);
        if (name is not null) _mainViewModel.FilterBar.SavePreset(name);
    }
}
