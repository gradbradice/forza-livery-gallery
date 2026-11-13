using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>What the last action was, for the words of its undo</summary>
internal enum UndoKind
{
    Favorites,
    Locks,
    Tags,
    ToStorage,
    ToArchive,
    ToGame,
}

/// <summary>
/// The last action that can be undone: stars, locks, tags, a move from the game to the storage (Del), from the storage
/// to the archive (Del there) or back to the game (Ins)
/// </summary>
/// <param name="Kind">What was done</param>
/// <param name="Count">To how many liveries</param>
/// <param name="AccountKey">The account it was done in: undone only while that account is shown</param>
/// <param name="Profile">The game it was done in</param>
/// <param name="Revert">Puts it back; false when nothing was put back</param>
internal sealed record UndoStep(UndoKind Kind, int Count, string? AccountKey, object Profile, Func<Task<bool>> Revert);

/// <summary>
/// Undo of the last action (Ctrl+Z, "Undo" in the status bar). One step, kept until the next action that can be undone,
/// until it is undone, or until another account or game is shown. Stars, locks and tags go back as they were. A move
/// goes back the way the user would send it back, with every check of that way: liveries sent to the storage return to
/// the game as "Return to the game" returns them (room in the save, the running game), liveries returned to the game are
/// stored again in the folders they had, liveries sent from the storage to the archive are taken back into theirs. A
/// move to the archive from the game (Shift+Del) is not undone here: it asks first, and the archive window restores.
/// What the undo does is not an action to undo: there is no redo
/// </summary>
internal sealed partial class MainViewModel
{
    private UndoStep? _undo;
    private bool _undoing;

    /// <summary>There is an action to undo, in the account and the game shown</summary>
    public bool CanUndo => _undo is { } step && StepIsHere(step);

    /// <summary>What "Undo" would undo, and its key ("Undo: Moved to the storage: 12 (Ctrl+Z)"); empty when nothing</summary>
    public string UndoTooltip => _undo is { } step && StepIsHere(step)
        ? $"{string.Format(Strings.UndoTooltipFormat, Describe(step))} ({GalleryShortcuts.GestureText(GalleryShortcut.Undo)})"
        : "";

    private bool StepIsHere(UndoStep step) =>
        string.Equals(step.AccountKey, CurrentAccountKey, StringComparison.OrdinalIgnoreCase) && ReferenceEquals(step.Profile, savePathService.Profile);

    internal static string Describe(UndoStep step) => string.Format(step.Kind switch
    {
        UndoKind.Favorites => Strings.UndoFavoritesFormat,
        UndoKind.Locks => Strings.UndoLocksFormat,
        UndoKind.Tags => Strings.UndoTagsFormat,
        UndoKind.ToStorage => Strings.StatusMovedToStorageFormat,
        UndoKind.ToArchive => Strings.StatusStoredToArchiveFormat,
        _ => Strings.StatusReturnedToGameFormat,
    }, step.Count);

    private void RememberForUndo(UndoKind kind, int count, Func<Task<bool>> revert)
    {
        if (_undoing) return; // what an undo does is not an action of the user's
        SetUndo(new UndoStep(kind, count, CurrentAccountKey, savePathService.Profile, revert));
    }

    private void SetUndo(UndoStep? step)
    {
        _undo = step;
        RefreshUndo();
    }

    /// <summary>The account or the game shown changed: "Undo" shows or hides with it</summary>
    public void RefreshUndo()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoTooltip));
        UndoCommand.NotifyCanExecuteChanged();
    }

    private void RememberFlagsForUndo(bool locks, IReadOnlyList<(string Key, bool Was)> was) =>
        RememberForUndo(locks ? UndoKind.Locks : UndoKind.Favorites, was.Count, () =>
        {
            Gallery.RestoreFlags(locks, was, GameEntries);
            return Task.FromResult(true);
        });

    private void RememberTagsForUndo(IReadOnlyList<(string Key, IReadOnlyList<string> Tags)> was) =>
        RememberForUndo(UndoKind.Tags, was.Count, () =>
        {
            var byKey = was.ToDictionary(w => w.Key, w => w.Tags, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, tags) in was) tagService.SetTags(key, tags);
            foreach (var entry in Gallery.AllEntries.Concat(GameEntries).Distinct())
                if (byKey.TryGetValue(entry.FolderName, out var tags)) entry.Tags = [.. tags];
            if (!RebuildTagsBar() && Gallery.HasTagFilter) RefreshGallery();
            return Task.FromResult(true);
        });

    /// <summary>
    /// The undo of a return to the game: the liveries go to the storage again, as Del sends them (its checks, its
    /// backup), and then into the folders they had there
    /// </summary>
    private async Task<bool> StoreAgainAsync(IReadOnlyCollection<string> keys, IReadOnlyDictionary<string, string?> folders)
    {
        var wanted = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = GameEntries.Where(e => wanted.Contains(e.FolderName)).ToList();
        if (entries.Count == 0 || !await MoveToStorageAsync(entries)) return false;
        await PutIntoFoldersAsync(keys, folders);
        return true;
    }

    /// <summary>The undo of a move from the storage to the archive: taken back, into the folders they had</summary>
    private async Task<bool> TakeBackFromArchiveAsync(IReadOnlyCollection<string> keys, IReadOnlyDictionary<string, string?> folders)
    {
        if (CurrentStorage is not { } store || CurrentAccountKey is not { } accountKey) return false;
        IReadOnlyList<string> back;
        Status.SetLoading(true, Strings.LoadingMovingToStorage);
        try
        {
            back = await store.TakeFromAsync(storage.ArchiveOf(accountKey), keys, GetAccountContext().Current, null);
            if (back.Count > 0) await PutIntoFoldersAsync(back, folders);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to take liveries back from the archive into the storage", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
            return false;
        }
        finally
        {
            Status.SetLoading(false);
        }
        return back.Count > 0;
    }

    // Each stored livery back into the folder it had; a folder deleted since leaves its liveries without one
    private async Task PutIntoFoldersAsync(IReadOnlyCollection<string> keys, IReadOnlyDictionary<string, string?> folders)
    {
        if (CurrentStorage is not { } store) return;
        try
        {
            var known = store.GetFolders().Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var group in keys.Where(k => folders.GetValueOrDefault(k) is { } id && known.Contains(id)).GroupBy(k => folders[k]!))
                await store.MoveToFolderAsync(group, group.Key);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to put stored liveries back into their folders", ex);
        }
        if (IsStorageMode) await RefreshStorageAsync();
        else RefreshStorageCount();
    }

    /// <summary>Ctrl+Z, "Undo" in the status bar</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (_undo is not { } step || _undoing) return;
        if (!StepIsHere(step)) return;
        SetUndo(null);
        _undoing = true;
        bool done;
        try
        {
            done = await step.Revert();
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to undo the last action", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
            done = false;
        }
        finally
        {
            _undoing = false;
        }
        if (done) Status.SetStatusText(string.Format(Strings.StatusUndoneFormat, Describe(step)));
        RefreshUndo();
    }
}
