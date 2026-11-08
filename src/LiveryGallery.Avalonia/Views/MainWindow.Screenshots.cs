using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using ForzaToolkit.UI.Dialogs;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Services.Collections;
using LiveryGallery.Services.Screenshots;
using LiveryGallery.ViewModels;
using LiveryGallery.ViewModels.Collections;
using LiveryGallery.Views.Collections;

namespace LiveryGallery.Views;

/// <summary>
/// The "Liveries | Screenshots" switch by the search, and the screenshots view: the screenshots of the liveries the
/// filters and the search show, in groups (by livery, as the liveries' grid, by day), with the albums on the left. A
/// click selects (Ctrl, Shift as on the cards), a double click or Enter opens the viewer, a drag onto an album puts the
/// selected ones in it, the menu adds to and takes out of albums. "Open the livery" goes back to the liveries and opens
/// its window
/// </summary>
internal partial class MainWindow
{
    // The side panel's rows of the app: every screenshot, and those in no album
    private const string AllAlbumsId = ":all";
    private const string NoAlbumId = ":none";

    // A tile in the screenshots view is 300 wide (MainWindow.axaml); the group heading's picture 44
    private const double ScreenshotTileWidth = 300;
    private const double GroupPictureWidth = 44;

    // The screenshots dragged onto an album, inside the gallery only
    private static readonly DataFormat<LiveryScreenshot[]> ScreenshotsDragFormat =
        DataFormat.CreateInProcessFormat<LiveryScreenshot[]>("LiveryGallery.Screenshots");

    private bool _showingScreenshots;
    private CancellationTokenSource _screenshotTilesCts = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<ScreenshotGroupViewModel> _screenshotGroups = [];
    private readonly ItemSelection<ScreenshotTile> _screenshotSelection = new();
    private readonly HashSet<string> _foldedScreenshotGroups = [];
    // Decoded pictures by file, kept while the view is open: a regroup or another album does not decode them again
    private readonly Dictionary<string, Bitmap?> _screenshotPictures = new(StringComparer.OrdinalIgnoreCase);
    private string _album = AllAlbumsId;
    // The liveries (and, as the liveries, their groups) the screenshots view was filled for; a regroup that leaves them
    // as they are does not fill it again
    private string? _screenshotsShownFor;
    private bool _screenshotsRefillQueued;
    private (PointerPressedEventArgs Args, ScreenshotTile Tile, Point At, bool SelectOnRelease)? _tilePress;

    private ScreenshotGroupMode ScreenshotGrouping =>
        Enum.TryParse<ScreenshotGroupMode>(_settings.ScreenshotsGrouping, out var mode) ? mode : ScreenshotGroupMode.ByLivery;

    private void InitializeScreenshotsView()
    {
        ViewSwitch.IsVisible = _mainViewModel.Screenshots is not null;
        if (_mainViewModel.Screenshots is not { } store) return;
        ScreenshotGroups.ItemsSource = _screenshotGroups;
        _mainViewModel.FilterBar.FiltersChanged += () => { if (_showingScreenshots) FillScreenshotsView(); };
        _mainViewModel.ScreenshotsChanged += _ => { if (_showingScreenshots) FillScreenshotsView(); };
        // The search, a filter, a scan: the gallery groups its cards again, and the screenshots follow the liveries shown
        _mainViewModel.Gallery.GroupsReplaced += RefillScreenshotsIfShownChanged;
        store.AlbumsChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_showingScreenshots) FillScreenshotsView(); });

        AlbumsSidebar.Picked += item => { _album = item.Id; AlbumsSidebar.Select(_album); FillScreenshotsView(); };
        AlbumsSidebar.CreateRequested += () => RunUiTask(() => CreateAlbumAsync(), "new album");
        AlbumsSidebar.RenameRequested += item => RunUiTask(() => RenameAlbumAsync(item), "rename album");
        AlbumsSidebar.DeleteRequested += item => RunUiTask(() => DeleteAlbumAsync(item), "delete album");
        AlbumsSidebar.Reordered += ids => store.ReorderAlbums(ids);
        AlbumsSidebar.CanDrop = e => e.DataTransfer.Contains(ScreenshotsDragFormat);
        AlbumsSidebar.Dropped += (item, e) =>
        {
            if (e.DataTransfer.TryGetValue(ScreenshotsDragFormat) is { Length: > 0 } shots) store.SetInAlbum(shots, item.Id, true);
        };
        Closed += (_, _) => ClearScreenshotTiles(disposePictures: true);
    }

    private void ApplyScreenshotsTexts()
    {
        LiveriesViewButton.Content = Strings.GalleryViewLiveries;
        ScreenshotsViewText.Text = Strings.ScreenshotsLabel;
        ScreenshotsGroupLabel.Text = Strings.ScreenshotsGroupLabel;
        GroupByLiveryButton.Content = Strings.ScreenshotsGroupByLivery;
        GroupLikeLiveriesButton.Content = Strings.ScreenshotsGroupLikeLiveries;
        GroupByDateButton.Content = Strings.ScreenshotsGroupByDate;
        AlbumsSidebar.SetTexts(Strings.AlbumsHeading, Strings.AlbumsNew, Strings.AlbumsHint);
        ScreenshotsOrderBox.ItemsSource = new[] { Strings.ScreenshotsNewestFirst, Strings.ScreenshotsOldestFirst };
        ScreenshotsOrderBox.SelectedIndex = _settings.ScreenshotsNewestFirst ? 0 : 1;
        MarkScreenshotGrouping();
    }

    private void MarkScreenshotGrouping()
    {
        var mode = ScreenshotGrouping;
        GroupByLiveryButton.Classes.Set("on", mode == ScreenshotGroupMode.ByLivery);
        GroupLikeLiveriesButton.Classes.Set("on", mode == ScreenshotGroupMode.LikeLiveries);
        GroupByDateButton.Classes.Set("on", mode == ScreenshotGroupMode.ByDate);
    }

    /// <summary>
    /// The gallery's keys while the screenshots are shown, as on the cards: the arrows, Home, End, Page Up and Page Down
    /// move the cursor from tile to tile (<see cref="MoveScreenshotCursor"/>), Space and Ctrl+Space put the tile under it
    /// in the selection or out, Ctrl+A selects every screenshot shown, Enter opens the viewer, Del deletes the selected
    /// ones (asked first), Esc clears the selection and then goes back to the liveries. The keys of the selected
    /// liveries do nothing; the search, the refresh and the windows work as with the liveries
    /// </summary>
    private bool RunScreenshotsShortcut(GalleryShortcut action, object? focused)
    {
        switch (action)
        {
            case GalleryShortcut.Escape when focused is not TextBox:
                if (!_screenshotSelection.Clear()) ShowScreenshotsView(false);
                return true;
            case GalleryShortcut.SelectAll when focused is not TextBox:
                _screenshotSelection.SelectAll();
                return true;
            case GalleryShortcut.OpenDetails when (_screenshotSelection.Cursor ?? _screenshotSelection.Selected.FirstOrDefault()) is { } first:
                RunUiTask(() => OpenScreenshotViewerAsync(first), "screenshot viewer");
                return true;
            case GalleryShortcut.ToggleSelection:
                return _screenshotSelection.ToggleCursor();
            case GalleryShortcut.MoveToArchive when _screenshotSelection.HasSelection:
                RunUiTask(DeleteSelectedScreenshotsAsync, "delete screenshots");
                return true;
            case GalleryShortcut.Search or GalleryShortcut.Escape or GalleryShortcut.Refresh or GalleryShortcut.Settings
                or GalleryShortcut.Archive or GalleryShortcut.Backups or GalleryShortcut.Statistics or GalleryShortcut.Authors
                or GalleryShortcut.ShortcutList:
                return RunShortcut(action, focused);
            default:
                return false;
        }
    }

    /// <summary>
    /// The arrows, Home, End, Page Up and Page Down over the tiles (<see cref="GalleryKeyboard"/>, as the cards): the
    /// rows are those on screen, a folded group's tiles left out; the list scrolls to the tile. Ctrl+Space toggles the
    /// tile under the cursor. False when the key is not one of them or there is no tile
    /// </summary>
    private bool MoveScreenshotCursor(KeyEventArgs e, object? focused)
    {
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control) return _screenshotSelection.ToggleCursor();
        if (GalleryKeyboard.Match(e.Key, e.KeyModifiers) is not { } key) return false;
        if (focused is ComboBox or ComboBoxItem or ListBoxItem or ListBox or Slider or MenuItem) return false;
        var (rows, borders) = ScreenshotRows();
        if (rows.Count == 0) return false;
        int row = -1, column = 0;
        if (_screenshotSelection.Cursor is { } cursor)
            for (int r = 0; r < rows.Count && row < 0; r++)
            {
                int c = rows[r].IndexOf(cursor);
                if (c >= 0) (row, column) = (r, c);
            }
        double tileHeight = borders.Values.FirstOrDefault()?.Bounds.Height + 14 ?? 220;
        int pageRows = Math.Max(1, (int)(ScreenshotsScroll.Viewport.Height / tileHeight));
        // From no cursor: the first row on screen
        int start = rows.FindIndex(r => borders[r[0]].TranslatePoint(new Point(0, 0), ScreenshotGroups) is { } p && p.Y + tileHeight > ScreenshotsScroll.Offset.Y);
        if (GalleryKeyboard.Target([.. rows.Select(r => r.Count)], row, column, key.Move, pageRows, start) is not { } target) return false;
        var tile = rows[target.Row][target.Column];
        _screenshotSelection.MoveTo(tile, key.Selection);
        borders[tile].BringIntoView();
        return true;
    }

    // The tiles in rows as they are on screen (by their place), the groups not folded, in order; and each tile's control
    private (List<List<ScreenshotTile>> Rows, Dictionary<ScreenshotTile, Control> Borders) ScreenshotRows()
    {
        var borders = new Dictionary<ScreenshotTile, Control>();
        foreach (var border in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(ScreenshotGroups).OfType<Border>())
            if (border.Classes.Contains("shotTile") && border.IsEffectivelyVisible && border.DataContext is ScreenshotTile tile)
                borders[tile] = border;
        var rows = new List<List<ScreenshotTile>>();
        double? lastY = null;
        foreach (var tile in _screenshotSelection.Items)
        {
            if (!borders.TryGetValue(tile, out var border) || border.TranslatePoint(new Point(0, 0), ScreenshotGroups) is not { } at) continue;
            if (lastY is not { } y || Math.Abs(at.Y - y) > 1) rows.Add([]);
            rows[^1].Add(tile);
            lastY = at.Y;
        }
        return (rows, borders);
    }

    private void LiveriesView_Click(object? sender, RoutedEventArgs e) => ShowScreenshotsView(false);

    private void ScreenshotsView_Click(object? sender, RoutedEventArgs e) => ShowScreenshotsView(true);

    private void ShowScreenshotsView(bool show)
    {
        _showingScreenshots = show;
        LiveriesViewButton.Classes.Set("on", !show);
        ScreenshotsViewButton.Classes.Set("on", show);
        ScreenshotsView.IsVisible = show;
        if (show) FillScreenshotsView();
        else ClearScreenshotTiles(disposePictures: true);
    }

    /// <summary>The screenshots view with an album chosen (from the livery window)</summary>
    private void ShowAlbum(string albumId)
    {
        _album = albumId;
        ShowScreenshotsView(true);
    }

    private void ScreenshotsGroup_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string mode }) return;
        _settings.ScreenshotsGrouping = mode;
        AppSettingsService.Save(_settings);
        MarkScreenshotGrouping();
        if (_showingScreenshots) FillScreenshotsView();
    }

    private void ScreenshotsOrder_Changed(object? sender, SelectionChangedEventArgs e)
    {
        bool newestFirst = ScreenshotsOrderBox.SelectedIndex != 1;
        if (newestFirst == _settings.ScreenshotsNewestFirst) return;
        _settings.ScreenshotsNewestFirst = newestFirst;
        AppSettingsService.Save(_settings);
        if (_showingScreenshots) FillScreenshotsView();
    }

    // What the view shows depends on: the liveries shown, in order, and their groups when grouped as the liveries
    private string ShownSignature() => ScreenshotGrouping == ScreenshotGroupMode.LikeLiveries
        ? string.Join("\n", _mainViewModel.Gallery.DisplayedGroups.Select(g => g.Key + "\t" + string.Join("|", g.Items.Select(e => e.FolderName))))
        : string.Join("|", _mainViewModel.Gallery.ShownEntries().Select(e => e.FolderName));

    private void RefillScreenshotsIfShownChanged()
    {
        if (!_showingScreenshots || _screenshotsRefillQueued) return;
        _screenshotsRefillQueued = true;
        // Once the gallery is done with this round of groups (it can replace them more than once)
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _screenshotsRefillQueued = false;
            if (_showingScreenshots && ShownSignature() != _screenshotsShownFor) FillScreenshotsView();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>The screenshots of the liveries shown now, of the album chosen, in groups; their pictures loaded one after another</summary>
    private void FillScreenshotsView()
    {
        if (_mainViewModel.Screenshots is not { } store) return;
        _screenshotTilesCts.Cancel();
        _screenshotTilesCts.Dispose();
        _screenshotTilesCts = new CancellationTokenSource();
        var ct = _screenshotTilesCts.Token;
        _screenshotsShownFor = ShownSignature();
        var shown = new Dictionary<string, LiveryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _mainViewModel.Gallery.ShownEntries()) shown.TryAdd(entry.FolderName, entry);
        var liveryGroups = _mainViewModel.Gallery.DisplayedGroups
            .Select(g => (g.Key, (IReadOnlyList<LiveryEntry>)[.. g.Items.OfType<LiveryEntry>()])).ToList();
        string album = _album;
        var mode = ScreenshotGrouping;
        bool newestFirst = _settings.ScreenshotsNewestFirst;
        int tileWidth = ScreenshotStore.PixelWidth(ScreenshotTileWidth, RenderScaling);
        int groupPictureWidth = ScreenshotStore.PixelWidth(GroupPictureWidth, RenderScaling);

        _ = Task.Run(() =>
        {
            var all = store.ListAll().Where(s => shown.ContainsKey(s.LiveryKey)).ToList();
            var albumsOf = all.ToDictionary(s => s.Path, store.AlbumsOf, StringComparer.OrdinalIgnoreCase);
            var covers = all.Select(s => s.LiveryKey).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(k => k, store.CoverPath, StringComparer.OrdinalIgnoreCase);
            return (All: all, AlbumsOf: albumsOf, Covers: covers, Albums: store.Albums());
        }, ct).ContinueWith(task =>
        {
            if (task.IsCanceled || task.IsFaulted || ct.IsCancellationRequested || !_showingScreenshots) return;
            var (all, albumsOf, covers, albums) = task.Result;

            // The side panel: counts among the liveries shown
            FillAlbumsSidebar(albums, all, albumsOf);
            if (album != AllAlbumsId && album != NoAlbumId && albums.All(a => a.Id != album)) album = _album = AllAlbumsId;
            AlbumsSidebar.Select(album);

            var inView = all.Where(s => album switch
            {
                AllAlbumsId => true,
                NoAlbumId => albumsOf[s.Path].Count == 0,
                _ => albumsOf[s.Path].Contains(album),
            });
            var tiles = inView.Select(s => new ScreenshotTile(s, shown[s.LiveryKey])
            {
                IsCover = string.Equals(covers[s.LiveryKey], s.Path, StringComparison.OrdinalIgnoreCase),
            }).ToList();
            var texts = new ScreenshotGroupTexts(Strings.ScreenshotsToday, Strings.ScreenshotsYesterday, AppLocalisationService.Culture,
                AppLocalisationService.MonthYearFormat);
            var plans = Services.Screenshots.ScreenshotGrouping.Group(tiles, mode, newestFirst, liveryGroups, DateTime.Now, texts);

            ClearScreenshotTiles(disposePictures: false);
            foreach (var plan in plans)
            {
                string count = plan.Livery is null && plan.LiveryCount > 1
                    ? $"{LiveryCountText(plan.LiveryCount)} · {ScreenshotCountText(plan.Tiles.Count)}"
                    : ScreenshotCountText(plan.Tiles.Count);
                _screenshotGroups.Add(new ScreenshotGroupViewModel(plan.Key, plan.Title, plan.Subtitle, count, plan.Livery, plan.Tiles)
                {
                    IsExpanded = !_foldedScreenshotGroups.Contains(plan.Key),
                    LinkText = plan.Livery is null ? null : Strings.ScreenshotGoToLivery,
                });
            }
            ResetScreenshotSelection();
            ScreenshotsEmpty.IsVisible = tiles.Count == 0;
            ScreenshotsEmptyText.Text = album == AllAlbumsId ? Strings.ScreenshotsEmptyView : Strings.ScreenshotsEmptyAlbum;
            _ = LoadScreenshotPicturesAsync([.. _screenshotGroups], tileWidth, groupPictureWidth, ct);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void FillAlbumsSidebar(IReadOnlyList<ScreenshotAlbum> albums, List<LiveryScreenshot> all, Dictionary<string, IReadOnlySet<string>> albumsOf)
    {
        var camera = (Avalonia.Media.Geometry?)this.FindResource("IconCamera");
        var picture = (Avalonia.Media.Geometry?)this.FindResource("IconPicture");
        AlbumsSidebar.Items.Clear();
        AlbumsSidebar.Items.Add(new CollectionSidebarItem(AllAlbumsId, Strings.AlbumsAll, camera, isUsers: false) { Count = all.Count });
        AlbumsSidebar.Items.Add(new CollectionSidebarItem(NoAlbumId, Strings.AlbumsNone, camera, isUsers: false)
        {
            Count = all.Count(s => albumsOf[s.Path].Count == 0),
        });
        bool first = true;
        foreach (var album in albums)
        {
            AlbumsSidebar.Items.Add(new CollectionSidebarItem(album.Id, album.Name, picture, isUsers: true)
            {
                Count = all.Count(s => albumsOf[s.Path].Contains(album.Id)),
                IsFirstOfUsers = first,
            });
            first = false;
        }
    }

    private static string ScreenshotCountText(int count) => string.Format(AppLocalisationService.Culture,
        StatisticsViewModel.PluralForm(count, AppLocalisationService.Culture) switch
        {
            StatisticsViewModel.Plural.One => Strings.ScreenshotCountOne,
            StatisticsViewModel.Plural.Few => Strings.ScreenshotCountFew,
            _ => Strings.ScreenshotCountMany,
        }, count);

    private static string LiveryCountText(int count) => string.Format(AppLocalisationService.Culture,
        StatisticsViewModel.PluralForm(count, AppLocalisationService.Culture) switch
        {
            StatisticsViewModel.Plural.One => Strings.StatsLiveryCountOne,
            StatisticsViewModel.Plural.Few => Strings.StatsLiveryCountFew,
            _ => Strings.StatsLiveryCountMany,
        }, count);

    // The tiles a click or Shift can reach: those of the groups not folded, in the order on screen
    private void ResetScreenshotSelection() =>
        _screenshotSelection.Reset([.. _screenshotGroups.Where(g => g.IsExpanded).SelectMany(g => g.Tiles)],
            (a, b) => string.Equals(a.Screenshot.Path, b.Screenshot.Path, StringComparison.OrdinalIgnoreCase));

    private async Task LoadScreenshotPicturesAsync(List<ScreenshotGroupViewModel> groups, int width, int groupWidth, CancellationToken ct)
    {
        // The headings' small pictures first (the livery's card picture), then the tiles in the order on screen
        foreach (var group in groups)
        {
            if (group.Livery is not { } livery || ((IThumbnailHost)livery).ThumbnailPath is not { } path) continue;
            group.Thumbnail = await PictureAsync("group|" + path, path, groupWidth, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;
        }
        foreach (var tile in groups.SelectMany(g => g.Tiles))
        {
            tile.Thumbnail = await PictureAsync(tile.Screenshot.Path, tile.Screenshot.Path, width, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;
        }
    }

    private async Task<Bitmap?> PictureAsync(string cacheKey, string path, int width, CancellationToken ct)
    {
        if (_screenshotPictures.TryGetValue(cacheKey, out var known)) return known;
        if (ct.IsCancellationRequested) return null;
        // Not given the token: a decode cancelled before it starts would end in an exception nobody waits for
        var bitmap = await Task.Run(() => DecodeTile(path, width), CancellationToken.None).ConfigureAwait(true);
        if (ct.IsCancellationRequested) { bitmap?.Dispose(); return null; }
        _screenshotPictures[cacheKey] = bitmap;
        return bitmap;
    }

    private static Bitmap? DecodeTile(string path, int width)
    {
        try
        {
            return ScreenshotStore.DecodeSmaller(path, width);
        }
#pragma warning disable CA1031 // a broken file is a tile without a picture
        catch (Exception ex)
#pragma warning restore CA1031
        {
            AppLog.LogErrorThrottled(path, $"Failed to read the screenshot '{path}'", ex);
            return null;
        }
    }

    private void ClearScreenshotTiles(bool disposePictures)
    {
        if (disposePictures)
        {
            _screenshotTilesCts.Cancel();
            _screenshotTilesCts.Dispose();
            _screenshotTilesCts = new CancellationTokenSource();
        }
        foreach (var group in _screenshotGroups)
        {
            group.Thumbnail = null;
            foreach (var tile in group.Tiles) tile.Thumbnail = null;
        }
        _screenshotGroups.Clear();
        if (!disposePictures) return;
        foreach (var bitmap in _screenshotPictures.Values) bitmap?.Dispose();
        _screenshotPictures.Clear();
        _screenshotSelection.Reset([]);
    }

    private void ScreenshotGroup_Folded(CollectionGroupViewModel group)
    {
        if (group.IsExpanded) _foldedScreenshotGroups.Remove(group.Key);
        else _foldedScreenshotGroups.Add(group.Key);
        ResetScreenshotSelection();
    }

    private void ScreenshotGroup_LinkClicked(CollectionGroupViewModel group)
    {
        if (group is ScreenshotGroupViewModel { Livery: { } livery }) OpenLiveryFromScreenshots(livery);
    }

    private void OpenLiveryFromScreenshots(LiveryEntry livery)
    {
        ShowScreenshotsView(false);
        // The livery as the gallery has it now: a scan since the screenshots were listed may have made a new card of it
        var current = _mainViewModel.Gallery.AllEntries.FirstOrDefault(e => string.Equals(e.FolderName, livery.FolderName, StringComparison.OrdinalIgnoreCase)) ?? livery;
        // From the viewer's button: Windows gives the main window its activation back just after the viewer closes, and a
        // livery window opened at once ended up behind it. Opened once the closing is over, it comes to the front
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_isClosing) return;
            Activate();
            _mainViewModel.Gallery.OpenDetailsCommand.Execute(current);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    // A press selects at once, unless it is on a selected tile without keys: then the release does (after no drag), so
    // the whole selection can be dragged
    private void ShotTile_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ScreenshotTile tile } control) return;
        var point = e.GetCurrentPoint(control);
        if (!point.Properties.IsLeftButtonPressed) return;
        bool control_ = e.KeyModifiers.HasFlag(KeyModifiers.Control), shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool later = tile.IsSelected && !control_ && !shift;
        if (!later) _screenshotSelection.Click(tile, control_, shift);
        _tilePress = (e, tile, e.GetPosition(this), later);
        ScreenshotsScroll.Focus();
    }

    private void ShotTile_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_tilePress is { SelectOnRelease: true } press) _screenshotSelection.Click(press.Tile, control: false, shift: false);
        _tilePress = null;
    }

    private async void ShotTile_PointerMoved(object? sender, PointerEventArgs e)
    {
        try
        {
            if (_tilePress is not { } press || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var delta = e.GetPosition(this) - press.At;
            if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6) return;
            _tilePress = null;
            var shots = _screenshotSelection.Selected.Select(t => t.Screenshot).ToArray();
            if (shots.Length == 0) return;
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(ScreenshotsDragFormat, shots));
            await DragDrop.DoDragDropAsync(press.Args, data, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Dragging screenshots failed", ex);
        }
    }

    private void ShotTile_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: ScreenshotTile tile }) RunUiTask(() => OpenScreenshotViewerAsync(tile), "screenshot viewer");
    }

    private async Task OpenScreenshotViewerAsync(ScreenshotTile tile)
    {
        if (_mainViewModel.Screenshots is not { } store) return;
        var tiles = _screenshotSelection.Items.Count > 0 ? _screenshotSelection.Items : [tile];
        var byKey = tiles.GroupBy(t => t.Screenshot.LiveryKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Entry, StringComparer.OrdinalIgnoreCase);
        LiveryEntry? toOpen = null;
        await new ScreenshotViewerWindow(store, [.. tiles.Select(t => t.Screenshot)], Math.Max(0, tiles.ToList().IndexOf(tile)),
            s => byKey.TryGetValue(s.LiveryKey, out var entry) ? entry.LiveryName : s.LiveryKey,
            key => toOpen = byKey.GetValueOrDefault(key)).ShowDialog(this);
        if (toOpen is not null) OpenLiveryFromScreenshots(toOpen);
    }

    // The menu of the selected screenshots (a right click outside the selection selects that one first)
    private void ShotTile_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: ScreenshotTile tile } control || _mainViewModel.Screenshots is not { } store) return;
        e.Handled = true;
        _screenshotSelection.Point(tile);
        var selected = _screenshotSelection.Selected;
        var shots = selected.Select(t => t.Screenshot).ToList();
        var menu = new ContextMenu();

        var add = new MenuItem { Header = $"{Strings.ScreenshotsAddToAlbum}{(shots.Count > 1 ? $" ({shots.Count})" : "")}" };
        var inAlbums = shots.Select(store.AlbumsOf).ToList();
        foreach (var album in store.Albums())
        {
            bool all = inAlbums.All(ids => ids.Contains(album.Id));
            var item = new MenuItem { Header = album.Name, ToggleType = MenuItemToggleType.CheckBox, IsChecked = all };
            item.Click += (_, _) => store.SetInAlbum(shots, album.Id, !all);
            add.Items.Add(item);
        }
        if (add.Items.Count > 0) add.Items.Add(new Separator());
        var create = new MenuItem { Header = Strings.ScreenshotsNewAlbumMenu };
        create.Click += (_, _) => RunUiTask(() => CreateAlbumAsync(shots), "new album");
        add.Items.Add(create);
        menu.Items.Add(add);

        if (store.Albums().FirstOrDefault(a => a.Id == _album) is { } current)
        {
            var remove = new MenuItem { Header = string.Format(Strings.ScreenshotsRemoveFromAlbumFormat, current.Name) };
            remove.Click += (_, _) => store.SetInAlbum(shots, current.Id, false);
            menu.Items.Add(remove);
        }
        menu.Items.Add(new Separator());

        var open = new MenuItem { Header = Strings.ScreenshotGoToLivery };
        open.Click += (_, _) => OpenLiveryFromScreenshots(tile.Entry);
        menu.Items.Add(open);
        if (selected.Count == 1)
        {
            var cover = new MenuItem { Header = tile.IsCover ? Strings.ScreenshotRestorePreview : Strings.ScreenshotMakeCover };
            cover.Click += (_, _) => store.SetCover(tile.Screenshot.LiveryKey, tile.IsCover ? null : tile.Screenshot);
            menu.Items.Add(cover);
            var folder = new MenuItem { Header = Strings.ScreenshotShowInFolder };
            folder.Click += (_, _) => Services.Diagnostics.ExplorerLauncher.ShowInFolder(tile.Screenshot.Path);
            menu.Items.Add(folder);
        }
        var delete = new MenuItem { Header = Strings.ScreenshotDelete, InputGesture = new KeyGesture(Key.Delete) };
        delete.Click += (_, _) => RunUiTask(DeleteSelectedScreenshotsAsync, "delete screenshots");
        menu.Items.Add(delete);

        control.ContextMenu = menu;
        menu.Open(control);
    }

    private async Task DeleteSelectedScreenshotsAsync()
    {
        if (_mainViewModel.Screenshots is not { } store) return;
        var shots = _screenshotSelection.Selected.Select(t => t.Screenshot).ToList();
        if (shots.Count == 0) return;
        if (!await ConfirmDialog.AskAsync(this, Strings.ScreenshotsDeleteSelectedTitle,
                string.Format(Strings.ScreenshotsDeleteSelectedFormat, shots.Count), Strings.ButtonDelete, Strings.ButtonCancel)) return;
        foreach (var shot in shots)
        {
            if (_screenshotPictures.Remove(shot.Path, out var bitmap))
            {
                foreach (var tile in _screenshotSelection.Items.Where(t => t.Screenshot == shot)) tile.Thumbnail = null;
                bitmap?.Dispose();
            }
            store.Delete(shot);
        }
    }

    private async Task CreateAlbumAsync(IReadOnlyList<LiveryScreenshot>? shots = null)
    {
        if (_mainViewModel.Screenshots is not { } store) return;
        string? name = await NameDialog.AskAsync(this, Strings.AlbumsNew, Strings.AlbumNameLabel, "", AlbumNameProblem(store, null), Strings.AlbumCreate);
        if (name is null || store.CreateAlbum(name) is not { } album) return;
        if (shots is { Count: > 0 }) store.SetInAlbum(shots, album.Id, true);
    }

    private async Task RenameAlbumAsync(CollectionSidebarItem item)
    {
        if (_mainViewModel.Screenshots is not { } store) return;
        string? name = await NameDialog.AskAsync(this, Strings.AlbumRenameTitle, Strings.AlbumNameLabel, item.Name, AlbumNameProblem(store, item.Id));
        if (name is not null) store.RenameAlbum(item.Id, name);
    }

    private async Task DeleteAlbumAsync(CollectionSidebarItem item)
    {
        if (_mainViewModel.Screenshots is not { } store) return;
        if (!await ConfirmDialog.AskAsync(this, Strings.AlbumDeleteTitle, string.Format(Strings.AlbumDeleteMessageFormat, item.Name),
                Strings.ButtonDelete, Strings.ButtonCancel)) return;
        if (_album == item.Id) _album = AllAlbumsId;
        store.DeleteAlbum(item.Id);
    }

    private static Func<string, string?> AlbumNameProblem(ScreenshotStore store, string? except) => name =>
        CollectionNames.Clean(name) is not { } clean ? Strings.CollectionNameEmpty
        : CollectionNames.IsTaken(clean, store.Albums().Select(a => (a.Id, a.Name)), except) ? Strings.AlbumNameTaken
        : null;
}
