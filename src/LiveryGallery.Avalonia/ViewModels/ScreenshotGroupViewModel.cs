using LiveryGallery.ViewModels.Collections;

namespace LiveryGallery.ViewModels;

/// <summary>A group of the "Screenshots" view: its heading and its tiles</summary>
internal sealed class ScreenshotGroupViewModel(string key, string title, string? subtitle, string countText, LiveryEntry? livery,
    IReadOnlyList<ScreenshotTile> tiles) : CollectionGroupViewModel(key, title, subtitle, countText)
{
    /// <summary>The livery of a group by livery ("Open the livery", the small picture); null otherwise</summary>
    public LiveryEntry? Livery { get; } = livery;
    public IReadOnlyList<ScreenshotTile> Tiles { get; } = tiles;
}
