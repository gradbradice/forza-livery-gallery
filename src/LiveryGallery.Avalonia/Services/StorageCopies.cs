using ForzaToolkit.Liveries.Scanning;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

/// <summary>How a livery of one place (the game, the storage) matches one of the other</summary>
internal enum CopyKind
{
    None,

    /// <summary>The same livery: the same file of the same car, or the same design by the duplicate check</summary>
    Same,

    /// <summary>A similar one, as the duplicate check finds a possible duplicate (at the threshold the settings set)</summary>
    Similar,
}

/// <summary>A livery of the other place this one matches</summary>
internal sealed record CopyMark(CopyKind Kind, LiveryEntry Other);

/// <summary>What <see cref="StorageCopies.Find"/> found</summary>
/// <param name="Stored">The duplicate check among the storage's liveries alone, by key (a missing key: no duplicate)</param>
/// <param name="StoredInGame">Stored liveries the game has too (or a similar one), by the stored key</param>
/// <param name="GameInStorage">The game's liveries the storage has too, by the game's key</param>
internal sealed record StorageCopiesResult(
    IReadOnlyDictionary<string, LiveryDuplicateOutcome> Stored,
    IReadOnlyDictionary<string, CopyMark> StoredInGame,
    IReadOnlyDictionary<string, CopyMark> GameInStorage);

/// <summary>
/// The storage's duplicates (docs/storage.md, stage 5): among its own liveries as the scan finds them among the game's,
/// and between the two places, "already in the game" and "already in the storage". The same rule as the scan: only
/// liveries of one car and one author are compared, by the game's duplicate check; without one, and always first, the
/// same livery file of the same car
/// </summary>
internal static class StorageCopies
{
    /// <param name="stored">The storage's liveries</param>
    /// <param name="game">The game's liveries</param>
    /// <param name="detector">The game's duplicate check; null: only the same files count</param>
    /// <param name="liveryPath">Where a livery's file is (the save or the storage); null when it cannot be read</param>
    /// <param name="thresholdPercent">A similar livery counts from this score on (the settings' threshold)</param>
    public static StorageCopiesResult Find(IReadOnlyList<LiveryEntry> stored, IReadOnlyList<LiveryEntry> game,
        ILiveryDuplicateDetector? detector, Func<LiveryEntry, string?> liveryPath, int thresholdPercent)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(game);
        var within = new Dictionary<string, LiveryDuplicateOutcome>(StringComparer.OrdinalIgnoreCase);
        var storedInGame = new Dictionary<string, CopyMark>(StringComparer.OrdinalIgnoreCase);
        var gameInStorage = new Dictionary<string, CopyMark>(StringComparer.OrdinalIgnoreCase);
        var gameLiveries = game.Where(e => !e.IsAuction).ToList();

        // The same file of the same car: in both places, and twice in the storage
        static (int, string)? FileOf(LiveryEntry e) => e.CLiveryHash is { Length: > 0 } hash ? (e.CarId, hash) : null;
        var gameByFile = gameLiveries.Where(e => FileOf(e) is not null).ToLookup(e => FileOf(e)!.Value);
        var storedByFile = stored.Where(e => FileOf(e) is not null).ToLookup(e => FileOf(e)!.Value);
        foreach (var group in storedByFile)
        {
            var members = group.ToList();
            if (gameByFile[group.Key].FirstOrDefault() is { } inGame)
                foreach (var member in members) storedInGame[member.FolderName] = new CopyMark(CopyKind.Same, inGame);
            foreach (var g in gameByFile[group.Key]) gameInStorage[g.FolderName] = new CopyMark(CopyKind.Same, members[0]);
            if (members.Count < 2) continue;
            foreach (var member in members)
                within[member.FolderName] = new LiveryDuplicateOutcome(DuplicateStatus.Duplicate,
                    [.. members.Where(m => m != member).Select(m => new DuplicateRelation(m.FolderName, null))]);
        }
        if (detector is null) return new StorageCopiesResult(within, storedInGame, gameInStorage);

        var storedKeys = stored.Select(e => e.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byKey = stored.Concat(gameLiveries).GroupBy(e => e.FolderName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var groups = stored.Concat(gameLiveries)
            .GroupBy(e => (e.CarId, Author: (e.AuthorKey ?? "").ToUpperInvariant()))
            .Where(g => g.Count() > 1 && g.Any(e => storedKeys.Contains(e.FolderName)));
        foreach (var group in groups)
        {
            var inputs = new List<(LiveryEntry Entry, LiveryDuplicateInput Input)>();
            foreach (var entry in group)
                if (liveryPath(entry) is { } path)
                    inputs.Add((entry, new LiveryDuplicateInput(entry.FolderName, entry.CarId, group.Key.Author, entry.CLiveryHash, path, null)));
            try
            {
                // Among the storage's own: the statuses its cards show
                var own = inputs.Where(i => storedKeys.Contains(i.Entry.FolderName)).Select(i => i.Input).ToList();
                if (own.Count > 1)
                    foreach (var (key, outcome) in detector.Detect(own))
                        if (outcome.Status != DuplicateStatus.Ok) within[key] = outcome;

                // Between the two places: the other's liveries this one relates to
                if (inputs.All(i => storedKeys.Contains(i.Entry.FolderName))) continue;
                foreach (var (key, outcome) in detector.Detect([.. inputs.Select(i => i.Input)]))
                {
                    bool isStored = storedKeys.Contains(key);
                    var marks = isStored ? storedInGame : gameInStorage;
                    if (marks.TryGetValue(key, out var mark) && mark.Kind == CopyKind.Same) continue;
                    var other = (outcome.Related ?? [])
                        .Where(r => storedKeys.Contains(r.Id) != isStored && byKey.ContainsKey(r.Id))
                        .OrderByDescending(r => r.Score ?? 1)
                        .FirstOrDefault();
                    if (other.Id is null) continue;
                    var kind = outcome.Status == DuplicateStatus.Duplicate ? CopyKind.Same
                        : PossibleDuplicateThreshold.Reaches(other.Score, thresholdPercent) ? CopyKind.Similar
                        : CopyKind.None;
                    if (kind != CopyKind.None) marks[key] = new CopyMark(kind, byKey[other.Id]);
                }
            }
#pragma warning disable CA1031 // a group that fails is left without marks; the others go on, as in the scan
            catch (Exception ex)
#pragma warning restore CA1031
            {
                AppLog.LogError($"The duplicate check of the storage failed for car {group.Key.CarId}", ex);
            }
        }
        return new StorageCopiesResult(within, storedInGame, gameInStorage);
    }
}
