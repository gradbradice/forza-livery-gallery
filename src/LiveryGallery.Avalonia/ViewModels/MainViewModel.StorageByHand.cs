using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaToolkit.Liveries.Scanning;
using LiveryGallery.Configuration;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;

namespace LiveryGallery.ViewModels;

/// <summary>
/// The storage in a folder of the user's (docs/storage.md, "Own folder"): liveries the user puts there by hand are
/// taken in by themselves (at the first scan of a launch, when the storage is shown, on "Refresh" and while it is shown,
/// the window watching its folder), the banner says what was found, and a card found so says "Added by hand" until it
/// is opened or the next launch. A folder that is not there (a disk not plugged in) makes the storage unavailable:
/// nothing goes there, nothing of it is taken for gone, and the window offers to look again or to choose another folder
/// </summary>
internal sealed partial class MainViewModel
{
    // The keys taken in by hand this launch: their cards say so until opened
    private readonly HashSet<string> _addedByHand = new(StringComparer.OrdinalIgnoreCase);

    // What was found since the banner was last closed, by where it was found; the folders' liveries gone
    private readonly Dictionary<string, LiveryIntakeItem> _foundItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _foundGone = [];
    private readonly HashSet<string> _foundRemovedFolders = new(StringComparer.OrdinalIgnoreCase);

    private int _intakeRunning;
    private bool _intakeAgain;

    /// <summary>The account's storage is in a folder of its own that is not there now</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStorageUnavailable), nameof(StorageCountText), nameof(StorageUnavailablePath))]
    private bool _isStorageUnavailable;

    /// <summary>The storage is shown and its folder is not there: the window says so instead of the grid</summary>
    public bool ShowStorageUnavailable => IsStorageMode && IsStorageUnavailable;

    partial void OnIsStorageModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowStorageUnavailable));
        OnPropertyChanged(nameof(ShowStorageFound));
    }

    /// <summary>The banner is shown: the storage is, and something was found</summary>
    public bool ShowStorageFound => IsStorageMode && HasStorageFound && !IsStorageUnavailable;

    partial void OnIsStorageUnavailableChanged(bool value) => MoveSelectedToStorageCommand.NotifyCanExecuteChanged();

    /// <summary>The folder of the storage that is not there</summary>
    public string StorageUnavailablePath => CurrentAccountKey is { } key && storage.CustomStorageFolderOf(key) is { } folder ? folder : "";

    /// <summary>The folder of the current account's storage (watched while it is shown); null without one or while it is not there</summary>
    public string? CurrentStorageFolder => CurrentAccountKey is { } key && HasStorage && !IsStorageUnavailable ? storage.StorageFolderOf(key) : null;

    /// <summary>The banner over the storage's cards: liveries put there by hand were found</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStorageFound))]
    private bool _hasStorageFound;

    /// <summary>What the banner says</summary>
    [ObservableProperty]
    private string _storageFoundText = "";

    /// <summary>Everything found since the banner was last closed, for "Details"</summary>
    public StorageFoundDetails StorageFoundDetails => new(
        [.. _foundItems.Values.OrderBy(i => i.FoundAt, StringComparer.CurrentCultureIgnoreCase)], [.. _foundGone], [.. _foundRemovedFolders]);

    [RelayCommand]
    private void CloseStorageFound()
    {
        _foundItems.Clear();
        _foundGone.Clear();
        _foundRemovedFolders.Clear();
        HasStorageFound = false;
        StorageFoundText = "";
    }

    /// <summary>
    /// Takes in what the user put into the storage's folder by hand (LiveryArchive.TakeInPlacedByHandAsync): its cards
    /// are built anew when something came or went. Asked again while it runs, it runs once more after
    /// </summary>
    public async Task FindPlacedByHandAsync()
    {
        if (Interlocked.Exchange(ref _intakeRunning, 1) == 1)
        {
            _intakeAgain = true;
            return;
        }
        try
        {
            do
            {
                _intakeAgain = false;
                if (CurrentAccountKey is not { } accountKey || CurrentStorage is not { } store) return;
                ulong? userId = savePathService.CurrentUserId;
                var intake = new LiveryArchiveIntake
                {
                    Read = (folder, name) => _scanService.ReadPlacedFolder(folder, name, userId),
                    NewKey = SaveKeys.Livery,
                    SavePath = savePathService.ResolveSaveDataPath(),
                    Account = GetAccountContext().Current,
                };
                LiveryArchiveIntakeReport report;
                try
                {
                    report = await store.TakeInPlacedByHandAsync(intake, ShutdownToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                if (!string.Equals(accountKey, CurrentAccountKey, StringComparison.OrdinalIgnoreCase)) return; // switched meanwhile
                if (report.IsEmpty) continue;
                AppLog.LogWarning($"Liveries placed in the storage by hand: {report.Taken.Count()} taken in, {report.Skipped.Count()} left, {report.Gone.Count} gone (not an error)");
                foreach (var item in report.Taken) _addedByHand.Add(item.Key!);
                bool news = AddFound(report);
                if (report.Taken.Any() || report.Gone.Count > 0 || report.Refiled.Count > 0)
                {
                    storage.RememberPreviews(accountKey);
                    await RefreshStorageAsync();
                }
                // Folders on disk: a folder made there by hand, a livery moved into another
                if (report.FoldersChanged || report.Refiled.Count > 0) StorageFoldersChanged?.Invoke();
                if (news) ShowFound();
            }
            while (_intakeAgain && !ShutdownToken.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to look for liveries placed in the storage by hand", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _intakeRunning, 0);
        }
    }

    // What the banner did not say yet: a livery left where it was is said once, not at every look
    private bool AddFound(LiveryArchiveIntakeReport report)
    {
        bool news = false;
        foreach (var item in report.Items)
        {
            if (!item.IsTaken && _foundItems.TryGetValue(item.FoundAt, out var known) && known.Problem == item.Problem) continue;
            _foundItems[item.FoundAt] = item;
            news = true;
        }
        foreach (string key in report.Gone.Where(k => !_foundGone.Contains(k, StringComparer.OrdinalIgnoreCase)))
        {
            _foundGone.Add(key);
            news = true;
        }
        _foundRemovedFolders.UnionWith(report.RemovedFolders);
        return news;
    }

    private void ShowFound()
    {
        var details = StorageFoundDetails;
        var taken = details.Items.Where(i => i.IsTaken).ToList();
        int skipped = details.Items.Count(i => !i.IsTaken);
        var parts = new List<string>(4)
        {
            string.Format(Strings.StorageFoundTitleFormat, N(details.Items.Count)),
        };
        if (taken.Count > 0)
        {
            var inner = new List<string>(2);
            var folders = taken.Select(i => i.FolderName).OfType<string>().Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            if (folders.Count > 0)
                inner.Add(string.Format(Strings.StorageFoundIntoFoldersFormat, string.Join(", ", folders), N(taken.Count(i => i.FolderName is not null))));
            int renamed = taken.Count(i => i.Renamed != LiveryIntakeRename.None);
            if (renamed > 0) inner.Add(string.Format(Strings.StorageFoundRenamedFormat, N(renamed)));
            parts.Add(inner.Count > 0
                ? string.Format(Strings.StorageFoundAddedWithFormat, N(taken.Count), string.Join(", ", inner))
                : string.Format(Strings.StorageFoundAddedFormat, N(taken.Count)));
        }
        if (skipped > 0) parts.Add(string.Format(Strings.StorageFoundSkippedFormat, N(skipped)));
        if (details.Gone.Count > 0) parts.Add(string.Format(Strings.StorageFoundGoneFormat, N(details.Gone.Count)));
        StorageFoundText = string.Join(" ", parts);
        HasStorageFound = details.Items.Count > 0 || details.Gone.Count > 0;
    }

    private static string N(int value) => value.ToString("N0", AppLocalisationService.Culture);

    /// <summary>The cards of liveries taken in by hand this launch say so</summary>
    private void MarkAddedByHand(IEnumerable<LiveryEntry> entries)
    {
        foreach (var entry in entries) entry.IsAddedByHand = entry.IsStored && _addedByHand.Contains(entry.FolderName);
    }

    /// <summary>A card was opened: "Added by hand" has been seen</summary>
    public void SeenAddedByHand(LiveryEntry entry)
    {
        if (!entry.IsAddedByHand) return;
        _addedByHand.Remove(entry.FolderName);
        entry.IsAddedByHand = false;
    }

    /// <summary>"Check again": the storage's folder is looked for again, its liveries shown when it is back</summary>
    [RelayCommand]
    private async Task CheckStorageAgainAsync()
    {
        if (CurrentAccountKey is not { } key) return;
        bool there = storage.CheckStorageAgain(key);
        RefreshStorageCount();
        if (!there)
        {
            Status.SetStatusText(string.Format(Strings.StorageStillUnavailableFormat, StorageUnavailablePath));
            return;
        }
        await RefreshStorageAsync();
        await FindPlacedByHandAsync();
    }

    /// <summary>While the storage's folder is not there: looked for again, quietly; its liveries come back with it</summary>
    public async Task LookForStorageFolderAsync()
    {
        if (CurrentAccountKey is not { } key || !HasStorage) return;
        bool there = storage.CheckStorageAgain(key);
        if (there == !IsStorageUnavailable) return;
        await RefreshStorageAsync();
        if (there) await FindPlacedByHandAsync();
    }

    /// <summary>
    /// The account's storage is in another folder now (moved by the window "Storage folder"): the one opened before
    /// is let go, the cards built anew, and what lay in the new folder taken in
    /// </summary>
    public async Task StorageFolderChangedAsync(string accountKey)
    {
        storage.StorageMoved(accountKey);
        if (!string.Equals(accountKey, CurrentAccountKey, StringComparison.OrdinalIgnoreCase)) return;
        RefreshStorageCount();
        if (IsStorageMode && CurrentStorage is { } store)
        {
            var entries = await _scanService.StoredEntriesAsync(store.GetAll(), savePathService.CurrentUserId, CurrentAccountKey);
            if (IsStorageMode && ReferenceEquals(store, CurrentStorage))
            {
                MarkAddedByHand(entries);
                Gallery.ReplaceEntries(Gallery.MergeWithLocalState(entries));
                OnEntriesChanged();
            }
        }
        await FindPlacedByHandAsync();
        RefreshCopiesLater();
    }

    /// <summary>What the window "Storage folder" needs to know of an account's storage</summary>
    public StorageFolderContext? StorageFolderContextOf(string accountKey, string? displayName)
    {
        if (!GamePaths.IsFolderName(accountKey) || accountKey == GamePaths.Unassigned) return null;
        string? custom = storage.CustomStorageFolderOf(accountKey);
        bool available = storage.IsStorageAvailable(accountKey);
        var open = available ? storage.StorageOf(accountKey) : null;
        string current = storage.StorageFolderOf(accountKey);
        var game = savePathService.Profile.Game;
        var others = storage.Folders?.All()
            .Where(f => !(string.Equals(f.Game, game.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(f.Account, accountKey, StringComparison.OrdinalIgnoreCase)))
            .ToList() ?? [];
        var titles = Accounts.ToDictionary(a => a.Info.Key, a => a.Title, StringComparer.OrdinalIgnoreCase);
        var forbidden = new List<StorageForbidden>
        {
            new(AppSettings.BaseCachePath, StorageForbiddenKind.Program),
            new(AppSettings.LocalCachePath, StorageForbiddenKind.Program),
        };
        foreach (var account in Accounts)
            if (!string.IsNullOrWhiteSpace(account.Info.SavePath)) forbidden.Add(new StorageForbidden(account.Info.SavePath, StorageForbiddenKind.Save));
        if (savePathService.SavePath is { } save) forbidden.Add(new StorageForbidden(save, StorageForbiddenKind.Save));
        foreach (var (otherGame, otherAccount, folder) in others)
            forbidden.Add(new StorageForbidden(folder, StorageForbiddenKind.OtherStorage,
                $"{titles.GetValueOrDefault(otherAccount) ?? AccountLabels.Title(otherAccount, null)}, {GameNameOf(otherGame)}"));

        // Another account's folder of the same name in the common folder: " 2"
        string gameFolder = StoragePlaces.Clean(game.ShortName) ?? game.Id;
        var takenNames = others
            .Where(f => string.Equals(f.Game, game.Id, StringComparison.OrdinalIgnoreCase))
            .Select(f => (Parent: Path.GetDirectoryName(f.Folder) ?? "", Name: Path.GetFileName(f.Folder)))
            .ToList();
        return new StorageFolderContext
        {
            AccountKey = accountKey,
            AccountTitle = AccountLabels.Title(accountKey, displayName),
            DisplayName = displayName,
            GameName = game.Name,
            GameFolderName = gameFolder,
            ProgramFolder = storage.Paths.StorageOf(accountKey),
            CurrentFolder = current,
            IsCustom = custom is not null,
            IsAvailable = available,
            CommonFolder = storage.Folders?.CommonFolder,
            Liveries = open?.GetAll().Count ?? 0,
            Bytes = open is null ? 0 : StoragePlaces.SizeOf(current),
            Forbidden = forbidden,
            Store = savePathService.Profile.Liveries,
            IsNameTaken = (common, name) => takenNames.Any(t =>
                StoragePlaces.Same(t.Parent, Path.Combine(common, gameFolder)) && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)),
        };
    }

    // Another game's storage, as the window names it: its id as the games write it short ("FH5")
    private string GameNameOf(string gameId) =>
        string.Equals(gameId, savePathService.Profile.Game.Id, StringComparison.OrdinalIgnoreCase) ? savePathService.Profile.Game.ShortName : gameId.ToUpperInvariant();

    /// <summary>
    /// Puts the account's storage into <paramref name="target"/> (null: the program's folder): its liveries are moved
    /// there (renamed on one disk, copied and checked between two), and what lay there is taken in. When its folder is
    /// not there, nothing can be moved: the storage is simply in the new folder from now on. The error when it failed;
    /// null when it went (the storage is then where it was, whole)
    /// </summary>
    public async Task<StorageMoveOutcome> MoveStorageAsync(string accountKey, string? target, string? commonFolder,
        IProgress<LiveryArchiveMoveProgress>? progress, CancellationToken ct)
    {
        if (storage.Folders is not { } folders) return new StorageMoveOutcome(false, Strings.FileOperationFailedMessage, []);
        string destination = target ?? storage.Paths.StorageOf(accountKey);
        string current = storage.StorageFolderOf(accountKey);
        if (StoragePlaces.Same(destination, current))
        {
            folders.Set(storage.GameId, accountKey, target, commonFolder);
            return new StorageMoveOutcome(true, null, []);
        }

        IReadOnlyList<string> leftovers = [];
        if (storage.IsStorageAvailable(accountKey) && storage.StorageOf(accountKey) is { } open && Directory.Exists(current))
        {
            var result = await LiveryArchive.MoveFolderAsync(open, destination, progress, ct);
            if (!result.Succeeded)
                return new StorageMoveOutcome(false, ct.IsCancellationRequested ? null : result.Error ?? Strings.FileOperationFailedMessage, []);
            leftovers = result.Leftovers;
        }
        else
        {
            try
            {
                Directory.CreateDirectory(destination);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.LogError($"Failed to make the storage folder '{destination}'", ex);
                return new StorageMoveOutcome(false, ex.Message, []);
            }
        }
        folders.Set(storage.GameId, accountKey, target, commonFolder);
        AppLog.LogWarning($"The storage of an account is in {(target is null ? "the program's folder" : "a folder of the user's")} now (not an error)");
        await StorageFolderChangedAsync(accountKey);
        Status.SetStatusText(string.Format(Strings.StorageMovedStatusFormat, destination));
        return new StorageMoveOutcome(true, null, leftovers);
    }

    /// <summary>For the settings: every account's storage, where it is and how big</summary>
    public Task<List<StorageRowInfo>> ListStoragesAsync()
    {
        var accounts = Accounts.Select(a => (a.Info.Key, a.Title, DisplayName: a.Info.Name)).ToList();
        string? currentKey = CurrentAccountKey;
        if (currentKey is not null && accounts.All(a => !string.Equals(a.Key, currentKey, StringComparison.OrdinalIgnoreCase)))
            accounts.Insert(0, (currentKey, AccountLabels.Title(currentKey, null), null));
        return Task.Run(() => accounts
            .Where(a => GamePaths.IsFolderName(a.Key) && a.Key != GamePaths.Unassigned)
            .Select(a =>
            {
                string folder = storage.StorageFolderOf(a.Key);
                bool custom = storage.CustomStorageFolderOf(a.Key) is not null;
                bool available = storage.IsStorageAvailable(a.Key);
                var open = available ? storage.StorageOf(a.Key) : null;
                return new StorageRowInfo(a.Key, a.Title, a.DisplayName, string.Equals(a.Key, currentKey, StringComparison.OrdinalIgnoreCase),
                    folder, custom, available, open?.GetAll().Count ?? 0, available ? StoragePlaces.SizeOf(folder) : 0,
                    available && custom ? StoragePlaces.FreeSpace(folder) : null);
            })
            .OrderByDescending(r => r.IsCurrent)
            .ToList());
    }
}

/// <summary>Everything found by hand since the banner was last closed</summary>
internal sealed record StorageFoundDetails(IReadOnlyList<LiveryIntakeItem> Items, IReadOnlyList<string> Gone, IReadOnlyList<string> RemovedFolders);

/// <summary>How a move of the storage went: the error when it failed (null when cancelled), what stayed in the old folder</summary>
internal sealed record StorageMoveOutcome(bool Succeeded, string? Error, IReadOnlyList<string> Leftovers);

/// <summary>One account's storage, as the settings list it</summary>
internal sealed record StorageRowInfo(string AccountKey, string Title, string? DisplayName, bool IsCurrent, string Folder, bool IsCustom,
    bool IsAvailable, int Liveries, long Bytes, (string Disk, long Free)? FreeSpace);

/// <summary>What the window "Storage folder" knows of the account and its storage</summary>
internal sealed class StorageFolderContext
{
    public required string AccountKey { get; init; }
    public required string AccountTitle { get; init; }
    public string? DisplayName { get; init; }
    public required string GameName { get; init; }

    /// <summary>The game's folder in a common folder ("FH6")</summary>
    public required string GameFolderName { get; init; }

    /// <summary>Where the storage is in the program's folder</summary>
    public required string ProgramFolder { get; init; }

    /// <summary>Where it is now</summary>
    public required string CurrentFolder { get; init; }

    public bool IsCustom { get; init; }
    public bool IsAvailable { get; init; } = true;
    public string? CommonFolder { get; init; }
    public int Liveries { get; init; }
    public long Bytes { get; init; }
    public IReadOnlyList<StorageForbidden> Forbidden { get; init; } = [];
    public required ILiveryStore Store { get; init; }

    /// <summary>Another account's storage has this name in the game's folder of this common folder</summary>
    public Func<string, string, bool> IsNameTaken { get; init; } = (_, _) => false;
}
