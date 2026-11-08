using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Controller;
using LiveryGallery.Services.Screenshots;

namespace LiveryGallery.ViewModels;

/// <summary>
/// The user's screenshots of each livery: the store, and the cards kept in step with it (the camera mark with the
/// count, the screenshot chosen as the cover)
/// </summary>
internal sealed partial class MainViewModel
{
    /// <summary>The user's screenshots of each livery; null when the app keeps none</summary>
    public ScreenshotStore? Screenshots => _scanService.Screenshots;

    private void InitializeScreenshots()
    {
        if (Screenshots is { } store) store.Changed += key => Dispatcher.UIThread.Post(() => ApplyScreenshots(key));
    }

    /// <summary>The cards of a livery (it may be on screen once) after its screenshots changed</summary>
    private void ApplyScreenshots(string liveryKey)
    {
        if (Screenshots is not { } store) return;
        var summary = store.Summary(liveryKey);
        foreach (var entry in Gallery.AllEntries)
        {
            if (!string.Equals(entry.FolderName, liveryKey, StringComparison.OrdinalIgnoreCase)) continue;
            entry.ScreenshotCount = summary.Count;
            if (entry.CoverPath == summary.CoverPath) continue;
            entry.CoverPath = summary.CoverPath;
            ThumbnailLifecycleController.ReloadThumbnailsOf(entry);
        }
        ScreenshotsChanged?.Invoke(liveryKey);
    }

    /// <summary>The card's button by the camera mark: the game's preview back instead of the cover screenshot</summary>
    [RelayCommand]
    private void RestorePreview(LiveryEntry? entry)
    {
        if (entry is { HasCover: true } && Screenshots is { } store) store.SetCover(entry.FolderName, null);
    }

    /// <summary>A livery's screenshots changed (the open livery window and the screenshots view follow)</summary>
    public event Action<string>? ScreenshotsChanged;
}
