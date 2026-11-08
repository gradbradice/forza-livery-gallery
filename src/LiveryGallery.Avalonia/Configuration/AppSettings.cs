using ForzaToolkit.Core;
using ForzaToolkit.Core.IO;

namespace LiveryGallery.Configuration;

/// <summary>The program's version, folders and names, from the <see cref="AppIdentity"/> the host sets at start</summary>
internal static class AppSettings
{
    private static AppIdentity? _identity;

    /// <summary>Sets who the program is; the host calls it first thing, before any path is read</summary>
    public static void Initialize(AppIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _identity = identity;
    }

    public static AppIdentity Identity => _identity
        ?? throw new InvalidOperationException("AppSettings.Initialize was not called: the host program sets its AppIdentity at start.");

    /// <summary>The version with its pre-release label (<c>1.7.1-beta.1</c>; a release <c>1.7.1</c>): what "About" and the diagnostics show</summary>
    public static string Version { get; } = AppVersion.ReadVersion(typeof(AppSettings).Assembly);
    public static string UserAgent => $"{Identity.UserAgentProduct}/{Version}";

    /// <summary>Set to 1 or true to leave garbage collection to the runtime (see <see cref="MemoryReclaimer"/>)</summary>
    public static string NoForcedGcVariable => Identity.EnvironmentPrefix + "_NO_FORCED_GC";

    /// <summary>How Windows draws the windows: angle, wgl or software (Program.RenderingOptions); unset: Avalonia's choice</summary>
    public static string RenderingVariable => Identity.EnvironmentPrefix + "_RENDERING";

    /// <summary>The program's data (%APPDATA%\&lt;folder&gt;)</summary>
    public static string BaseCachePath => ToolkitFolders.AppData(Identity.FolderName);

    /// <summary>The program's caches that stay on this PC (%LOCALAPPDATA%\&lt;folder&gt;)</summary>
    public static string LocalCachePath => ToolkitFolders.AppCache(Identity.FolderName);

    public static string LogsPath => Path.Combine(BaseCachePath, "logs");

    /// <summary>One folder per game, <c>games\&lt;id&gt;</c>: everything of its liveries and accounts (<see cref="GamePaths"/>)</summary>
    public static string GamesPath => Path.Combine(BaseCachePath, "games");

    /// <summary>The folder of the program before it was renamed, whose data the first start copies; null when there is none</summary>
    public static string? PreviousBaseCachePath => Identity.PreviousFolderName is { Length: > 0 } previous
        ? ToolkitFolders.AppData(previous)
        : null;

    public static void CheckPaths()
    {
        Directory.CreateDirectory(BaseCachePath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(GamesPath);
    }
}
