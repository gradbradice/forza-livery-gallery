using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>
/// "Storage folder": where an account keeps its storage. The move runs in the window, with its progress; "Cancel"
/// stops it (the storage stays where it was, whole). Closes with true once the storage is in its new folder
/// </summary>
internal partial class StorageFolderDialog : Window
{
    /// <summary>Moves the storage: the folder (null: the program's), the common folder chosen, the progress, the stop</summary>
    public delegate Task<StorageMoveOutcome> MoveStorage(string? target, string? commonFolder,
        IProgress<LiveryArchiveMoveProgress> progress, CancellationToken ct);

    private readonly StorageFolderViewModel _viewModel;
    private readonly MoveStorage? _move;
    private CancellationTokenSource? _moving;
    private bool _done;
    private bool _closeAsked;

    public StorageFolderDialog() // the designer
    {
        InitializeComponent();
        _viewModel = null!;
    }

    public StorageFolderDialog(StorageFolderViewModel viewModel, MoveStorage move)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _move = move;
        DataContext = viewModel;
        Title = Strings.StorageFolderDialogTitle;
        TitleBarText.Text = Strings.StorageFolderDialogTitle;
        ProgramOptionText.Text = Strings.StorageOptionProgram;
        CommonOptionText.Text = Strings.StorageOptionCommon;
        RecommendedText.Text = Strings.StorageOptionRecommended;
        ExactOptionText.Text = Strings.StorageOptionExact;
        ExactHintText.Text = Strings.StorageOptionExactHint;
        NotSuitableRun.Text = Strings.StorageNotSuitable;
        CancelButton.Content = Strings.ButtonCancel;
        Closing += (_, e) =>
        {
            // A move in progress is stopped first: it undoes itself, then the window may close
            if (_moving is null) return;
            e.Cancel = true;
            _closeAsked = true;
            _moving.Cancel();
        };
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close(_done);

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_moving is { } moving) moving.Cancel();
        else Close(_done);
    }

    private async void BrowseCommon_Click(object? sender, RoutedEventArgs e)
    {
        if (await FolderPicker.PickAsync(this, Strings.StorageFolderPickerTitle) is { } path) _viewModel.CommonFolder = path;
    }

    private async void BrowseExact_Click(object? sender, RoutedEventArgs e)
    {
        if (await FolderPicker.PickAsync(this, Strings.StorageFolderPickerTitle) is { } path) _viewModel.ExactFolder = path;
    }

    private async void ApplyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_move is null || !_viewModel.CanApply || _moving is not null) return;
        string? target = _viewModel.IsProgramChosen ? null : _viewModel.Target;
        using var moving = new CancellationTokenSource();
        _moving = moving;
        _viewModel.MoveStarted();
        CancelButton.Content = Strings.ButtonCancel;
        StorageMoveOutcome outcome;
        try
        {
            outcome = await _move(target, _viewModel.ChosenCommonFolder, new Progress<LiveryArchiveMoveProgress>(_viewModel.Report), moving.Token);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to move the storage", ex);
            outcome = new StorageMoveOutcome(false, ex.Message, []);
        }
        finally
        {
            _moving = null;
            _viewModel.MoveEnded();
        }

        if (!outcome.Succeeded && _closeAsked)
        {
            Close(_done);
            return;
        }
        if (!outcome.Succeeded)
        {
            await InfoDialog.ShowAsync(this, Strings.StorageFolderDialogTitle, outcome.Error is { } error
                ? string.Format(Strings.StorageMoveFailedFormat, error)
                : Strings.StorageMoveCancelled);
            return;
        }
        _done = true;
        if (outcome.Leftovers.Count > 0)
            await InfoDialog.ShowAsync(this, Strings.StorageFolderDialogTitle,
                string.Format(Strings.StorageMoveLeftoversFormat, string.Join(Environment.NewLine, outcome.Leftovers)));
        Close(true);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape || _moving is not null) return;
        e.Handled = true;
        Close(_done);
    }

    /// <summary>Shows the window over <paramref name="owner"/>; true when the storage went to another folder</summary>
    public static Task<bool> ShowAsync(Window owner, StorageFolderViewModel viewModel, MoveStorage move) =>
        new StorageFolderDialog(viewModel, move).ShowDialog<bool>(owner);
}
