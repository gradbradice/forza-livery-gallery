using ForzaToolkit.Liveries.History;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

/// <summary>A livery like the one shown: an exact copy or a possible duplicate, with how alike they are</summary>
internal sealed record SimilarLivery(LiveryEntry Entry, int Percent, bool IsExact);

/// <summary>One line of a livery's history: when, and what happened, in the interface language</summary>
internal sealed record LiveryHistoryLine(DateTime AtLocal, string Text);

/// <summary>How many backups hold the livery as it is now, of how many, and the newest of them</summary>
internal readonly record struct BackupPresence(int With, int Total, DateTime? LastUtc);

/// <summary>What the livery window shows besides the livery's own data: other liveries, history, backups. Pure</summary>
internal static class LiveryDetails
{
    /// <summary>A relation with this score or more is an exact copy (as the free-space planner counts it)</summary>
    private const double ExactScore = 0.9999;

    /// <summary>The author's other liveries in the gallery, the newest first; none for an unknown author</summary>
    public static List<LiveryEntry> OtherByAuthor(LiveryEntry entry, IEnumerable<LiveryEntry> all)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(all);
        if (entry.AuthorKey is not { } key) return [];
        return [.. all
            .Where(e => !ReferenceEquals(e, entry) && e.FolderName != entry.FolderName
                && string.Equals(e.AuthorKey, key, StringComparison.Ordinal))
            .OrderByDescending(e => e.DownloadDate ?? DateTime.MinValue)
            .ThenBy(e => e.FolderName, StringComparer.Ordinal)];
    }

    /// <summary>How many other liveries of the gallery are for the same car; 0 for an unknown car</summary>
    public static int SameCarCount(LiveryEntry entry, IEnumerable<LiveryEntry> all)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(all);
        if (entry.CarId <= 0) return 0;
        return all.Count(e => e.CarId == entry.CarId && e.FolderName != entry.FolderName);
    }

    /// <summary>
    /// The liveries like this one, as the duplicate check found them (either side of the relation): exact copies first,
    /// then the possible duplicates from <paramref name="thresholdPercent"/> up, the closest first
    /// </summary>
    public static List<SimilarLivery> Similar(LiveryEntry entry, IReadOnlyCollection<LiveryEntry> all, int thresholdPercent)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(all);
        var byKey = new Dictionary<string, LiveryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in all) byKey.TryAdd(e.FolderName, e);
        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        void Add(string key, double? score)
        {
            if (string.Equals(key, entry.FolderName, StringComparison.OrdinalIgnoreCase)) return;
            double s = score ?? 1;
            if (!scores.TryGetValue(key, out var known) || s > known) scores[key] = s;
        }
        foreach (var relation in entry.PossibleDuplicateOf ?? []) Add(relation.Id, relation.Score);
        foreach (var other in all)
            foreach (var relation in other.PossibleDuplicateOf ?? [])
                if (string.Equals(relation.Id, entry.FolderName, StringComparison.OrdinalIgnoreCase))
                    Add(other.FolderName, relation.Score);

        return [.. scores
            .Where(kv => byKey.ContainsKey(kv.Key))
            .Select(kv => new SimilarLivery(byKey[kv.Key], (int)Math.Round(kv.Value * 100), kv.Value >= ExactScore))
            .Where(s => s.IsExact || s.Percent >= thresholdPercent)
            .OrderByDescending(s => s.IsExact)
            .ThenByDescending(s => s.Percent)
            .ThenBy(s => s.Entry.FolderName, StringComparer.Ordinal)];
    }

    /// <summary>
    /// What happened to the livery by the journal (its key, or the key it moved from), the newest first: added, archived,
    /// restored, changed, moved. The journal's own notes that it was there (its first look, backups and the archive
    /// telling it was in the save) say nothing that happened and are left out
    /// </summary>
    public static List<LiveryHistoryLine> History(LiveryEntry entry, IEnumerable<LiveryHistoryEvent> events, int max = 8)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(events);
        return [.. events
            .Where(e => e.Kind is not (LiveryHistoryKind.Journal or LiveryHistoryKind.Baseline or LiveryHistoryKind.Seen)
                && (string.Equals(e.Key, entry.FolderName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(e.PreviousKey, entry.FolderName, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.AtUtc)
            .Take(max)
            .Select(e => new LiveryHistoryLine(e.AtUtc.ToLocalTime(), Describe(e)))];
    }

    /// <summary>One event in words</summary>
    public static string Describe(LiveryHistoryEvent e) => e.Kind switch
    {
        LiveryHistoryKind.Added => e.Cause switch
        {
            LiveryHistoryCause.RestoredFromArchive => Strings.HistoryRestoredFromArchive,
            LiveryHistoryCause.RestoredFromStorage => Strings.HistoryRestoredFromStorage,
            LiveryHistoryCause.Returned => Strings.HistoryReturned,
            _ => Strings.HistoryAdded,
        },
        LiveryHistoryKind.Removed => e.Cause switch
        {
            LiveryHistoryCause.Archived => Strings.HistoryArchived,
            LiveryHistoryCause.Stored => Strings.HistoryStored,
            _ => Strings.HistoryRemoved,
        },
        LiveryHistoryKind.Changed => Strings.HistoryChanged,
        LiveryHistoryKind.Moved => Strings.HistoryMoved,
        _ => e.Kind.ToString(),
    };

    /// <summary>
    /// The backups that hold the livery as it is now (the same <c>C_livery</c>; by its key when it has no hash), out of
    /// every backup that could be read
    /// </summary>
    public static BackupPresence Backups(LiveryEntry entry, IEnumerable<LiveryBackupManifest?> manifests)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(manifests);
        int with = 0, total = 0;
        DateTime? last = null;
        foreach (var manifest in manifests)
        {
            if (manifest is null) continue;
            total++;
            bool holds = manifest.Entries.Any(d => entry.CLiveryHash is { } hash
                ? string.Equals(d.CLiveryHash, hash, StringComparison.OrdinalIgnoreCase)
                : string.Equals(d.FolderName, entry.FolderName, StringComparison.OrdinalIgnoreCase));
            if (!holds) continue;
            with++;
            if (last is null || manifest.CreatedAtUtc > last) last = manifest.CreatedAtUtc;
        }
        return new BackupPresence(with, total, last);
    }
}
