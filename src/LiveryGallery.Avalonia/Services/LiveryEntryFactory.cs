using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

internal sealed class LiveryEntryFactory(
    AppCacheService appCacheService,
    CarDatabaseService carDatabaseService,
    FavoriteService favoriteService,
    LockService lockService,
    TagService tagService,
    AuthorCardService authorCardService,
    LiveryIdService liveryIdService,
    GameProfile profile,
    NoteService? noteService = null,
    Screenshots.ScreenshotStore? screenshots = null)
{
    private volatile AuthorNameInference _authorInference = AuthorNameInference.Empty;

    // Kept next to the author cards: "Name (2)" stays the same author in every app and from run to run
    private AuthorNameNumbers AuthorNumbers => AuthorContext.NumbersFor(authorCardService);

    public AuthorNameInference AuthorInference => _authorInference;

    public void ReconcileAuthorCards(IEnumerable<LiveryCacheEntry> cacheEntries)
    {
        var list = cacheEntries as IReadOnlyCollection<LiveryCacheEntry> ?? [.. cacheEntries];
        _authorInference = AuthorNameInference.Build(list, PlaceholderTexts.IsAuthorMissing);
        authorCardService.ReconcileAutoCards(list
            .Where(c => !PlaceholderTexts.IsAuthorMissing(c))
            .Select(c => new AuthorObservation(c.Author, c.AuthorIdentityTagHex, c.DownloadDate)));
    }

    public (string Name, bool IsInferred, bool IsUnknown) ResolveRawAuthor(LiveryCacheEntry c) =>
        _authorInference.Resolve(c, tag => authorCardService.FindCardForIdentityTag(tag)?.KnownNames
            .LastOrDefault(n => !string.IsNullOrWhiteSpace(n)));

    /// <summary>The card the user assigned the livery to, by its file's hash; null when none (or the card is gone)</summary>
    public AuthorCard? AssignedCard(LiveryCacheEntry c) =>
        authorCardService.FindAssignment(c.CLiveryHash) is { } assignment ? authorCardService.FindCard(assignment.CardId) : null;

    /// <summary>
    /// Who a livery is by: the card the user assigned it to, the account (the shown save's own account for its liveries
    /// without an id, see <see cref="OwnAccount"/>), the name as it shows, whether nothing tells, and the key
    /// (<see cref="AuthorKeys"/>)
    /// </summary>
    private (AuthorCard? Assigned, string? Account, string Name, bool IsUnknown, string? Key) Who(LiveryCacheEntry c, OwnAccount? own)
    {
        var assigned = AssignedCard(c);
        var (name, _, isUnknown) = ResolveRawAuthor(c);
        string? account = own?.AccountOf(c.AuthorIdentityTagHex, isUnknown ? null : name, profile.IsAuction(c.FolderName))
            ?? c.AuthorIdentityTagHex;
        string? key = AuthorKeys.Of(assigned?.Id, account, isUnknown ? null : name, authorCardService.FindCardForIdentityTag);
        return (assigned, account, name, isUnknown, key);
    }

    /// <summary>The author a livery's duplicates are looked for under: its key; liveries whose author nothing tells are one group</summary>
    public string DuplicateGroupAuthor(LiveryCacheEntry c, AuthorContext context) => Who(c, context.Own).Key ?? string.Empty;

    /// <summary>
    /// Who the liveries of a save are by (<see cref="AuthorContext"/>): the save's own account, and a label for every
    /// author whose name another author shares. Call after <see cref="ReconcileAuthorCards"/>
    /// </summary>
    public AuthorContext CreateAuthorContext(IEnumerable<LiveryCacheEntry> cacheEntries, ulong? currentUserId)
    {
        var cache = cacheEntries as IReadOnlyCollection<LiveryCacheEntry> ?? [.. cacheEntries];
        var own = OwnAccount.FromCache(currentUserId, cache, PlaceholderTexts.IsAuthorMissing);
        string? ownKey = own is null ? null
            : authorCardService.FindCardForIdentityTag(own.IdentityTag)?.Id ?? AuthorKeys.ForAccount(own.IdentityTag);

        var subjects = new List<AuthorLabelSubject>();
        var details = new Dictionary<string, AuthorDetails>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in cache
            .Select(c => (Entry: c, Who: Who(c, own)))
            .Where(x => x.Who.Key is not null && !x.Who.IsUnknown)
            .GroupBy(x => x.Who.Key!, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First().Who;
            var card = first.Assigned ?? (first.Account is { } tag ? authorCardService.FindCardForIdentityTag(tag) : null);
            // The nicknames its liveries show, the latest last
            var seen = group
                .Where(x => !AuthorAccounts.IsBlankName(x.Who.Name))
                .GroupBy(x => x.Who.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n.Max(x => x.Entry.DownloadDate ?? DateTime.MinValue))
                .Select(n => n.Key)
                .ToList();
            var names = card is null ? seen
                : [.. card.KnownNames.Where(n => !AuthorAccounts.IsBlankName(n)).Concat(seen).Distinct(StringComparer.OrdinalIgnoreCase)];
            string name = card?.DisplayName ?? (seen.Count > 0 ? seen[^1] : first.Name);
            DateTime? firstSeen = group.Min(x => x.Entry.DownloadDate);

            subjects.Add(new AuthorLabelSubject(
                group.Key, name, names, IsOwn: string.Equals(group.Key, ownKey, StringComparison.OrdinalIgnoreCase),
                HasAccount: !AuthorKeys.IsNameKey(group.Key), firstSeen,
                card is null ? null : [.. card.IdentityTags.Select(AuthorKeys.ForAccount)]));
            details[group.Key] = new AuthorDetails(names, group.Count(), firstSeen);
        }
        return new AuthorContext(own, AuthorLabels.Build(subjects, AuthorNumbers), details);
    }

    public void EnsureLiveryIds(IEnumerable<LiveryCacheEntry> cacheEntries) =>
        liveryIdService.EnsureAssigned(cacheEntries.Select(c => (c.FolderName, c.DownloadDate)));

    /// <param name="c">The cached livery</param>
    /// <param name="currentUserId">The shown save's account</param>
    /// <param name="authors">Who the save's liveries are by (<see cref="CreateAuthorContext"/>); names are shown as they are when null</param>
    /// <param name="known">
    /// What the gallery knew of the livery before (a stored one): its car's names when the car list does not know the
    /// car, and its number when the numbers have none
    /// </param>
    public LiveryEntry ToEntry(LiveryCacheEntry c, ulong? currentUserId, AuthorContext? authors = null, LiveryData? known = null)
    {
        authors ??= AuthorContext.Empty;
        var car = carDatabaseService.Get(c.CarId);
        var (rawAuthor, isAuthorInferred, isAuthorUnknown) = ResolveRawAuthor(c);
        var who = Who(c, authors.Own);
        var data = new LiveryData
        {
            FolderName = c.FolderName,
            LiveryName = PlaceholderTexts.IsNameMissing(c) ? string.Empty : c.LiveryName,
            TextVersion = LiveryCacheEntry.CurrentTextVersion,
            AuthorRaw = rawAuthor,
            IsAuthorInferred = isAuthorInferred,
            IsAuthorUnknown = isAuthorUnknown,
            AuthorIdentityTagHex = c.AuthorIdentityTagHex,
            CreatorUserId = c.CreatorUserId,
            IsPossiblyGenerated = c.IsPossiblyGenerated,
            HasNoLayers = c.HasNoLayers,
            ArtworkSketch = c.ArtworkSketch,
            LayerCount = c.LayerCount,
            HasParseError = c.HasParseError,
            ParseIssues = c.ParseIssues,
            CarId = c.CarId,
            CarManufacturerRaw = car?.Manufacturer ?? known?.CarManufacturerRaw ?? string.Empty,
            CarModelNameRaw = car?.Name ?? known?.CarModelNameRaw ?? string.Empty,
            CarYear = car?.Year ?? known?.CarYear,
            CarKnown = car is not null || known?.CarKnown == true,
            CreatedYear = c.CreatedYear,
            CreatedMonth = c.CreatedMonth,
            Created = c.Created,
            DownloadDate = c.DownloadDate,
            ThumbnailPath = c.ThumbnailFile is not null
                ? Path.Combine(appCacheService.ThumbsDir, c.ThumbnailFile)
                : null,
            HasThumbnail = c.ThumbnailFile is not null,
            ThumbnailDrawn = c.ThumbnailDrawn,
            ExternalPreviewPath = c.ExternalThumbSource,
            CLiveryHash = c.CLiveryHash,
            LiveryId = liveryIdService.TryGet(c.FolderName) ?? known?.LiveryId ?? 0,
            RelatedLiveryIds = BuildRelatedLiveryIds(c.PossibleDuplicateOf),
        };

        // A livery the user assigned to a card shows the card's name: its own id says nothing. An author whose name
        // another author shares shows what tells them apart
        var assignedCard = who.Assigned;
        string author = authors.LabelOf(who.Key) is { } label ? AuthorContext.Format(label)
            : assignedCard?.DisplayName ?? authorCardService.ResolveDisplayName(rawAuthor, who.Account);
        bool ownWithoutId = authors.Own is { } own && who.Account == own.IdentityTag && AuthorAccounts.Classify(c.AuthorIdentityTagHex) == AuthorIdKind.None;
        return new LiveryEntry
        {
            Data = data,
            AssignedCardId = assignedCard?.Id,
            AuthorAccountTag = who.Account,
            AuthorKey = who.Key,
            AuthorLabel = authors.LabelOf(who.Key),
            AuthorDetails = authors.LabelOf(who.Key) is { Kind: not AuthorLabelKind.Plain } ? authors.DetailsOf(who.Key) : null,
            IsOwnUnpublished = ownWithoutId,
            Author = author,
            IsMine = (currentUserId is not null && c.CreatorUserId == currentUserId) || ownWithoutId,
            Tags = tagService.GetTags(c.FolderName),
            Note = noteService?.GetNote(c.FolderName),
            ScreenshotCount = screenshots?.Summary(c.FolderName).Count ?? 0,
            CoverPath = screenshots?.Summary(c.FolderName).CoverPath,
            IsFavorite = favoriteService.IsFavorite(c.FolderName),
            IsLocked = lockService.IsLocked(c.FolderName),
            IsAuction = profile.IsAuction(c.FolderName),
            DuplicateStatus = c.DuplicateStatus,
            PossibleDuplicateOf = c.PossibleDuplicateOf,
        };
    }

    private Dictionary<string, ulong>? BuildRelatedLiveryIds(IReadOnlyList<DuplicateRelation>? relations)
    {
        if (relations is not { Count: > 0 }) return null;
        var result = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in relations)
            if (liveryIdService.TryGet(r.Id) is { } id) result[r.Id] = id;
        return result;
    }

    /// <param name="cacheEntries">The scanned cache</param>
    /// <param name="currentUserId">The shown save's account</param>
    /// <param name="ct">Stops building</param>
    /// <param name="authors">Who the liveries are by, as the entries show it</param>
    public List<LiveryEntry> BuildEntries(IEnumerable<LiveryCacheEntry> cacheEntries, ulong? currentUserId, CancellationToken ct, out AuthorContext authors)
    {
        var cacheList = cacheEntries as IReadOnlyCollection<LiveryCacheEntry> ?? [.. cacheEntries];
        ReconcileAuthorCards(cacheList);
        EnsureLiveryIds(cacheList);
        authors = CreateAuthorContext(cacheList, currentUserId);
        var entries = new List<LiveryEntry>();
        foreach (var cacheEntry in cacheList)
        {
            ct.ThrowIfCancellationRequested();
            entries.Add(ToEntry(cacheEntry, currentUserId, authors));
        }
        return entries;
    }

    /// <summary>
    /// The liveries of the storage as the grid shows them: what the gallery knew when each went there, with today's
    /// car names, tags, notes and author cards. Who they are by is worked out with the game's liveries beside them, as
    /// one collection: the save's own account and its nicknames come from the game (a livery without an id is the
    /// player's own only under a nickname the account published under), and an author's label is the same in both
    /// places. The game's author inference stays as the last scan made it
    /// </summary>
    /// <param name="stored">The storage's entries</param>
    /// <param name="currentUserId">The shown save's account</param>
    /// <param name="gameCache">The game's scanned liveries (the account's cache)</param>
    public List<LiveryEntry> BuildStoredEntries(IEnumerable<ArchivedLiveryEntry> stored, ulong? currentUserId,
        IEnumerable<LiveryCacheEntry>? gameCache = null)
    {
        var list = stored as IReadOnlyCollection<ArchivedLiveryEntry> ?? [.. stored];
        var caches = list.Select(s => ToCache(s.Data)).ToList();
        var storedKeys = caches.Select(c => c.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var authors = CreateAuthorContext([.. caches, .. (gameCache ?? []).Where(c => !storedKeys.Contains(c.FolderName))], currentUserId);
        // A livery put into the storage by hand never went through a scan of the game: its number is given here, as a
        // scan gives one (the numbers' file keeps it; one moved from the game keeps the number it had there)
        liveryIdService.EnsureAssigned(list
            .Where(s => s.Data.LiveryId == 0 && liveryIdService.TryGet(s.Data.FolderName) is null)
            .Select(s => (s.Data.FolderName, (DateTime?)(s.Data.DownloadDate ?? s.ArchivedAtUtc))));
        var entries = new List<LiveryEntry>(list.Count);
        int i = 0;
        foreach (var s in list)
        {
            var entry = ToEntry(caches[i++], currentUserId, authors, s.Data);
            entry.StoredAtUtc = s.ArchivedAtUtc;
            entry.StorageFolderId = s.FolderId;
            entries.Add(entry);
        }
        return entries;
    }

    /// <summary>A stored livery's data as a scan would have cached it, so a stored card is built as any other</summary>
    private static LiveryCacheEntry ToCache(LiveryData d) => new()
    {
        SchemaVersion = LiveryCacheEntry.CurrentSchemaVersion,
        FolderName = d.FolderName,
        LiveryName = d.LiveryName,
        Author = d.AuthorRaw,
        AuthorIdentityTagHex = d.AuthorIdentityTagHex,
        TextVersion = d.TextVersion,
        CreatorUserId = d.CreatorUserId,
        IsPossiblyGenerated = d.IsPossiblyGenerated,
        HasNoLayers = d.HasNoLayers,
        ArtworkSketch = d.ArtworkSketch,
        LayerCount = d.LayerCount,
        HasParseError = d.HasParseError,
        ParseIssues = d.ParseIssues,
        CarId = d.CarId,
        CreatedYear = d.CreatedYear ?? 0,
        CreatedMonth = d.CreatedMonth ?? 0,
        Created = d.Created,
        DownloadDate = d.DownloadDate,
        // A drawn preview that is gone (deleted when the gallery began to draw another way) is none: the card draws it again
        ThumbnailFile = d.ThumbnailPath is { } path && !(d.ThumbnailDrawn && !File.Exists(path)) ? Path.GetFileName(path) : null,
        ThumbnailDrawn = d.ThumbnailDrawn && d.ThumbnailPath is { } drawn && File.Exists(drawn),
        ExternalThumbSource = d.ExternalPreviewPath,
        CLiveryHash = d.CLiveryHash,
    };
}
