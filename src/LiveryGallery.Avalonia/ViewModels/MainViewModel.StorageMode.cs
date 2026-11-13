using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Configuration;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>What the main window shows: the liveries of the game, or those of the storage (docs/storage.md)</summary>
internal enum GalleryMode
{
    Game,
    Storage,
}

/// <summary>
/// The main window's two modes, "Game | Storage" (docs/storage.md). One grid shows either: while the storage is shown,
/// its liveries are the gallery's entries and the game's wait aside (<see cref="GameEntries"/>), so a scan of the save
/// goes on and only updates them. Each mode keeps its own search, filters, tags, selection and cursor; the window
/// keeps its scroll and folded groups (<see cref="ModeChanging"/>, <see cref="ModeChanged"/>). In the storage Ins
/// sends the selected liveries back into the game and Del to the archive
/// </summary>
internal sealed partial class MainViewModel
{
    // The game's cards while the storage is shown; null while the game is
    private List<LiveryEntry>? _gameEntriesAside;

    // What the mode not shown showed: the bar of filters and the gallery's selection and tags. Null before the
    // storage was first shown, which then starts as the game with no search and no "only …" filter
    private (FilterBarState Bar, GalleryViewState? View)? _hiddenModeState;

    private bool _switchingMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGameMode), nameof(Mode), nameof(ShowSlotsCounter))]
    private bool _isStorageMode;

    public bool IsGameMode => !IsStorageMode;

    public GalleryMode Mode => IsStorageMode ? GalleryMode.Storage : GalleryMode.Game;

    /// <summary>The liveries of the game: the shown ones, or those set aside while the storage is shown</summary>
    public IReadOnlyList<LiveryEntry> GameEntries => _gameEntriesAside ?? (IReadOnlyList<LiveryEntry>)Gallery.AllEntries;

    /// <summary>The account has a storage (one is chosen and it is not the liveries of no account)</summary>
    [ObservableProperty]
    private bool _hasStorage;

    /// <summary>How many liveries the storage holds</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StorageCountText), nameof(StorageStatusText))]
    private int _storageCount;

    /// <summary>The switch's count of the storage: "1 450"; nothing while its folder is not there</summary>
    public string StorageCountText => IsStorageUnavailable ? "" : StorageCount.ToString("N0", AppLocalisationService.Culture);

    /// <summary>The status bar in the storage: "In the storage: 1 450"</summary>
    public string StorageStatusText => string.Format(Strings.StatusStoredCountFormat, StorageCountText);

    /// <summary>The status bar's keys in the storage: "Ins: to the game · Del: to the archive"</summary>
    public string StorageKeysHint => string.Format(Strings.StorageKeysHintFormat,
        GalleryShortcuts.GestureText(GalleryShortcut.ReturnToGame), GalleryShortcuts.GestureText(GalleryShortcut.StoredToArchive));

    /// <summary>The switch's count of the game: "238 / 1000", or how many when the game has no limit</summary>
    [ObservableProperty]
    private string _gameCountText = "";

    /// <summary>The room in the save is shown in the status bar: the game has a limit and the game is shown</summary>
    public bool ShowSlotsCounter => HasSlotsLimit && !IsStorageMode;

    partial void OnHasSlotsLimitChanged(bool value) => OnPropertyChanged(nameof(ShowSlotsCounter));

    /// <summary>Before the grid shows the other mode: the mode it leaves (the window remembers its scroll and folds)</summary>
    public event Action<GalleryMode>? ModeChanging;

    /// <summary>After the grid shows the other mode: the mode it shows now</summary>
    public event Action<GalleryMode>? ModeChanged;

    /// <summary>
    /// Asked when the game has too little room for the liveries that come back (the window of room): the choice, or
    /// null to cancel
    /// </summary>
    public Func<ReturnRoomViewModel, Task<ReturnRoomChoice?>>? ChooseReturnRoomAsync { get; set; }

    /// <summary>Asked when a livery to return is a copy of one in the game; true to return it anyway</summary>
    public Func<string, Task<bool>>? ConfirmRestoreDuplicatesAsync { get; set; }

    /// <summary>Asked when liveries to return came from another account; true to bring them here</summary>
    public Func<string, Task<bool>>? ConfirmRestoreIntoCurrentAccountAsync { get; set; }

    // The account's storage; null without an account, and while its folder of its own is not there
    private LiveryArchive? CurrentStorage =>
        CurrentAccountKey is { } key && AccountHasStorage(key) ? storage.StorageOf(key) : null;

    private static bool AccountHasStorage(string key) => GamePaths.IsFolderName(key) && key != GamePaths.Unassigned;

    /// <summary>The texts of the modes in another language</summary>
    public void RefreshModeTexts()
    {
        OnPropertyChanged(nameof(StorageCountText));
        OnPropertyChanged(nameof(StorageStatusText));
        OnPropertyChanged(nameof(StorageKeysHint));
        UpdateSlots();
    }

    /// <summary>The switch's counts: the storage's from its index, the game's from the room counter</summary>
    private void RefreshStorageCount()
    {
        var store = CurrentStorage;
        HasStorage = CurrentAccountKey is { } key && AccountHasStorage(key);
        IsStorageUnavailable = HasStorage && store is null;
        OnPropertyChanged(nameof(StorageUnavailablePath));
        OnPropertyChanged(nameof(CurrentStorageFolder));
        StorageCount = store?.GetAll().Count ?? 0;
        // A folder deleted (or another account's): the whole storage is shown
        if (StorageFolderView is not (AllStoredView or NoFolderView) && store?.GetFolders().All(f => f.Id != StorageFolderView) != false)
            StorageFolderView = AllStoredView;
        StorageFoldersChanged?.Invoke();
    }

    private void RefreshGameCountText(int counted, int? limit)
    {
        var culture = AppLocalisationService.Culture;
        GameCountText = limit is { } max
            ? $"{counted.ToString("N0", culture)} / {max.ToString("N0", culture)}"
            : counted.ToString("N0", culture);
    }

    [RelayCommand]
    private Task ShowGameAsync() => ShowModeAsync(GalleryMode.Game);

    [RelayCommand]
    private Task ShowStorageAsync() => ShowModeAsync(GalleryMode.Storage);

    /// <summary>Shows the game or the storage in the grid, each with what it showed the last time</summary>
    public async Task ShowModeAsync(GalleryMode mode)
    {
        if (mode == Mode || _switchingMode) return;
        if (mode == GalleryMode.Game)
        {
            SwapMode(_gameEntriesAside ?? [], storageMode: false);
            return;
        }
        RefreshStorageCount();
        if (!HasStorage) return;
        _switchingMode = true;
        try
        {
            // Its folder is not there: the storage is shown with what the window says about it, and no card
            if (CurrentStorage is not { } store)
            {
                SwapMode([], storageMode: true);
                return;
            }
            // What the user put there by hand since the last look comes in first
            await FindPlacedByHandAsync();
            if (CurrentStorage is not { } now) return;
            var entries = await _scanService.StoredEntriesAsync(now.GetAll(), savePathService.CurrentUserId, CurrentAccountKey);
            if (IsStorageMode || !ReferenceEquals(now, CurrentStorage)) return; // switched meanwhile
            MarkAddedByHand(entries);
            SwapMode(entries, storageMode: true);
        }
        finally
        {
            _switchingMode = false;
        }
    }

    private void SwapMode(List<LiveryEntry> entries, bool storageMode)
    {
        ModeChanging?.Invoke(Mode);
        var leaving = (FilterBar.Capture(), Gallery.CaptureView());
        var coming = _hiddenModeState ?? (leaving.Item1.ForAnotherMode(), (GalleryViewState?)null);
        _gameEntriesAside = storageMode ? [.. Gallery.AllEntries] : null;
        IsStorageMode = storageMode;
        FilterBar.Restore(coming.Item1, storageMode);
        Gallery.SwapEntries(entries, coming.Item2);
        _hiddenModeState = leaving;
        ApplyStorageScope();
        OnEntriesChanged();
        RefreshArchiveAvailability();
        NotifyNavigationCommands();
        ModeChanged?.Invoke(Mode);
        if (storageMode) RefreshCopiesLater(); // the storage's cards were built anew
    }

    /// <summary>
    /// Back to the game, and the storage's state forgotten: another account (or game) is shown, with a storage of its
    /// own
    /// </summary>
    private void LeaveStorageMode()
    {
        if (IsStorageMode) SwapMode(_gameEntriesAside ?? [], storageMode: false);
        _hiddenModeState = null;
        StorageFolderView = AllStoredView;
    }

    /// <summary>The storage's cards again after it changed, and its count</summary>
    public async Task RefreshStorageAsync()
    {
        RefreshStorageCount();
        if (IsStorageMode && IsStorageUnavailable && Gallery.AllEntries.Count > 0)
        {
            // Its folder went away (a disk unplugged): no card of it stays, nothing of it is taken for gone
            Gallery.ReplaceEntries([]);
            OnEntriesChanged();
        }
        if (!IsStorageMode || CurrentStorage is not { } store)
        {
            RefreshCopiesLater(); // the game's marks "In the storage"
            return;
        }
        // Laid out on disk as the storages are (folders there or flat), once a session: its disk may have been away
        if (CurrentAccountKey is { } accountKey)
        {
            try
            {
                await storage.EnsureArrangedAsync(accountKey);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.LogError("Failed to lay out the storage", ex);
            }
        }
        var fresh = await _scanService.StoredEntriesAsync(store.GetAll(), savePathService.CurrentUserId, CurrentAccountKey);
        if (!IsStorageMode || !ReferenceEquals(store, CurrentStorage)) return;
        MarkAddedByHand(fresh);
        var byKey = fresh.ToDictionary(e => e.FolderName, StringComparer.Ordinal);
        var merged = Gallery.MergeWithLocalState(fresh);
        // A card kept as it was keeps its object: what the storage knows of it may still have changed
        foreach (var entry in merged)
        {
            if (!byKey.TryGetValue(entry.FolderName, out var now) || ReferenceEquals(now, entry)) continue;
            entry.StoredAtUtc = now.StoredAtUtc;
            entry.StorageFolderId = now.StorageFolderId;
            entry.IsAddedByHand = now.IsAddedByHand;
        }
        Gallery.ReplaceEntries(merged);
        OnEntriesChanged();
        RefreshCopiesLater();
    }

    /// <summary>
    /// A stored card without a preview got one drawn from its 3D model (<see cref="RenderedThumbnails.KeepStored"/>): the
    /// storage's index keeps it, as a scan gives one to a livery of the save. Kept from the preview cleanup (remembered
    /// for a storage whose folder may go away), and back in the game with the livery
    /// </summary>
    public void KeepDrawnPreview(LiveryEntry entry, string previewPath)
    {
        if (!entry.IsStored || CurrentAccountKey is not { } key || CurrentStorage is not { } store) return;
        try
        {
            if (store.SetDrawnPreview(entry.FolderName, previewPath)) storage.RememberPreviews(key);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to keep the drawn preview of a stored livery", ex);
        }
    }

    private bool CanReturnSelectedToGame() => IsStorageMode && Gallery.HasSelection;

    /// <summary>Ins, "Return to the game": the selected liveries of the storage</summary>
    [RelayCommand(CanExecute = nameof(CanReturnSelectedToGame))]
    private async Task ReturnSelectedToGameAsync()
    {
        if (!CanReturnSelectedToGame()) return;
        // "As many as fit" takes the first in the window's order, not in the order they were clicked
        await ReturnToGameAsync(ReturnRoom.InShownOrder([.. Gallery.SelectedEntries], Gallery.ShownEntries()));
    }

    /// <summary>The card's "To the game": that livery alone</summary>
    [RelayCommand]
    private async Task ReturnToGameOneAsync(LiveryEntry? entry)
    {
        if (entry is not { IsStored: true }) return;
        await ReturnToGameAsync([entry]);
    }

    /// <summary>
    /// Moves stored liveries back into the save, with the checks of a restore from the archive: there is a save,
    /// another account's liveries come only when the user agrees, the running game is told, a livery the save has
    /// stays, a copy of one in it only when the user says so. With too little room the window of room asks (docs/storage.md,
    /// stage 3): swap (the game's liveries ticked go to the storage first), all beyond the limit, or as many as fit
    /// </summary>
    /// <param name="entries">The stored liveries, in the window's order</param>
    /// <returns>True when liveries went back</returns>
    public Task<bool> ReturnToGameAsync(IReadOnlyList<LiveryEntry> entries) =>
        ReturnKeysToGameAsync([.. entries.Select(e => e.FolderName)]);

    /// <summary><see cref="ReturnToGameAsync"/> for stored liveries known by their keys (the undo of a move to the storage)</summary>
    private async Task<bool> ReturnKeysToGameAsync(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0 || CurrentStorage is not { } store) return false;
        var index = store.GetAll().ToDictionary(e => e.Data.FolderName, StringComparer.OrdinalIgnoreCase);
        var chosen = keys.Select(k => index.GetValueOrDefault(k)).OfType<ArchivedLiveryEntry>().ToList();
        if (chosen.Count == 0) return false;
        // Where each was in the storage, and the step a swap for room leaves: both are for the undo of this return
        var foldersBefore = chosen.ToDictionary(c => c.Data.FolderName, c => c.FolderId, StringComparer.OrdinalIgnoreCase);
        var undoBefore = _undo;

        if (await LiveryRestoreConflicts.PrepareAsync(savePathService, GetAccountContext(), chosen.Select(c => c.SourceAccount),
                ConfirmRestoreIntoCurrentAccountAsync, ConfirmWhileGameRunningAsync, ErrorMessageRequested) is not { } savePath)
            return false;

        ArchiveOperationResult result;
        LiveryRestoreConflicts.Plan plan;
        try
        {
            plan = await LiveryRestoreConflicts.PlanAsync([.. chosen.Select(c => c.Data)], [.. GameEntries.Select(e => e.Data)],
                savePath, savePathService.Profile.Liveries, ConfirmRestoreDuplicatesAsync);
            IReadOnlyList<LiveryData> toRestore = plan.ToRestore;
            if (toRestore.Count > 0 && savePathService.Profile.MaxLiveries is { } limit)
            {
                int counted = SaveRoom.Counted(GameEntries);
                int free = Math.Max(0, limit - counted);
                if (toRestore.Count > free)
                {
                    if (ChooseReturnRoomAsync is not { } choose) return false;
                    var room = new ReturnRoomViewModel(toRestore.Count, counted, limit, ReturnRoom.Candidates(GameEntries),
                        store.GetFolders(), DateTime.Now);
                    if (await choose(room) is not { } choice) return false;
                    switch (choice.Mode)
                    {
                        case ReturnRoomMode.Fit:
                            toRestore = [.. toRestore.Take(free)];
                            plan = plan with { ToRestore = toRestore }; // the others stay by choice, not by failure
                            break;
                        case ReturnRoomMode.Exchange:
                            string? folderId = choice.NewFolderName is { } name
                                ? store.GetFolders().FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))?.Id
                                  ?? store.CreateFolder(name)?.Id
                                : choice.FolderId;
                            // The room first: the game's liveries go with every check of Del; none went, none come back
                            if (!await MoveToStorageAsync(choice.ToStorage, folderId)) return false;
                            break;
                    }
                }
            }
            if (ShutdownToken.IsCancellationRequested) return false;

            Status.SetLoading(true, Strings.LoadingReturningToGame);
            try
            {
                result = toRestore.Count > 0
                    ? await store.RestoreAsync(toRestore.Select(d => d.FolderName), savePath)
                    : ArchiveOperationResult.Empty;
                if (result.Completed.Count > 0)
                {
                    Gallery.ClearSelectionCommand.Execute(null);
                    await RunScanAsyncTracked(isUserInitiated: true);
                    await RefreshStorageAsync();
                }
            }
            finally
            {
                Status.SetLoading(false);
            }
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to return liveries from the storage", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
            return false;
        }

        var restored = result.Completed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>(2);
        if (plan.Report(restored) is { } report) notes.Add(report);
        if (result.LeftoverPaths.Count > 0)
            notes.Add(string.Format(Strings.ArchiveLeftoverFoldersFormat, string.Join(Environment.NewLine, result.LeftoverPaths)));
        if (notes.Count > 0) ErrorMessageRequested?.Invoke(string.Join(Environment.NewLine + Environment.NewLine, notes));
        if (restored.Count > 0)
        {
            Status.SetStatusText(string.Format(Strings.StatusReturnedToGameFormat, restored.Count));
            // Undone by storing them again, each in the folder it had; a swap for room made on the way is undone after
            // it (the liveries that went to the storage to make the room come back)
            var swap = ReferenceEquals(_undo, undoBefore) ? null : _undo;
            RememberForUndo(UndoKind.ToGame, restored.Count, async () =>
            {
                bool back = await StoreAgainAsync([.. restored], foldersBefore);
                if (back && swap is not null) await swap.Revert();
                return back;
            });
        }
        return restored.Count > 0;
    }

    /// <summary>
    /// Del (and Shift+Del) in the storage: the liveries go to the archive, to be deleted from there. Nothing leaves the
    /// save, so there is no question; the files are renamed on one disk, copied when the storage is on another
    /// </summary>
    /// <returns>True when liveries went</returns>
    public async Task<bool> MoveStoredToArchiveAsync(IReadOnlyList<LiveryEntry> entries)
    {
        if (entries.Count == 0 || CurrentStorage is not { } store || CurrentAccountKey is not { } accountKey) return false;
        IReadOnlyList<string> moved;
        var archive = storage.ArchiveOf(accountKey);
        // The keys the archive has already: those stay in the storage for that, the others only when their files did not move
        var inArchive = archive.GetAll().Select(e => e.Data.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Status.SetLoading(true, Strings.LoadingMovingToArchive);
        try
        {
            moved = await archive.TakeFromAsync(store, [.. entries.Select(e => e.FolderName)], GetAccountContext().Current);
            Gallery.ClearSelectionCommand.Execute(null);
            await RefreshStorageAsync();
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to move liveries from the storage to the archive", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
            return false;
        }
        finally
        {
            Status.SetLoading(false);
        }
        if (NotTakenReport([.. entries.Select(e => e.Data)], moved, inArchive, Strings.StoredToArchiveSkippedFormat, Strings.ArchiveFailedFormat) is { } report
            && !ShutdownToken.IsCancellationRequested)
            ErrorMessageRequested?.Invoke(report);
        if (moved.Count > 0)
        {
            Status.SetStatusText(string.Format(Strings.StatusStoredToArchiveFormat, moved.Count));
            var folders = entries.ToDictionary(e => e.FolderName, e => e.StorageFolderId, StringComparer.OrdinalIgnoreCase);
            string[] keys = [.. moved];
            RememberForUndo(UndoKind.ToArchive, keys.Length, () => TakeBackFromArchiveAsync(keys, folders));
        }
        return moved.Count > 0;
    }
}
