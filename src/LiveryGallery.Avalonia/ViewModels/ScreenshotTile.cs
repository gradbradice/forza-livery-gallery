using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Services.Collections;
using LiveryGallery.Services.Screenshots;

namespace LiveryGallery.ViewModels;

/// <summary>
/// A screenshot in the gallery's "Screenshots" view: the picture and a line under it (the time when its group is the
/// livery, else the livery). Selected as the cards are (<see cref="ItemSelection{T}"/>)
/// </summary>
internal sealed partial class ScreenshotTile(LiveryScreenshot screenshot, LiveryEntry entry) : ObservableObject, ISelectableItem
{
    public LiveryScreenshot Screenshot { get; } = screenshot;
    public LiveryEntry Entry { get; } = entry;

    /// <summary>The line under the picture; the grouping sets it</summary>
    [ObservableProperty]
    private string _caption = "";

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>The cursor of the keys is on it</summary>
    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>The livery's card shows this screenshot</summary>
    [ObservableProperty]
    private bool _isCover;
}
