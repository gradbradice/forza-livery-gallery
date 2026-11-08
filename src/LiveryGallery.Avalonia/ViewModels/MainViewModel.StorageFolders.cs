using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Services.Collections;

namespace LiveryGallery.ViewModels;

/// <summary>
/// The storage's folders (docs/storage.md, stage 4): the part of the storage the grid shows (all, no folder, a folder),
/// the folders made, renamed, reordered and deleted, liveries laid into a folder, a folder returned to the game. A
/// folder is a field of the livery's entry, not a folder on disk (<see cref="LiveryArchive.MoveToFolder"/>)
/// </summary>
internal sealed partial class MainViewModel
{
    /// <summary>The panel's row "All the storage"</summary>
    public const string AllStoredView = "storage:all";

    /// <summary>The panel's row "No folder"</summary>
    public const string NoFolderView = "storage:none";

    /// <summary>What the storage shows: <see cref="AllStoredView"/>, <see cref="NoFolderView"/> or a folder's id</summary>
    [ObservableProperty]
    private string _storageFolderView = AllStoredView;

    /// <summary>The storage's folders or their counts changed: the panel is filled again</summary>
    public event Action? StorageFoldersChanged;

    partial void OnStorageFolderViewChanged(string value)
    {
        ApplyStorageScope();
        if (IsStorageMode) RefreshGallery();
    }

    private void ApplyStorageScope()
    {
        string view = StorageFolderView;
        Gallery.Scope = !IsStorageMode || view == AllStoredView ? null
            : view == NoFolderView ? e => e.StorageFolderId is null
            : e => e.StorageFolderId == view;
    }

    /// <summary>The storage's folders in their order; none without a storage</summary>
    public IReadOnlyList<LiveryArchiveFolder> StorageFolders() => CurrentStorage?.GetFolders() ?? [];

    /// <summary>How many stored liveries each folder holds; the key null for those in none</summary>
    public (int All, int None, IReadOnlyDictionary<string, int> ByFolder) StorageFolderCounts()
    {
        var all = CurrentStorage?.GetAll() ?? [];
        var byFolder = all.Where(e => e.FolderId is not null).GroupBy(e => e.FolderId!).ToDictionary(g => g.Key, g => g.Count());
        return (all.Count, all.Count(e => e.FolderId is null), byFolder);
    }

    /// <summary>Why a folder name cannot be taken (empty, taken by another folder); null when it can</summary>
    /// <param name="except">The folder being renamed</param>
    public string? StorageFolderNameProblem(string name, string? except = null) =>
        CollectionNames.Clean(name) is not { } clean ? Strings.CollectionNameEmpty
        : CollectionNames.IsTaken(clean, StorageFolders().Select(f => (f.Id, f.Name)), except) ? Strings.StorageFolderNameTaken
        : null;

    /// <summary>A new folder; null when the name cannot be taken or there is no storage</summary>
    public LiveryArchiveFolder? CreateStorageFolder(string name)
    {
        if (CurrentStorage is not { } store || StorageFolderNameProblem(name) is not null) return null;
        var folder = store.CreateFolder(name);
        StorageFoldersChanged?.Invoke();
        return folder;
    }

    public bool RenameStorageFolder(string id, string name)
    {
        if (CurrentStorage is not { } store || StorageFolderNameProblem(name, id) is not null || !store.RenameFolder(id, name)) return false;
        StorageFoldersChanged?.Invoke();
        return true;
    }

    public void ReorderStorageFolders(IReadOnlyList<string> ids)
    {
        if (CurrentStorage is not { } store) return;
        store.ReorderFolders(ids);
        StorageFoldersChanged?.Invoke();
    }

    /// <summary>The stored liveries in a folder, in the window's order</summary>
    public IReadOnlyList<LiveryEntry> StoredIn(string folderId) => !IsStorageMode ? []
        : ReturnRoom.InShownOrder([.. Gallery.AllEntries.Where(e => e.StorageFolderId == folderId)], Gallery.ShownEntries());

    /// <summary>
    /// Deletes a folder; its liveries stay in the storage, in none, or go to the archive first when
    /// <paramref name="toArchive"/>. False when the liveries were to go and none could
    /// </summary>
    public async Task<bool> DeleteStorageFolderAsync(string id, bool toArchive)
    {
        if (CurrentStorage is not { } store) return false;
        if (toArchive && StoredIn(id) is { Count: > 0 } liveries && !await MoveStoredToArchiveAsync(liveries)) return false;
        // The stored liveries that were in it (a game's copy of the same name is not one of them)
        var keys = store.DeleteFolder(id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Gallery.AllEntries.Where(e => e.StorageFolderId == id && keys.Contains(e.FolderName))) entry.StorageFolderId = null;
        if (StorageFolderView == id) StorageFolderView = AllStoredView;
        RefreshStorageCount();
        RefreshGallery();
        return true;
    }

    /// <summary>
    /// Lays stored liveries into a folder (null: in none). Nothing moves on disk: only their entries change, and the
    /// cards at once
    /// </summary>
    public int MoveToStorageFolder(IReadOnlyList<LiveryEntry> entries, string? folderId)
    {
        if (CurrentStorage is not { } store || entries.Count == 0) return 0;
        var moved = store.MoveToFolder(entries.Select(e => e.FolderName), folderId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.Where(e => moved.Contains(e.FolderName))) entry.StorageFolderId = folderId;
        string name = folderId is null ? Strings.StorageNoFolder : StorageFolders().FirstOrDefault(f => f.Id == folderId)?.Name ?? "";
        if (moved.Count > 0) Status.SetStatusText(string.Format(Strings.StatusMovedToFolderFormat, name, moved.Count));
        RefreshStorageCount();
        RefreshGallery();
        return moved.Count;
    }

    private (int Count, DateTime At, long Bytes)? _storageBytes;

    /// <summary>The storage's folders and size for the statistics; its size is read again when it changed or after a while</summary>
    private StorageStatistics? StorageStatisticsNow()
    {
        if (CurrentStorage is null || CurrentAccountKey is not { } key) return null;
        var (all, none, _) = StorageFolderCounts();
        if (_storageBytes is not { } known || known.Count != all || DateTime.UtcNow - known.At > TimeSpan.FromMinutes(1))
        {
            long bytes = 0;
            try
            {
                var root = new DirectoryInfo(storage.Paths.StorageOf(key));
                if (root.Exists) bytes = root.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.LogError("Failed to measure the storage", ex);
            }
            _storageBytes = (all, DateTime.UtcNow, bytes);
        }
        return new StorageStatistics(StorageFolders().Count, none, _storageBytes.Value.Bytes);
    }

    /// <summary>
    /// A backup of the storage (<paramref name="folderId"/> null) or of one folder of it (docs/storage.md, stage 6), kept
    /// with the account's backups
    /// </summary>
    public async Task<bool> BackUpStorageAsync(string? folderId)
    {
        if (CurrentStorage is not { } store || CurrentAccountKey is not { } accountKey) return false;
        var keys = folderId is null ? null : store.GetAll().Where(e => e.FolderId == folderId).Select(e => e.Data.FolderName).ToList();
        if (keys is { Count: 0 } || store.GetAll().Count == 0)
        {
            ErrorMessageRequested?.Invoke(Strings.BackupNoStorageMessage);
            return false;
        }
        Status.SetLoading(true, Strings.BackupCreateStorageButton);
        try
        {
            string path = await storage.BackupsOf(accountKey).CreateFromArchiveAsync(store, keys, GetAccountContext().Current, LiveryBackupKind.Manual);
            Status.SetStatusText(string.Format(Strings.StatusStorageBackupMadeFormat, Path.GetFileName(path)));
            return true;
        }
        catch (OperationCanceledException) when (ShutdownToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to back up the storage", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
            return false;
        }
        finally
        {
            Status.SetLoading(false);
        }
    }

    /// <summary>"Return the folder to the game": every livery in it, with the window of room when the game is short of it</summary>
    public Task<bool> ReturnStorageFolderToGameAsync(string folderId) => StoredIn(folderId) is { Count: > 0 } liveries
        ? ReturnToGameAsync(liveries)
        : Task.FromResult(false);
}
