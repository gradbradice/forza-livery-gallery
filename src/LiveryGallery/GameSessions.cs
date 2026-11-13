using ForzaToolkit.Liveries.Formats;
using ForzaToolkit.Liveries.History;
using LiveryGallery.Configuration;
using LiveryGallery.Localisation;
using LiveryGallery.Render;
using ForzaToolkit.AppHost.Updates;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;
using LiveryGallery.Services.Diagnostics;
using LiveryGallery.Services.Screenshots;
using LiveryGallery.Views;
using Microsoft.Extensions.Logging;

namespace LiveryGallery;

/// <summary>
/// The games the program shows, one at a time: builds a game's services and main window, and on a switch the other
/// game's. The host shows each window it is handed (<c>show</c>); the window of the game left closes itself once its
/// services have let go of everything
/// </summary>
internal sealed class GameSessions
{
    private readonly AppSettingsData _settings;
    private readonly IAppUpdates _updates;
    private readonly IReadOnlyList<GameProfile> _profiles;
    private readonly Func<ForzaGame, GamePaths> _pathsOf;
    private readonly Func<GameProfile, ISavePathPrompter>? _prompter;
    private readonly Action<MainWindow> _show;
    private readonly Func<HttpClient> _http;
    // Where the storages are that the user moved to folders of their own: one file for every game
    private readonly StorageFolderSettings _storageFolders;

    /// <param name="settings">The program's settings; the game shown is chosen in them (<see cref="AppSettingsData.UseGame"/>)</param>
    /// <param name="updates">The program's updates, the same whatever game is shown</param>
    /// <param name="profiles">The supported games, as the switcher lists them</param>
    /// <param name="show">Makes a new game's window the program's main window and shows it</param>
    /// <param name="pathsOf">Each game's data folder; <see cref="GamePaths.For"/> when null (tests pass a temporary one)</param>
    /// <param name="prompter">Asks for a save folder; the window's own dialog when null (tests answer themselves)</param>
    /// <param name="http">Downloads the car data; the program's client when null (tests answer offline)</param>
    public GameSessions(
        AppSettingsData settings, IAppUpdates updates, IReadOnlyList<GameProfile> profiles, Action<MainWindow> show,
        Func<ForzaGame, GamePaths>? pathsOf = null, Func<GameProfile, ISavePathPrompter>? prompter = null, HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(show);
        if (profiles is not { Count: > 0 }) throw new ArgumentException("No game to show", nameof(profiles));
        _settings = settings;
        _updates = updates;
        _profiles = profiles;
        _show = show;
        _pathsOf = pathsOf ?? GamePaths.For;
        _prompter = prompter;
        _http = http is null ? () => AppHttp.Client : () => http;
        // Beside the games' folders (%APPDATA%\ForzaLiveryGallery\storage-folders.json; a test's own folder)
        string gamesRoot = Path.GetDirectoryName(_pathsOf(profiles[0].Game).Root) ?? AppSettings.GamesPath;
        _storageFolders = new StorageFolderSettings(Path.Combine(Path.GetDirectoryName(gamesRoot) ?? gamesRoot, StorageFolderSettings.FileName));
    }

    /// <summary>The supported game with this id, or null</summary>
    public GameProfile? Find(string? gameId) =>
        gameId is null ? null : _profiles.FirstOrDefault(p => string.Equals(p.Game.Id, gameId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The game to open with: the one shown last time, else the first supported</summary>
    public GameProfile First => Find(_settings.LastGame) ?? _profiles[0];

    /// <summary>
    /// The services of one game and its main window: built from its profile, its settings and its data folder. A switch
    /// to another game closes the window and its services and builds the other game's, as at start, so nothing of
    /// the first game stays subscribed to anything
    /// </summary>
    public MainWindow Open(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var settings = _settings;
        // The game's own settings (its saves, sorting, "Find in game") are the ones the windows see from now on
        settings.UseGame(profile.Game.Id, profile.NewSettings);
        AppSettingsService.Save(settings);
        profile.WarmUpInstallFolder();

        // Everything of the game's liveries and accounts lives in games\<id> (GamePaths)
        var gamePaths = _pathsOf(profile.Game);
        gamePaths.Create();
        var cacheService = new AppCacheService(gamePaths);
        // The car list and the car texts taken from the game live in the ForzaToolkit cache of the game
        // (%LOCALAPPDATA%\ForzaToolkit\fh6), shared with the other ForzaToolkit apps; the list the gallery kept in its
        // own folder before is copied there once
        var carDatabase = new CarDatabaseService(_http(), CarDatabaseOptions.ForGame(profile.Game) with
        {
            LegacyCacheFilePath = Path.Combine(gamePaths.Root, CarDatabaseSources.FileName(profile.Game)),
        });
        // The paints of the livery editor (finish code -> game material) live in the same cache; the 3D renderer reads
        // them there, so a paint the game adds or changes needs no new gallery version. Checked once per start
        var liveryMaterials = new LiveryMaterialsService(_http(), LiveryMaterialsOptions.ForGame(profile.Game),
            AppLog.LoggerFactory.CreateLogger<LiveryMaterialsService>());
        _ = Task.Run(() => liveryMaterials.RefreshAsync());
        var carTexts = new CarTextProvider(carDatabase,
            new CarLocalizationService(CarLocalizationOptions.ForGame(profile.Game), AppLog.LoggerFactory.CreateLogger<CarLocalizationService>()),
            settings);
        var tagService = new TagService(gamePaths.Root);
        // The user's notes per livery, beside the tags (notes.json); backups take them along
        var noteService = new NoteService(gamePaths.Root);
        var favoriteService = new FavoriteService(gamePaths.Root);
        var lockService = new LockService(gamePaths.Root);
        // Shared with the other ForzaToolkit apps (%APPDATA%\ForzaToolkit) and so with every game; the first start copies
        // the cards the gallery kept in its own folder before there (1.6.0's root, copied to games\fh6)
        var authorCardService = SharedAuthorCards.Open(gamePaths.Root, PlaceholderTexts.IsUnknownAuthor);
        var saveFolderLock = new SaveFolderLock();
        // The user's screenshots of each livery (games\<game>\screenshots); backups take them along when the setting says so
        var screenshots = new ScreenshotStore(Path.Combine(gamePaths.Root, "screenshots"));
        var liveryStore = profile.Liveries;
        // The archive and the backups: each account of the game its own, never shared
        var storage = new AccountStorage(gamePaths,
            folder => new LiveryArchive(new LiveryArchiveOptions
            {
                Directory = folder,
                Store = liveryStore,
                SaveFolderLock = saveFolderLock,
                RecoveredEntry = RecoveredArchiveEntry,
                Logger = AppLog.LoggerFactory.CreateLogger<LiveryArchive>(),
            }),
            folder => new LiveryBackups(new LiveryBackupsOptions
            {
                Directory = folder,
                Store = liveryStore,
                SaveFolderLock = saveFolderLock,
                Favorites = favoriteService,
                Tags = tagService,
                Notes = noteService,
                Locks = lockService,
                Extras = [new ScreenshotBackupExtras(screenshots, () => settings.ScreenshotsInBackups)],
                Logger = AppLog.LoggerFactory.CreateLogger<LiveryBackups>(),
            }),
            // A storage may be in a folder of the user's (storage-folders.json), by account
            profile.Game.Id, _storageFolders);
        var liveryIdService = new LiveryIdService(gamePaths.Root);
        // The journal of each account's liveries (history.jsonl next to its scan cache): what came and went, for the
        // statistics. It starts from what the archive and the backups tell about the past
        var historyStore = new LiveryHistoryStore(new LiveryHistoryOptions
        {
            AccountsRoot = cacheService.AccountsRoot,
            ArchiveOf = key => GamePaths.IsFolderName(key) ? storage.ArchiveOf(key) : null,
            StorageOf = key => GamePaths.IsFolderName(key) && key != GamePaths.Unassigned ? storage.StorageOf(key) : null,
            // Only the account's own archive and backups: each entry in them is of this account
            Reconstruct = (accountKey, current) => GamePaths.IsFolderName(accountKey)
                ? LiveryHistoryReconstruction.FromArchiveAndBackups(
                    accountKey, storage.ArchiveOf(accountKey).GetAll(), storage.BackupsOf(accountKey).GetAll().Select(b => b.Manifest),
                    current, singleAccount: true)
                : [],
            Logger = AppLog.LoggerFactory.CreateLogger<LiveryHistoryStore>(),
        });
        var scanService = new LiveryScanner(cacheService, carDatabase, favoriteService, lockService, tagService, authorCardService,
            storage, saveFolderLock, liveryIdService, profile, profile.ScanParts(gamePaths), historyStore, noteService,
            // The user's screenshots of each livery, on this PC only: games\<id>\screenshots\<livery>
            screenshots);
        // The closed side: 3D viewer, game folder check, versions and parse error names (LiveryGallery.Render on stage 5)
        // The car list gives the renderer each car's stock wheels: the art files place them wrong on some cars
        // The names of the tuning parts in the 3D window, in the language of the car texts: the list shared with the other
        // ForzaToolkit apps, checked once per start
        var carUpgrades = new CarUpgradesService(_http(), CarUpgradesOptions.ForGame(profile.Game), AppLog.LoggerFactory.CreateLogger<CarUpgradesService>());
        _ = PublishedLists.Start([carUpgrades]);
        // The camera left on each car: the game's own file, games\<id>\viewer-cameras.json
        var viewer3D = new Livery3DViewer(settings, carDatabase, profile.Game.Id, carUpgrades, () => carTexts.LanguageCode,
            new ViewerCameraStore(gamePaths.Root));
        var diagnosticsService = new DiagnosticsExportService(cacheService);
        var accountRegistry = new AccountRegistry(cacheService, profile.Saves, AccountLabels.Title);
        var window = new MainWindow(
            settings, profile, new LiveryServerService(profile.Game.Id), carDatabase, carTexts, tagService, favoriteService, lockService, authorCardService, storage,
            scanService, _updates, viewer3D, new RenderGameFolderCheck(), RenderComponents.ReadVersions(),
            saveFolderLock, diagnosticsService, accountRegistry, _prompter?.Invoke(profile))
        {
            Games = new GameSwitcher(profile.Game.Id, () => GameChoices.Of(_profiles, settings), SwitchAsync),
        };
        // The renderer is the game's: it goes with the window
        window.Closed += (_, _) => viewer3D.Dispose();
        return window;
    }

    /// <summary>
    /// The main window of the game shown has closed its services; the other game's opens. Called by that window once
    /// it has shut down, before it closes, so the program has a window throughout
    /// </summary>
    public Task SwitchAsync(string gameId)
    {
        if (Find(gameId) is not { } profile) return Task.CompletedTask;
        AppLog.LogWarning($"Switching to {profile.Game.Name} (not an error)");
        _show(Open(profile));
        return Task.CompletedTask;
    }

    /// <summary>A folder in the archive that its index lost: the key as the name, the rest unknown</summary>
    private static LiveryData RecoveredArchiveEntry(string key) => new()
    {
        FolderName = key,
        LiveryName = key,
        AuthorRaw = string.Empty, // shown as "Unknown" in the current language
        TextVersion = LiveryCacheEntry.CurrentTextVersion,
        CarId = 0,
        CarManufacturerRaw = Strings.ArchiveRecoveredUnknownValue,
        CarModelNameRaw = "",
        CarKnown = false,
        HasThumbnail = false,
    };
}
