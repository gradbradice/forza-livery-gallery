using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>A badge a livery has: which, its number (cars, how alike), its name and what it means for this livery</summary>
internal sealed record LiveryBadge(CardBadge Badge, string? Number, string Name, string? Detail)
{
    /// <summary>The number after the name in a list (" · 87%"), empty without one</summary>
    public string NumberText => Number is null ? "" : " · " + Number;

    /// <summary>"Name: what it means" for a tooltip, the name alone when there is nothing more to say</summary>
    public string Tooltip => string.IsNullOrEmpty(Detail) ? Name : $"{Name}\n{Detail}";
}

/// <summary>
/// The badges of a livery, the same everywhere they are shown (the card, the livery window, the changes since the last
/// launch): which ones a livery has, in their order, and their look (<see cref="BadgeChip"/>)
/// </summary>
internal static class LiveryBadges
{
    public const int Count = (int)CardBadge.ReadPartial + 1;

    /// <summary>
    /// Each badge's look: its group (over the picture or under it), icon, colour (the fill of a fact, the line of an
    /// assumption) and, on a fill, the icon's colour when it is not white
    /// </summary>
    public static readonly (bool Top, string Icon, string Colour, bool Outlined, string? Ink)[] Looks =
    [
        (true, "IconBadgeInstalled", "BadgeInstalledBrush", false, null),
        (true, "IconBadgeDuplicate", "BadgeDangerBrush", false, null),
        (true, "IconBadgePossibleDuplicate", "BadgeWarningInk", true, null),
        (true, "IconBadgeGenerated", "BadgeNeutralInk", true, null),
        (true, "IconBadgePaintOnly", "BadgeNeutralBrush", false, null),
        (true, "IconBadgeAuction", "BadgeAuctionBrush", false, null),
        (true, "IconBadgeMine", "BadgeMineBrush", false, null),
        (false, "IconBadgeGame", "BadgeGameBrush", false, null),
        (false, "IconBadgeGame", "BadgeGameInk", true, null),
        (false, "IconBadgeStorage", "BadgeStorageBrush", false, null),
        (false, "IconBadgeStorage", "BadgeStorageInk", true, null),
        (false, "IconBadgeAddedByHand", "BadgeStorageBrush", false, null),
        (false, "IconBadgeReadError", "BadgeProblemBrush", false, "BadgeProblemErrorInk"),
        (false, "IconBadgeReadPartial", "BadgeProblemBrush", false, "BadgeProblemWarningInk"),
    ];

    public static bool IsTop(CardBadge badge) => Looks[(int)badge].Top;

    /// <summary>The livery has the badge. <paramref name="onCard"/>: the card's own "mine" (it follows the gallery's account)</summary>
    public static bool Has(LiveryEntry e, CardBadge badge, bool onCard = false) => badge switch
    {
        CardBadge.Installed => e.IsInstalled,
        CardBadge.Duplicate => e.IsDuplicate,
        CardBadge.PossibleDuplicate => e.IsPossibleDuplicate,
        CardBadge.Generated => e.IsPossiblyGenerated,
        CardBadge.PaintOnly => e.HasNoLayers,
        CardBadge.Auction => e.IsAuction,
        CardBadge.Mine => onCard ? e.ShowMineBadge : e.IsMine,
        CardBadge.CopyInGame => e.ShowCopyInGame && e.CopyKind == CopyKind.Same,
        CardBadge.SimilarInGame => e.ShowCopyInGame && e.CopyKind == CopyKind.Similar,
        CardBadge.CopyInStorage => e.ShowCopyInStorage && e.CopyKind == CopyKind.Same,
        CardBadge.SimilarInStorage => e.ShowCopyInStorage && e.CopyKind == CopyKind.Similar,
        CardBadge.AddedByHand => e.IsAddedByHand,
        CardBadge.ReadError => e.HasParseError,
        CardBadge.ReadPartial => e.HasParseWarning,
        _ => false,
    };

    /// <summary>The badge's name: the first line of its tooltip, its line in "+N", its title in the livery window</summary>
    public static string Name(LiveryEntry e, CardBadge badge) => badge switch
    {
        CardBadge.Installed => Strings.InstalledBadgeLabel,
        CardBadge.Duplicate => Strings.DuplicateBadgeLabel,
        CardBadge.PossibleDuplicate => Strings.PossibleDuplicateBadgeLabel,
        CardBadge.Generated => Strings.PossiblyGeneratedBadgeLabel,
        CardBadge.PaintOnly => Strings.PaintBadgeLabel,
        CardBadge.Auction => Strings.AuctionBadgeLabel,
        CardBadge.Mine => Strings.MyLiveryBadgeLabel,
        CardBadge.CopyInGame or CardBadge.SimilarInGame or CardBadge.CopyInStorage or CardBadge.SimilarInStorage => e.CopyText ?? "",
        CardBadge.AddedByHand => Strings.AddedByHandBadgeLabel,
        CardBadge.ReadError => Strings.ParseErrorBadgeLabel,
        _ => Strings.ParsePartialBadgeLabel,
    };

    /// <summary>What the badge means for this livery</summary>
    public static string? Detail(LiveryEntry e, CardBadge badge) => badge switch
    {
        CardBadge.Installed => e.InstalledBadgeTooltip,
        CardBadge.Duplicate or CardBadge.PossibleDuplicate => e.DuplicateMatchTooltip,
        CardBadge.Generated => Strings.PossiblyGeneratedBadgeTooltip,
        CardBadge.PaintOnly => Strings.PaintBadgeTooltip,
        CardBadge.Auction => Strings.AuctionBadgeTooltip,
        CardBadge.Mine => Strings.MyLiveryBadgeTooltip,
        CardBadge.CopyInGame or CardBadge.SimilarInGame or CardBadge.CopyInStorage or CardBadge.SimilarInStorage => e.CopyTooltip,
        CardBadge.AddedByHand => Strings.AddedByHandBadgeTooltip,
        _ => e.ParseIssueTooltip,
    };

    /// <summary>The number beside the icon: the cars it is on (one too), how alike the closest similar livery is</summary>
    public static string? Number(LiveryEntry e, CardBadge badge) => badge switch
    {
        CardBadge.Installed => e.InstalledCarCount.ToString(AppLocalisationService.Culture),
        CardBadge.PossibleDuplicate => e.PossibleDuplicatePercentText,
        _ => null,
    };

    /// <summary>Every badge the livery has, in their order (the top group first), with its texts as they are now</summary>
    public static IReadOnlyList<LiveryBadge> Of(LiveryEntry e, bool onCard = false)
    {
        var list = new List<LiveryBadge>();
        for (int i = 0; i < Count; i++)
        {
            var badge = (CardBadge)i;
            if (Has(e, badge, onCard)) list.Add(new LiveryBadge(badge, Number(e, badge), Name(e, badge), Detail(e, badge)));
        }
        return list;
    }
}
