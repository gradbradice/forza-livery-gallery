using System.Text.Json;

namespace LiveryGallery.Configuration;

/// <summary>
/// Where each account keeps its storage when the user chose a folder of their own (docs/storage.md, "Own folder"):
/// <c>storage-folders.json</c> in the program's folder, by game and account. An account not listed keeps it in the
/// program's folder (<see cref="GamePaths.StorageOf"/>). Also the common folder chosen last, which the window offers
/// the next account and game, and whether the storages keep their folders as folders on disk (one choice for all).
/// Its own file, not settings.json: a move of the storage is written at once, whatever the settings window does with
/// its copy of the settings. Thread-safe
/// </summary>
internal sealed class StorageFolderSettings
{
    /// <summary>The file's name in the program's folder</summary>
    public const string FileName = "storage-folders.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private sealed class Data
    {
        public string? CommonFolder { get; set; }

        public bool FoldersOnDisk { get; set; }

#pragma warning disable CA2227 // set by the JSON serializer
        public Dictionary<string, Dictionary<string, string>> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);
#pragma warning restore CA2227
    }

    private readonly string _path;
    private readonly Lock _lock = new();
    private Data _data;

    /// <param name="path">The file; read now, an unreadable one is put aside and every storage is in the program's folder</param>
    public StorageFolderSettings(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _data = Load(path);
    }

    /// <summary>The file in the program's folder</summary>
    public static StorageFolderSettings Open() => new(Path.Combine(AppSettings.BaseCachePath, FileName));

    /// <summary>The common folder chosen last ("D:\Forza liveries"); null before the first</summary>
    public string? CommonFolder
    {
        get
        {
            lock (_lock) return _data.CommonFolder;
        }
    }

    /// <summary>
    /// Every storage keeps its folders as folders on disk, each livery in its folder's (LiveryArchive.FoldersOnDisk);
    /// false (the default): flat, the folders only names
    /// </summary>
    public bool FoldersOnDisk
    {
        get
        {
            lock (_lock) return _data.FoldersOnDisk;
        }
    }

    /// <summary>Whether the storages keep their folders on disk from now on (<see cref="FoldersOnDisk"/>). Written at once</summary>
    public void SetFoldersOnDisk(bool on)
    {
        string json;
        lock (_lock)
        {
            _data.FoldersOnDisk = on;
            json = JsonSerializer.Serialize(_data, JsonOptions);
        }
        Write(json);
    }

    /// <summary>The folder the user chose for the account's storage; null: the program's folder</summary>
    public string? FolderOf(string gameId, string accountKey)
    {
        lock (_lock)
            return _data.Games.TryGetValue(gameId, out var accounts) && accounts.TryGetValue(accountKey, out string? folder) ? folder : null;
    }

    /// <summary>The accounts of the game with a folder of their own, and their folders</summary>
    public IReadOnlyDictionary<string, string> FoldersOf(string gameId)
    {
        lock (_lock)
            return _data.Games.TryGetValue(gameId, out var accounts)
                ? new Dictionary<string, string>(accounts, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every folder of its own, of every game and account</summary>
    public IReadOnlyList<(string Game, string Account, string Folder)> All()
    {
        lock (_lock)
            return [.. _data.Games.SelectMany(g => g.Value.Select(a => (g.Key, a.Key, a.Value)))];
    }

    /// <summary>
    /// The account's storage is in <paramref name="folder"/> from now on (null: the program's folder); the common
    /// folder it was made in is remembered for the next one. Written at once
    /// </summary>
    public void Set(string gameId, string accountKey, string? folder, string? commonFolder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountKey);
        string json;
        lock (_lock)
        {
            if (!_data.Games.TryGetValue(gameId, out var accounts))
                _data.Games[gameId] = accounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (folder is null) accounts.Remove(accountKey);
            else accounts[accountKey] = Path.GetFullPath(folder);
            if (accounts.Count == 0) _data.Games.Remove(gameId);
            if (commonFolder is not null) _data.CommonFolder = Path.GetFullPath(commonFolder);
            json = JsonSerializer.Serialize(_data, JsonOptions);
        }
        Write(json);
    }

    private void Write(string json)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            AtomicFile.WriteAllText(_path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogError("Failed to write where the storages are", ex);
        }
    }

    private static Data Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Data();
            var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(path), JsonOptions) ?? new Data();
            // Case-insensitive keys whatever the file had, and no folder that is not a full path
            var games = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (game, accounts) in data.Games ?? [])
            {
                var clean = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (account, folder) in accounts ?? [])
                    if (GamePaths.IsFolderName(account) && !string.IsNullOrWhiteSpace(folder) && Path.IsPathFullyQualified(folder))
                        clean[account] = folder;
                if (clean.Count > 0) games[game] = clean;
            }
            data.Games = games;
            if (data.CommonFolder is { } common && !Path.IsPathFullyQualified(common)) data.CommonFolder = null;
            return data;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.LogError("Failed to read where the storages are: each one is in the program's folder until chosen again", ex);
            AtomicFile.TryBackupCorruptedFile(path);
            return new Data();
        }
    }
}
