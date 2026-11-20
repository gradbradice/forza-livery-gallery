using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>
/// A save whose liveries are only shown (<see cref="Profiles.GameProfile.IsSaveReadOnly"/>; FH3 to FH5 in 1.8.0-beta.1):
/// nothing moves into the archive or the storage, nothing comes back from them, nothing is restored from a backup,
/// and "free up room" does not open. The commands are off and their tooltips, like the messages of the keys, say why
/// </summary>
internal sealed partial class MainViewModel
{
    /// <summary>The save's liveries are only shown: the profile's store says so for the save the gallery shows</summary>
    public bool IsSaveReadOnly => savePathService.Profile.IsSaveReadOnly(savePathService.SavePath);

    /// <summary>Why nothing moves, for the window's notice and the items' tooltips; null when liveries move</summary>
    public string? ReadOnlySaveText => IsSaveReadOnly ? ReadOnlySaveNotice.For(savePathService.Profile) : null;
}
