using System.Text.Json.Serialization;
using LiveryGallery.Enums;

using LiveryGallery.Services;

namespace LiveryGallery.Models;

/// <summary>
/// The settings of one game, <c>Games["&lt;id&gt;"]</c> in <c>settings.json</c> (version 2): its saves and game
/// folder, how its gallery is sorted and filtered, its "Find in game" timings. The property names are a file format,
/// and the same as at the top level of 1.6.0's file, which is how that one is read
/// </summary>
internal sealed class GameSettingsData
{
    /// <summary>
    /// The game language car names, countries and car types are shown in (EN, RU, CHS…, see GameLanguages);
    /// null: the game language that matches the interface language
    /// </summary>
    public string? CarTextLanguage { get; set; }
    public string? SavePath { get; set; }
    public SortMode SortMode { get; set; } = SortMode.Manufacture;
    public FavoriteMode FavoriteMode { get; set; } = FavoriteMode.None;
    public MineMode MineMode { get; set; } = MineMode.None;
    public InstalledMode InstalledMode { get; set; } = InstalledMode.None;
    public DuplicatesFilterMode DuplicatesFilterMode { get; set; } = DuplicatesFilterMode.All;
    public GeneratedFilterMode GeneratedFilterMode { get; set; } = GeneratedFilterMode.All;
    /// <summary>The filter "In the storage" of the game's liveries (since 1.7.1)</summary>
    public CopiesFilterMode CopiesFilterMode { get; set; } = CopiesFilterMode.All;
    public PaintFilterMode PaintFilterMode { get; set; } = PaintFilterMode.All;
    public AuctionFilterMode AuctionFilterMode { get; set; } = AuctionFilterMode.All;

    /// <summary>The last searches in this game, newest first (the search box offers them again)</summary>
    public List<string> SearchHistory { get; set; } = [];

    /// <summary>The sets of filters the user saved for this game, in the order they were saved</summary>
    public List<FilterPresetData> FilterPresets { get; set; } = [];
    public bool ServerConnectionEnabled { get; set; }
    public bool WarnArchiveWithoutInstalledData { get; set; } = true;
    public bool WarnArchiveWithOutdatedInstalledData { get; set; } = true;
    public string? GameInstallPath { get; set; }
    /// <summary>
    /// The way the previews in the preview folder were drawn from the 3D model (<see cref="Services.RenderedThumbnails.DrawingRevision"/>):
    /// older ones are deleted once and drawn again
    /// </summary>
    public int DrawnPreviewsRevision { get; set; }
    public int? LastKnownContainerId { get; set; }
    public string? LastKnownContainerSavePath { get; set; }
    public MyDesignsRowsMode MyDesignsRowsMode { get; set; } = MyDesignsRowsMode.Auto;
    /// <summary>
    /// Liveries the game keeps on its servers for this player, not in the save (FH6: one the developers gave some
    /// players): they come first in the livery menu's grid, before the save's (<see cref="Profiles.MyDesignsGrid"/>)
    /// </summary>
    public int ServerLiveries { get; set; }
    public int NavigationKeyIntervalMs { get; set; } = 65;
    public int NavigationKeyHoldMs { get; set; } = 15;
    public int NavigationSwitchDelayMs { get; set; } = 500;
    public int NavigationTurnDelayMs { get; set; } = 200;
    public int NavigationWrapDelayMs { get; set; } = 800;
    public bool NavigationReopenMenu { get; set; } = true;
    public int NavigationMenuBackDelayMs { get; set; } = 1000;
    public int NavigationMenuOpenDelayMs { get; set; } = 2000;
    /// <summary>After the first scan of a launch, say when little room is left in the save (the "Room in the save" window)</summary>
    public bool WarnFewFreeSlots { get; set; } = true;

    /// <summary>
    /// The 3D view's cameras as 1.7.0 kept them here; read only to be moved into the game's own file
    /// (<c>games\&lt;id&gt;\viewer-cameras.json</c>, Services/ViewerCameraStore.cs), then null and no longer written
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ViewerCameraData>? ViewerCameras { get; set; }

    public GameSettingsData Clone()
    {
        var copy = (GameSettingsData)MemberwiseClone();
        copy.ViewerCameras = ViewerCameras?.ToList();
        return copy;
    }
}

/// <summary>A car's camera in the 3D view, as <c>settings.json</c> keeps it</summary>
/// <param name="Car">The car's number</param>
/// <param name="Yaw">Degrees round the car</param>
/// <param name="Pitch">Degrees up</param>
/// <param name="Zoom">The zoom</param>
/// <param name="PanX">The view shifted sideways</param>
/// <param name="PanY">The view shifted up</param>
internal sealed record ViewerCameraData(int Car, float Yaw, float Pitch, float Zoom, float PanX, float PanY);
