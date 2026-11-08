using LiveryGallery.Configuration;

namespace LiveryGallery.Services;

/// <summary>
/// The archive, the storage and the backups of each account of one game (<see cref="GamePaths.ArchiveOf"/>,
/// <see cref="StorageFolderOf"/>, <see cref="GamePaths.BackupsOf"/>), opened the first time they are asked for and
/// kept. Nothing is shared between accounts: a livery archived or stored from one account is returned only into that
/// account's save, a backup made from it is listed only with it. A storage is in the program's folder, or in a folder
/// the user chose (<see cref="StorageFolderSettings"/>); one of those that is not there (a disk not plugged in) is
/// unavailable: never made again, its liveries never taken for gone. Thread-safe.
/// </summary>
internal sealed class AccountStorage
{
    // How long a look whether a chosen folder is there holds: asked for each command of the window
    private static readonly TimeSpan AvailabilityHold = TimeSpan.FromSeconds(2);

    private readonly GamePaths _paths;
    private readonly string _gameId;
    private readonly StorageFolderSettings? _folders;
    private readonly Func<string, LiveryArchive> _openArchive;
    private readonly Func<string, LiveryBackups> _openBackups;
    private readonly Dictionary<string, LiveryArchive> _archives = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Folder, LiveryArchive Archive)> _storages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (bool There, DateTime At)> _available = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LiveryBackups> _backups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    /// <param name="paths">The game's folders</param>
    /// <param name="openArchive">Opens the archive in the folder it is given</param>
    /// <param name="openBackups">Opens the backups in the folder it is given</param>
    /// <param name="gameId">The game, for the folders the user chose</param>
    /// <param name="folders">The folders the user chose; null: every storage in the program's folder</param>
    public AccountStorage(GamePaths paths, Func<string, LiveryArchive> openArchive, Func<string, LiveryBackups> openBackups,
        string gameId = "", StorageFolderSettings? folders = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(openArchive);
        ArgumentNullException.ThrowIfNull(openBackups);
        _paths = paths;
        _openArchive = openArchive;
        _openBackups = openBackups;
        _gameId = gameId;
        _folders = folders;
    }

    public GamePaths Paths => _paths;

    /// <summary>The folders the user chose; null when the storages are all in the program's folder</summary>
    public StorageFolderSettings? Folders => _folders;

    /// <summary>The game the storages are of</summary>
    public string GameId => _gameId;

    /// <summary>The folder the user chose for the account's storage; null: the program's folder</summary>
    public string? CustomStorageFolderOf(string accountKey) => _folders?.FolderOf(_gameId, accountKey);

    /// <summary>Where the account's storage is: the folder the user chose, or the program's</summary>
    public string StorageFolderOf(string accountKey) => CustomStorageFolderOf(accountKey) ?? _paths.StorageOf(accountKey);

    /// <summary>
    /// The account's storage can be opened: in the program's folder (made when needed), or in a chosen folder that is
    /// there. Looked at again after a moment
    /// </summary>
    public bool IsStorageAvailable(string accountKey)
    {
        if (CustomStorageFolderOf(accountKey) is not { } folder) return true;
        lock (_lock)
            if (_available.TryGetValue(accountKey, out var known) && DateTime.UtcNow - known.At < AvailabilityHold) return known.There;
        bool there = Directory.Exists(folder);
        lock (_lock) _available[accountKey] = (there, DateTime.UtcNow);
        return there;
    }

    /// <summary>Looks again whether the account's chosen folder is there ("Check again")</summary>
    public bool CheckStorageAgain(string accountKey)
    {
        lock (_lock) _available.Remove(accountKey);
        return IsStorageAvailable(accountKey);
    }

    /// <summary>
    /// The account's storage is in another folder from now on (moved there, or chosen with nothing in it): the one
    /// opened before is let go
    /// </summary>
    public void StorageMoved(string accountKey)
    {
        lock (_lock)
        {
            _storages.Remove(accountKey);
            _available.Remove(accountKey);
        }
    }

    /// <summary>The archive of the account</summary>
    public LiveryArchive ArchiveOf(string accountKey)
    {
        string folder = _paths.ArchiveOf(accountKey);
        lock (_lock)
        {
            if (!_archives.TryGetValue(accountKey, out var archive))
                _archives[accountKey] = archive = _openArchive(folder);
            return archive;
        }
    }

    /// <summary>
    /// The storage of the account: liveries kept for good beyond the game's limit, in folders. The same kind of archive
    /// as <see cref="ArchiveOf"/>, in its own folder (a rename between the two on one disk, a copy between two). Null
    /// while its chosen folder is not there (<see cref="IsStorageAvailable"/>)
    /// </summary>
    public LiveryArchive? StorageOf(string accountKey)
    {
        if (!IsStorageAvailable(accountKey)) return null;
        string folder = StorageFolderOf(accountKey);
        LiveryArchive storage;
        lock (_lock)
        {
            if (_storages.TryGetValue(accountKey, out var open) && StoragePlaces.Same(open.Folder, folder)) return open.Archive;
            _storages[accountKey] = (folder, storage = _openArchive(folder));
        }
        RememberPreviews(accountKey, storage);
        return storage;
    }

    /// <summary>The backups of the account</summary>
    public LiveryBackups BackupsOf(string accountKey)
    {
        string folder = _paths.BackupsOf(accountKey);
        lock (_lock)
        {
            if (!_backups.TryGetValue(accountKey, out var backups))
                _backups[accountKey] = backups = _openBackups(folder);
            return backups;
        }
    }

    /// <summary>Where the PNG previews of the account's backups go</summary>
    public string BackupThumbsOf(string accountKey) => _paths.BackupThumbsOf(accountKey);

    /// <summary>The archive of liveries whose account is not known, when there is one with something in it</summary>
    public LiveryArchive? Unassigned()
    {
        if (!Directory.Exists(_paths.ArchiveOf(GamePaths.Unassigned))) return null;
        var archive = ArchiveOf(GamePaths.Unassigned);
        return archive.GetAll().Count > 0 ? archive : null;
    }

    /// <summary>The backups whose account is not known, when there are any</summary>
    public LiveryBackups? UnassignedBackups()
    {
        if (!Directory.Exists(_paths.BackupsOf(GamePaths.Unassigned))) return null;
        var backups = BackupsOf(GamePaths.Unassigned);
        return backups.GetAll().Count > 0 ? backups : null;
    }

    /// <summary>
    /// Puts the backups whose account is not known with <paramref name="accountKey"/>, with their PNG previews: the
    /// user says they are this account's. How many moved
    /// </summary>
    public async Task<int> TakeUnassignedBackupsAsync(string accountKey)
    {
        if (UnassignedBackups() is not { } unassigned) return 0;
        var moved = await BackupsOf(accountKey).TakeFromAsync(unassigned).ConfigureAwait(false);
        string from = _paths.BackupThumbsOf(GamePaths.Unassigned), to = _paths.BackupThumbsOf(accountKey);
        try
        {
            if (Directory.Exists(from))
                foreach (string file in Directory.EnumerateFiles(from, "*.png"))
                {
                    string destination = Path.Combine(to, Path.GetFileName(file));
                    if (File.Exists(destination)) continue;
                    Directory.CreateDirectory(to);
                    File.Copy(file, destination); // made again from the backup when this fails
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogError("Failed to copy the previews of backups without an account", ex);
        }
        return moved.Count;
    }

    /// <summary>
    /// The preview file names every archive and storage of the game uses, of every account: the previews are the game's,
    /// one folder for all its accounts, and the scan's cleanup must keep them. A storage whose folder is not there
    /// gives the names it had when it was last opened
    /// </summary>
    public IReadOnlySet<string> GetReferencedThumbnailFiles()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in FoldersIn(_paths.ArchivesRoot))
            names.UnionWith(ArchiveOf(key).GetReferencedThumbnailFiles());
        foreach (string key in StorageAccounts())
        {
            if (StorageOf(key) is { } storage) names.UnionWith(storage.GetReferencedThumbnailFiles());
            else names.UnionWith(RememberedPreviews(key));
        }
        return names;
    }

    /// <summary>The accounts with an archive, a storage or backups on disk (and <see cref="GamePaths.Unassigned"/>, when it is there)</summary>
    public IReadOnlyList<string> KnownAccounts() =>
        [.. FoldersIn(_paths.ArchivesRoot).Union(StorageAccounts(), StringComparer.OrdinalIgnoreCase)
            .Union(FoldersIn(_paths.BackupsRoot), StringComparer.OrdinalIgnoreCase)];

    // The accounts with a storage in the program's folder, or in a folder of their own
    private IEnumerable<string> StorageAccounts() =>
        FoldersIn(_paths.StoragesRoot).Union(_folders?.FoldersOf(_gameId).Keys ?? [], StringComparer.OrdinalIgnoreCase);

    // The previews of a storage in a folder of its own, kept in the program's folder: while its disk is away, the
    // scan's cleanup must not take them
    private string PreviewListOf(string accountKey) => Path.Combine(_paths.Root, "storage-previews", accountKey + ".txt");

    private void RememberPreviews(string accountKey, LiveryArchive storage)
    {
        if (CustomStorageFolderOf(accountKey) is null) return;
        try
        {
            string path = PreviewListOf(accountKey);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicFile.WriteAllText(path, string.Join('\n', storage.GetReferencedThumbnailFiles().Order(StringComparer.Ordinal)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogErrorThrottled("storage-previews|" + accountKey, "Failed to remember the previews of a storage", ex);
        }
    }

    private IEnumerable<string> RememberedPreviews(string accountKey)
    {
        try
        {
            string path = PreviewListOf(accountKey);
            return File.Exists(path) ? File.ReadAllLines(path).Where(l => l.Length > 0) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogErrorThrottled("storage-previews|" + accountKey, "Failed to read the previews of a storage", ex);
            return [];
        }
    }

    /// <summary>The previews the storage uses now are remembered (after liveries came into it)</summary>
    public void RememberPreviews(string accountKey)
    {
        LiveryArchive? storage;
        lock (_lock) storage = _storages.TryGetValue(accountKey, out var open) ? open.Archive : null;
        if (storage is not null) RememberPreviews(accountKey, storage);
    }

    private static IEnumerable<string> FoldersIn(string root)
    {
        if (!Directory.Exists(root)) return [];
        try
        {
            return [.. Directory.EnumerateDirectories(root).Select(Path.GetFileName).OfType<string>().Where(GamePaths.IsFolderName)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogErrorThrottled(root, $"Failed to list '{root}'", ex);
            return [];
        }
    }
}
