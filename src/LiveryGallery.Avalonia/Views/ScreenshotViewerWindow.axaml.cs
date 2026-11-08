using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Services.Diagnostics;
using LiveryGallery.Services.Screenshots;

namespace LiveryGallery.Views;

/// <summary>
/// The screenshots of a livery (or of the whole gallery), one at a time as large as the window. Left and Right go
/// through them, Esc closes (the gallery's rule for its windows). Below: when it was added, its size, and what can be
/// done with it: make it the card's cover (or put the game's preview back), show it in its folder, copy the picture,
/// delete it
/// </summary>
internal partial class ScreenshotViewerWindow : Window
{
    private readonly ScreenshotStore _store;
    private readonly List<LiveryScreenshot> _shots;
    private readonly Func<LiveryScreenshot, string> _liveryTitle;
    private readonly Action<string>? _openLivery;
    private int _index;
    private Bitmap? _bitmap;

    public ScreenshotViewerWindow() : this(null!, [], 0, _ => "") { }

    /// <param name="store">Where the screenshots are kept</param>
    /// <param name="shots">The screenshots to go through</param>
    /// <param name="index">The one shown first</param>
    /// <param name="liveryTitle">The name of a screenshot's livery, for the title and the line below</param>
    /// <param name="openLivery">Opens a screenshot's livery (its key), in the gallery's screenshots view; null hides the button</param>
    public ScreenshotViewerWindow(ScreenshotStore store, IReadOnlyList<LiveryScreenshot> shots, int index,
        Func<LiveryScreenshot, string> liveryTitle, Action<string>? openLivery = null)
    {
        InitializeComponent();
        _store = store;
        _shots = [.. shots];
        _index = Math.Clamp(index, 0, Math.Max(0, _shots.Count - 1));
        _liveryTitle = liveryTitle;
        _openLivery = openLivery;
        if (store is null) return; // the designer

        LiveryButton.Content = Strings.ScreenshotGoToLivery;
        LiveryButton.IsVisible = openLivery is not null;
        FolderButton.Content = Strings.ScreenshotShowInFolder;
        CopyButton.Content = Strings.ScreenshotCopy;
        DeleteButton.Content = Strings.ScreenshotDelete;
        NotReadableText.Text = Strings.LiveryDetailsNoPreview;
        Closed += (_, _) =>
        {
            ShotImage.Source = null;
            _bitmap?.Dispose();
        };
        Show(_index);
    }

    private LiveryScreenshot? Current => _index >= 0 && _index < _shots.Count ? _shots[_index] : null;

    private void Show(int index)
    {
        if (_shots.Count == 0) { Close(); return; }
        _index = Math.Clamp(index, 0, _shots.Count - 1);
        var shot = _shots[_index];
        string livery = _liveryTitle(shot);
        Title = $"{Strings.ScreenshotsLabel} · {livery}";
        TitleBarText.Text = Title;
        PositionText.Text = $"{string.Format(Strings.ScreenshotPositionFormat, _index + 1, _shots.Count)} · {livery}";

        var old = _bitmap;
        _bitmap = null;
        try
        {
            using var stream = File.OpenRead(shot.Path);
            _bitmap = new Bitmap(stream);
        }
#pragma warning disable CA1031 // a broken file is shown as such, the others still go
        catch (Exception ex)
#pragma warning restore CA1031
        {
            AppLog.LogErrorThrottled(shot.Path, $"Failed to open the screenshot '{shot.Path}'", ex);
        }
        ShotImage.Source = _bitmap;
        NotReadableText.IsVisible = _bitmap is null;
        old?.Dispose();

        var parts = new List<string> { shot.AddedUtc.ToLocalTime().ToString("g", AppLocalisationService.Culture) };
        if (_bitmap is { } b) parts.Add($"{b.PixelSize.Width}×{b.PixelSize.Height}");
        parts.Add($"{Path.GetExtension(shot.Path).TrimStart('.').ToUpperInvariant()} {ByteSizeFormatter.Format(shot.Bytes)}");
        DetailsText.Text = string.Join("  ·  ", parts);

        bool isCover = string.Equals(_store.CoverPath(shot.LiveryKey), shot.Path, StringComparison.OrdinalIgnoreCase);
        ShowAlbums(shot);
        CoverButton.Content = isCover ? Strings.ScreenshotRestorePreview : Strings.ScreenshotMakeCover;
        CoverButton.Classes.Set("primary", !isCover);
        CoverButton.Classes.Set("secondary", isCover);
        PreviousButton.IsVisible = _index > 0;
        NextButton.IsVisible = _index < _shots.Count - 1;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        switch (e.Key)
        {
            case Key.Left:
                e.Handled = true;
                if (_index > 0) Show(_index - 1);
                break;
            case Key.Right:
                e.Handled = true;
                if (_index < _shots.Count - 1) Show(_index + 1);
                break;
            case Key.Delete:
                e.Handled = true;
                _ = DeleteAsync();
                break;
        }
    }

    // "Albums: Best, Wallpapers", or "Albums" when the screenshot is in none
    private void ShowAlbums(LiveryScreenshot shot)
    {
        var ids = _store.AlbumsOf(shot);
        var names = _store.Albums().Where(a => ids.Contains(a.Id)).Select(a => a.Name).ToList();
        AlbumsButtonText.Text = names.Count == 0 ? Strings.ScreenshotAlbumsButton : string.Format(Strings.ScreenshotAlbumsButtonFormat, string.Join(", ", names));
        ToolTip.SetTip(AlbumsButton, names.Count == 0 ? null : string.Join(", ", names));
    }

    private void Albums_Click(object? sender, RoutedEventArgs e)
    {
        if (Current is not { } shot) return;
        var ids = _store.AlbumsOf(shot);
        var menu = new ContextMenu();
        foreach (var album in _store.Albums())
        {
            bool inIt = ids.Contains(album.Id);
            var item = new MenuItem { Header = album.Name, ToggleType = MenuItemToggleType.CheckBox, IsChecked = inIt };
            item.Click += (_, _) =>
            {
                _store.SetInAlbum([shot], album.Id, !inIt);
                ShowAlbums(shot);
            };
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        var create = new MenuItem { Header = Strings.ScreenshotsNewAlbumMenu };
        create.Click += async (_, _) =>
        {
            try
            {
                string? name = await Collections.NameDialog.AskAsync(this, Strings.AlbumsNew, Strings.AlbumNameLabel, "", n =>
                    Services.Collections.CollectionNames.Clean(n) is not { } clean ? Strings.CollectionNameEmpty
                    : Services.Collections.CollectionNames.IsTaken(clean, _store.Albums().Select(a => (a.Id, a.Name))) ? Strings.AlbumNameTaken
                    : null, Strings.AlbumCreate);
                if (name is not null && _store.CreateAlbum(name) is { } album) _store.SetInAlbum([shot], album.Id, true);
                ShowAlbums(shot);
            }
            catch (Exception ex)
            {
                AppLog.LogError("Viewer: a new album failed", ex);
            }
        };
        menu.Items.Add(create);
        AlbumsButton.ContextMenu = menu;
        menu.Open(AlbumsButton);
    }

    private void Previous_Click(object? sender, RoutedEventArgs e) => Show(_index - 1);

    private void Next_Click(object? sender, RoutedEventArgs e) => Show(_index + 1);

    private void Cover_Click(object? sender, RoutedEventArgs e)
    {
        if (Current is not { } shot) return;
        bool isCover = string.Equals(_store.CoverPath(shot.LiveryKey), shot.Path, StringComparison.OrdinalIgnoreCase);
        _store.SetCover(shot.LiveryKey, isCover ? null : shot);
        Show(_index);
    }

    private void Folder_Click(object? sender, RoutedEventArgs e)
    {
        if (Current is { } shot) ExplorerLauncher.ShowInFolder(shot.Path);
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_bitmap is { } bitmap && Clipboard is { } clipboard) await clipboard.SetBitmapAsync(bitmap);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to copy the screenshot", ex);
        }
    }

    private void Delete_Click(object? sender, RoutedEventArgs e) => _ = DeleteAsync();

    private async Task DeleteAsync()
    {
        try
        {
            if (Current is not { } shot) return;
            if (!await ConfirmDialog.AskAsync(this, Strings.ScreenshotDeleteTitle, Strings.ScreenshotDeleteMessage)) return;
            ShotImage.Source = null;
            _bitmap?.Dispose();
            _bitmap = null;
            if (!_store.Delete(shot)) { Show(_index); return; }
            _shots.RemoveAt(_index);
            Show(Math.Min(_index, _shots.Count - 1));
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to delete the screenshot", ex);
        }
    }

    private void Livery_Click(object? sender, RoutedEventArgs e)
    {
        if (Current is not { } shot || _openLivery is null) return;
        Close();
        _openLivery(shot.LiveryKey);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDragOrMaximize(e, MaximizeIcon);

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e) => this.ToggleMaximize(MaximizeIcon);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
