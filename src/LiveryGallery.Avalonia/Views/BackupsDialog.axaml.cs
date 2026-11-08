using Avalonia.Controls;
using Avalonia.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

internal partial class BackupsDialog : Window
{
    private readonly LiveryBackups _backupService;
    private readonly BackupPreviews _previews;
    private readonly SavePathService _savePathService;
    private readonly Func<IReadOnlyList<LiveryData>> _getCurrentEntries;
    private readonly Func<Task> _onRestored;
    private readonly StorageBackupContext? _storage;
    private readonly BackupsViewModel _viewModel;
    private readonly DialogWorkScope _work;

    public BackupsDialog(
        LiveryBackups backupService, BackupPreviews previews, SavePathService savePathService,
        Func<IReadOnlyList<LiveryData>> getCurrentEntries, Func<Task> onRestored, AccountContext accounts,
        Func<int>? unassignedCount = null, Func<Task<int>>? takeUnassigned = null, StorageBackupContext? storage = null)
    {
        InitializeComponent();
        _storage = storage;
        _backupService = backupService;
        _previews = previews;
        _savePathService = savePathService;
        _getCurrentEntries = getCurrentEntries;
        _onRestored = onRestored;
        _viewModel = new BackupsViewModel(backupService, previews, savePathService, getCurrentEntries, accounts, unassignedCount, takeUnassigned, storage);
        DataContext = _viewModel;
        _work = new DialogWorkScope(this, () => _viewModel.CreateBackupCommand.ExecutionTask ?? _viewModel.CreateStorageBackupCommand.ExecutionTask);

        _viewModel.ErrorMessageRequested += message => _work.Run(
            () => InfoDialog.ShowAsync(this, Strings.BackupsDialogTitle, message), "backups: error message");
        _viewModel.OpenRequested += row => _work.Run(() => OpenDetailAsync(row), "backups: open detail");
        _viewModel.DeleteRequested += row => _work.Run(() => ConfirmDeleteAsync(row), "backups: delete");

        Title = Strings.BackupsDialogTitle;
        TitleBarText.Text = Strings.BackupsDialogTitle;
        TitleText.Text = Strings.BackupsDialogTitle;
        CreateBackupButton.Content = Strings.BackupCreateButton;
        TakeUnassignedButton.Content = Strings.UnassignedTakeButton;
        ToolTip.SetTip(TakeUnassignedButton, Strings.BackupsUnassignedTooltip);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Close();
        base.OnClosed(e);
    }

    private async Task OpenDetailAsync(BackupRowViewModel row)
    {
        if (row.Manifest is null)
        {
            await InfoDialog.ShowAsync(this, Strings.BackupsDialogTitle, Strings.BackupUnreadableMessage);
            return;
        }

        var dialog = new BackupDetailDialog(
            _backupService, _previews, _savePathService, row, _getCurrentEntries, _onRestored, _storage);
        await dialog.ShowDialog(this);
    }

    private async Task ConfirmDeleteAsync(BackupRowViewModel row)
    {
        bool confirmed = await ConfirmDialog.AskAsync(
            this, Strings.BackupDeleteConfirmTitle,
            string.Format(Strings.BackupDeleteConfirmMessage, row.DateText, row.SizeText));
        if (!confirmed) return;

        _viewModel.DeleteConfirmed(row);
    }
}
