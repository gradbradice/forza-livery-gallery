using ForzaToolkit.Games;

namespace LiveryGallery.Configuration;

/// <summary>
/// Where one game's data lives: <c>%APPDATA%\ForzaLiveryGallery\games\&lt;game&gt;\</c>. What belongs to the liveries
/// of the game (previews, favorites, notes, locks, tags, the duplicate check) is in it; what belongs to one account of
/// the game is one level further down. The archive and the backups are kept by account and never shared between two
/// accounts or two games: the XUID of a player is the same in every game, so an account's folder is always inside its
/// game's. The scan cache's folder (<see cref="Accounts"/>) is not where the archive and the backups go, because
/// forgetting an account deletes that folder whole.
/// </summary>
internal sealed class GamePaths
{
    /// <summary>
    /// The folder of the archive and the backups whose account is not known (made by 1.6.0 and earlier before it was
    /// recorded, with more than one account): not an account key, which is hex, <c>steam_…</c> or <c>path_…</c>
    /// </summary>
    public const string Unassigned = "unassigned";

    public GamePaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    /// <summary>The folder of <paramref name="game"/> among the program's data</summary>
    public static GamePaths For(ForzaGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return new GamePaths(Path.Combine(AppSettings.GamesPath, game.Id));
    }

    public string Root { get; }

    /// <summary>The 3D view's camera left on each car of the game (Services/ViewerCameraStore.cs)</summary>
    public string ViewerCameras => Path.Combine(Root, Services.ViewerCameraStore.FileName);

    /// <summary>The previews of the game's liveries, those the game made and those drawn in 3D</summary>
    public string Thumbs => Path.Combine(Root, "thumbs");

    /// <summary>Previews of liveries removed while the gallery was closed, kept until the next launch</summary>
    public string ChangesPreviews => Path.Combine(Root, ".changes-thumbs");

    /// <summary>The scan cache and the journal, one folder per account (ForzaToolkit.Liveries' <c>LiveryCacheStore</c>)</summary>
    public string Accounts => Path.Combine(Root, "accounts");

    /// <summary>What the duplicate check of the game's files keeps</summary>
    public string DuplicateInputs => Path.Combine(Root, "duplicate-inputs");

    /// <summary>Every account's archive, one folder each</summary>
    public string ArchivesRoot => Path.Combine(Root, "archive");

    /// <summary>Every account's backups, one folder each</summary>
    public string BackupsRoot => Path.Combine(Root, "backups");

    /// <summary>Every account's storage of liveries beyond the game's limit (docs/storage.md), one folder each</summary>
    public string StoragesRoot => Path.Combine(Root, "storage");

    /// <summary>Every account's PNG previews of the liveries in its backups, one folder each</summary>
    public string BackupThumbsRoot => Path.Combine(Root, "backup-thumbs");

    /// <summary>The archive of one account (or <see cref="Unassigned"/>)</summary>
    public string ArchiveOf(string accountKey) => Path.Combine(ArchivesRoot, Checked(accountKey));

    /// <summary>The storage of one account: an archive kept for good, in folders</summary>
    public string StorageOf(string accountKey) => Path.Combine(StoragesRoot, Checked(accountKey));

    /// <summary>The backups of one account (or <see cref="Unassigned"/>)</summary>
    public string BackupsOf(string accountKey) => Path.Combine(BackupsRoot, Checked(accountKey));

    /// <summary>The PNG previews of one account's backups</summary>
    public string BackupThumbsOf(string accountKey) => Path.Combine(BackupThumbsRoot, Checked(accountKey));

    /// <summary>The folders every start needs</summary>
    public void Create()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Thumbs);
        Directory.CreateDirectory(Accounts);
    }

    /// <summary>An account key that can name a folder: one plain name, nothing that leads out of its parent</summary>
    public static bool IsFolderName(string? accountKey) =>
        !string.IsNullOrWhiteSpace(accountKey)
        && accountKey is not ("." or "..")
        && accountKey.Trim() == accountKey
        && accountKey.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && accountKey.IndexOfAny(['\\', '/', ':']) < 0;

    private static string Checked(string accountKey) => IsFolderName(accountKey)
        ? accountKey
        : throw new ArgumentException($"'{accountKey}' cannot name an account's folder", nameof(accountKey));
}
