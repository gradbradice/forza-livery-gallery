using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LiveryGallery.ViewModels;

internal sealed partial class BackupDetailViewModel : ObservableObject
{
    private readonly LiveryBackups _backupService;
    private readonly BackupPreviews _previews;
    private readonly SavePathService _savePathService;
    private readonly string _backupPath;
    private readonly Func<Task> _onRestored;
    private readonly Func<IReadOnlyList<LiveryData>> _getCurrentEntries;
    private readonly AccountContext _accounts;
    private readonly AccountRef? _sourceAccount;
    private readonly CancellationTokenSource _previewCts = new();
    // A backup of the storage: restored into it, not into the save
    private readonly StorageBackupContext? _storage;

    /// <summary>A backup of the storage: its liveries go back into the storage, each into its folder</summary>
    public bool IsStorageBackup => _storage is not null;
    private Task _previewGeneration = Task.CompletedTask;

    public string DateText { get; }
    public string SizeText { get; }

    public ObservableCollection<BackupLiveryRowViewModel> RemovedRows { get; } = [];
    public ObservableCollection<BackupLiveryRowViewModel> AddedEntries { get; } = [];

    public bool ShowNoRemovedHint => RemovedRows.Count == 0;
    public bool ShowNoAddedHint => AddedEntries.Count == 0;
    public bool HasSelection => RemovedRows.Any(r => r.IsSelected);
    public string SelectedCountText => string.Format(Strings.ArchiveSelectedCountFormat, RemovedRows.Count(r => r.IsSelected));

    /// <summary>Made on another account: the tabs compare it with the open account, and restoring puts liveries here</summary>
    public bool IsFromOtherAccount { get; }
    public string OtherAccountNote { get; } = "";

    public event Action<string>? ErrorMessageRequested;
    public Func<string, Task<bool>>? ConfirmRestoreDuplicatesAsync { get; set; }
    public Func<string, Task<bool>>? ConfirmRestoreIntoCurrentAccountAsync { get; set; }
    /// <summary>Asked when the game is running; true to restore anyway</summary>
    public Func<Task<bool>>? ConfirmWhileGameRunningAsync { get; set; }

    public BackupDetailViewModel(
        LiveryBackups backupService, BackupPreviews previews, SavePathService savePathService, BackupRowViewModel backup,
        Func<IReadOnlyList<LiveryData>> getCurrentEntries, Func<Task> onRestored, StorageBackupContext? storage = null)
    {
        if (backup.IsStorage && storage is not null)
        {
            _storage = storage;
            getCurrentEntries = storage.Entries;
            onRestored = storage.OnRestored;
        }
        _backupService = backupService;
        _previews = previews;
        _savePathService = savePathService;
        _backupPath = backup.Path;
        _onRestored = onRestored;
        _getCurrentEntries = getCurrentEntries;
        DateText = backup.DateText;
        SizeText = backup.SizeText;
        _accounts = backup.Accounts;
        _sourceAccount = backup.Manifest!.SourceAccount;
        IsFromOtherAccount = backup.IsFromOtherAccount;
        if (IsFromOtherAccount)
            OtherAccountNote = string.Format(Strings.BackupOtherAccountNote, _accounts.Title(_sourceAccount!), _accounts.CurrentTitle);

        var (added, removed) = LiveryBackups.ComputeDiff(backup.Manifest!, getCurrentEntries());

        foreach (var data in added.OrderBy(d => d.DisplayLiveryName, StringComparer.OrdinalIgnoreCase))
            AddedEntries.Add(BackupLiveryRowViewModel.FromCurrent(data));

        foreach (var data in removed.OrderBy(d => d.DisplayLiveryName, StringComparer.OrdinalIgnoreCase))
        {
            var row = BackupLiveryRowViewModel.FromBackup(data, _previews);
            row.PropertyChanged += OnRowPropertyChanged;
            RemovedRows.Add(row);
        }

        GenerateMissingPreviews();
    }

    public Task CancelBackgroundWork()
    {
        _previewCts.Cancel();
        return _previewGeneration;
    }

    private void GenerateMissingPreviews()
    {
        var missing = RemovedRows.Where(r => r.NeedsPreview).Select(r => r.Data).ToList();
        if (missing.Count == 0) return;

        var progress = new Progress<(string FolderName, string PreviewPath)>(generated =>
        {
            if (_previewCts.IsCancellationRequested) return;
            ReplaceRowPreview(generated.FolderName, generated.PreviewPath);
        });
        _previewGeneration = GenerateMissingPreviewsSafelyAsync(missing, progress);
    }

    private async Task GenerateMissingPreviewsSafelyAsync(
        List<LiveryData> missing, IProgress<(string FolderName, string PreviewPath)> progress)
    {
        try
        {
            await _previews.GenerateMissingAsync(
                _backupPath, missing, _savePathService.Profile.PreviewFileNames, progress, _previewCts.Token);
        }
        catch (OperationCanceledException) when (_previewCts.IsCancellationRequested)
        {
            // The dialog was closed. The remaining previews are simply not needed any more
        }
        catch (Exception ex)
        {
            // Rows without a preview keep their placeholder
            AppLog.LogError($"Failed to generate previews from backup '{_backupPath}'", ex);
        }
    }

    private void ReplaceRowPreview(string folderName, string previewPath)
    {
        for (int i = 0; i < RemovedRows.Count; i++)
        {
            var old = RemovedRows[i];
            if (!string.Equals(old.FolderName, folderName, StringComparison.OrdinalIgnoreCase)) continue;

            var replacement = BackupLiveryRowViewModel.FromBackup(old.Data, _previews, previewPath);
            replacement.IsSelected = old.IsSelected;
            old.PropertyChanged -= OnRowPropertyChanged;
            replacement.PropertyChanged += OnRowPropertyChanged;
            RemovedRows[i] = replacement;
            return;
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BackupLiveryRowViewModel.IsSelected)) RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedCountText));
    }

    /// <summary>Into the storage: no save, no game to ask about; only another account's liveries are asked about</summary>
    private async Task RestoreIntoStorageAsync(StorageBackupContext storage, List<BackupLiveryRowViewModel> selected)
    {
        if (_accounts.BuildRestoreIntoCurrentPrompt(selected.Select(_ => _sourceAccount)) is { } prompt
            && ConfirmRestoreIntoCurrentAccountAsync is { } ask && !await ask(prompt))
            return;
        try
        {
            var restored = (await _backupService.RestoreIntoArchiveAsync(_backupPath, selected.Select(r => r.FolderName), storage.Store))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (restored.Count > 0)
            {
                foreach (var row in RemovedRows.Where(r => restored.Contains(r.FolderName)).ToList())
                {
                    row.PropertyChanged -= OnRowPropertyChanged;
                    RemovedRows.Remove(row);
                }
                OnPropertyChanged(nameof(ShowNoRemovedHint));
                RaiseSelectionChanged();
                await storage.OnRestored();
            }
            var failed = selected.Where(r => !restored.Contains(r.FolderName)).Select(r => r.Data).ToList();
            if (LiveryRestoreConflicts.BuildReport([], failed) is { } report) ErrorMessageRequested?.Invoke(report);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to restore from a backup of the storage", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in RemovedRows) row.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in RemovedRows) row.IsSelected = false;
    }

    [RelayCommand]
    private async Task RestoreSelectedAsync()
    {
        var selected = RemovedRows.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;
        if (_storage is not null)
        {
            await RestoreIntoStorageAsync(_storage, selected);
            return;
        }

        if (await LiveryRestoreConflicts.PrepareAsync(_savePathService, _accounts, selected.Select(_ => _sourceAccount),
                ConfirmRestoreIntoCurrentAccountAsync, ConfirmWhileGameRunningAsync, ErrorMessageRequested) is not { } savePath)
            return;

        try
        {
            var plan = await LiveryRestoreConflicts.PlanAsync(
                [.. selected.Select(r => r.Data)], _getCurrentEntries(), savePath, _savePathService.Profile.Liveries,
                ConfirmRestoreDuplicatesAsync);

            IReadOnlyList<string> restored = plan.ToRestore.Count > 0
                ? await _backupService.RestoreAsync(_backupPath, plan.ToRestore.Select(d => d.FolderName), savePath)
                : [];
            var restoredSet = restored.ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (restoredSet.Count > 0)
            {
                foreach (var row in RemovedRows.Where(r => restoredSet.Contains(r.FolderName)).ToList())
                {
                    row.PropertyChanged -= OnRowPropertyChanged;
                    RemovedRows.Remove(row);
                }
                OnPropertyChanged(nameof(ShowNoRemovedHint));
                RaiseSelectionChanged();

                await _onRestored();
            }

            string? report = plan.Report(restoredSet);
            if (report is not null) ErrorMessageRequested?.Invoke(report);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to restore from backup", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
        }
    }
}
