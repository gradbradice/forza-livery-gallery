using Avalonia.Controls;
using ForzaToolkit.Games.Installation;
using ForzaToolkit.UI.Dialogs;

namespace LiveryGallery.Viewer3D;

// The 3D viewer and the game folder check as the gallery sees them. Nothing here refers to a closed package: the
// implementations live in the closed LiveryGallery.Render (on ForzaToolkit.LiveryRender and ForzaToolkit.Formats),
// and the host (LiveryGallery) hands them to the gallery.

// The game folder check's report is open: ForzaToolkit.Games.Installation (GameFolder, GameFolderReport,
// GameFolderFinding, GameFolderIssueCode). The closed side adds what needs its formats (can a car model be read).

/// <summary>Checks a game folder: can the 3D viewer work with it (the open <see cref="GameFolder.Check"/> and more)</summary>
internal interface IGameFolderCheck
{
    /// <summary>Checks <paramref name="path"/> off the UI thread; a failure of the check itself is a NotFound error</summary>
    Task<GameFolderReport> CheckAsync(string? path);
}

/// <summary>A livery to show in 3D</summary>
/// <param name="LiveryName">Its name, for the window title</param>
/// <param name="CarId">Its car</param>
/// <param name="LiveryFilePath">Its livery file (C_livery)</param>
/// <param name="GameName">The game whose livery and car are shown (Forza Horizon 6): the window names it</param>
/// <param name="CarText">The car with its year and number, «Honda S2000 2004 (4231)»: the window's title names it</param>
internal sealed record Livery3DRequest(string LiveryName, int CarId, string LiveryFilePath, string GameName, string CarText = "")
{
    /// <summary>The gallery's own name of the livery (its folder): what the window's ← and → step from</summary>
    public string? Key { get; init; }

    /// <summary>
    /// The window to show it in: <see cref="Livery3DWindows.Active"/> (the last one used, a new one when there is none),
    /// <see cref="Livery3DWindows.New"/> (one more, up to <see cref="Livery3DWindows.Max"/>), or the number of a window
    /// (<see cref="Livery3DNavigation.Window"/>; a closed one's livery goes to the last one used)
    /// </summary>
    public int Window { get; init; } = Livery3DWindows.Active;
}

/// <summary>The windows a livery is shown in (<see cref="Livery3DRequest.Window"/>)</summary>
internal static class Livery3DWindows
{
    /// <summary>The window used last; a new one when none is open</summary>
    public const int Active = 0;

    /// <summary>One more window, to see liveries side by side; the one used last when <see cref="Max"/> are open</summary>
    public const int New = -1;

    /// <summary>How many windows can be open at once (each draws its car on the graphics card)</summary>
    public const int Max = 4;
}

/// <summary>← or → in a 3D window: the livery before or after <paramref name="Key"/> in the gallery, in that window</summary>
/// <param name="Window">The window's number (<see cref="Livery3DRequest.Window"/>)</param>
/// <param name="Key">The livery it shows (<see cref="Livery3DRequest.Key"/>)</param>
/// <param name="Step">−1 the one before, +1 the one after</param>
internal sealed record Livery3DNavigation(int Window, string Key, int Step)
{
    /// <summary>Shift with ← or →: the livery goes to one more window (<see cref="Livery3DWindows.New"/>), this one stays as it is</summary>
    public bool NewWindow { get; init; }
}

/// <summary>Why a livery was not shown</summary>
internal enum Livery3DProblem
{
    None,

    /// <summary>The game folder is not usable (see <see cref="ILivery3DViewer.FolderReport"/>)</summary>
    NotAvailable,

    /// <summary>The livery file could not be read</summary>
    ReadFailed,

    /// <summary>The livery file could not be parsed; <see cref="Livery3DResult.Detail"/> names the parse error</summary>
    ParseFailed,
}

/// <summary>What <see cref="ILivery3DViewer.ShowAsync"/> did</summary>
internal readonly record struct Livery3DResult(Livery3DProblem Problem, string? Detail = null)
{
    public static Livery3DResult Shown => new(Livery3DProblem.None);
}

/// <summary>A livery to draw a preview of, from its car's 3D model</summary>
/// <param name="CarId">Its car</param>
/// <param name="LiveryFilePath">Its livery file (C_livery)</param>
internal sealed record LiveryThumbnailRequest(int CarId, string LiveryFilePath);

/// <summary>
/// Draws the preview of a livery whose save has none from the car's 3D model, with the game's preview camera and size.
/// Where it is kept is the gallery's business (<see cref="Services.RenderedThumbnails"/>). Used from the UI thread.
/// </summary>
internal interface ILiveryThumbnails
{
    /// <summary>Previews can be drawn: the game folder is usable</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// The PNG of the livery's preview (seconds: the car is loaded). Null when it cannot be drawn: no game folder, the
    /// file is unreadable, the car is unknown.
    /// </summary>
    Task<byte[]?> DrawAsync(LiveryThumbnailRequest request, CancellationToken ct);
}

/// <summary>
/// The 3D viewer: knows the game folder (checked in the background), opens one viewer window and shows liveries in
/// it. Used from the UI thread.
/// </summary>
internal interface ILivery3DViewer
{
    /// <summary>The game folder is being checked</summary>
    bool IsChecking { get; }

    /// <summary>The game folder is usable</summary>
    bool IsAvailable { get; }

    /// <summary>The usable game folder; null when there is none</summary>
    string? GameDirectory { get; }

    /// <summary>The last check of the game folder; null before the first one finished</summary>
    GameFolderReport? FolderReport { get; }

    /// <summary>Checking started or finished, or the game folder changed</summary>
    event Action? StateChanged;

    /// <summary>Uses <paramref name="path"/> as the game folder (null: none); checks it first</summary>
    Task SetGamePathAsync(string? path);

    /// <summary>Shows the livery in a viewer window (<see cref="Livery3DRequest.Window"/>), opening it over <paramref name="owner"/> when needed</summary>
    Task<Livery3DResult> ShowAsync(Window owner, Livery3DRequest request);

    /// <summary>← or → in a window: the gallery shows the neighbouring livery there (<see cref="ShowAsync"/>), with Shift in one more</summary>
    event Action<Livery3DNavigation>? NavigateRequested;

    /// <summary>How many more windows can open now (<see cref="Livery3DWindows.Max"/> less those open)</summary>
    int FreeWindows { get; }

    /// <summary>Previews drawn from the 3D model, with the same game folder</summary>
    ILiveryThumbnails Thumbnails { get; }

    /// <summary>The app is closing: closes the viewer window and opens no new one</summary>
    void BeginShutdown();

    /// <summary>Closes everything and waits up to <paramref name="timeout"/> for background work</summary>
    Task ShutdownAsync(TimeSpan timeout);
}

/// <summary>Versions of the closed components (packages and the native library), for the About dialog</summary>
internal sealed record AboutComponents(IReadOnlyList<ComponentVersion> Closed)
{
    public static AboutComponents Unknown { get; } = new([]);
}
