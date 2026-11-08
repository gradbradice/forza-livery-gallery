using System.Text.Json;
using System.Text.Json.Serialization;
using LiveryGallery.Enums;
using LiveryGallery.Services;

namespace LiveryGallery.Models;

/// <summary>
/// The program's settings, <c>settings.json</c>. Version 2 (1.7.0): what is the program's at the top, what is a game's
/// in <see cref="Games"/> by game id. The windows read and set a game's settings here as before, through the
/// properties that stand for the current game's (<see cref="LastGame"/>): they do not know there is more than one.
/// </summary>
internal class AppSettingsData
{
    /// <summary>
    /// The version of the file: 0 (none) in 1.6.0 and earlier, whose game settings were at the top. New settings are
    /// given <see cref="CurrentVersion"/> where they are made (AppSettingsService.Load)
    /// </summary>
    public int SettingsVersion { get; set; }

    public const int CurrentVersion = 2;

    /// <summary>The game of the first start, and the one 1.6.0's settings were of</summary>
    public const string DefaultGameId = "fh6";

    /// <summary>The game the gallery shows, and opens with next time (a ForzaToolkit.Games id)</summary>
    public string? LastGame { get; set; }

    /// <summary>Each game's settings by its id; a game without one gets the defaults when it is first shown</summary>
    public Dictionary<string, GameSettingsData> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the file had that is not a property here: 1.6.0's game settings, until they are moved</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>The settings of the game shown, made with the defaults when it has none yet</summary>
    [JsonIgnore]
    public GameSettingsData Game
    {
        get
        {
            string id = LastGame ??= DefaultGameId;
            if (!Games.TryGetValue(id, out var game)) Games[id] = game = new GameSettingsData();
            return game;
        }
    }

    /// <summary>Makes <paramref name="gameId"/> the game shown: the game properties are its settings from now on</summary>
    /// <param name="gameId">The game</param>
    /// <param name="defaults">Its settings when it has none yet (the profile's); the general defaults when null</param>
    public void UseGame(string gameId, Func<GameSettingsData>? defaults = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        if (Games.Comparer != StringComparer.OrdinalIgnoreCase)
            Games = new Dictionary<string, GameSettingsData>(Games, StringComparer.OrdinalIgnoreCase);
        LastGame = gameId;
        if (!Games.ContainsKey(gameId)) Games[gameId] = defaults?.Invoke() ?? new GameSettingsData();
    }

    public AppLanguage Language { get; set; } = AppLanguage.English;
    // DarkTheme for backward compatibility (<= 1.1.0)
    public bool DarkTheme { get; set; }
    public AppThemeMode? ThemeMode { get; set; }
    /// <summary>The range and default are every ForzaToolkit app's (<see cref="PossibleDuplicateThreshold"/>)</summary>
    public int PossibleDuplicateThresholdPercent { get; set; } = PossibleDuplicateThreshold.DefaultPercent;
    public bool GroupingEnabled { get; set; } = true;
    public int ViewerQualityIndex { get; set; } = 2;
    /// <summary>The 3D view shows the cars' license plates; turned off in its tuning panel</summary>
    public bool ViewerShowPlates { get; set; } = true;
    /// <summary>The 3D windows turn together (their «Sync camera» switch)</summary>
    public bool ViewerSyncCamera { get; set; } = true;
    /// <summary>New screenshots of a livery are re-encoded as WebP (<see cref="ScreenshotsWebPQuality"/>); off: kept as they are</summary>
    public bool ScreenshotsWebP { get; set; }
    public int ScreenshotsWebPQuality { get; set; } = 90;
    /// <summary>Backups take the screenshots of their liveries along</summary>
    public bool ScreenshotsInBackups { get; set; }
    /// <summary>The groups of the "Screenshots" view (<see cref="Services.Screenshots.ScreenshotGroupMode"/>, by name)</summary>
    public string ScreenshotsGrouping { get; set; } = nameof(Services.Screenshots.ScreenshotGroupMode.ByLivery);
    /// <summary>The "Screenshots" view shows the newest first; off: the oldest</summary>
    public bool ScreenshotsNewestFirst { get; set; } = true;
    public bool AutoRefreshLiveries { get; set; } = true;
    public bool RefreshLiveriesOnButtonClick { get; set; } = false;
    public bool SearchByFolderName { get; set; } = false;
    public bool ShowChangesSinceLastLaunch { get; set; } = true;
    /// <summary>Take betas as well as releases when the gallery updates itself (the beta channel)</summary>
    public bool BetaUpdates { get; set; }
    public bool AutoBackupBeforeArchive { get; set; } = true;
    public bool AutoBackupOnChange { get; set; } = true;
    public bool AutoBackupPeriodic { get; set; }
    public int AutoBackupPeriodDays { get; set; } = AutoBackupPolicy.DefaultPeriodDays;
    public int AutoBackupKeep { get; set; } = AutoBackupPolicy.DefaultKeep;

    // The current game's settings, as they were named before there was more than one game: not written here, the
    // file has them in Games
    [JsonIgnore] public string? CarTextLanguage { get => Game.CarTextLanguage; set => Game.CarTextLanguage = value; }
    [JsonIgnore] public string? SavePath { get => Game.SavePath; set => Game.SavePath = value; }
    [JsonIgnore] public SortMode SortMode { get => Game.SortMode; set => Game.SortMode = value; }
    [JsonIgnore] public FavoriteMode FavoriteMode { get => Game.FavoriteMode; set => Game.FavoriteMode = value; }
    [JsonIgnore] public MineMode MineMode { get => Game.MineMode; set => Game.MineMode = value; }
    [JsonIgnore] public InstalledMode InstalledMode { get => Game.InstalledMode; set => Game.InstalledMode = value; }
    [JsonIgnore] public DuplicatesFilterMode DuplicatesFilterMode { get => Game.DuplicatesFilterMode; set => Game.DuplicatesFilterMode = value; }
    [JsonIgnore] public GeneratedFilterMode GeneratedFilterMode { get => Game.GeneratedFilterMode; set => Game.GeneratedFilterMode = value; }
    [JsonIgnore] public PaintFilterMode PaintFilterMode { get => Game.PaintFilterMode; set => Game.PaintFilterMode = value; }
    [JsonIgnore] public AuctionFilterMode AuctionFilterMode { get => Game.AuctionFilterMode; set => Game.AuctionFilterMode = value; }
    [JsonIgnore] public bool ServerConnectionEnabled { get => Game.ServerConnectionEnabled; set => Game.ServerConnectionEnabled = value; }
    [JsonIgnore] public bool WarnArchiveWithoutInstalledData { get => Game.WarnArchiveWithoutInstalledData; set => Game.WarnArchiveWithoutInstalledData = value; }
    [JsonIgnore] public bool WarnArchiveWithOutdatedInstalledData { get => Game.WarnArchiveWithOutdatedInstalledData; set => Game.WarnArchiveWithOutdatedInstalledData = value; }
    [JsonIgnore] public string? GameInstallPath { get => Game.GameInstallPath; set => Game.GameInstallPath = value; }
    [JsonIgnore] public int DrawnPreviewsRevision { get => Game.DrawnPreviewsRevision; set => Game.DrawnPreviewsRevision = value; }
    [JsonIgnore] public int? LastKnownContainerId { get => Game.LastKnownContainerId; set => Game.LastKnownContainerId = value; }
    [JsonIgnore] public string? LastKnownContainerSavePath { get => Game.LastKnownContainerSavePath; set => Game.LastKnownContainerSavePath = value; }
    [JsonIgnore] public MyDesignsRowsMode MyDesignsRowsMode { get => Game.MyDesignsRowsMode; set => Game.MyDesignsRowsMode = value; }
    [JsonIgnore] public int ServerLiveries { get => Game.ServerLiveries; set => Game.ServerLiveries = value; }
    [JsonIgnore] public int NavigationKeyIntervalMs { get => Game.NavigationKeyIntervalMs; set => Game.NavigationKeyIntervalMs = value; }
    [JsonIgnore] public int NavigationKeyHoldMs { get => Game.NavigationKeyHoldMs; set => Game.NavigationKeyHoldMs = value; }
    [JsonIgnore] public int NavigationSwitchDelayMs { get => Game.NavigationSwitchDelayMs; set => Game.NavigationSwitchDelayMs = value; }
    [JsonIgnore] public int NavigationTurnDelayMs { get => Game.NavigationTurnDelayMs; set => Game.NavigationTurnDelayMs = value; }
    [JsonIgnore] public int NavigationWrapDelayMs { get => Game.NavigationWrapDelayMs; set => Game.NavigationWrapDelayMs = value; }
    [JsonIgnore] public bool NavigationReopenMenu { get => Game.NavigationReopenMenu; set => Game.NavigationReopenMenu = value; }
    [JsonIgnore] public int NavigationMenuBackDelayMs { get => Game.NavigationMenuBackDelayMs; set => Game.NavigationMenuBackDelayMs = value; }
    [JsonIgnore] public int NavigationMenuOpenDelayMs { get => Game.NavigationMenuOpenDelayMs; set => Game.NavigationMenuOpenDelayMs = value; }
    [JsonIgnore] public bool WarnFewFreeSlots { get => Game.WarnFewFreeSlots; set => Game.WarnFewFreeSlots = value; }

    public AppSettingsData Clone()
    {
        var copy = (AppSettingsData)MemberwiseClone();
        copy.Games = Games.ToDictionary(g => g.Key, g => g.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        copy.Unknown = Unknown is null ? null : new Dictionary<string, JsonElement>(Unknown);
        return copy;
    }

    public void CopyFrom(AppSettingsData other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Language = other.Language;
        DarkTheme = other.DarkTheme;
        ThemeMode = other.ThemeMode;
        PossibleDuplicateThresholdPercent = other.PossibleDuplicateThresholdPercent;
        GroupingEnabled = other.GroupingEnabled;
        ViewerQualityIndex = other.ViewerQualityIndex;
        ViewerShowPlates = other.ViewerShowPlates;
        ViewerSyncCamera = other.ViewerSyncCamera;
        ScreenshotsWebP = other.ScreenshotsWebP;
        ScreenshotsWebPQuality = other.ScreenshotsWebPQuality;
        ScreenshotsInBackups = other.ScreenshotsInBackups;
        ScreenshotsGrouping = other.ScreenshotsGrouping;
        ScreenshotsNewestFirst = other.ScreenshotsNewestFirst;
        AutoRefreshLiveries = other.AutoRefreshLiveries;
        RefreshLiveriesOnButtonClick = other.RefreshLiveriesOnButtonClick;
        SearchByFolderName = other.SearchByFolderName;
        ShowChangesSinceLastLaunch = other.ShowChangesSinceLastLaunch;
        BetaUpdates = other.BetaUpdates;
        AutoBackupBeforeArchive = other.AutoBackupBeforeArchive;
        AutoBackupOnChange = other.AutoBackupOnChange;
        AutoBackupPeriodic = other.AutoBackupPeriodic;
        AutoBackupPeriodDays = other.AutoBackupPeriodDays;
        AutoBackupKeep = other.AutoBackupKeep;
        SettingsVersion = other.SettingsVersion;
        LastGame = other.LastGame;
        Games = other.Games.ToDictionary(g => g.Key, g => g.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        Unknown = other.Unknown is null ? null : new Dictionary<string, JsonElement>(other.Unknown);
    }
}
