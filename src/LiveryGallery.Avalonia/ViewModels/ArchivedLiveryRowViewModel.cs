using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;
using ForzaToolkit.Core.Text;

namespace LiveryGallery.ViewModels;

/// <summary>A livery of the archive (or the storage's list in it): when it was archived, whose it is</summary>
/// <param name="showFolderName">
/// The setting "show folder names and search by them": the row shows the folder under its date, as a gallery card does,
/// the number's tooltip names it, and search looks at it
/// </param>
internal sealed class ArchivedLiveryRowViewModel(ArchivedLiveryEntry entry, AccountContext accounts, bool showFolderName = false)
    : LiveryRowViewModel(entry.Data)
{
    public ArchivedLiveryEntry Entry { get; } = entry;

    /// <summary>Only another account is shown: on rows of the open account the note would be noise</summary>
    public override bool IsFromOtherAccount { get; } = accounts.IsOther(entry.SourceAccount);

    public override string OtherAccountText { get; } = accounts.SourceNote(entry.SourceAccount);

    public override bool ShowFolderName { get; } = showFolderName;

    /// <summary>As on a gallery card: the number, and the folder when folder names are shown</summary>
    public override string? LiveryIdTooltip => ShowFolderName && LiveryIdText.Length > 0 ? $"{LiveryIdText} ({FolderName})" : null;

    public override string? ThumbnailPath => Entry.Data.ThumbnailPath;

    public override string DetailText => string.Format(Strings.ArchivedOnFormat, Entry.ArchivedAtUtc.ToLocalTime().ToString("d"));

    private string? _searchHaystack;

    // What the row shows, as the gallery's search looks at a card: car, year, name, author, and the number
    private string SearchHaystack => _searchHaystack ??= $"{CarText} {Entry.Data.CarYear} {LiveryName} {AuthorRaw} {LiveryIdText}";

    /// <summary>Every token is in the row's texts (or, with folder names shown, in the folder name), ignoring case</summary>
    public bool MatchesSearch(string[] tokens) => SearchWords.AllIn(tokens, SearchHaystack, ShowFolderName ? FolderName : null);
}
