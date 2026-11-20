using ForzaToolkit.Core.Collections;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

/// <summary>A Shift-click range of cards, as in Explorer (the rules: Collections.ExplorerSelection). Pure</summary>
internal static class GallerySelection
{
    /// <summary>
    /// The cards of <paramref name="shown"/> from the one named <paramref name="anchorFolderName"/> to
    /// <paramref name="target"/>, both included, in the order on screen (either may come first). No anchor, or one not
    /// shown (filtered out, its group folded): <paramref name="target"/> alone
    /// </summary>
    public static IReadOnlyList<LiveryEntry> Range(IReadOnlyList<LiveryEntry> shown, string? anchorFolderName, LiveryEntry target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var anchor = anchorFolderName is null ? null : shown.FirstOrDefault(e => string.Equals(e.FolderName, anchorFolderName, StringComparison.Ordinal));
        return Rules.Range(shown, anchor, target);
    }

    private static readonly ExplorerSelection<LiveryEntry> Rules = new(SameLivery);

    /// <summary>Two cards of the same livery: the same folder (a card after a rescan is another object)</summary>
    public static bool SameLivery(LiveryEntry a, LiveryEntry b) => string.Equals(a.FolderName, b.FolderName, StringComparison.Ordinal);
}
