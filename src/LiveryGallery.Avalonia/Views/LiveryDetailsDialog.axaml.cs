using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>What the gallery does once the livery window has closed</summary>
internal enum LiveryDetailsNext
{
    None,
    FindInGame,
    View3D,
    Archive,
    Filter,

    /// <summary>The gallery's screenshots view with an album (<see cref="LiveryDetailsOutcome.AlbumId"/>)</summary>
    Album,
}

/// <param name="Next">What to do</param>
/// <param name="Entry">The livery it is about (the one shown when the window closed)</param>
/// <param name="Filter">The filter to show, for <see cref="LiveryDetailsNext.Filter"/></param>
/// <param name="AlbumId">The album to show, for <see cref="LiveryDetailsNext.Album"/></param>
internal sealed record LiveryDetailsOutcome(LiveryDetailsNext Next, LiveryEntry? Entry, QuickFilter? Filter = null, string? AlbumId = null);

/// <summary>The dialogs the livery window opens over itself</summary>
internal sealed record LiveryDetailsDialogActions(
    Func<Window, LiveryEntry, Task> EditTags,
    Func<Window, LiveryEntry, Task> OpenAuthor,
    Func<Window, AuthorCard, Task<AuthorCard?>> EditAuthorCard);

/// <summary>
/// One livery and everything the gallery knows about it (<see cref="LiveryDetailsViewModel"/>). Left and right show the
/// neighbours on screen, Esc closes. Finding in the game, the 3D view, the archive and a filter close the window first
/// and are done by the gallery (<see cref="Outcome"/>)
/// </summary>
internal partial class LiveryDetailsDialog : Window
{
    private readonly LiveryDetailsViewModel _vm;
    private readonly LiveryDetailsDialogActions _actions;

    public LiveryDetailsOutcome Outcome { get; private set; } = new(LiveryDetailsNext.None, null);

    public LiveryDetailsDialog() : this(null!, null!) { }

    public LiveryDetailsDialog(LiveryDetailsViewModel vm, LiveryDetailsDialogActions actions)
    {
        InitializeComponent();
        _vm = vm;
        _actions = actions;
        DataContext = vm;
        if (vm is null) return; // the designer

        Title = vm.Title;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LiveryDetailsViewModel.Title)) Title = vm.Title; };

        FindButton.Content = Strings.ContextMenuNavigateInGame;
        View3DButton.Content = Strings.LiveryDetailsView3D;
        ArchiveButton.Content = Strings.ContextMenuMoveToArchive;
        OtherByAuthorLabel.Text = Strings.LiveryDetailsOtherByAuthor;
        NoOtherByAuthorText.Text = Strings.LiveryDetailsNoOtherByAuthor;
        SameCarButton.Content = Strings.LiveryDetailsShow;
        SimilarLabel.Text = Strings.LiveryDetailsSimilar;
        AuthorLabel.Text = Strings.LiveryDetailsAuthorLabel;
        IdLabel.Text = Strings.LiveryDetailsIdLabel;
        DatesLabel.Text = Strings.LiveryDetailsDatesLabel;
        LayersLabel.Text = Strings.LiveryDetailsLayersLabel;
        BackupsLabel.Text = Strings.MenuOpenBackups;
        TagsLabel.Text = Strings.TagsFilterLabel;
        AddTagButton.Content = Strings.LiveryDetailsAddTag;
        NoteLabel.Text = Strings.LiveryDetailsNoteLabel;
        NoteHint.Text = Strings.LiveryDetailsNoteHint;
        NoteBox.PlaceholderText = Strings.LiveryDetailsNotePlaceholder;
        AuthorNoteLabel.Text = Strings.LiveryDetailsAuthorNoteLabel;
        AuthorNoteEditButton.Content = Strings.LiveryDetailsAuthorNoteEdit;
        HistoryLabel.Text = Strings.LiveryDetailsHistoryLabel;
        HistoryEmptyText.Text = Strings.LiveryDetailsHistoryEmpty;
        FooterHint.Text = Strings.LiveryDetailsFooterHint;
        CloseDialogButton.Content = Strings.ButtonClose;
        ScreenshotsLabel.Text = Strings.ScreenshotsLabel;
        AddScreenshotsButton.Content = "+ " + Strings.ScreenshotsAdd;
        ScreenshotsHint.Text = Strings.ScreenshotsLocalHint;
        ScreenshotAlbumsLabel.Text = Strings.ScreenshotsInAlbums;
        AddHandler(DragDrop.DragOverEvent, Screenshots_DragOver);
        AddHandler(DragDrop.DropEvent, Screenshots_Drop);

        Closed += (_, _) => vm.Dispose();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        bool typing = FocusManager?.GetFocusedElement() is TextBox;
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close();
                break;
            case Key.Left when !typing:
                e.Handled = true;
                _vm.GoPrevious();
                break;
            case Key.Right when !typing:
                e.Handled = true;
                _vm.GoNext();
                break;
            // A picture in the clipboard (a snip, a copied screenshot) or picture files: the note keeps its own Ctrl+V
            case Key.V when !typing && e.KeyModifiers.HasFlag(KeyModifiers.Control) && _vm.CanAddScreenshots:
                e.Handled = true;
                _ = PasteScreenshotAsync();
                break;
        }
    }

    private async Task PasteScreenshotAsync()
    {
        try
        {
            if (Clipboard is not { } clipboard) return;
            if (await clipboard.TryGetFilesAsync() is { Length: > 0 } files)
            {
                await _vm.AddScreenshotFilesAsync([.. files.Select(f => f.TryGetLocalPath()).OfType<string>()]);
                return;
            }
            using var bitmap = await clipboard.TryGetBitmapAsync();
            if (bitmap is null) return;
            using var png = new MemoryStream();
            bitmap.Save(png, new PngBitmapEncoderOptions());
            await _vm.AddScreenshotPictureAsync(png.ToArray());
        }
        catch (Exception ex)
        {
            AppLog.LogError("Livery window: pasting a screenshot failed", ex);
        }
    }

    private void Screenshots_DragOver(object? sender, DragEventArgs e)
    {
        bool files = _vm.CanAddScreenshots && e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = files ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void Screenshots_Drop(object? sender, DragEventArgs e)
    {
        if (!_vm.CanAddScreenshots || e.DataTransfer.TryGetFiles() is not { Length: > 0 } files) return;
        e.Handled = true;
        _ = _vm.AddScreenshotFilesAsync([.. files.Select(f => f.TryGetLocalPath()).OfType<string>()]);
    }

    private async void AddScreenshots_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Strings.ScreenshotsPickTitle,
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType(Strings.ScreenshotsFileType)
                    {
                        Patterns = [.. Services.Screenshots.ScreenshotStore.Extensions.Select(x => "*" + x)],
                    },
                ],
            });
            await _vm.AddScreenshotFilesAsync([.. picked.Select(f => f.TryGetLocalPath()).OfType<string>()]);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Livery window: adding screenshots failed", ex);
        }
    }

    private void Album_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: Services.Screenshots.ScreenshotAlbumCount album }) return;
        _vm.SaveNote();
        Outcome = new LiveryDetailsOutcome(LiveryDetailsNext.Album, _vm.Entry, AlbumId: album.Album.Id);
        Close();
    }

    private void RestorePreview_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true; // not the tile's click under it
        if (_vm.ScreenshotStore is { } store) store.SetCover(_vm.Entry.FolderName, null);
    }

    private async void Screenshot_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Control { DataContext: ScreenshotItem item } || _vm.ScreenshotStore is not { } store) return;
            if (item.Screenshot is null)
            {
                AddScreenshots_Click(sender, e);
                return;
            }
            var list = _vm.ScreenshotList;
            int index = list.ToList().FindIndex(s => s.Path == item.Screenshot.Path);
            string title = _vm.Entry.LiveryName;
            await new ScreenshotViewerWindow(store, list, Math.Max(0, index), _ => title).ShowDialog(this);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Livery window: the screenshot viewer failed", ex);
        }
    }

    private void CloseWith(LiveryDetailsNext next, QuickFilter? filter = null)
    {
        _vm.SaveNote();
        Outcome = new LiveryDetailsOutcome(next, _vm.Entry, filter);
        Close();
    }

    private void Previous_Click(object? sender, RoutedEventArgs e) => _vm.GoPrevious();

    private void Next_Click(object? sender, RoutedEventArgs e) => _vm.GoNext();

    private void Find_Click(object? sender, RoutedEventArgs e) => CloseWith(LiveryDetailsNext.FindInGame);

    private void View3D_Click(object? sender, RoutedEventArgs e) => CloseWith(LiveryDetailsNext.View3D);

    private void Archive_Click(object? sender, RoutedEventArgs e) => CloseWith(LiveryDetailsNext.Archive);

    private void AllByAuthor_Click(object? sender, RoutedEventArgs e) =>
        CloseWith(LiveryDetailsNext.Filter, QuickFilter.ForKeys(_vm.AuthorLiveryKeys, _vm.AuthorText));

    private void SameCar_Click(object? sender, RoutedEventArgs e) =>
        CloseWith(LiveryDetailsNext.Filter, QuickFilter.ForCar(_vm.Entry));

    private void Thumb_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: LiveryThumbItem item }) _vm.Show(item.Entry);
    }

    private void Favorite_Click(object? sender, RoutedEventArgs e) => _vm.ToggleFavorite();

    private void Lock_Click(object? sender, RoutedEventArgs e) => _vm.ToggleLock();

    private async void Author_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _actions.OpenAuthor(this, _vm.Entry);
            _vm.Refresh();
        }
        catch (Exception ex)
        {
            AppLog.LogError("Livery window: the author card failed", ex);
        }
    }

    private async void EditAuthorNote_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_vm.AuthorCard() is not { } card) return;
            await _actions.EditAuthorCard(this, card);
            _vm.Refresh();
        }
        catch (Exception ex)
        {
            AppLog.LogError("Livery window: editing the author card failed", ex);
        }
    }

    private async void EditTags_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _actions.EditTags(this, _vm.Entry);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Livery window: editing the tags failed", ex);
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDragOrMaximize(e, MaximizeIcon);

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e) => this.ToggleMaximize(MaximizeIcon);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
