using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;
using System.Collections.ObjectModel;

namespace LiveryGallery.ViewModels;

/// <summary>
/// The account's storage (docs/storage.md, stage 6), for its backups: they are kept with the save's, compared with the
/// storage and restored into it
/// </summary>
/// <param name="Store">The storage</param>
/// <param name="Entries">Its liveries now</param>
/// <param name="OnRestored">After liveries came back into it</param>
internal sealed record StorageBackupContext(LiveryArchive Store, Func<IReadOnlyList<LiveryData>> Entries, Func<Task> OnRestored);

internal sealed partial class BackupsViewModel : ObservableObject
{
    private readonly LiveryBackups _backupService;
    private readonly BackupPreviews _previews;
    private readonly SavePathService _savePathService;
    private readonly Func<IReadOnlyList<LiveryData>> _getCurrentEntries;
    private readonly AccountContext _accounts;
    private readonly Func<int>? _unassignedCount;
    private readonly Func<Task<int>>? _takeUnassigned;
    private readonly StorageBackupContext? _storage;

    /// <summary>The account has a storage: it can be backed up too</summary>
    public bool HasStorage => _storage is not null;

    public ObservableCollection<BackupRowViewModel> Backups { get; } = [];
    public bool ShowEmptyState => !_isLoading && Backups.Count == 0;

    private List<BackupRowViewModel> _allRows = [];

    /// <summary>On by default: another account's backups are only shown when asked for</summary>
    [ObservableProperty]
    private bool _onlyCurrentAccount = true;

    public bool CanFilterByAccount => _allRows.Any(r => r.IsHiddenByAccountFilter);
    public int HiddenByAccountCount => OnlyCurrentAccount ? _allRows.Count(r => r.IsHiddenByAccountFilter) : 0;
    public bool HasHiddenByAccount => HiddenByAccountCount > 0;
    public string HiddenByAccountText => string.Format(Strings.AccountFilterHiddenFormat, HiddenByAccountCount);

    partial void OnOnlyCurrentAccountChanged(bool value) => ApplyAccountFilter();

    /// <summary>
    /// Backups of unknown origin (made before accounts were recorded) and of accounts that are gone are always shown
    /// </summary>
    private void ApplyAccountFilter()
    {
        Backups.Clear();
        foreach (var row in _allRows)
            if (!OnlyCurrentAccount || !row.IsHiddenByAccountFilter) Backups.Add(row);

        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CanFilterByAccount));
        OnPropertyChanged(nameof(HiddenByAccountCount));
        OnPropertyChanged(nameof(HasHiddenByAccount));
        OnPropertyChanged(nameof(HiddenByAccountText));
    }

    private bool _isLoading;
    private int _reloadVersion;
    private readonly CancellationTokenSource _lifetimeCts = new();

    [ObservableProperty]
    private bool _isCreatingBackup;
    public event Action<string>? ErrorMessageRequested;
    public event Action<BackupRowViewModel>? OpenRequested;
    public event Action<BackupRowViewModel>? DeleteRequested;

    public BackupsViewModel(
        LiveryBackups backupService, BackupPreviews previews, SavePathService savePathService,
        Func<IReadOnlyList<LiveryData>> getCurrentEntries, AccountContext accounts,
        Func<int>? unassignedCount = null, Func<Task<int>>? takeUnassigned = null, StorageBackupContext? storage = null)
    {
        _storage = storage;
        _accounts = accounts;
        _unassignedCount = unassignedCount;
        _takeUnassigned = takeUnassigned;
        _previews = previews;
        _backupService = backupService;
        _savePathService = savePathService;
        _getCurrentEntries = getCurrentEntries;
        StartReload();
    }

    public void Close() => _lifetimeCts.Cancel();
    private void StartReload() => _ = ReloadAsync();

    private async Task ReloadAsync()
    {
        var ct = _lifetimeCts.Token;
        if (ct.IsCancellationRequested) return;
        int version = ++_reloadVersion;
        _isLoading = true;
        OnPropertyChanged(nameof(ShowEmptyState));

        var currentEntries = _getCurrentEntries();
        var storedEntries = _storage?.Entries() ?? [];
        List<BackupRowViewModel> rows;
        try
        {
            rows = await Task.Run(() =>
            {
                var backups = _backupService.GetAll();
                ct.ThrowIfCancellationRequested();
                _previews.Prune(backups);
                // A backup of the storage is compared with the storage, one of the save with the save
                return backups.Select(summary => new BackupRowViewModel(summary,
                    summary.Manifest?.IsOfArchive == true ? storedEntries : currentEntries, _accounts)).ToList();
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return; // dialog closed
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to list backups", ex);
            rows = [];
        }

        if (ct.IsCancellationRequested || version != _reloadVersion) return; // closed or a newer reload is in progress

        _allRows = rows;
        _isLoading = false;
        UnassignedCount = _unassignedCount?.Invoke() ?? 0;
        OnPropertyChanged(nameof(UnassignedCount));
        OnPropertyChanged(nameof(HasUnassigned));
        OnPropertyChanged(nameof(UnassignedText));
        ApplyAccountFilter();
    }

    /// <summary>
    /// Backups an earlier version made without recording the account, while it knew more than one: kept apart
    /// (<see cref="Configuration.GamePaths.Unassigned"/>) until put with an account
    /// </summary>
    public int UnassignedCount { get; private set; }
    public bool HasUnassigned => UnassignedCount > 0;
    public string UnassignedText => string.Format(Strings.UnassignedCountFormat, UnassignedCount);

    /// <summary>Puts the backups without an account with this account; the user says they are its own</summary>
    [RelayCommand]
    private async Task TakeUnassignedAsync()
    {
        if (_takeUnassigned is null) return;
        try
        {
            await _takeUnassigned();
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to move backups without an account", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
        }
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        string? savePath = _savePathService.ResolveSaveDataPath();
        if (savePath is null)
        {
            ErrorMessageRequested?.Invoke(Strings.ArchiveNoSavePathMessage);
            return;
        }

        var entries = _getCurrentEntries();
        if (entries.Count == 0)
        {
            ErrorMessageRequested?.Invoke(Strings.BackupNothingToBackUpMessage);
            return;
        }

        IsCreatingBackup = true;
        try
        {
            await _backupService.CreateAsync(entries, savePath, _accounts.Current);
            await ReloadAsync();
        }
        catch (OperationCanceledException) when (_backupService.IsClosing)
        {
            // The app is closing. The unfinished backup file was removed, nothing to report
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to create backup", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
        }
        finally
        {
            IsCreatingBackup = false;
        }
    }

    /// <summary>A backup of the whole storage, with its folders</summary>
    [RelayCommand]
    private async Task CreateStorageBackupAsync()
    {
        if (_storage is null) return;
        if (_storage.Store.GetAll().Count == 0)
        {
            ErrorMessageRequested?.Invoke(Strings.BackupNoStorageMessage);
            return;
        }
        IsCreatingBackup = true;
        try
        {
            await _backupService.CreateFromArchiveAsync(_storage.Store, null, _accounts.Current, LiveryBackupKind.Manual);
            await ReloadAsync();
        }
        catch (OperationCanceledException) when (_backupService.IsClosing)
        {
            // The app is closing: the unfinished file was removed
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to back up the storage", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
        }
        finally
        {
            IsCreatingBackup = false;
        }
    }

    [RelayCommand]
    private void Open(BackupRowViewModel row) => OpenRequested?.Invoke(row);

    [RelayCommand]
    private void RequestDelete(BackupRowViewModel row) => DeleteRequested?.Invoke(row);

    public void DeleteConfirmed(BackupRowViewModel row)
    {
        if (LiveryBackups.TryDelete(row.Path)) StartReload();
    }
}
