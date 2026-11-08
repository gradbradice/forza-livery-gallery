namespace LiveryGallery.Services;

/// <summary>Why a livery cannot go from the game into the storage</summary>
internal enum StorageBlock
{
    None,

    /// <summary>The user locked it: a locked livery never leaves the game</summary>
    Locked,

    /// <summary>A car uses it (the livery server's list)</summary>
    Installed,

    /// <summary>An auction livery: never stored (docs/storage.md)</summary>
    Auction,
}

/// <summary>
/// Which liveries may go into the storage. Stricter than the archive (<see cref="ArchiveRules"/>): an auction livery
/// never goes, whatever the list of cars says. A livery on a car cannot be told when that list is not known; the
/// caller asks the user then, as before the archive. Pure
/// </summary>
internal static class StorageRules
{
    /// <param name="isLocked">The user locked it</param>
    /// <param name="isAuction">A <c>SoulBoundLivery_</c> folder</param>
    /// <param name="isInstalled">The livery server's list says a car uses it</param>
    public static StorageBlock Check(bool isLocked, bool isAuction, bool isInstalled) =>
        isLocked ? StorageBlock.Locked
        : isInstalled ? StorageBlock.Installed
        : isAuction ? StorageBlock.Auction
        : StorageBlock.None;
}
