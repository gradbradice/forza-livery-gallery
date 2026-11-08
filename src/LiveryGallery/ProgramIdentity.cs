using LiveryGallery.Configuration;

namespace LiveryGallery;

/// <summary>
/// This program, Forza Livery Gallery: the names the open gallery (LiveryGallery.Avalonia) takes from the host. Up to
/// 1.6.0 it was FH6 Livery Gallery; the first start of 1.7.0 copies that one's data (<see cref="AppIdentity.PreviousFolderName"/>).
/// </summary>
internal static class ProgramIdentity
{
    public static AppIdentity ForzaLiveryGallery { get; } = new()
    {
        Name = "Forza Livery Gallery",
        // %APPDATA%\ForzaLiveryGallery and %LOCALAPPDATA%\ForzaLiveryGallery since 1.7.0
        FolderName = "ForzaLiveryGallery",
        // Up to 1.6.0, as FH6 Livery Gallery: copied from on the first start, never changed
        PreviousFolderName = "FH6LiveryGallery",
        UserAgentProduct = "Forza-Livery-Gallery",
        EnvironmentPrefix = "FORZA_LIVERY_GALLERY",
        // Changes once the repository is renamed on GitHub (forza-livery-gallery); GitHub sends the old name on to it
        GitHubRepository = "gradbradice/forza-livery-gallery",
        // The key that signs the gallery's releases (forza-release keygen), the same as publicKeys of
        // release/app-release.json: a gallery installs only releases signed with it (release/README.md)
        UpdatePublicKeys = ["MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE3yA/MBECZvv6OMVUN5z39McGV5Q7Hy8IvSEToWZTYvIlT7RCeLD7ezr1HQSwN/MYbCJ+iJv2HdiTg5uMy7Nl3g=="],
        XUrl = "https://x.com/bradice_livery",
        XLabel = "@bradice__livery",
        Email = "tbradice@proton.me",
    };
}
