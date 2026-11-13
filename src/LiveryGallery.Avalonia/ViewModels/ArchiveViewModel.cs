using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    public bool HasSelection => Rows.Any(r => r.IsSelected);
    public int SelectedCount => Rows.Count(r => r.IsSelected);
    public string SelectedCountText => string.Format(Strings.ArchiveSelectedCountFormat, SelectedCount);
    public event Action<string>? ErrorMessageRequested;
    public Func<string, Task<bool>>? ConfirmRestoreDuplicatesAsync { get; set; }
    public Func<string, Task<bool>>? ConfirmRestoreIntoCurrentAccountAsync { get; set; }
    /// <summary>Asked when the game is running; true to restore anyway</summary>
    public Func<Task<bool>>? ConfirmWhileGameRunningAsync { get; set; }
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
        foreach (var row in _allRows) row.PropertyChanged -= OnRowPropertyChanged;
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
            var row = new ArchivedLiveryRowViewModel(entry, _accounts, SearchByFolderName);
            row.PropertyChanged += OnRowPropertyChanged;
            _allRows.Add(row);
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

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ArchivedLiveryRowViewModel.IsSelected)) RaiseSelectionChanged();
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
        var selected = Rows.Where(r => r.IsSelected).ToList();
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
    private void SelectAll()
    {
        foreach (var row in Rows) row.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in Rows) row.IsSelected = false;
    }

    [RelayCommand]
    private async Task RestoreSelectedAsync()
    {
        var selected = Rows.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        if (await LiveryRestoreConflicts.PrepareAsync(_savePathService, _accounts, selected.Select(r => r.Entry.SourceAccount),
                ConfirmRestoreIntoCurrentAccountAsync, ConfirmWhileGameRunningAsync, ErrorMessageRequested) is not { } savePath)
            return;

        try
        {
            var plan = await LiveryRestoreConflicts.PlanAsync(
                [.. selected.Select(r => r.Entry.Data)], _getCurrentEntries(), savePath, _savePathService.Profile.Liveries,
                ConfirmRestoreDuplicatesAsync);

            var result = plan.ToRestore.Count > 0
                ? await _archiveService.RestoreAsync(plan.ToRestore.Select(d => d.FolderName), savePath)
                : ArchiveOperationResult.Empty;
            var restoredSet = result.Completed.ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (restoredSet.Count > 0)
            {
                Reload();
                await _onRestored();
            }

            string? report = plan.Report(restoredSet);
            if (report is not null) ErrorMessageRequested?.Invoke(report);
            if (result.LeftoverPaths.Count > 0)
                ErrorMessageRequested?.Invoke(string.Format(Strings.ArchiveLeftoverFoldersFormat, string.Join("\n", result.LeftoverPaths)));
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to restore from archive", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
        }
    }

    [RelayCommand]
    private void RequestDelete()
    {
        var selected = Rows.Where(r => r.IsSelected).ToList();
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
