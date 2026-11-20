using ForzaToolkit.Saves;
using LiveryGallery.Profiles;

namespace LiveryGallery.Services;

/// <summary>
/// Finds the profile save of a save folder: what the livery server reads the installed liveries and the player's name
/// from. Open: it only looks in the save, whichever client sends the file. By the save's layout:
/// <list type="bullet">
/// <item><see cref="GameSaveLayout.LiveryFolders"/> (FH6's PC GameSave): <c>ContainersRoot\User_&lt;hex&gt;\C_ProfileData</c>;</item>
/// <item><see cref="GameSaveLayout.SaveItems"/> (FH3 to FH5): a Steam save's flat <c>User_&lt;hex&gt;.ProfileData</c>, a
/// Microsoft Store save's part <c>ProfileData</c> of the item <c>User_&lt;hex&gt;</c> (a file of its container). Not the
/// backups (<c>User_&lt;hex&gt;_Backup</c>); the account's own when the save tells whose it is, else the newest.</item>
/// </list>
/// </summary>
/// <param name="saves">The game's save folders: whose save a folder is</param>
/// <param name="layout">How the game keeps its saves</param>
internal sealed class ProfileDataLocator(IGameSaveFolders saves, GameSaveLayout layout = GameSaveLayout.LiveryFolders)
{
    public const string ProfileDataFileName = "C_ProfileData";
    public const string ProfileUserFolderPrefix = "User_";

    /// <summary>FH3 to FH5: the part of the item <c>User_&lt;hex&gt;</c> that is the profile (Steam: the file's extension)</summary>
    public const string ProfileDataPartName = "ProfileData";

    /// <summary>The profile save to send; null when there is none</summary>
    /// <param name="saveDataPath">Where the save's liveries are (<see cref="IGameSaveFolders.FindDataRoot"/>)</param>
    public string? FindProfileData(string? saveDataPath)
    {
        if (string.IsNullOrEmpty(saveDataPath) || !Directory.Exists(saveDataPath)) return null;
        return layout == GameSaveLayout.SaveItems ? FindInSaveItems(saveDataPath) : FindInContainersRoot(saveDataPath);
    }

    /// <summary>
    /// Which build's files the profile of <paramref name="saveDataPath"/> is (FH3 to FH5: the Steam and the Microsoft
    /// Store builds keep and encrypt it differently, the server is told): a save with <c>containers.index</c> is a
    /// Microsoft Store one, flat files are Steam's, also in a folder chosen by hand. Null for FH6 (both builds alike)
    /// and when there is no folder
    /// </summary>
    public SaveStoreKind? StoreKindOf(string? saveDataPath)
    {
        if (layout != GameSaveLayout.SaveItems || string.IsNullOrEmpty(saveDataPath) || !Directory.Exists(saveDataPath)) return null;
        return SaveItemSources.IsContainerSave(saveDataPath) ? SaveStoreKind.MicrosoftStore : SaveStoreKind.SteamCloud;
    }

    /// <summary>True for an item that is a profile, <c>User_&lt;hex&gt;</c> (not its backup)</summary>
    public static bool IsProfileItem(string key) => XboxUserId.TryParseHexFolderName(key, ProfileUserFolderPrefix) is not null;

    /// <summary>True for a Steam save's profile file, <c>User_&lt;hex&gt;.ProfileData</c></summary>
    public static bool IsFlatProfileFile(string fileName) =>
        FlatSaveItems.Split(fileName) is { } item
        && string.Equals(item.Part, ProfileDataPartName, StringComparison.OrdinalIgnoreCase)
        && IsProfileItem(item.Key);

    // FH3 to FH5: in the folder of the liveries; a Steam save whose liveries are one level down (FH5's account folder
    // holding them) keeps its profile beside that folder
    private string? FindInSaveItems(string saveDataPath)
    {
        try
        {
            if (FindProfileItem(saveDataPath) is { } here) return here;
            string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(saveDataPath));
            if (parent is not null && !SaveItemSources.IsContainerSave(saveDataPath)
                && string.Equals(FlatLiveryFiles.FindDataRoot(parent), saveDataPath, StringComparison.OrdinalIgnoreCase))
                return FindProfileItem(parent);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppLog.LogErrorThrottled(saveDataPath, $"Failed to look for the profile save in '{saveDataPath}'", ex);
            return null;
        }
    }

    private string? FindProfileItem(string folder)
    {
        var items = SaveItemSources.Open(folder);
        var profiles = items.ListKeys().Where(IsProfileItem)
            .Select(key => (Key: key, Path: items.Find(key)?.Part(ProfileDataPartName)?.Path))
            .Where(p => p.Path is not null && File.Exists(p.Path))
            .ToList();
        if (profiles.Count == 0) return null;
        if (profiles.Count > 1 && saves.TryGetUserId(folder) is { } userId
            && profiles.FirstOrDefault(p => XboxUserId.TryParseHexFolderName(p.Key, ProfileUserFolderPrefix) == userId).Path is { } own)
            return own;
        return profiles.OrderByDescending(p => File.GetLastWriteTimeUtc(p.Path!)).First().Path;
    }

    // saveDataPath = ...\u_<decUserId>_<gameId>\<containerId>\ContainersRoot
    // Profile lives in ContainersRoot\User_<hexUserId>\C_ProfileData
    private string? FindInContainersRoot(string saveDataPath)
    {
        try
        {
            if (ResolveUserId(saveDataPath) is not { } userId)
            {
                AppLog.LogErrorThrottled($"{saveDataPath}|ProfileUserId",
                    $"Cannot determine the UserId for '{saveDataPath}', so {ProfileUserFolderPrefix}<hex> cannot be located",
                    new InvalidOperationException("ResolveSaveIdentity returned null"));
                return null;
            }

            string? userDir = PcGameSaveStore.FindUserFolder(saveDataPath, userId);
            if (userDir is null)
            {
                AppLog.LogErrorThrottled($"{saveDataPath}|ProfileUserDir",
                    $"Folder '{ProfileUserFolderPrefix}{userId:X}' (UserId {userId}) was not found in '{saveDataPath}'",
                    new DirectoryNotFoundException(Path.Combine(saveDataPath, $"{ProfileUserFolderPrefix}{userId:X}")));
                return null;
            }

            var file = new FileInfo(Path.Combine(userDir, ProfileDataFileName));
            return file.Exists ? file.FullName : null;
        }
        catch (Exception ex)
        {
            AppLog.LogErrorThrottled(saveDataPath, $"Failed to look for {ProfileDataFileName} in '{saveDataPath}'", ex);
            return null;
        }
    }

    private ulong? ResolveUserId(string saveDataPath)
    {
        var containerDir = Directory.GetParent(Path.TrimEndingDirectorySeparator(saveDataPath));
        var uFolder = containerDir?.Parent;
        if (containerDir is null || uFolder is null) return null;
        int? containerId = int.TryParse(containerDir.Name, out int id) ? id : null;
        return saves.ResolveUserId(uFolder.FullName, containerId);
    }
}
