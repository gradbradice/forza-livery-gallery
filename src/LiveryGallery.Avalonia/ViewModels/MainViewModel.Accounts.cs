using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;
using System.Collections.ObjectModel;

namespace LiveryGallery.ViewModels;

/// <summary>One entry of the account switcher</summary>
internal sealed record AccountOption(AccountInfo Info, string Title, string? Subtitle)
{
    public bool HasSubtitle => Subtitle is not null;

    /// <summary>The folder tells two unnamed accounts apart</summary>
    public string Tooltip => Info.SavePath;

    /// <summary>The player's name (the save's, else the one its liveries tell), or "Account" while it is not known</summary>
    public string Name => AccountLabels.Name(Info.Name);

    /// <summary>The end of the XUID beside the name: "***E1FB"</summary>
    public string Marker => AccountLabels.Marker(Info.Key);

    public static AccountOption From(AccountInfo info) => new(
        info,
        AccountLabels.Title(info.Key, info.Name),
        info.ItemCount is { } count ? string.Format(Strings.AccountLiveriesFormat, count) : null);
}

/// <summary>An account in the settings' list</summary>
/// <param name="Info">What is known about it</param>
/// <param name="IsCurrent">The account open now</param>
/// <param name="IsFound">The gallery finds its save folder itself; otherwise it was added by hand</param>
internal sealed record AccountListItem(AccountInfo Info, bool IsCurrent, bool IsFound);

/// <summary>What adding an account by hand did</summary>
internal enum AccountAddResult
{
    Added,

    /// <summary>The folder holds no save of the game</summary>
    NoSave,

    /// <summary>The account is in the list already (found on this PC or added before)</summary>
    AlreadyListed,
}

/// <summary>
/// Several game accounts on one PC. Each has its own scan cache (see <see cref="AppCacheService"/>), so switching
/// only reads the other account's folder for changes, like a normal refresh, instead of parsing everything again.
/// Favorites, locks, tags, livery IDs and author cards stay shared: they are keyed by livery folder name or by author,
/// which do not clash between accounts.
/// </summary>
internal sealed partial class MainViewModel
{
    private string? _appliedAccountKey;
    private bool _updatingAccounts;
    private int _accountsVersion;
    private Task? _accountSwitchTask;

    public ObservableCollection<AccountOption> Accounts { get; } = [];

    /// <summary>The switcher is only shown when there is something to switch to</summary>
    public bool HasMultipleAccounts => Accounts.Count > 1;

    [ObservableProperty]
    private AccountOption? _selectedAccount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitchAccount))]
    private bool _isSwitchingAccount;

    public bool CanSwitchAccount => !IsSwitchingAccount;

    private string? CurrentAccountKey => savePathService.SavePath is { } path ? AccountKey.For(path) : null;

    /// <summary>For the archive and backups: the open account, and the names of all known ones</summary>
    public AccountContext GetAccountContext()
    {
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in Accounts) titles.TryAdd(option.Info.Key, option.Title);

        AccountRef? current = CurrentAccountKey is { } key
            ? new AccountRef(key, Accounts.FirstOrDefault(a => string.Equals(a.Info.Key, key, StringComparison.OrdinalIgnoreCase))?.Info.Name)
            : null;
        // The switcher lists exactly the accounts whose save folder is on this PC (before its first refresh: unknown)
        var existing = Accounts.Count == 0 ? null : Accounts.Select(a => a.Info.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new AccountContext(current, titles, existing);
    }

    /// <summary>For the settings: every account with data, how much it takes, and whether its save folder is gone</summary>
    public Task<List<StoredAccount>> ListStoredAccountsAsync()
    {
        string? currentKey = CurrentAccountKey;
        return Task.Run(() => _accountRegistry.ListStored(currentKey));
    }

    /// <summary>
    /// For the settings: every account the switcher offers, with where it came from (found on this PC or added by
    /// hand) and whether it is the open one
    /// </summary>
    public Task<List<AccountListItem>> ListAccountsAsync()
    {
        string? currentSavePath = savePathService.SavePath;
        return Task.Run(() =>
        {
            var found = savePathService.Profile.Saves.FindAllSaveFolders();
            return _accountRegistry.Discover(currentSavePath)
                .Select(info => new AccountListItem(info,
                    IsCurrent: AccountKey.SamePath(info.SavePath, currentSavePath),
                    IsFound: found.Any(path => AccountKey.SamePath(path, info.SavePath))))
                .ToList();
        });
    }

    /// <summary>
    /// Adds the account of a save folder the gallery does not find itself: from now on the switcher offers it. Not
    /// opened: the switcher does that
    /// </summary>
    public async Task<AccountAddResult> AddAccountAsync(string savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath)) return AccountAddResult.NoSave;
        savePath = savePath.Trim();
        string? currentSavePath = savePathService.SavePath;
        var result = await Task.Run(() =>
        {
            if (_accountRegistry.Discover(currentSavePath).Any(a => AccountKey.SamePath(a.SavePath, savePath)))
                return AccountAddResult.AlreadyListed;
            return _accountRegistry.Add(savePath) ? AccountAddResult.Added : AccountAddResult.NoSave;
        });
        if (result == AccountAddResult.Added)
        {
            AppLog.LogWarning("An account was added by hand (not an error)");
            await RefreshAccountsAsync();
        }
        return result;
    }

    /// <summary>Deletes an account's data; the scan right after removes the previews only it used</summary>
    public async Task<bool> ForgetAccountAsync(string accountKey)
    {
        if (string.Equals(accountKey, CurrentAccountKey, StringComparison.OrdinalIgnoreCase)) return false;
        if (!await _accountRegistry.ForgetAsync(accountKey)) return false;

        scanController.RequestOrphanCleanup();
        await RunScanAsyncTracked(isUserInitiated: false);
        await RefreshAccountsAsync();
        return true;
    }

    /// <summary>
    /// Once per launch, after the first scan: says in the status bar that accounts gone for a while still take
    /// space. Only a hint; nothing is deleted without the user (a folder may be on a drive that is just unplugged).
    /// </summary>
    private async Task PointOutGoneAccountsAsync()
    {
        List<StoredAccount> stored;
        try
        {
            stored = await ListStoredAccountsAsync();
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to list the stored accounts", ex);
            return;
        }

        var longGone = stored
            .Where(a => !a.IsCurrent && !a.SaveFolderExists
                && (a.GoneSinceUtc is not { } since || DateTime.UtcNow - since > AccountRegistry.ForgetSuggestionAge))
            .ToList();
        if (longGone.Count == 0) return;

        Status.SetStatusText(string.Format(
            Strings.StatusGoneAccountsFormat, longGone.Count, ByteSizeFormatter.Format(longGone.Sum(a => a.Bytes))));
    }

    partial void OnSelectedAccountChanged(AccountOption? value)
    {
        if (_updatingAccounts || value is null || AccountKey.SamePath(value.Info.SavePath, savePathService.SavePath)) return;
        _accountSwitchTask = RunSafelyAsync(() => SwitchAccountAsync(value.Info.SavePath), nameof(SwitchAccountAsync));
    }

    private async Task SwitchAccountAsync(string savePath)
    {
        IsSwitchingAccount = true;
        try
        {
            if (!await Task.Run(() => savePathService.Profile.Saves.IsValidSaveFolder(savePath)))
            {
                ErrorMessageRequested?.Invoke(Strings.AccountUnavailableMessage);
                await RefreshAccountsAsync();
                return;
            }

            savePathService.SetSavePath(savePath);
            await OnSavePathSwitchedAsync();
        }
        finally
        {
            IsSwitchingAccount = false;
        }
    }

    /// <summary>After any change of the save folder: the switcher, the settings, or the first-run prompt</summary>
    private async Task OnSavePathSwitchedAsync()
    {
        LeaveStorageMode(); // the other account has a storage of its own
        Gallery.ClearSelectionCommand.Execute(null);
        SavePathChanged?.Invoke();

        if (savePathService.ResolveSaveDataPath() is not null)
        {
            // An auto-refresh of the previous account may still be running: it is waited for, then this one scans
            for (int attempt = 0; attempt < 5 && !ShutdownToken.IsCancellationRequested; attempt++)
                if (await RunScanCoreAsync(isUserInitiated: true)) break;
        }
        await RefreshAccountsAsync();
        await FindPlacedByHandAsync(); // the other account's storage, as it lies now
    }

    public async Task RefreshAccountsAsync()
    {
        int version = ++_accountsVersion;
        string? currentSavePath = savePathService.SavePath;
        List<AccountInfo> infos;
        try
        {
            infos = await Task.Run(() => _accountRegistry.Discover(currentSavePath));
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to list the game accounts", ex);
            return;
        }
        if (version != _accountsVersion) return; // a newer refresh is on its way

        _updatingAccounts = true;
        try
        {
            Accounts.Clear();
            foreach (var info in infos) Accounts.Add(AccountOption.From(info));
            SelectedAccount = Accounts.FirstOrDefault(a => AccountKey.SamePath(a.Info.SavePath, savePathService.SavePath));
        }
        finally
        {
            _updatingAccounts = false;
        }
        OnPropertyChanged(nameof(HasMultipleAccounts));
    }

    /// <summary>
    /// Remembers what the switcher shows for this account: its name (<see cref="AccountDisplayName"/>: from its
    /// liveries, vinyl groups, tunes and base liveries) and how many liveries it has; an account without a name shows a
    /// short ID.
    /// </summary>
    private void RecordCurrentAccount()
    {
        if (savePathService.SavePath is not { } savePath) return;

        ulong? userId = savePathService.CurrentUserId;
        // The liveries here, on the UI thread; the headers of the groups, tunes and base liveries are read off it
        var liveries = LiveryAuthors(GameEntries).ToList();
        string? dataPath = savePathService.ResolveSaveDataPath();
        var itemAuthors = savePathService.Profile.ReadItemAuthor is { } readHeader ? _itemAuthors ??= new(readHeader) : null;
        int liveryCount = GameEntries.Count;

        _ = RunSafelyAsync(async () =>
        {
            await Task.Run(() =>
            {
                string? displayName = userId is { } id
                    ? AccountNicknames.Current(id, liveries.Concat(ItemAuthors(itemAuthors?.Read(dataPath) ?? [])))
                    : null;
                _accountRegistry.Record(savePath, userId, displayName, liveryCount);
            });
            await RefreshAccountsAsync();
        }, nameof(RecordCurrentAccount));
    }

    /// <summary>
    /// The player's name the livery server read in the profile save (<see cref="LiveryServerConnection.PlayerNameRead"/>):
    /// from now on the account's name, before the one its liveries tell. Kept in the account's <c>account.json</c>, so
    /// it stays when the connection to the server is switched off (which forgets everything else the server told). Taken
    /// only for the save folder open now: a profile save of another folder is not this account's
    /// </summary>
    public void RecordSaveName(string profilePath, string name)
    {
        if (savePathService.SavePath is not { } savePath || !IsInFolder(profilePath, savePath)) return;
        _ = RunSafelyAsync(async () =>
        {
            if (await Task.Run(() => _accountRegistry.RecordSaveName(savePath, name))) await RefreshAccountsAsync();
        }, nameof(RecordSaveName));
    }

    internal static bool IsInFolder(string path, string folder)
    {
        try
        {
            string full = Path.GetFullPath(path);
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// The account's name from its items (<see cref="AccountInfo.DisplayName"/>; the save's own name, when the server
    /// read it, comes first: <see cref="AccountInfo.Name"/>), by the rule every app shares (<see cref="AccountNicknames"/>):
    /// the nickname of its newest livery, vinyl group, tune or base livery (by the date in the header; the game writes a
    /// base livery for each car it gets), so the first car or item after a rename of the gamertag names the account;
    /// without dates, the one most of them bear. Only an id that matches tells: an item without one (MINE as made on
    /// this PC) may be a file someone shared with the id cut out, and its nickname would become the account's. Null
    /// when the save's account is not known or none of its items is the account's
    /// </summary>
    /// <param name="entries">The save's liveries</param>
    /// <param name="userId">The save's account</param>
    /// <param name="items">The authors of the save's vinyl groups, tunes and base liveries (<see cref="AccountItemAuthors"/>)</param>
    internal static string? AccountDisplayName(IEnumerable<LiveryEntry> entries, ulong? userId, IEnumerable<SaveItemAuthor>? items = null) =>
        userId is not { } id ? null : AccountNicknames.Current(id, LiveryAuthors(entries).Concat(ItemAuthors(items ?? [])));

    /// <summary>
    /// Who made each livery and when: its header's date, else the first of its month (a cache written before kept no
    /// more). A livery whose author the scan does not know names no one
    /// </summary>
    private static IEnumerable<AuthoredItem> LiveryAuthors(IEnumerable<LiveryEntry> entries) => entries
        .Where(e => !e.Data.IsAuthorUnknown)
        .Select(e => new AuthoredItem(e.Data.CreatorUserId, e.Data.AuthorRaw, e.Data.CreatedOrMonth));

    /// <summary>Who made each vinyl group, tune and base livery and when</summary>
    private static IEnumerable<AuthoredItem> ItemAuthors(IEnumerable<SaveItemAuthor> items) =>
        items.Select(a => new AuthoredItem(a.UserId, a.Name, a.Created));
}
