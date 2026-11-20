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
using ForzaToolkit.Games.Grids;

namespace LiveryGallery.Profiles;

/// <summary>
/// The games this program shows, built from the open ForzaToolkit packages. The gallery (LiveryGallery.Avalonia) gets
/// a profile and knows no game itself; another program (an FH4 gallery) builds its own here.
/// </summary>
internal static class GameProfiles
{
    /// <summary>
    /// Every game this version supports, in the order the game switcher lists them: FH6 first (the default), then the
    /// older ones from the newest. Up to 1.7.x FH6 only; FH3 to FH5 since 1.8.0-beta.1
    /// </summary>
    public static IReadOnlyList<GameProfile> All => _all.Value;

    private static readonly Lazy<IReadOnlyList<GameProfile>> _all = new(() => [Fh6(), Fh5(), Fh4(), Fh3()]);

    /// <summary>The format of <paramref name="game"/>'s livery files, as ForzaToolkit.Formats reads them</summary>
    public static FormatsGame FormatOf(ForzaGame game) =>
        game == Fh6Game.Definition ? FormatsGame.ForzaHorizon6
        : game == Fh5Game.Definition ? FormatsGame.ForzaHorizon5
        : game == Fh4Game.Definition ? FormatsGame.ForzaHorizon4
        : game == Fh3Game.Definition ? FormatsGame.ForzaHorizon3
        : throw new ArgumentOutOfRangeException(nameof(game), game.Id, "The gallery reads FH3 to FH6");

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
            GamePackageType = typeof(Fh6Game),
            PreviewFileNames = Fh6LiveryFiles.PreviewFileNames,
            Features = new GameFeatures { Auction = true, LiveryServer = true },
            ScanParts = Fh6ScanParts,
            ReadItemAuthor = header => SaveItemAuthors.ReadFile(header, FormatsGame.ForzaHorizon6, ItemAuthorLog),
            MyDesigns = MenuGrid(Fh6Game.MyDesignsMenu, ForzaToolkit.Games.FH6.MyDesignsLayout.FromSaveFolder, Fh6RowChoices),
            // Some players have a livery the developers gave, kept on the game's servers: first in My Designs
            // The game's only limit on liveries: at 1000 it lets the player neither download nor make another
            MaxLiveries = 1000,
            ListingPreviews = new ListingPreviewCache(auctionPreviews.FindCacheDirectory, AuctionThumbnailResolver.ManifestFileName),
        };
    }

    /// <summary>Forza Horizon 3: Microsoft Store saves only; 500 liveries</summary>
    public static GameProfile Fh3() => SaveItemGame(Fh3Game.Definition, typeof(Fh3Game), FormatsGame.ForzaHorizon3, maxLiveries: 500,
        myDesigns: SaveItemGrid(Fh3Game.MyDesignsMenu));

    /// <summary>Forza Horizon 4: Steam saves (flat files) and Microsoft Store saves (containers); 500 liveries since February 2019</summary>
    public static GameProfile Fh4() => SaveItemGame(Fh4Game.Definition, typeof(Fh4Game), FormatsGame.ForzaHorizon4, maxLiveries: 500,
        myDesigns: SaveItemGrid(Fh4Game.MyDesignsMenu));

    /// <summary>Forza Horizon 5: Steam saves (a folder per Xbox account, flat files) and Microsoft Store saves; 1000 liveries since August 2023</summary>
    public static GameProfile Fh5() => SaveItemGame(Fh5Game.Definition, typeof(Fh5Game), FormatsGame.ForzaHorizon5, maxLiveries: 1000,
        myDesigns: SaveItemGrid(Fh5Game.MyDesignsMenu));

    /// <summary>
    /// My Designs of FH3, FH4 and FH5 (FH3's order taken to be theirs): the liveries whose header the game wrote last
    /// first (it writes it again when a livery is made a favourite or no longer one), column by column; the toolkit lists
    /// the save and lays the grid out (ForzaToolkit.Saves' SaveItemLiveryStore.MyDesignsFromSave: a Steam save's header
    /// files, a Microsoft Store save's containers in containers.index; ForzaToolkit.Games' NewestWrittenFirstGrid). The
    /// rows are the game's menu's (FH3 one, FH4 three, FH5 two), read from its UI files so a menu mod counts
    /// (<see cref="MenuRowsOf"/>); Settings offers one to three by hand
    /// </summary>
    internal static MyDesignsGrid SaveItemGrid(MyDesignsMenu menu) => MenuGrid(menu, SaveItemLiveryStore.MyDesignsFromSave, [1, 2, 3]);

    /// <summary>A game's grid whose rows <see cref="MenuRowsOf"/> reads from its menu</summary>
    private static MyDesignsGrid MenuGrid(MyDesignsMenu menu, Func<string, int, int, GridLayout> layout, IReadOnlyList<int> rowChoices) =>
        new(directory => MenuRowsOf(menu, rowChoices, directory), layout, DefaultRows: menu.StandardRows, RowChoices: rowChoices);

    /// <summary>
    /// The rows of a game's My Designs (ForzaToolkit.Games' MyDesignsRowsReader: the game's media\UI.zip and a mod's
    /// files in mediapc\UI over it), in the gallery's terms; rows the grid cannot take (not among
    /// <paramref name="rowChoices"/>) are rows it cannot tell, for Settings to ask
    /// </summary>
    internal static GridRows MenuRowsOf(MyDesignsMenu menu, IReadOnlyList<int> rowChoices, string? gameDirectory)
    {
        var found = MyDesignsRowsReader.Detect(menu, gameDirectory);
        if (found.Rows is { } rows && !rowChoices.Contains(rows)) return new GridRows(null, GridRowsSource.Unknown, found.WrittenUtc);
        return found.Reason switch
        {
            MenuRowsReason.StandardMenu => new GridRows(found.Rows, GridRowsSource.Game),
            MenuRowsReason.ModdedMenu => new GridRows(found.Rows, GridRowsSource.Mod, found.WrittenUtc),
            MenuRowsReason.GameFolderUnknown => new GridRows(null, GridRowsSource.GameFolderUnknown),
            _ => new GridRows(null, GridRowsSource.Unknown, found.WrittenUtc),
        };
    }

    /// <summary>
    /// FH3, FH4 or FH5: one profile, the game a parameter. Their saves lie alike (<see cref="SaveItemLiveryStore"/>),
    /// their files read alike (<see cref="Fh3To5LiveryParser"/>). The auction house: <c>SoulBoundLivery_…</c> are the
    /// liveries of cars bought there, with files of their own; neither they nor base liveries count against the limit.
    /// Only read in 1.8.0-beta.1 (<see cref="ReadOnlyLiveryStore"/>, docs/fh3-fh5.md). The livery server reads their
    /// profiles once it has the game's keys (until then <c>LiveryServerService.ForGame</c>: not supported yet). The My
    /// Designs grid: <see cref="SaveItemGrid"/> (docs/fh3-fh5.md, stage 3)
    /// </summary>
    private static GameProfile SaveItemGame(ForzaGame game, Type package, FormatsGame format, int maxLiveries,
        MyDesignsGrid? myDesigns = null) => new()
    {
        MyDesigns = myDesigns,
        Game = game,
        GamePackageType = package,
        Saves = GameSaveFolders.ForGame(game, logger: AppLog.LoggerFactory.CreateLogger<GameSaveFolders>()),
        Liveries = new ReadOnlyLiveryStore(new SaveItemLiveryStore(AppLog.LoggerFactory.CreateLogger<SaveItemLiveryStore>())),
        PreviewFileNames = FlatLiveryFiles.PreviewParts,
        SaveLayout = GameSaveLayout.SaveItems,
        // The livery server reads their profiles too (server 1.5.0, its keys of the game; until it has them the
        // window says the game is not supported yet: LiveryServerService.ForGame)
        Features = new GameFeatures { Auction = true, LiveryServer = true },
        AuctionKeys = SaveKeys.IsSoulBound,
        // The game counts neither auction nor base liveries; the scan lists no base livery (SaveKeys.IsListedLivery)
        MaxLiveries = maxLiveries,
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
    internal static GridRows Fh6Rows(string? gameDirectory) => MenuRowsOf(Fh6Game.MyDesignsMenu, Fh6RowChoices, gameDirectory);

    /// <summary>The rows FH6's grid can take (ForzaToolkit.Games.FH6's MyDesignsLayout): its own two, a menu mod's three</summary>
    private static int[] Fh6RowChoices => [2, 3];
}
