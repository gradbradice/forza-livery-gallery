using Avalonia;
using ForzaToolkit.AppHost.Updates;
using ForzaToolkit.Games;
using ForzaToolkit.Games.FH6;
using LiveryGallery.Configuration;
using LiveryGallery.Services;

namespace LiveryGallery;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // First of all: Velopack's own steps (installed, updated, about to be removed) run here and end the process, and
        // an update downloaded in the last run and not installed yet is installed now (ForzaToolkit.AppHost.Updates)
        AppUpdateStartup.Run(args);
        // Who the program is (name, folders, User-Agent, repository): the gallery itself has no name of its own
        AppSettings.Initialize(ProgramIdentity.ForzaLiveryGallery);
        // The first start after the rename: FH6 Livery Gallery's data copied in, before anything is written here
        var migration = AppSettings.PreviousBaseCachePath is { } previous
            ? DataMigration.Run(previous, AppSettings.BaseCachePath, Fh6Game.Definition.Id, AppSettings.Version)
            : DataMigrationReport.Nothing;
        AppSettings.CheckPaths();
        // The log (also of the ForzaToolkit packages and of what nothing caught), the User-Agent of every request, the
        // app, and the log closed at the end: ForzaToolkit.AppHost, the same as every ForzaToolkit app
        AppStartup.Run(AppSettings.LogsPath, AppSettings.UserAgent, () =>
        {
            MemoryReclaimer.IsEnabled = Environment.GetEnvironmentVariable(AppSettings.NoForcedGcVariable) is not ("1" or "true");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }, afterLogStarted: () =>
        {
            // The render caches are shared with the other ForzaToolkit apps: the gallery's own old ones go
            LiveryGallery.Render.LiveryRenderService.RemoveOldCaches();
            foreach (string note in migration.Notes) AppLog.LogWarning("Data from 1.6.0: " + note);
            if (migration.Error is { } error) AppLog.LogError("Failed to copy the data of 1.6.0; tried again on the next start", error);
        });
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .With(RenderingOptions())
        .WithInterFont()
        .LogToTrace();

    // How Windows draws the windows, Avalonia's choice unless <prefix>_RENDERING says otherwise (to tell what holds the
    // memory a 3D view leaves): "angle" (Direct3D 11 through ANGLE, Avalonia's default), "wgl" (the driver's OpenGL),
    // "software" (no GPU: no 3D view)
    private static Win32PlatformOptions RenderingOptions()
    {
        var options = new Win32PlatformOptions();
        string? mode = Environment.GetEnvironmentVariable(AppSettings.RenderingVariable)?.Trim().ToLowerInvariant();
        Win32RenderingMode[]? modes = mode switch
        {
            "angle" => [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software],
            "wgl" => [Win32RenderingMode.Wgl, Win32RenderingMode.AngleEgl, Win32RenderingMode.Software],
            "software" => [Win32RenderingMode.Software],
            _ => null,
        };
        if (modes is not null) options.RenderingMode = modes;
        return options;
    }
}