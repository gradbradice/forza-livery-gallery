using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Services.Screenshots;

namespace LiveryGallery.ViewModels;

/// <summary>A screenshot in the livery window's strip: its small picture, and whether it is the card's cover</summary>
internal sealed partial class ScreenshotItem(LiveryScreenshot? screenshot, bool isCover) : ObservableObject
{
    /// <summary>The tile after the screenshots that adds more (a click, or files dropped on the window)</summary>
    public static ScreenshotItem AddTile() => new(null, false);

    public LiveryScreenshot? Screenshot { get; } = screenshot;
    public bool IsCover { get; } = isCover;
    public bool IsAddTile => Screenshot is null;
    public bool IsShot => Screenshot is not null;
    public string? ToolTip => Screenshot?.FileName;

    [ObservableProperty]
    private Bitmap? _thumbnail;
}

/// <summary>The livery window's screenshots: the strip under the picture, adding (files, a pasted picture), the cover</summary>
internal sealed partial class LiveryDetailsViewModel
{
    private CancellationTokenSource _screenshotsCts = new();
    private string? _screenshotsKey;
    private bool _watchingScreenshots;
    // The window closed: a change of the store told after it (a picture still being added) is let be
    private bool _screenshotsClosed;

    public ObservableCollection<ScreenshotItem> Screenshots { get; } = [];

    /// <summary>Where the screenshots are kept; null when the app keeps none</summary>
    public ScreenshotStore? ScreenshotStore => _source.Screenshots;

    /// <summary>The window has a store to keep screenshots in (else the strip is hidden)</summary>
    public bool CanAddScreenshots => _source.Screenshots is not null;

    [ObservableProperty] private string _screenshotsCountText = "";

    /// <summary>The albums with screenshots of this livery ("In albums: Best · 2"); a click shows the album in the gallery</summary>
    public ObservableCollection<ScreenshotAlbumCount> ScreenshotAlbums { get; } = [];

    [ObservableProperty] private bool _hasScreenshotAlbums;
    [ObservableProperty] private string _screenshotsError = "";

    /// <summary>The current livery's screenshots, for the viewer</summary>
    public IReadOnlyList<LiveryScreenshot> ScreenshotList => [.. Screenshots.Select(s => s.Screenshot).OfType<LiveryScreenshot>()];

    private void LoadScreenshots(LiveryEntry entry)
    {
        if (_source.Screenshots is not { } store) return;
        if (!_watchingScreenshots)
        {
            _watchingScreenshots = true;
            store.Changed += OnScreenshotsChanged;
            store.AlbumsChanged += OnAlbumsChanged;
        }
        _screenshotsKey = entry.FolderName;
        ScreenshotsError = "";
        _screenshotsCts.Cancel();
        _screenshotsCts.Dispose();
        _screenshotsCts = new CancellationTokenSource();
        _ = FillScreenshotsAsync(store, entry.FolderName, _screenshotsCts.Token);
    }

    private void OnAlbumsChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (_screenshotsClosed) return;
        if (_source.Screenshots is { } store && _screenshotsKey is { } key) FillAlbums(store, key);
    });

    private void FillAlbums(ScreenshotStore store, string key)
    {
        ScreenshotAlbums.Clear();
        foreach (var album in store.AlbumsOfLivery(key)) ScreenshotAlbums.Add(album);
        HasScreenshotAlbums = ScreenshotAlbums.Count > 0;
    }

    private void OnScreenshotsChanged(string key) => Dispatcher.UIThread.Post(() =>
    {
        if (_screenshotsClosed) return;
        if (_source.Screenshots is { } store && string.Equals(key, _screenshotsKey, StringComparison.OrdinalIgnoreCase))
        {
            _screenshotsCts.Cancel();
            _screenshotsCts.Dispose();
            _screenshotsCts = new CancellationTokenSource();
            _ = FillScreenshotsAsync(store, key, _screenshotsCts.Token);
        }
    });

    private async Task FillScreenshotsAsync(ScreenshotStore store, string key, CancellationToken ct)
    {
        IReadOnlyList<LiveryScreenshot> shots;
        string? cover;
        try
        {
            (shots, cover) = await Task.Run(() => (store.List(key), store.CoverPath(key)), ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogError($"Livery window: failed to list the screenshots of '{key}'", ex);
            return;
        }
        if (ct.IsCancellationRequested) return;
        DisposeScreenshotThumbnails();
        Screenshots.Clear();
        foreach (var shot in shots)
            Screenshots.Add(new ScreenshotItem(shot, string.Equals(shot.Path, cover, StringComparison.OrdinalIgnoreCase)));
        Screenshots.Add(ScreenshotItem.AddTile());
        ScreenshotsCountText = shots.Count > 0 ? shots.Count.ToString(AppLocalisationService.Culture) : "";
        FillAlbums(store, key);
        OnPropertyChanged(nameof(ScreenshotList));
        foreach (var item in Screenshots.Where(s => s.Screenshot is not null).ToList())
        {
            if (ct.IsCancellationRequested) return;
            var thumb = await Task.Run(() => DecodeStripTile(item.Screenshot!.Path), CancellationToken.None).ConfigureAwait(true);
            if (ct.IsCancellationRequested) { thumb?.Dispose(); return; }
            if (Screenshots.Contains(item)) item.Thumbnail = thumb;
            else thumb?.Dispose();
        }
    }

    // A tile of the strip is 116 wide: enough pixels for a screen scaled up to 200 %, scaled down smoothly
    private static Avalonia.Media.Imaging.Bitmap? DecodeStripTile(string path)
    {
        try
        {
            return ScreenshotStore.DecodeSmaller(path, ScreenshotStore.PixelWidth(116, 2));
        }
#pragma warning disable CA1031 // a broken file is a tile without a picture
        catch (Exception ex)
#pragma warning restore CA1031
        {
            AppLog.LogErrorThrottled(path, $"Failed to read the screenshot '{path}'", ex);
            return null;
        }
    }

    private (ScreenshotFormat Format, int Quality) FormatNow() => _source.ScreenshotFormat?.Invoke() ?? (ScreenshotFormat.Original, 90);

    /// <summary>Picture files the user chose or dropped; those that are not pictures are said under the strip</summary>
    public async Task AddScreenshotFilesAsync(IReadOnlyList<string> paths)
    {
        if (_source.Screenshots is not { } store || paths.Count == 0) return;
        string key = Entry.FolderName;
        var (format, quality) = FormatNow();
        var skipped = await Task.Run(() =>
        {
            var notPictures = new List<string>();
            foreach (string path in paths)
                if (store.AddFile(key, path, format, quality) is null) notPictures.Add(Path.GetFileName(path));
            return notPictures;
        }).ConfigureAwait(true);
        ScreenshotsError = skipped.Count == 0 ? "" : string.Format(Strings.ScreenshotsSkippedFormat, string.Join(", ", skipped));
    }

    /// <summary>A picture from the clipboard (a snip, a copied image): kept as PNG, or as WebP</summary>
    public async Task AddScreenshotPictureAsync(byte[] png)
    {
        if (_source.Screenshots is not { } store) return;
        string key = Entry.FolderName;
        var (format, quality) = FormatNow();
        var added = await Task.Run(() => store.Add(key, png, ".png", format, quality)).ConfigureAwait(true);
        ScreenshotsError = added is null ? string.Format(Strings.ScreenshotsSkippedFormat, "Ctrl+V") : "";
    }

    private void DisposeScreenshotThumbnails()
    {
        foreach (var item in Screenshots)
        {
            item.Thumbnail?.Dispose();
            item.Thumbnail = null;
        }
    }

    private void DisposeScreenshots()
    {
        _screenshotsClosed = true;
        _screenshotsCts.Cancel();
        _screenshotsCts.Dispose();
        if (_watchingScreenshots && _source.Screenshots is { } store)
        {
            store.Changed -= OnScreenshotsChanged;
            store.AlbumsChanged -= OnAlbumsChanged;
        }
        DisposeScreenshotThumbnails();
    }
}
