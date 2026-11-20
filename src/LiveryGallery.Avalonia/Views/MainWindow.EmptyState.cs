using Avalonia.Interactivity;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

/// <summary>
/// The way out of an empty gallery (docs/plans.md, «Пустые состояния»): its text says why nothing shows, its buttons
/// undo that — the search cleared, the filters and the tags reset, the game shown instead of an empty storage, another
/// folder chosen for an empty save, the scan run again. The screenshots' empty view has «All screenshots» and the reset
/// </summary>
internal partial class MainWindow
{
    private void EmptyClearSearch_Click(object? sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        _mainViewModel.FilterBar.SearchText = null;
        // At once, not after the typing's pause
        _searchDebounceTimer.Stop();
        RefreshGallery();
        GalleryScroll.Focus();
    }

    private void EmptyResetFilters_Click(object? sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        _mainViewModel.FilterBar.ShowEverything();
        _searchDebounceTimer.Stop();
        RefreshGallery();
        _mainViewModel.Gallery.ClearTagFilter();
        if (_showingScreenshots) FillScreenshotsView();
        else GalleryScroll.Focus();
    }

    private void EmptyShowGame_Click(object? sender, RoutedEventArgs e) => RunModeShortcut(GalleryShortcut.ShowGame);

    private void EmptyLookAgain_Click(object? sender, RoutedEventArgs e)
    {
        if (_mainViewModel.Status.CanRefresh && _mainViewModel.RefreshCommand.CanExecute(null)) _mainViewModel.RefreshCommand.Execute(null);
    }

    private void ScreenshotsAllAlbums_Click(object? sender, RoutedEventArgs e)
    {
        _album = AllAlbumsId;
        AlbumsSidebar.Select(_album);
        FillScreenshotsView();
    }
}
