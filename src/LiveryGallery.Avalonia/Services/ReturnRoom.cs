using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

/// <summary>What "Return to the game" does when the game has too little room (docs/storage.md, stage 3)</summary>
internal enum ReturnRoomMode
{
    /// <summary>As many of the game's liveries as are missing go to the storage, and all come back</summary>
    Exchange,

    /// <summary>All come back, beyond the game's limit</summary>
    All,

    /// <summary>Only as many as fit, the first in the window's order</summary>
    Fit,
}

/// <summary>The user's choice in the window of room</summary>
/// <param name="Mode">What to do</param>
/// <param name="ToStorage">For <see cref="ReturnRoomMode.Exchange"/>: the game's liveries that make room</param>
/// <param name="FolderId">The storage folder they go into; null with <paramref name="NewFolderName"/> or for none</param>
/// <param name="NewFolderName">A folder to make for them (or the one of that name, when there is one)</param>
internal sealed record ReturnRoomChoice(ReturnRoomMode Mode, IReadOnlyList<LiveryEntry> ToStorage, string? FolderId, string? NewFolderName);

/// <summary>
/// The rules of the room when liveries come back from the storage: how many slots are missing, and which liveries of
/// the game may give theirs. Never one on a car, an auction livery, a locked one, a favorite or the player's own; the
/// ones added to the game first are offered first
/// </summary>
internal static class ReturnRoom
{
    /// <summary>The slots missing for <paramref name="toReturn"/> liveries with <paramref name="free"/> free</summary>
    public static int Missing(int toReturn, int free) => Math.Max(0, toReturn - Math.Max(0, free));

    /// <summary>The livery may go to the storage to make room</summary>
    public static bool MayMakeRoom(LiveryEntry entry) =>
        !entry.IsInstalled && !entry.IsAuction && !entry.IsLocked && !entry.IsFavorite && !entry.IsMine && !entry.IsStored;

    /// <summary>The game's liveries that may make room, the ones added to the game first at the top (no date: last)</summary>
    public static List<LiveryEntry> Candidates(IEnumerable<LiveryEntry> game) =>
    [
        .. game.Where(MayMakeRoom)
            .OrderBy(e => e.DownloadDate is null)
            .ThenBy(e => e.DownloadDate)
            .ThenBy(e => e.FolderName, StringComparer.Ordinal),
    ];

    /// <summary>
    /// <paramref name="selected"/> in the order the window shows them (<paramref name="shown"/>); those not shown (a folded
    /// group) after them, as they were
    /// </summary>
    public static List<LiveryEntry> InShownOrder(IReadOnlyList<LiveryEntry> selected, IReadOnlyList<LiveryEntry> shown)
    {
        var place = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < shown.Count; i++) place.TryAdd(shown[i].FolderName, i);
        return [.. selected.Select((e, i) => (e, i)).OrderBy(x => place.GetValueOrDefault(x.e.FolderName, int.MaxValue)).ThenBy(x => x.i).Select(x => x.e)];
    }
}
