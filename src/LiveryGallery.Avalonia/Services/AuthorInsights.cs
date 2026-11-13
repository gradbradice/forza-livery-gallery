using System.Globalization;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

/// <summary>
/// What the artwork sketches tell about the shown liveries (ForzaToolkit.Liveries: <see cref="AuthorHints"/>): who a
/// livery whose own author id says nothing may be by. Worded for the cards; recomputed whenever the entries change.
/// (The same artwork on an author's other cars is not looked for since ForzaToolkit 0.16.0: the matches were not
/// reliable enough yet.)
/// </summary>
internal static class AuthorInsights
{
    /// <summary>The livery as the hints see it: its author is its assigned card, else its account's card, else its account</summary>
    public static AuthorHintInput ToInput(LiveryEntry entry, AuthorCardService cards) => new(
        entry.FolderName, entry.CLiveryHash, entry.CarId, entry.AuthorIdentityTagHex, entry.Data.AuthorRaw,
        entry.AssignedCardId ?? (entry.AuthorIdentityTagHex is { } tag ? cards.FindCardForIdentityTag(tag)?.Id : null),
        entry.Sketch);

    /// <summary>
    /// The key of a livery of the other place (the storage while the game is shown, the game while the storage is): a
    /// livery may be in both under the same key, and the hints tell them apart by it
    /// </summary>
    public const string OtherPlacePrefix = "other:";

    /// <summary>
    /// Sets <see cref="LiveryEntry.AuthorHints"/> of every entry. <paramref name="others"/>: the liveries of the other
    /// place (the storage, or the game), which only give hints: a livery of an unknown author in the game is hinted by the
    /// same artwork in the storage too. A copy of the same file never hints itself
    /// </summary>
    public static void Apply(IReadOnlyList<LiveryEntry> entries, AuthorCardService cards, IReadOnlyList<LiveryEntry>? others = null)
    {
        others ??= [];
        var inputs = entries.Select(e => ToInput(e, cards))
            .Concat(others.Select(e => ToInput(e, cards) with { Key = OtherPlacePrefix + e.FolderName }))
            .ToList();
        var byKey = ByKey(entries, others);
        var names = new AuthorNames([.. entries, .. others], cards);
        var hints = AuthorHints.ForLiveries(inputs, cards.GetRememberedTags(), cards.GetDismissedHints());

        foreach (var entry in entries)
        {
            entry.AuthorHints = hints.TryGetValue(entry.FolderName, out var given)
                ? [.. given.Select(h => View(h, names, byKey))]
                : [];
        }
    }

    /// <param name="entries">The liveries shown</param>
    /// <param name="others">Those of the other place, under <see cref="OtherPlacePrefix"/></param>
    public static Dictionary<string, LiveryEntry> ByKey(IReadOnlyList<LiveryEntry> entries, IReadOnlyList<LiveryEntry>? others = null)
    {
        var byKey = new Dictionary<string, LiveryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) byKey[entry.FolderName] = entry;
        foreach (var entry in others ?? []) byKey[OtherPlacePrefix + entry.FolderName] = entry;
        return byKey;
    }

    public static AuthorHintView View(AuthorHint hint, AuthorNames names, IReadOnlyDictionary<string, LiveryEntry> byKey)
    {
        string target = names.Of(hint.TargetId);
        return new AuthorHintView(hint, target, $"{target}: {Reason(hint, byKey)}");
    }

    /// <summary>Why the hint was given, in the user's language</summary>
    public static string Reason(AuthorHint hint, IReadOnlyDictionary<string, LiveryEntry> byKey) => hint.Reason switch
    {
        AuthorHintReason.SameInvalidId => Strings.AuthorHintReasonSameId,
        AuthorHintReason.SameName => Strings.AuthorHintReasonSameName,
        _ => Artwork(hint, byKey),
    };

    // "the artwork is 92% similar to “Rising Sun”"; a livery of the other place says where it is ("… in the storage")
    private static string Artwork(AuthorHint hint, IReadOnlyDictionary<string, LiveryEntry> byKey)
    {
        string percent = ((int)(hint.Score * 100)).ToString(CultureInfo.InvariantCulture);
        if (hint.EvidenceKey is not { } key || !byKey.TryGetValue(key, out var evidence))
            return string.Format(Strings.AuthorHintReasonArtworkFormat, percent, hint.EvidenceKey);
        string format = !key.StartsWith(OtherPlacePrefix, StringComparison.Ordinal) ? Strings.AuthorHintReasonArtworkFormat
            : evidence.IsStored ? Strings.AuthorHintReasonArtworkInStorageFormat
            : Strings.AuthorHintReasonArtworkInGameFormat;
        return string.Format(format, percent, Label(evidence));
    }

    /// <summary>A livery as a list names it: its name, else its number, else its key</summary>
    public static string Label(LiveryEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Data.LiveryName) ? entry.Data.LiveryName
        : entry.LiveryId > 0 ? entry.LiveryIdText
        : entry.FolderName;

    /// <summary>The names authors are shown under: a card's own name, an account's latest name</summary>
    internal sealed class AuthorNames
    {
        private readonly AuthorCardService _cards;
        private readonly Dictionary<string, string> _latestByTag = new(StringComparer.OrdinalIgnoreCase);

        public AuthorNames(IReadOnlyList<LiveryEntry> entries, AuthorCardService cards)
        {
            _cards = cards;
            var latestDate = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (!AuthorAccounts.IsAccount(entry.AuthorIdentityTagHex) || entry.Data.IsAuthorUnknown
                    || AuthorAccounts.IsBlankName(entry.Data.AuthorRaw)) continue;
                var date = entry.DownloadDate ?? DateTime.MinValue;
                string tag = entry.AuthorIdentityTagHex!;
                if (latestDate.TryGetValue(tag, out var known) && known > date) continue;
                latestDate[tag] = date;
                _latestByTag[tag] = entry.Data.AuthorRaw.Trim();
            }
        }

        /// <summary>The names an account used, for a card made on the fly: just the latest one is known here</summary>
        public IReadOnlyList<string> NamesOf(string identityTag) =>
            _latestByTag.TryGetValue(identityTag, out var name) ? [name] : [];

        /// <summary>The name of a card id or of <c>account:&lt;tag&gt;</c></summary>
        public string Of(string authorId)
        {
            string prefix = AuthorCard.ImplicitId("");
            if (authorId.StartsWith(prefix, StringComparison.Ordinal))
            {
                string tag = authorId[prefix.Length..];
                if (_cards.FindCardForIdentityTag(tag) is { } card) return card.DisplayName;
                return _latestByTag.TryGetValue(tag, out var name) ? name : tag;
            }
            return _cards.FindCard(authorId)?.DisplayName ?? authorId;
        }

        /// <summary>The card a hint's target stands for: a saved card, or one made on the fly for an account</summary>
        public AuthorCard? CardOf(string authorId)
        {
            string prefix = AuthorCard.ImplicitId("");
            if (!authorId.StartsWith(prefix, StringComparison.Ordinal)) return _cards.FindCard(authorId);
            string tag = authorId[prefix.Length..];
            return _cards.GetCardForAccount(tag, NamesOf(tag));
        }
    }
}
