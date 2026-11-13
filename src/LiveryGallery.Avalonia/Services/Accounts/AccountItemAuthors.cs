using Microsoft.Extensions.Logging;

namespace LiveryGallery.Services.Accounts;

/// <summary>
/// The authors of the save's vinyl groups, tunes and base liveries: with the liveries' they tell the account's name
/// (<see cref="ViewModels.MainViewModel.AccountDisplayName"/>), so an account that has published only groups or tunes is
/// named too. The game writes a base livery for each car it gets, with the account's id and its gamertag of that moment:
/// after a rename, the next car tells the new name. The headers are found by <see cref="SaveItemHeaders.Find"/> and read by the game's reader
/// (<see cref="Profiles.GameProfile.ReadItemAuthor"/>: fdata). A header is read again only when its file changed; what
/// is kept is the last save's. Thread-safe; does disk work
/// </summary>
/// <param name="readHeader">Who made an item, from its header file; null when it is not a header</param>
internal sealed class AccountItemAuthors(Func<string, SaveItemAuthor?> readHeader)
{
    private readonly Lock _lock = new();
    private readonly ILogger _logger = AppLog.LoggerFactory.CreateLogger<AccountItemAuthors>();
    private Dictionary<string, Known> _known = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct Known(long Length, DateTime WrittenUtc, SaveItemAuthor? Author);

    /// <summary>The kinds of items read: vinyl groups, tunes and base liveries (the other liveries are the scan's)</summary>
    public static bool IsWanted(string key) => SaveKeys.IsGroup(key) || SaveKeys.IsTuning(key) || SaveKeys.IsBaseLivery(key);

    /// <summary>The authors of the vinyl groups, tunes and base liveries in the save's data folder; empty without one</summary>
    public IReadOnlyList<SaveItemAuthor> Read(string? saveDataPath)
    {
        if (saveDataPath is null) return [];
        Dictionary<string, Known> known;
        lock (_lock) known = _known;
        var next = new Dictionary<string, Known>(StringComparer.OrdinalIgnoreCase);
        var authors = new List<SaveItemAuthor>();
        foreach (var (_, path) in SaveItemHeaders.Find(saveDataPath, IsWanted, _logger))
        {
            FileInfo file;
            try
            {
                file = new FileInfo(path);
                if (!file.Exists) continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }
            var entry = known.TryGetValue(path, out var before) && before.Length == file.Length && before.WrittenUtc == file.LastWriteTimeUtc
                ? before
                : new Known(file.Length, file.LastWriteTimeUtc, readHeader(path));
            next[path] = entry;
            if (entry.Author is { } author) authors.Add(author);
        }
        lock (_lock) _known = next;
        return authors;
    }
}
