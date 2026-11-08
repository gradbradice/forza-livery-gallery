using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Configuration;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>
/// The storage of the current account (docs/storage.md): liveries kept for good beyond the game's limit. Del sends
/// the selected liveries there; one that is locked, on a car or an auction livery stays in the game, and the gallery
/// says how many and why. Nothing is lost on the way, so there is no question but those of the archive: the game
/// running, the list of the cars' liveries not known or old. The automatic backup before it, as before the archive
/// </summary>
internal sealed partial class MainViewModel
{
    private StorageBlock StorageBlockOf(LiveryEntry entry) => StorageRules.Check(entry.IsLocked, entry.IsAuction, entry.IsInstalled);

    private bool CanStore(LiveryEntry entry) => StorageBlockOf(entry) == StorageBlock.None;

    private bool CanMoveSelectedToStorage() => !IsStorageMode && !IsStorageUnavailable && Gallery.SelectedEntries.Any(CanStore);

    /// <summary>Why none of the selected liveries can go into the storage (the menu item's tooltip, Del's message)</summary>
    public string? StorageBlockedTooltip
    {
        get
        {
            if (!Gallery.HasSelection || IsStorageMode || CanMoveSelectedToStorage()) return null;
            if (IsStorageUnavailable) return string.Format(Strings.StorageUnavailableReasonFormat, StorageUnavailablePath);
            var selected = Gallery.SelectedEntries;
            return string.Join(Environment.NewLine, StorageSkipNotes(
                selected.Count(e => StorageBlockOf(e) == StorageBlock.Locked),
                selected.Count(e => StorageBlockOf(e) == StorageBlock.Installed),
                selected.Count(e => StorageBlockOf(e) == StorageBlock.Auction)));
        }
    }

    /// <summary>Why liveries stayed in the game: a line per kind, with how many</summary>
    private static List<string> StorageSkipNotes(int locked, int installed, int auction)
    {
        var notes = new List<string>(3);
        if (locked > 0) notes.Add(string.Format(Strings.StorageLockedSkippedFormat, locked));
        if (installed > 0) notes.Add(string.Format(Strings.StorageInstalledSkippedFormat, installed));
        if (auction > 0) notes.Add(string.Format(Strings.StorageAuctionSkippedFormat, auction));
        return notes;
    }

    [RelayCommand(CanExecute = nameof(CanMoveSelectedToStorage))]
    private async Task MoveSelectedToStorageAsync()
    {
        if (!Gallery.HasSelection) return;
        await MoveToStorageAsync(Gallery.SelectedEntries.ToList());
    }

    /// <summary>
    /// Moves <paramref name="entries"/> from the game into the storage, into <paramref name="folderId"/> (none when
    /// null), with the archive's checks: the questions about the list of cars and the running game, the automatic
    /// backup before it, and what stayed and why
    /// </summary>
    /// <returns>True when liveries went</returns>
    public async Task<bool> MoveToStorageAsync(IReadOnlyList<LiveryEntry> entries, string? folderId = null)
    {
        if (entries.Count == 0) return false;
        string? savePath = savePathService.ResolveSaveDataPath();
        if (savePath is null || CurrentAccountKey is not { } accountKey || !GamePaths.IsFolderName(accountKey) || accountKey == GamePaths.Unassigned)
        {
            ErrorMessageRequested?.Invoke(Strings.ArchiveNoSavePathMessage);
            return false;
        }

        if (storage.StorageOf(accountKey) is not { } store)
        {
            ErrorMessageRequested?.Invoke(string.Format(Strings.StorageUnavailableReasonFormat, StorageUnavailablePath));
            return false;
        }

        var selected = entries.ToList();
        int locked = selected.Count(e => StorageBlockOf(e) == StorageBlock.Locked);
        int installed = selected.Count(e => StorageBlockOf(e) == StorageBlock.Installed);
        int auction = selected.Count(e => StorageBlockOf(e) == StorageBlock.Auction);
        var toStore = selected.Where(CanStore).ToList();
        if (toStore.Count == 0)
        {
            ErrorMessageRequested?.Invoke(string.Join(Environment.NewLine + Environment.NewLine, StorageSkipNotes(locked, installed, auction)));
            return false;
        }

        // The rule "never from a car" holds only as far as the list of the cars' liveries is known and new: the same
        // questions (and settings) as before the archive
        if (!await ConfirmArchiveRiskAsync(toStore, toStorage: true)) return false;
        if (!await savePathService.Profile.RunningGuard.ConfirmAsync(ConfirmWhileGameRunningAsync)) return false;
        if (ShutdownToken.IsCancellationRequested) return false;
        if (!await BackUpBeforeArchiveAsync(savePath)) return false;

        Status.SetLoading(true, Strings.LoadingMovingToStorage);
        ArchiveOperationResult result;
        try
        {
            result = await store.MoveToArchiveAsync([.. toStore.Select(e => e.Data)], savePath,
                GetAccountContext().Current, folderId);
            storage.RememberPreviews(accountKey);
            Gallery.ClearSelectionCommand.Execute(null);
            await RunScanAsyncTracked(isUserInitiated: true);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to move liveries to the storage", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
            return false;
        }
        finally
        {
            Status.SetLoading(false);
        }

        var notes = StorageSkipNotes(locked, installed, auction);
        // The storage has other liveries of their keys (put there by hand): they stayed in the game
        if (result.Conflicts.Count > 0)
            notes.Add(string.Format(Strings.StorageConflictsFormat, string.Join(Environment.NewLine, result.Conflicts)));
        if (result.LeftoverPaths.Count > 0)
            notes.Add(string.Format(Strings.ArchiveLeftoverFoldersFormat, string.Join(Environment.NewLine, result.LeftoverPaths)));
        if (notes.Count > 0) ErrorMessageRequested?.Invoke(string.Join(Environment.NewLine + Environment.NewLine, notes));
        if (result.Completed.Count > 0) Status.SetStatusText(string.Format(Strings.StatusMovedToStorageFormat, result.Completed.Count));
        return result.Completed.Count > 0;
    }
}
