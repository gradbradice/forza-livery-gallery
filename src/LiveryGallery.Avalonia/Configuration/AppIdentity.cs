namespace LiveryGallery.Configuration;

/// <summary>
/// Who the program is: its name, folders, User-Agent and where it is published. The gallery (LiveryGallery.Avalonia)
/// has no name of its own, like it knows no game of its own (<c>GameProfile</c>); the host program sets this once
/// at start with <see cref="AppSettings.Initialize"/>.
/// </summary>
internal sealed record AppIdentity
{
    /// <summary>The program's name: window title, About, the diagnostics README (<c>Forza Livery Gallery</c>)</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The folder of the program's data in %APPDATA% and of its caches in %LOCALAPPDATA%, and the start of the
    /// diagnostics archive's name (<c>ForzaLiveryGallery</c>). Changing it moves nothing by itself: the data of the
    /// old one is copied from <see cref="PreviousFolderName"/>
    /// </summary>
    public required string FolderName { get; init; }

    /// <summary>
    /// The folder the program used before it was renamed (<c>FH6LiveryGallery</c>, up to 1.6.0): the first start
    /// copies its data when <see cref="FolderName"/> has none, and leaves it as it was. Null: nothing to copy
    /// </summary>
    public string? PreviousFolderName { get; init; }

    /// <summary>The product token of the User-Agent, before the version (<c>Forza-Livery-Gallery</c>)</summary>
    public required string UserAgentProduct { get; init; }

    /// <summary>The prefix of the environment variables the program reads (<c>FORZA_LIVERY_GALLERY</c>)</summary>
    public required string EnvironmentPrefix { get; init; }

    /// <summary>The GitHub repository, <c>owner/name</c>: the link in About and Contacts, and the releases the updates come from (release/app-release.json has the same)</summary>
    public required string GitHubRepository { get; init; }

    /// <summary>
    /// The public keys whose signatures of a release list the gallery trusts when it updates itself (base64, from
    /// <c>forza-release keygen</c>; ForzaToolkit.AppHost.Updates; the same in publicKeys of release/app-release.json). Empty: the gallery installs nothing and only says a new
    /// version is out, with a link to it
    /// </summary>
    public IReadOnlyList<string> UpdatePublicKeys { get; init; } = [];

    /// <summary>The author's page on X shown in Contacts, or null to hide the line</summary>
    public string? XUrl { get; init; }

    /// <summary>How the X link reads (<c>@name</c>)</summary>
    public string? XLabel { get; init; }

    /// <summary>The author's e-mail shown in Contacts, or null to hide the line</summary>
    public string? Email { get; init; }

    /// <summary>The repository's page</summary>
    public string GitHubUrl => $"https://github.com/{GitHubRepository}";
}
