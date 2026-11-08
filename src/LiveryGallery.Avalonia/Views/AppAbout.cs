using ForzaToolkit.UI.Dialogs;
using LiveryGallery.Configuration;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Viewer3D;

namespace LiveryGallery.Views;

/// <summary>What the About window (<see cref="AboutDialog"/> of ForzaToolkit.UI.Avalonia) shows for the gallery</summary>
internal static class AppAbout
{
    /// <param name="components">Versions of the closed components (the host reads them); the open ones are read here</param>
    /// <param name="profile">The game: its package is listed with the open ones</param>
    public static AboutInfo Info(AboutComponents components, GameProfile profile) => AboutInfo.For(
        AppSettings.Identity.Name, AppSettings.Version, string.Format(Strings.AboutDescriptionFormat, profile.Game.Name),
        ComponentVersions.OpenPackages(profile), components.Closed, new Uri("avares://LiveryGallery.Avalonia/Assets/app-icon.png"),
        Strings.ContactsGithubLabel, () => TrustedUrlLauncher.TryOpen(AppSettings.Identity.GitHubUrl));
}
