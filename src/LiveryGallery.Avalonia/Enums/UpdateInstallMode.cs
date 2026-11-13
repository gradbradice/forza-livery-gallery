namespace LiveryGallery.Enums;

/// <summary>How the gallery installs a newer version it found (installed by its Setup or run from its portable zip)</summary>
internal enum UpdateInstallMode
{
    /// <summary>It says a new version is out, and downloads and installs it only when the user presses Install</summary>
    Ask = 0,

    /// <summary>It downloads the version at once, in the background, and installs it when the gallery closes</summary>
    Automatic = 1,
}
