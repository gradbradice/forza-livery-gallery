using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>
/// The storage's duplicates (docs/storage.md, stage 5): among its liveries as the scan finds them among the game's, and
/// the marks "In the game" on a stored card and "In the storage" on a card of the game. Found in the background after
/// each scan and each change of the storage; the newest search wins
/// </summary>
internal sealed partial class MainViewModel
{
    private int _copiesVersion;

    private void RefreshCopiesLater() => _ = RunSafelyAsync(RefreshCopiesAsync, nameof(RefreshCopiesAsync));

    private async Task RefreshCopiesAsync()
    {
        int version = ++_copiesVersion;
        if (CurrentStorage is not { } store || CurrentAccountKey is not { } accountKey)
        {
            foreach (var entry in GameEntries) entry.SetCopy(CopyKind.None, null);
            return;
        }
        // The storage's cards: those shown, or built for the search alone while the game is shown
        var stored = IsStorageMode ? [.. Gallery.AllEntries]
            : await _scanService.StoredEntriesAsync(store.GetAll(), savePathService.CurrentUserId, CurrentAccountKey);
        var game = GameEntries.ToList();
        var result = await _scanService.FindStorageCopiesAsync(stored, game, storage.Paths.StorageOf(accountKey),
            savePathService.ResolveSaveDataPath(), LiveryEntry.PossibleDuplicateThresholdPercent);
        if (version != _copiesVersion) return; // a newer search runs

        foreach (var entry in stored)
        {
            var outcome = result.Stored.GetValueOrDefault(entry.FolderName);
            entry.SetDuplicates(outcome?.Status ?? DuplicateStatus.Ok, outcome?.Related);
            var mark = result.StoredInGame.GetValueOrDefault(entry.FolderName);
            entry.SetCopy(mark?.Kind ?? CopyKind.None, mark?.Other);
        }
        foreach (var entry in game)
        {
            var mark = result.GameInStorage.GetValueOrDefault(entry.FolderName);
            entry.SetCopy(mark?.Kind ?? CopyKind.None, mark?.Other);
        }
        // The duplicate filter and the counts follow the storage's statuses
        if (IsStorageMode && FilterBar.DuplicatesFilterMode != DuplicatesFilterMode.All) RefreshGallery();
        else Gallery.RefreshCountsOnly();
    }
}
