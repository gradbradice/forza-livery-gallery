using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

/// <summary>A Shift-click range of cards, as in Explorer. Pure</summary>
internal static class GallerySelection
{
    /// <summary>
    /// The cards of <paramref name="shown"/> from the one named <paramref name="anchorFolderName"/> to
    /// <paramref name="target"/>, both included, in the order on screen (either may come first). No anchor, or one not
    /// shown (filtered out, its group folded): <paramref name="target"/> alone
    /// </summary>
    public static IReadOnlyList<LiveryEntry> Range(IReadOnlyList<LiveryEntry> shown, string? anchorFolderName, LiveryEntry target)
    {
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(target);
        int to = IndexOf(shown, target.FolderName);
        int from = anchorFolderName is null ? -1 : IndexOf(shown, anchorFolderName);
        if (to < 0 || from < 0) return [target];
        int start = Math.Min(from, to), end = Math.Max(from, to);
        return [.. shown.Skip(start).Take(end - start + 1)];
    }

    private static int IndexOf(IReadOnlyList<LiveryEntry> shown, string folderName)
    {
        for (int i = 0; i < shown.Count; i++)
            if (string.Equals(shown[i].FolderName, folderName, StringComparison.Ordinal)) return i;
        return -1;
    }
}
