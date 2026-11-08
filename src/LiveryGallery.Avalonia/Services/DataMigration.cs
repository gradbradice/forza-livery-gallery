using System.Diagnostics;
using System.Text.Json;
using LiveryGallery.Configuration;

namespace LiveryGallery.Services;

/// <summary>What <see cref="DataMigration.Run"/> did</summary>
/// <param name="Copied">The old folder was copied</param>
/// <param name="Notes">What is worth a line in the log: counts, where liveries without an account went</param>
/// <param name="Error">Why the copy failed; it is tried again on the next start</param>
internal sealed record DataMigrationReport(bool Copied, IReadOnlyList<string> Notes, Exception? Error = null)
{
    public static DataMigrationReport Nothing { get; } = new(false, []);
}

/// <summary>
/// The first start of 1.7.0: the data of FH6 Livery Gallery (<c>%APPDATA%\FH6LiveryGallery</c>, one game, one archive
/// and one set of backups for every account) is copied into the folders of Forza Livery Gallery
/// (<c>%APPDATA%\ForzaLiveryGallery</c>): the settings to the root, everything of the liveries to
/// <c>games\fh6\</c>, the archive and the backups split by the account each entry remembers. The old folder is only
/// read: 1.6.0 keeps working and nothing is lost if this fails. The copy is made in a folder beside the new one and
/// moved in at the end, and the marker file is written last: a copy that broke off is made again on the next start.
/// Once the marker is there, nothing is copied again.
/// </summary>
internal static class DataMigration
{
    /// <summary>Written into the new folder when the copy is complete: from where, when, by which version</summary>
    public const string MarkerFileName = "migrated-from.json";

    /// <summary>What goes to the root of the new folder; everything else is the game's</summary>
    private static readonly string[] RootFiles = ["settings.json", "update-check.meta.json"];

    /// <summary>Not copied: the new version starts its own log</summary>
    private static readonly string[] Skipped = ["logs"];

    // 1.6.0's names, inside its folder
    private const string OldArchive = ".archive";
    private const string OldBackups = ".backups";
    private const string OldBackupThumbs = ".backup-thumbs";

    private static readonly JsonSerializerOptions IndexJson = new() { WriteIndented = true };

    /// <summary>The old folder when the copy at this start failed, for the main window to say so; null otherwise</summary>
    public static string? FailedFrom { get; private set; }

    /// <summary>Copies <paramref name="oldRoot"/> into <paramref name="newRoot"/> when it was not done yet</summary>
    /// <param name="oldRoot">The folder of 1.6.0</param>
    /// <param name="newRoot">The program's folder now</param>
    /// <param name="gameId">The game whose data 1.6.0 kept (<c>fh6</c>)</param>
    /// <param name="version">The version that copies, written into the marker</param>
    public static DataMigrationReport Run(string oldRoot, string newRoot, string gameId, string version)
    {
        oldRoot = Path.GetFullPath(oldRoot);
        newRoot = Path.GetFullPath(newRoot);
        if (File.Exists(Path.Combine(newRoot, MarkerFileName)) || !HasData(oldRoot)) return DataMigrationReport.Nothing;

        string staging = newRoot.TrimEnd(Path.DirectorySeparatorChar) + ".migrating";
        var notes = new List<string>();
        var watch = Stopwatch.StartNew();
        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); // the rest of a copy that broke off
            Directory.CreateDirectory(staging);
            var game = new GamePaths(Path.Combine(staging, "games", gameId));
            Directory.CreateDirectory(game.Root);

            var accounts = KnownAccounts(oldRoot);
            string withoutAccount = accounts.Count == 1 ? accounts.First() : GamePaths.Unassigned;

            foreach (string path in Directory.EnumerateFileSystemEntries(oldRoot))
            {
                string name = Path.GetFileName(path);
                if (Skipped.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                if (name.Equals(OldArchive, StringComparison.OrdinalIgnoreCase))
                    notes.Add(SplitArchive(path, game, withoutAccount));
                else if (name.Equals(OldBackups, StringComparison.OrdinalIgnoreCase))
                    notes.Add(SplitBackups(path, Path.Combine(oldRoot, OldBackupThumbs), game, withoutAccount));
                else if (name.Equals(OldBackupThumbs, StringComparison.OrdinalIgnoreCase))
                    continue; // with the backups that use them
                else if (RootFiles.Contains(name, StringComparer.OrdinalIgnoreCase) && File.Exists(path))
                    File.Copy(path, Path.Combine(staging, name));
                else
                    CopyEntry(path, Path.Combine(game.Root, name));
            }

            File.WriteAllText(Path.Combine(staging, MarkerFileName), JsonSerializer.Serialize(new MigrationMarker(oldRoot, DateTime.UtcNow, version), IndexJson));
            MoveIn(staging, newRoot);
            notes.Add($"Copied '{oldRoot}' into '{newRoot}' in {watch.ElapsedMilliseconds} ms; accounts known: {accounts.Count}");
            return new DataMigrationReport(true, notes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            TryDelete(staging);
            FailedFrom = oldRoot;
            return new DataMigrationReport(false, notes, ex);
        }
    }

    /// <summary>The marker of a finished copy, when there is one</summary>
    public static MigrationMarker? ReadMarker(string newRoot)
    {
        try
        {
            string path = Path.Combine(newRoot, MarkerFileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<MigrationMarker>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool HasData(string root)
    {
        try
        {
            return Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root)
                .Any(p => !Skipped.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The accounts 1.6.0 knew: those with a scan cache, and those its archive and backups name</summary>
    private static HashSet<string> KnownAccounts(string oldRoot)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string accounts = Path.Combine(oldRoot, "accounts");
        if (Directory.Exists(accounts))
            keys.UnionWith(Directory.EnumerateDirectories(accounts).Select(Path.GetFileName).OfType<string>().Where(GamePaths.IsFolderName));
        foreach (var entry in ReadArchiveIndex(Path.Combine(oldRoot, OldArchive)).Values)
            if (AccountOf(entry.SourceAccount) is { } key) keys.Add(key);
        foreach (string file in BackupFiles(Path.Combine(oldRoot, OldBackups)))
            if (AccountOf(LiveryBackups.ReadManifest(file)?.SourceAccount) is { } key) keys.Add(key);
        keys.Remove(GamePaths.Unassigned);
        return keys;
    }

    private static string? AccountOf(AccountRef? account) =>
        account?.Key is { } key && GamePaths.IsFolderName(key) && !key.Equals(GamePaths.Unassigned, StringComparison.OrdinalIgnoreCase)
            ? key
            : null;

    /// <summary>Each archived livery into the archive of its account, with its entry in that archive's index</summary>
    private static string SplitArchive(string oldArchive, GamePaths game, string withoutAccount)
    {
        if (!Directory.Exists(oldArchive)) return "No archive";
        var index = ReadArchiveIndex(oldArchive);
        var byAccount = new Dictionary<string, Dictionary<string, ArchivedLiveryEntry>>(StringComparer.OrdinalIgnoreCase);
        int folders = 0, unassigned = 0;
        foreach (string folder in Directory.EnumerateDirectories(oldArchive))
        {
            string key = Path.GetFileName(folder);
            index.TryGetValue(key, out var entry);
            string account = AccountOf(entry?.SourceAccount) ?? withoutAccount;
            if (account == GamePaths.Unassigned) unassigned++;
            CopyDirectory(folder, Path.Combine(game.ArchiveOf(account), key));
            folders++;
            if (entry is null) continue; // the archive finds the folder and gives it an entry from its key, as before
            if (!byAccount.TryGetValue(account, out var entries))
                byAccount[account] = entries = new Dictionary<string, ArchivedLiveryEntry>(StringComparer.OrdinalIgnoreCase);
            entries[key] = entry;
        }
        foreach (var (account, entries) in byAccount)
            File.WriteAllText(Path.Combine(game.ArchiveOf(account), LiveryArchive.IndexFileName), JsonSerializer.Serialize(entries, IndexJson));
        return $"Archive: {folders} liveries into {byAccount.Count} accounts' archives, {unassigned} without an account";
    }

    /// <summary>Each backup into the backups of its account, with the PNG previews its liveries use</summary>
    private static string SplitBackups(string oldBackups, string oldThumbs, GamePaths game, string withoutAccount)
    {
        int files = 0, unassigned = 0;
        foreach (string file in BackupFiles(oldBackups))
        {
            var manifest = LiveryBackups.ReadManifest(file);
            string account = AccountOf(manifest?.SourceAccount) ?? withoutAccount;
            if (account == GamePaths.Unassigned) unassigned++;
            string folder = game.BackupsOf(account);
            Directory.CreateDirectory(folder);
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), overwrite: true);
            files++;

            if (manifest is null || !Directory.Exists(oldThumbs)) continue;
            foreach (string? preview in manifest.Entries.Select(e => e.ThumbnailPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (preview is null || preview != Path.GetFileName(preview) || preview.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                string source = Path.Combine(oldThumbs, preview);
                string destination = Path.Combine(game.BackupThumbsOf(account), preview);
                if (!File.Exists(source) || File.Exists(destination)) continue;
                Directory.CreateDirectory(game.BackupThumbsOf(account));
                File.Copy(source, destination);
            }
        }
        return $"Backups: {files} into their accounts' folders, {unassigned} without an account";
    }

    private static Dictionary<string, ArchivedLiveryEntry> ReadArchiveIndex(string oldArchive)
    {
        string path = Path.Combine(oldArchive, LiveryArchive.IndexFileName);
        try
        {
            if (File.Exists(path)
                && JsonSerializer.Deserialize<Dictionary<string, ArchivedLiveryEntry>>(File.ReadAllText(path)) is { } index)
                return new Dictionary<string, ArchivedLiveryEntry>(index, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            // An unreadable index: the folders are copied, and the archive finds them again from their keys
        }
        return new Dictionary<string, ArchivedLiveryEntry>(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> BackupFiles(string folder) => Directory.Exists(folder)
        ? Directory.EnumerateFiles(folder, "*" + LiveryBackups.FileExtension)
        : [];

    private static void CopyEntry(string source, string destination)
    {
        if (Directory.Exists(source)) CopyDirectory(source, destination);
        else File.Copy(source, destination, overwrite: true);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (string folder in Directory.EnumerateDirectories(source))
            CopyDirectory(folder, Path.Combine(destination, Path.GetFileName(folder)));
    }

    /// <summary>
    /// The finished copy into the program's folder: renamed whole when there is none yet. When there is one (a start
    /// before whose copy failed), the copy is merged into it: a folder of both is gone through file by file, so what
    /// that start made in it stays (a livery it archived, a backup it made: an archive's and the backups' folders find
    /// their liveries again by their folders); a file of both is the copy's, and that start's is kept aside
    /// (<c>.before-migration-&lt;time&gt;</c>, by the same path), not deleted
    /// </summary>
    private static void MoveIn(string staging, string newRoot)
    {
        if (!Directory.Exists(newRoot))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(newRoot)!);
            Directory.Move(staging, newRoot);
            return;
        }

        string aside = Path.Combine(newRoot, $".before-migration-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
        var entries = Directory.EnumerateFileSystemEntries(staging)
            .OrderBy(p => Path.GetFileName(p).Equals(MarkerFileName, StringComparison.OrdinalIgnoreCase)) // the marker last
            .ToList();
        foreach (string path in entries) Merge(path, Path.Combine(newRoot, Path.GetFileName(path)), Path.Combine(aside, Path.GetFileName(path)));
        Directory.Delete(staging, recursive: true);
    }

    // One entry of the copy into the program's folder: see MoveIn
    private static void Merge(string source, string target, string aside)
    {
        if (Directory.Exists(source))
        {
            if (File.Exists(target)) Keep(target, aside);
            if (!Directory.Exists(target))
            {
                Directory.Move(source, target);
                return;
            }
            foreach (string entry in Directory.EnumerateFileSystemEntries(source).ToList())
                Merge(entry, Path.Combine(target, Path.GetFileName(entry)), Path.Combine(aside, Path.GetFileName(entry)));
            return;
        }
        if (Directory.Exists(target) || File.Exists(target)) Keep(target, aside);
        File.Move(source, target);
    }

    // What a start made where the copy goes: kept aside by the same path
    private static void Keep(string target, string aside)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(aside)!);
        if (Directory.Exists(target)) Directory.Move(target, aside);
        else File.Move(target, aside);
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Deleted on the next attempt
        }
    }
}

/// <summary>The contents of <see cref="DataMigration.MarkerFileName"/></summary>
internal sealed record MigrationMarker(string From, DateTime AtUtc, string Version);
