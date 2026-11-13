using ForzaToolkit.AppHost;
using LiveryGallery.Models;

namespace LiveryGallery.Services;

/// <summary>The file of a game's 3D cameras: <c>games\&lt;id&gt;\viewer-cameras.json</c></summary>
internal sealed class ViewerCameraFileData
{
    /// <summary>The layout of the file; 1 so far</summary>
    public int Version { get; set; } = 1;

    /// <summary>The camera last left on each car, the latest first</summary>
    public List<ViewerCameraData> Cameras { get; set; } = [];
}

/// <summary>
/// The 3D view's camera left on each car of one game, in a file of the game's own (<see cref="FileName"/> in its folder
/// of data): the car ids are the game's, and the list grows with every car looked at, so it stays out of
/// <c>settings.json</c>. Until 1.7.1 the list was in the game's settings (<see cref="GameSettingsData.ViewerCameras"/>):
/// the first load moves it here. A file that cannot be read is set aside, and the cameras start again
/// </summary>
internal sealed class ViewerCameraStore
{
    public const string FileName = "viewer-cameras.json";

    private readonly JsonSettingsFile<ViewerCameraFileData> _file;

    /// <param name="gameFolder">The game's folder of data (<c>games\&lt;id&gt;</c>, <see cref="Configuration.GamePaths.Root"/>)</param>
    public ViewerCameraStore(string gameFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameFolder);
        _file = new JsonSettingsFile<ViewerCameraFileData>(System.IO.Path.Combine(gameFolder, FileName));
    }

    public string Path => _file.Path;

    /// <summary>
    /// The cameras of the file. Without the file, those the game's settings still keep: written into the file, then
    /// taken out of the settings (<paramref name="settingsChanged"/> saves them); left there when the file cannot be written
    /// </summary>
    /// <param name="legacy">The game's settings, which kept the cameras before; null when the game has none</param>
    /// <param name="settingsChanged">Called when the cameras were taken out of <paramref name="legacy"/></param>
    public IReadOnlyList<ViewerCameraData> Load(GameSettingsData? legacy = null, Action? settingsChanged = null)
    {
        if (File.Exists(Path))
        {
            var cameras = _file.TryLoad()?.Cameras ?? [];
            if (legacy?.ViewerCameras is not null)
            {
                legacy.ViewerCameras = null; // the file's are the newer
                settingsChanged?.Invoke();
            }
            return cameras;
        }
        if (legacy?.ViewerCameras is not { } kept) return [];
        if (kept.Count > 0 && !Save(kept)) return kept;
        legacy.ViewerCameras = null;
        settingsChanged?.Invoke();
        return kept;
    }

    /// <summary>Writes the cameras now; false (logged) when the file could not be written</summary>
    public bool Save(IEnumerable<ViewerCameraData> cameras) => _file.Save(new ViewerCameraFileData { Cameras = [.. cameras] });
}
