using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ForzaToolkit.UI.Dialogs;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>
/// The main window's two modes, "Game | Storage" (docs/storage.md): the switch in the title bar, the storage's cyan
/// (the window's class <c>storageMode</c>), and what the window keeps of each mode: its folded groups and its scroll
/// </summary>
internal partial class MainWindow
{
    // The folded groups and the scroll of the mode not shown
    private HashSet<string> _hiddenModeCollapsed = new(StringComparer.Ordinal);
    private Vector _hiddenModeOffset;

    // The storage's folder while it is shown (liveries put there by hand), or looked for while it is not there
    private StorageFolderWatcher? _storageWatcher;

    private void InitializeStorageMode()
    {
        _mainViewModel.ModeChanging += OnModeChanging;
        _mainViewModel.ModeChanged += _ => SyncStorageWatcher();
        _mainViewModel.ModeChanged += OnModeChanged;
        _mainViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.HasStorage)) ApplyModeTooltips();
            if (e.PropertyName is nameof(MainViewModel.IsStorageMode) or nameof(MainViewModel.IsStorageUnavailable)
                or nameof(MainViewModel.CurrentStorageFolder) or nameof(MainViewModel.HasStorage))
                SyncStorageWatcher();
            if (e.PropertyName == nameof(MainViewModel.IsStorageUnavailable))
                StorageSidebar.IsVisible = _mainViewModel.IsStorageMode && !_mainViewModel.IsStorageUnavailable;
        };
        _storageWatcher = new StorageFolderWatcher(_mainViewModel.FindPlacedByHandAsync, _mainViewModel.LookForStorageFolderAsync);
        Closed += (_, _) => _storageWatcher?.Dispose();
        _mainViewModel.ChooseReturnRoomAsync = room => ReturnRoomDialog.AskAsync(this, room);
        _mainViewModel.ConfirmRestoreDuplicatesAsync = message => ConfirmDialog.AskAsync(
            this, Strings.RestoreDuplicateTitle, message, Strings.RestoreAnywayButton, Strings.RestoreSkipDuplicatesButton);
        _mainViewModel.ConfirmRestoreIntoCurrentAccountAsync = message => ConfirmDialog.AskAsync(
            this, Strings.RestoreFromOtherAccountTitle, message, Strings.RestoreIntoCurrentAccountButton, Strings.ButtonCancel);
    }

    private void ApplyModeTexts()
    {
        GameModeText.Text = Strings.ModeGame;
        StorageModeText.Text = Strings.ModeStorage;
        StorageFoundDetailsButton.Content = Strings.StorageFoundDetailsButton;
        StorageUnavailableTitle.Text = Strings.StorageUnavailableTitle;
        StorageUnavailableText.Text = Strings.StorageUnavailableText;
        StorageCheckAgainButton.Content = Strings.StorageCheckAgainButton;
        StorageChooseOtherButton.Content = Strings.StorageChooseOtherButton;
        ApplyModeTooltips();
    }

    // Watched while shown; looked for every few seconds while its folder is not there, whatever is shown
    private void SyncStorageWatcher()
    {
        if (_isClosing) return;
        _storageWatcher?.Configure(_mainViewModel.IsStorageMode ? _mainViewModel.CurrentStorageFolder : null,
            _mainViewModel.HasStorage && _mainViewModel.IsStorageUnavailable);
    }

    private void StorageFoundDetails_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        RunUiTask(() => StorageFoundDialog.ShowAsync(this, _mainViewModel.StorageFoundDetails), "liveries placed by hand");

    // "Choose another folder…" while the storage's folder is not there
    private void StorageChooseOther_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_mainViewModel.GetAccountContext().Current is not { } account) return;
        RunUiTask(() => ChangeStorageFolderAsync(this, account.Key, account.DisplayName), "storage folder");
    }

    /// <summary>"Storage folder" for an account, over <paramref name="owner"/>: true when its storage went to another folder</summary>
    private async Task<bool> ChangeStorageFolderAsync(Window owner, string accountKey, string? displayName)
    {
        if (_isClosing || _mainViewModel.StorageFolderContextOf(accountKey, displayName) is not { } context) return false;
        return await StorageFolderDialog.ShowAsync(owner, new StorageFolderViewModel(context),
            (target, common, progress, ct) => _mainViewModel.MoveStorageAsync(accountKey, target, common, progress, ct));
    }

    /// <summary>
    /// "Keep the storage's folders as folders on disk" in the settings: how many liveries move, asked over the settings
    /// window; then every storage laid out, and what could not be said
    /// </summary>
    private async Task<bool> SetStorageFoldersOnDiskAsync(Window owner, bool on, IProgress<(int Done, int Total)> progress)
    {
        if (_isClosing || on == _storage.FoldersOnDisk) return false;
        var culture = AppLocalisationService.Culture;
        int count = await Task.Run(() => _storage.CountToArrange(on));
        string question = string.Format(on ? Strings.StorageLayoutOnConfirmFormat : Strings.StorageLayoutOffConfirmFormat, count.ToString("N0", culture));
        if (!await ConfirmDialog.AskAsync(owner, Strings.StorageLayoutTitle, question,
                on ? Strings.StorageLayoutOnButton : Strings.StorageLayoutOffButton, Strings.ButtonCancel))
            return false;

        var (moved, failed, away) = await _storage.SetFoldersOnDiskAsync(on, progress);
        AppLog.LogWarning($"The storages keep their folders {(on ? "on disk" : "as names only")}: {moved} liveries moved, {failed} could not, {away.Count} storages away (not an error)");
        if (_isClosing) return true;
        await _mainViewModel.RefreshStorageAsync();
        FillStorageSidebar();
        var notes = new List<string>(2);
        if (failed > 0) notes.Add(string.Format(Strings.StorageLayoutFailedFormat, failed.ToString("N0", culture)));
        if (away.Count > 0) notes.Add(string.Format(Strings.StorageLayoutAwayFormat, away.Count.ToString("N0", culture)));
        if (notes.Count > 0) await InfoDialog.ShowAsync(owner, Strings.StorageLayoutTitle, string.Join(Environment.NewLine + Environment.NewLine, notes));
        _mainViewModel.Status.SetStatusText(string.Format(Strings.StorageLayoutDoneFormat, moved.ToString("N0", culture)));
        return true;
    }

    private void ApplyModeTooltips()
    {
        GameModeButton.SetValue(ToolTip.TipProperty, new ShortcutTip(Strings.ModeGameTooltip, GalleryShortcut.ShowGame));
        StorageModeButton.SetValue(ToolTip.TipProperty, _mainViewModel.HasStorage
            ? new ShortcutTip(Strings.ModeStorageTooltip, GalleryShortcut.ShowStorage)
            : Strings.StorageNoAccountTooltip);
    }

    // Before the grid shows the other mode: what this one shows is kept, the other's folds come back
    private void OnModeChanging(GalleryMode leaving)
    {
        var collapsed = new HashSet<string>(_collapsedGroupKeys, StringComparer.Ordinal);
        _collapsedGroupKeys.Clear();
        _collapsedGroupKeys.UnionWith(_hiddenModeCollapsed);
        _hiddenModeCollapsed = collapsed;
        (_placeBeforeFilters, _hiddenModePlaceBeforeFilters) = (_hiddenModePlaceBeforeFilters, _placeBeforeFilters);
        (_wasFiltered, _hiddenModeWasFiltered) = (_hiddenModeWasFiltered, _wasFiltered);
        (_hiddenModeOffset, var offset) = (GalleryScroll.Offset, _hiddenModeOffset);
        _comingModeOffset = offset;
    }

    private Vector _comingModeOffset;

    private void OnModeChanged(GalleryMode mode)
    {
        Classes.Set("storageMode", mode == GalleryMode.Storage);
        RebuildGalleryItems(preservePosition: false);
        var offset = _comingModeOffset;
        // After the layout of the new rows, the scroll of the last time
        // After the layout (the folders' panel takes its room): the cards as many as fit, then the scroll of the last time
        Dispatcher.UIThread.Post(() =>
        {
            if (_isLoaded) UpdateGroupWidthsOnly();
            Dispatcher.UIThread.Post(() => GalleryScroll.Offset = offset, DispatcherPriority.Background);
        }, DispatcherPriority.Background);
    }

    /// <summary>Ctrl+1 and Ctrl+2: the mode, whichever view (liveries or screenshots) is shown</summary>
    private bool RunModeShortcut(GalleryShortcut action)
    {
        switch (action)
        {
            case GalleryShortcut.ShowGame:
                RunUiTask(() => _mainViewModel.ShowModeAsync(GalleryMode.Game), "show the game");
                return true;
            case GalleryShortcut.ShowStorage:
                if (_mainViewModel.HasStorage) RunUiTask(() => _mainViewModel.ShowModeAsync(GalleryMode.Storage), "show the storage");
                return true;
            case GalleryShortcut.RenameFolder:
                return RenameShownStorageFolder();
            default:
                return false;
        }
    }
}
