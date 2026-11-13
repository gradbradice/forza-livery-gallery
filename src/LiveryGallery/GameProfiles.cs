using ForzaToolkit.Games;
using ForzaToolkit.Games.FH3;
using ForzaToolkit.Games.FH4;
using ForzaToolkit.Games.FH5;
using ForzaToolkit.Games.FH6;
using ForzaToolkit.Liveries.Formats;
using LiveryGallery.Configuration;
using LiveryGallery.Services;
using ForzaToolkit.Saves;
using Microsoft.Extensions.Logging;
using FormatsGame = ForzaToolkit.Formats.ForzaGame;

namespace LiveryGallery.Profiles;

/// <summary>
/// The games this program shows, built from the open ForzaToolkit packages. The gallery (LiveryGallery.Avalonia) gets
/// a profile and knows no game itself; another program (an FH4 gallery) builds its own here.
/// </summary>
internal static class GameProfiles
{
    /// <summary>
    /// Every game this version supports, in the order the game switcher lists them: those with a profile. 1.7.0 has
    /// FH6 only; each beta that brings a game adds it here
    /// </summary>
    public static IReadOnlyList<GameProfile> All => _all.Value;

    private static readonly Lazy<IReadOnlyList<GameProfile>> _all = new(() => [Fh6()]);

    /// <summary>
    /// The games ready to be shown but not yet in <see cref="All"/>: FH3, FH4 and FH5. Their saves, liveries and files
    /// are read by the same code as the converter's; adding one to <see cref="All"/> shows it. Not checked on their
    /// real saves through the gallery yet
    /// </summary>
    public static IReadOnlyList<GameProfile> Prepared => [Fh3(), Fh4(), Fh5()];

    /// <summary>The supported game with this id (ForzaToolkit.Games), or null</summary>
    public static GameProfile? Find(string? gameId) =>
        gameId is null ? null : All.FirstOrDefault(p => string.Equals(p.Game.Id, gameId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Forza Horizon 6: Xbox app saves, a folder per livery, the auction house, the livery server and the My Designs
    /// grid of ForzaToolkit.Games.FH6
    /// </summary>
    // Where a header that cannot be read is reported
    private static readonly ILogger ItemAuthorLog = AppLog.LoggerFactory.CreateLogger("LiveryGallery.ItemAuthors");

    public static GameProfile Fh6()
    {
        // One resolver for the scan's auction previews and the gallery's copies of them: it looks for the game's cache
        // once every few minutes and keeps the index it read
        var auctionPreviews = new AuctionThumbnailResolver();
        return new GameProfile
        {
            Game = Fh6Game.Definition,
            Saves = PcGameSaveStore.ForGame(Fh6Game.Definition),
            Liveries = new Fh6LiveryStore(auctionPreviews),
            PreviewFileNames = Fh6LiveryFiles.PreviewFileNames,
            Features = new GameFeatures { Auction = true, LiveryServer = true },
            ScanParts = Fh6ScanParts,
            ReadItemAuthor = header => SaveItemAuthors.ReadFile(header, FormatsGame.ForzaHorizon6, ItemAuthorLog),
            MyDesigns = new MyDesignsGrid(Fh6Rows, MyDesignsLayout.FromSaveFolder, DefaultRows: 2, RowChoices: [2, 3]),
            // Some players have a livery the developers gave, kept on the game's servers: first in My Designs
            // The game's only limit on liveries: at 1000 it lets the player neither download nor make another
            MaxLiveries = 1000,
            ListingPreviews = new ListingPreviewCache(auctionPreviews.FindCacheDirectory, AuctionThumbnailResolver.ManifestFileName),
        };
    }

    /// <summary>Forza Horizon 3: Microsoft Store saves only, their containers read only</summary>
    public static GameProfile Fh3() => SaveItemGame(Fh3Game.Definition, FormatsGame.ForzaHorizon3);

    /// <summary>Forza Horizon 4: Steam saves (flat files) and Microsoft Store saves (containers, read only)</summary>
    public static GameProfile Fh4() => SaveItemGame(Fh4Game.Definition, FormatsGame.ForzaHorizon4);

    /// <summary>Forza Horizon 5: Steam saves (a folder per Xbox account, flat files) and Microsoft Store saves</summary>
    public static GameProfile Fh5() => SaveItemGame(Fh5Game.Definition, FormatsGame.ForzaHorizon5);

    /// <summary>
    /// FH3, FH4 or FH5: one profile, the game a parameter. Their saves lie alike (<see cref="SaveItemLiveryStore"/>),
    /// their files read alike (<see cref="Fh3To5LiveryParser"/>). None of FH6's own: no auction house, no livery
    /// server, no My Designs grid, no known limit on liveries
    /// </summary>
    private static GameProfile SaveItemGame(ForzaGame game, FormatsGame format) => new()
    {
        Game = game,
        Saves = GameSaveFolders.ForGame(game, logger: AppLog.LoggerFactory.CreateLogger<GameSaveFolders>()),
        Liveries = new SaveItemLiveryStore(AppLog.LoggerFactory.CreateLogger<SaveItemLiveryStore>()),
        PreviewFileNames = FlatLiveryFiles.PreviewParts,
        SaveLayout = GameSaveLayout.SaveItems,
        Features = new GameFeatures(),
        ReadItemAuthor = header => SaveItemAuthors.ReadFile(header, format, ItemAuthorLog),
        ScanParts = _ =>
        {
            var parser = new Fh3To5LiveryParser(format, AppLog.LoggerFactory.CreateLogger<Fh3To5LiveryParser>());
            return new LiveryScanParts(parser, new Fh3To5DuplicateDetector(format, AppLog.LoggerFactory.CreateLogger<Fh3To5DuplicateDetector>()));
        },
    };

    /// <summary>The closed parts of the FH6 scan: the parser of header and C_livery, and the duplicate check with its cache</summary>
    private static LiveryScanParts Fh6ScanParts(GamePaths paths)
    {
        var parser = new Fh6LiveryParser(AppLog.LoggerFactory.CreateLogger<Fh6LiveryParser>());
        var duplicates = new Fh6DuplicateDetector(paths.DuplicateInputs, parser.Version, AppLog.LoggerFactory.CreateLogger<Fh6DuplicateDetector>());
        return new LiveryScanParts(parser, duplicates);
    }

    /// <summary>FH6's My Designs rows: two in the base game; a menu mod's scene may make them three</summary>
    internal static GridRows Fh6Rows(string? gameDirectory)
    {
        var found = MyDesignsRowsDetector.Detect(gameDirectory);
        return found.Reason switch
        {
            RowsDetectionReason.VanillaScene => new GridRows(found.Rows, GridRowsSource.Game),
            RowsDetectionReason.SceneOverride => new GridRows(found.Rows, GridRowsSource.Mod, found.SceneWriteUtc),
            RowsDetectionReason.GameFolderUnknown => new GridRows(null, GridRowsSource.GameFolderUnknown),
            _ => new GridRows(null, GridRowsSource.Unknown, found.SceneWriteUtc),
        };
    }
}
