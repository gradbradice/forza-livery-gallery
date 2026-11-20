using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;
using System.Collections.ObjectModel;
using ForzaToolkit.Core.Text;

namespace LiveryGallery.ViewModels;

internal sealed partial class ArchiveViewModel : ObservableObject
{
    private readonly LiveryArchive _archiveService;
    private readonly SavePathService _savePathService;
    private readonly Func<Task> _onRestored;
    private readonly Func<IReadOnlyList<LiveryData>> _getCurrentEntries;
    private readonly AccountContext _accounts;
    private readonly LiveryArchive? _unassigned;
    // The account's storage: an archived livery can go there to be kept for good (docs/storage.md)
    private readonly LiveryArchive? _storage;

    /// <summary>"Into the storage" is offered: the account has one</summary>
    public bool HasStorage => _storage is not null;

    // Every row of the archive (after the account filter); Rows are the ones the search leaves
    private readonly List<ArchivedLiveryRowViewModel> _allRows = [];

    public ObservableCollection<ArchivedLiveryRowViewModel> Rows { get; } = [];

    // The ticked rows of those shown
    private readonly CheckedRows<ArchivedLiveryRowViewModel> _checked;
    public bool ShowEmptyState => Rows.Count == 0;

    /// <summary>The archive (after the account filter) has liveries: the search box is shown</summary>
    public bool HasAnyRows => _allRows.Count > 0;

    /// <summary>"Nothing found" when the search hides every livery, else "the archive is empty"</summary>
    public string EmptyStateText => HasAnyRows ? Strings.ArchiveNoSearchResultsHint : Strings.ArchiveEmptyHint;

    /// <summary>The setting "show folder names and search by them"</summary>
    public bool SearchByFolderName { get; }

    public string SearchPlaceholder => SearchByFolderName ? Strings.ArchiveSearchPlaceholderWithFolder : Strings.ArchiveSearchPlaceholder;

    /// <summary>Words to look for, all of them, in the car, year, name, author, number (and folder name)</summary>
    [ObservableProperty]
    private string _searchText = "";

    partial void OnSearchTextChanged(string value) => ApplySearch();

    /// <summary>On by default: another account's liveries are only shown when asked for</summary>
    [ObservableProperty]
    private bool _onlyCurrentAccount = true;

    /// <summary>The filter is only offered when the archive has liveries of another account at all</summary>
    public bool CanFilterByAccount { get; private set; }
    public int HiddenByAccountCount { get; private set; }
    public bool HasHiddenByAccount => HiddenByAccountCount > 0;
    public string HiddenByAccountText => string.Format(Strings.AccountFilterHiddenFormat, HiddenByAccountCount);

    partial void OnOnlyCurrentAccountChanged(bool value) => Reload();
    public bool HasSelection => _checked.Any;
    public int SelectedCount => _checked.Count;
    public string SelectedCountText => _checked.CountText;
    public event Action<string>? ErrorMessageRequested;

    /// <summary>What a restore asks (a copy in the game, another account, the game running), asked by the window</summary>
    public RestoreQuestions Ask { get; } = new();
    public event Action<List<ArchivedLiveryRowViewModel>>? DeleteRequested;

    public ArchiveViewModel(
        LiveryArchive archiveService, SavePathService savePathService,
        Func<IReadOnlyList<LiveryData>> getCurrentEntries, Func<Task> onRestored, AccountContext accounts,
        bool searchByFolderName = false, LiveryArchive? unassigned = null, LiveryArchive? storage = null)
    {
        SearchByFolderName = searchByFolderName;
        _storage = storage;
        _unassigned = unassigned;
        _accounts = accounts;
        _archiveService = archiveService;
        _savePathService = savePathService;
        _getCurrentEntries = getCurrentEntries;
        _onRestored = onRestored;
        _checked = new CheckedRows<ArchivedLiveryRowViewModel>(Rows, RaiseSelectionChanged);
        Reload();
    }

    /// <summary>
    /// Liveries an earlier version archived without recording the account, while it knew more than one: they are in
    /// an archive of their own (<see cref="Configuration.GamePaths.Unassigned"/>) until put with an account
    /// </summary>
    public int UnassignedCount { get; private set; }
    public bool HasUnassigned => UnassignedCount > 0;
    public string UnassignedText => string.Format(Strings.UnassignedCountFormat, UnassignedCount);

    /// <summary>Puts the liveries without an account into this account's archive; the user says they are its own</summary>
    [RelayCommand]
    private async Task TakeUnassignedAsync()
    {
        if (_unassigned is null) return;
        try
        {
            await _archiveService.TakeFromAsync(_unassigned, [.. _unassigned.GetAll().Select(e => e.Data.FolderName)], _accounts.Current);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to move archived liveries without an account", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
        }
        Reload();
    }

    private void Reload()
    {
        _allRows.Clear();
        UnassignedCount = _unassigned?.GetAll().Count ?? 0;
        OnPropertyChanged(nameof(UnassignedCount));
        OnPropertyChanged(nameof(HasUnassigned));
        OnPropertyChanged(nameof(UnassignedText));

        // Liveries of unknown origin (archived before accounts were recorded) and of accounts that are gone are always shown
        var all = _archiveService.GetAll();
        int fromOtherAccounts = all.Count(e => _accounts.IsHiddenByAccountFilter(e.SourceAccount));
        CanFilterByAccount = fromOtherAccounts > 0;
        HiddenByAccountCount = OnlyCurrentAccount ? fromOtherAccounts : 0;
        OnPropertyChanged(nameof(CanFilterByAccount));
        OnPropertyChanged(nameof(HiddenByAccountCount));
        OnPropertyChanged(nameof(HasHiddenByAccount));
        OnPropertyChanged(nameof(HiddenByAccountText));

        foreach (var entry in all)
        {
            if (OnlyCurrentAccount && _accounts.IsHiddenByAccountFilter(entry.SourceAccount)) continue;
            _allRows.Add(new ArchivedLiveryRowViewModel(entry, _accounts, SearchByFolderName));
        }

        ApplySearch();
    }

    // Shows the rows the search leaves. A row the search hides is unselected: restore and delete act on what is seen
    private void ApplySearch()
    {
        string[] tokens = SearchWords.Split(SearchText);
        Rows.Clear();
        foreach (var row in _allRows)
        {
            if (tokens.Length == 0 || row.MatchesSearch(tokens)) Rows.Add(row);
            else row.IsSelected = false;
        }

        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasAnyRows));
        OnPropertyChanged(nameof(EmptyStateText));
        RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountText));
    }

    /// <summary>
    /// The selected liveries into the account's storage, kept for good: a move between two folders on the same disk,
    /// the save is not touched. The screenshots, notes and tags stay with the key
    /// </summary>
    [RelayCommand]
    private async Task MoveSelectedToStorageAsync()
    {
        var selected = _checked.Selected;
        if (_storage is null || selected.Count == 0) return;
        try
        {
            // The keys the storage has already: those stay in the archive for that, the others only when their files did not move
            var inStorage = _storage.GetAll().Select(e => e.Data.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var moved = await _storage.TakeFromAsync(_archiveService, [.. selected.Select(r => r.Entry.Data.FolderName)], _accounts.Current);
            Reload();
            if (MainViewModel.NotTakenReport([.. selected.Select(r => r.Entry.Data)], moved, inStorage,
                    Strings.ArchiveToStorageSkippedFormat, Strings.StoreFailedFormat) is { } report)
                ErrorMessageRequested?.Invoke(report);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to move archived liveries into the storage", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
        }
    }

    [RelayCommand]
    private void SelectAll() => _checked.SelectAll();

    [RelayCommand]
    private void ClearSelection() => _checked.Clear();

    [RelayCommand]
    private async Task RestoreSelectedAsync()
    {
        IReadOnlyList<string> leftover = [];
        await LiveryRestoreConflicts.RestoreToSaveAsync(_savePathService, _accounts,
            [.. _checked.Selected.Select(r => (r.Entry.Data, r.Entry.SourceAccount))], _getCurrentEntries(), Ask,
            async (keys, savePath) =>
            {
                var result = await _archiveService.RestoreAsync(keys, savePath);
                leftover = result.LeftoverPaths;
                return result.Completed;
            },
            async _ =>
            {
                Reload();
                await _onRestored();
            },
            ErrorMessageRequested, "from the archive");
        if (leftover.Count > 0)
            ErrorMessageRequested?.Invoke(string.Format(Strings.ArchiveLeftoverFoldersFormat, string.Join("\n", leftover)));
    }

    [RelayCommand]
    private void RequestDelete()
    {
        var selected = _checked.Selected.ToList();
        if (selected.Count == 0) return;
        DeleteRequested?.Invoke(selected);
    }

    public async Task DeleteConfirmedAsync(List<ArchivedLiveryRowViewModel> rows)
    {
        var folderNames = rows.Select(r => r.FolderName).ToList();
        try
        {
            await _archiveService.DeletePermanentlyAsync(folderNames);
            Reload();
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to delete from archive", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
        }
    }
}
