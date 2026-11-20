using Avalonia.Controls;
using Avalonia.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

internal partial class BackupDetailDialog : Window
{
    private readonly BackupDetailViewModel _viewModel;
    private readonly DialogWorkScope _work;

    public BackupDetailDialog(
        LiveryBackups backupService, BackupPreviews previews, SavePathService savePathService, BackupRowViewModel backup,
        Func<IReadOnlyList<LiveryData>> getCurrentEntries, Func<Task> onRestored, StorageBackupContext? storage = null)
    {
        InitializeComponent();
        _viewModel = new BackupDetailViewModel(backupService, previews, savePathService, backup, getCurrentEntries, onRestored, storage);
        DataContext = _viewModel;
        _work = new DialogWorkScope(this, () => _viewModel.RestoreSelectedCommand.ExecutionTask);

        _viewModel.ErrorMessageRequested += message => _work.Run(
            () => InfoDialog.ShowAsync(this, Strings.BackupsDialogTitle, message), "backup detail: error message");
        _viewModel.Ask.AskOver(this, savePathService.Profile.Game.Name);

        string title = string.Format(Strings.BackupDetailTitleFormat, backup.DateText);
        Title = title;
        Bar.Title = title;
        RestoreButton.Content = Strings.ArchiveRestoreButton;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.CancelBackgroundWork();
        base.OnClosed(e);
    }

}
