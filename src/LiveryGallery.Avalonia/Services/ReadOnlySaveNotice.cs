using LiveryGallery.Localisation;
using LiveryGallery.Profiles;

namespace LiveryGallery.Services;

/// <summary>The words of a save whose liveries are only shown (<see cref="GameProfile.IsSaveReadOnly"/>)</summary>
internal static class ReadOnlySaveNotice
{
    /// <summary>"Liveries of FH4 are only shown in this version: …", with the game's short name</summary>
    public static string For(GameProfile profile) => string.Format(Strings.SaveReadOnlyNoticeFormat, profile.Game.ShortName);
}
