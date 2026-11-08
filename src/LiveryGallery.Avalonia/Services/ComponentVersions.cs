using ForzaToolkit.UI.Dialogs;

namespace LiveryGallery.Services;

/// <summary>Versions of the packages the app is built with, read from their assemblies</summary>
internal static class ComponentVersions
{
    /// <summary>The open ForzaToolkit packages the gallery uses, with the game's package of the profile, by name</summary>
    public static IReadOnlyList<ComponentVersion> OpenPackages(GameProfile profile) =>
    [
        .. new[]
        {
            typeof(global::ForzaToolkit.Core.AppVersion),
            typeof(global::ForzaToolkit.AppHost.AppLog),
            typeof(global::ForzaToolkit.Games.ForzaGame),
            profile.Liveries.GetType(), // the game's package: ForzaToolkit.Games.FH6 for FH6
            typeof(global::ForzaToolkit.Saves.SaveFolderPointers),
            typeof(global::ForzaToolkit.Liveries.LiveryData),
            typeof(global::ForzaToolkit.Liveries.Server.LiveryServerConnection),
            typeof(global::ForzaToolkit.CarDatabase.CarDatabaseService),
            typeof(global::ForzaToolkit.StringTables.StringTable),
            typeof(global::ForzaToolkit.GameInput.GameNavigator),
            typeof(global::ForzaToolkit.UI.WindowExtensions),
        }
        .Select(ComponentVersion.Of)
        .DistinctBy(c => c.Name, StringComparer.Ordinal)
        .OrderBy(c => c.Name, StringComparer.Ordinal),
    ];
}
