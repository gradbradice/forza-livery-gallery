using Avalonia.Controls;
using Avalonia.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

internal partial class ArchiveDialog : Window
{
    private readonly ArchiveViewModel _viewModel;
    private readonly DialogWorkScope _work;

    public ArchiveDialog(
        LiveryArchive archiveService, SavePathService savePathService,
        Func<IReadOnlyList<LiveryData>> getCurrentEntries, Func<Task> onRestored, AccountContext accounts,
        bool searchByFolderName = false, LiveryArchive? unassigned = null, Services.Screenshots.ScreenshotStore? screenshots = null,
        LiveryArchive? storage = null)
    {
        InitializeComponent();
        _screenshots = screenshots;
        _viewModel = new ArchiveViewModel(archiveService, savePathService, getCurrentEntries, onRestored, accounts, searchByFolderName, unassigned,
            storage);
        DataContext = _viewModel;
        _work = new DialogWorkScope(this, () => _viewModel.RestoreSelectedCommand.ExecutionTask);

        _viewModel.ErrorMessageRequested += message => _work.Run(
            () => InfoDialog.ShowAsync(this, Strings.ArchiveDialogTitle, message), "archive: error message");
        _viewModel.DeleteRequested += rows => _work.Run(() => ConfirmDeleteAsync(rows), "archive: delete");
        _viewModel.Ask.AskOver(this, savePathService.Profile.Game.Name);

        Title = Strings.ArchiveDialogTitle;
        Bar.Title = Strings.ArchiveDialogTitle;
        TitleText.Text = Strings.ArchiveDialogTitle;
        RestoreButton.Content = Strings.ArchiveRestoreButton;
        ToStorageButton.Content = Strings.ArchiveToStorageButton;
        ToolTip.SetTip(ToStorageButton, Strings.ArchiveToStorageTooltip);
        DeleteButton.Content = Strings.ArchiveDeleteButton;
        TakeUnassignedButton.Content = Strings.UnassignedTakeButton;
        ToolTip.SetTip(TakeUnassignedButton, Strings.ArchiveUnassignedTooltip);
    }

    private readonly Services.Screenshots.ScreenshotStore? _screenshots;

    private async Task ConfirmDeleteAsync(List<ArchivedLiveryRowViewModel> rows)
    {
        string names = string.Join(", ", rows.Select(r => r.LiveryName));
        bool confirmed = await ConfirmDialog.AskAsync(
            this, Strings.ArchiveDeleteConfirmTitle, string.Format(Strings.ArchiveDeleteConfirmMessage, names));
        if (!confirmed) return;

        // The user's screenshots of these liveries: asked about apart, they may want to keep the pictures
        var keys = rows.Select(r => r.FolderName).ToList();
        int screenshots = _screenshots is { } store ? keys.Sum(k => store.Summary(k).Count) : 0;
        bool deleteScreenshots = screenshots > 0 && await ConfirmDialog.AskAsync(
            this, Strings.ArchiveDeleteConfirmTitle, string.Format(Strings.ScreenshotsDeleteWithLiveryFormat, screenshots));

        await _viewModel.DeleteConfirmedAsync(rows);
        if (deleteScreenshots)
            foreach (string key in keys) _screenshots!.DeleteAll(key);
    }

}
