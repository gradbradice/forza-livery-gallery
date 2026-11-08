using ForzaToolkit.Liveries.Scanning;
using LiveryGallery.Localisation;

namespace LiveryGallery.Services;

/// <summary>Why a folder cannot hold an account's storage (<see cref="StoragePlaces.Check"/>)</summary>
internal enum StoragePlaceProblem
{
    None,

    /// <summary>Not a whole path (<c>D:\…</c>)</summary>
    NotAFullPath,

    /// <summary>A disk's root: everything on the disk would be looked through</summary>
    DriveRoot,

    /// <summary>A file of that name is there</summary>
    IsAFile,

    /// <summary>The game's save, or inside it</summary>
    InSave,

    /// <summary>Holds a save of the game</summary>
    HoldsSave,

    /// <summary>The program's folder (its archive, its data), or inside it</summary>
    InProgram,

    /// <summary>Holds the program's folder</summary>
    HoldsProgram,

    /// <summary>Another account's storage, inside it, or holding it</summary>
    OtherStorage,

    /// <summary>Inside the account's storage as it is now, or holding it (the same folder is no move at all)</summary>
    AroundCurrent,
}

/// <summary>A folder the storage cannot be in, and why</summary>
/// <param name="Path">The folder</param>
/// <param name="Kind">What it is</param>
/// <param name="Name">For another account's storage: whose ("bradice, FH6")</param>
internal sealed record StorageForbidden(string Path, StorageForbiddenKind Kind, string? Name = null);

internal enum StorageForbiddenKind
{
    Save,
    Program,
    OtherStorage,

    /// <summary>The account's storage where it is now</summary>
    Current,
}

/// <summary>What a folder holds already, before the storage goes there</summary>
/// <param name="Exists">The folder is there</param>
/// <param name="AtTop">Liveries at its top (under their keys or renamed)</param>
/// <param name="InFolders">Liveries in folders of liveries, one level down</param>
/// <param name="Folders">Those folders, by name</param>
/// <param name="IsStorage">A storage's index is there (one the gallery kept there before)</param>
internal sealed record StorageSurvey(bool Exists, int AtTop, int InFolders, IReadOnlyList<string> Folders, bool IsStorage)
{
    public static StorageSurvey Missing { get; } = new(false, 0, 0, [], false);

    public int Liveries => AtTop + InFolders;
}

/// <summary>
/// Where an account's storage may go (docs/storage.md, "Own folder"): the folder of an account inside a common folder
/// (<c>D:\Forza liveries\FH6\bradice (…CDEF)</c>), the checks that keep it out of the save, the program's folder and
/// another account's storage, and what a folder holds already
/// </summary>
internal static class StoragePlaces
{
    // What Windows does not take in a file name, wherever the program runs (the folders are for Windows)
    private static readonly char[] NotInNames = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>
    /// The account's folder in a common folder: its nickname and the end of its key ("bradice (…CDEF)"), "Account (…CDEF)"
    /// while the nickname is not known, " 2" (" 3"…) after it when another account has that name
    /// </summary>
    /// <param name="accountKey">The account</param>
    /// <param name="displayName">Its nickname; null when not known</param>
    /// <param name="taken">A name another account's folder has (case-insensitive)</param>
    public static string AccountFolderName(string accountKey, string? displayName, Func<string, bool>? taken = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountKey);
        string tail = AccountKey.ShortId(accountKey);
        string name = Clean(displayName) is { Length: > 0 } nick
            ? $"{nick} (…{tail})"
            : string.Format(Strings.StorageAccountFolderUnnamedFormat, tail);
        name = Clean(name) ?? tail;
        if (taken is null || !taken(name)) return name;
        for (int n = 2; ; n++)
            if (!taken($"{name} {n}")) return $"{name} {n}";
    }

    /// <summary>A name as a folder may have it: what Windows does not take becomes "_", no dot or space at its end; null when nothing is left</summary>
    public static string? Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var chars = name.Trim().Select(c => char.IsControl(c) || NotInNames.Contains(c) ? '_' : c).ToArray();
        string clean = new string(chars).TrimEnd('.', ' ');
        return clean.Length == 0 ? null : clean;
    }

    /// <summary>The account's storage in a common folder: <c>&lt;common&gt;\&lt;game&gt;\&lt;account&gt;</c></summary>
    public static string InCommonFolder(string commonFolder, string gameShortName, string accountFolderName) =>
        Path.Combine(commonFolder, Clean(gameShortName) ?? "Game", accountFolderName);

    /// <summary>Why the storage cannot be in <paramref name="folder"/>, and the forbidden place it meets; None when it can</summary>
    public static (StoragePlaceProblem Problem, StorageForbidden? With) Check(string? folder, IEnumerable<StorageForbidden> forbidden)
    {
        ArgumentNullException.ThrowIfNull(forbidden);
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) return (StoragePlaceProblem.NotAFullPath, null);
        string full;
        try
        {
            full = Trim(Path.GetFullPath(folder));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (StoragePlaceProblem.NotAFullPath, null);
        }
        if (Path.GetPathRoot(full) is { } root && Same(root, full)) return (StoragePlaceProblem.DriveRoot, null);
        if (File.Exists(full)) return (StoragePlaceProblem.IsAFile, null);
        foreach (var place in forbidden)
        {
            string other = Trim(Path.GetFullPath(place.Path));
            if (place.Kind == StorageForbiddenKind.Current && Same(full, other)) continue;
            bool inside = Same(full, other) || IsInside(full, other), holds = IsInside(other, full);
            if (!inside && !holds) continue;
            return (place.Kind switch
            {
                StorageForbiddenKind.Save => inside ? StoragePlaceProblem.InSave : StoragePlaceProblem.HoldsSave,
                StorageForbiddenKind.Program => inside ? StoragePlaceProblem.InProgram : StoragePlaceProblem.HoldsProgram,
                StorageForbiddenKind.Current => StoragePlaceProblem.AroundCurrent,
                _ => StoragePlaceProblem.OtherStorage,
            }, place);
        }
        return (StoragePlaceProblem.None, null);
    }

    /// <summary>The words of a problem: "it is the game's save" and the rule after it</summary>
    public static string Describe(StoragePlaceProblem problem, StorageForbidden? with) => problem switch
    {
        StoragePlaceProblem.NotAFullPath => Strings.StoragePlaceNotFullPath,
        StoragePlaceProblem.DriveRoot => Strings.StoragePlaceDriveRoot,
        StoragePlaceProblem.IsAFile => Strings.StoragePlaceIsFile,
        StoragePlaceProblem.InSave => Strings.StoragePlaceInSave,
        StoragePlaceProblem.HoldsSave => Strings.StoragePlaceHoldsSave,
        StoragePlaceProblem.InProgram => Strings.StoragePlaceInProgram,
        StoragePlaceProblem.HoldsProgram => Strings.StoragePlaceHoldsProgram,
        StoragePlaceProblem.OtherStorage => string.Format(Strings.StoragePlaceOtherStorageFormat, with?.Name ?? ""),
        StoragePlaceProblem.AroundCurrent => Strings.StoragePlaceAroundCurrent,
        _ => "",
    };

    /// <summary>
    /// What a folder holds already: liveries at its top (a folder with a header and a livery file, whatever its name)
    /// and in folders one level down. Only the files' presence is looked at: nothing is read
    /// </summary>
    public static StorageSurvey Survey(string folder, ILiveryStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            if (!Directory.Exists(folder)) return StorageSurvey.Missing;
            int top = 0, inner = 0;
            var folders = new List<string>();
            foreach (string dir in Directory.EnumerateDirectories(folder))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith('.')) continue;
                if (IsLivery(store, folder, name))
                {
                    top++;
                    continue;
                }
                int here = 0;
                foreach (string sub in Directory.EnumerateDirectories(dir))
                    if (IsLivery(store, dir, Path.GetFileName(sub))) here++;
                if (here == 0) continue;
                inner += here;
                folders.Add(name);
            }
            return new StorageSurvey(true, top, inner, [.. folders.Order(StringComparer.CurrentCultureIgnoreCase)],
                File.Exists(Path.Combine(folder, LiveryArchive.IndexFileName)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogErrorThrottled("storage-survey|" + folder, $"Failed to look into '{folder}'", ex);
            return new StorageSurvey(true, 0, 0, [], false);
        }
    }

    private static bool IsLivery(ILiveryStore store, string parent, string name)
    {
        if (store.GetLiveryFolder(parent, name) is null) return false;
        var files = store.GetFiles(parent, name);
        return File.Exists(files.HeaderPath) && File.Exists(files.LiveryPath);
    }

    /// <summary>How many bytes a folder's files take; 0 when it cannot be read</summary>
    public static long SizeOf(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>The disk of a path ("D:") and its free bytes; null when it cannot be told</summary>
    public static (string Disk, long Free)? FreeSpace(string folder)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (string.IsNullOrEmpty(root)) return null;
            var drive = new DriveInfo(root);
            return drive.IsReady ? (Path.TrimEndingDirectorySeparator(root), drive.AvailableFreeSpace) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Two folders on one disk: a move of the storage is a rename, not a copy</summary>
    public static bool SameDisk(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>About how long a copy of so many bytes takes to another disk (some 60 MB a second), in whole minutes, at least one</summary>
    public static int CopyMinutes(long bytes) => (int)Math.Max(1, Math.Ceiling(bytes / (60.0 * 1024 * 1024) / 60));

    /// <summary>The same folder</summary>
    public static bool Same(string a, string b) =>
        string.Equals(Trim(Path.GetFullPath(a)), Trim(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string path, string folder) =>
        Path.GetFullPath(path).StartsWith(Trim(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Trim(string path) => Path.GetPathRoot(path) is { } root && root == path ? path : Path.TrimEndingDirectorySeparator(path);
}
