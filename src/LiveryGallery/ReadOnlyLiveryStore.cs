using ForzaToolkit.Liveries.Scanning;

namespace LiveryGallery.Profiles;

/// <summary>
/// A game's livery store whose saves are only read: <see cref="IsReadOnly"/> is always true, so the archive, the
/// storage and the backups of ForzaToolkit.Liveries move nothing out of the save and restore nothing into it, whatever
/// the gallery asks. FH3, FH4 and FH5 in 1.8.0-beta.1 (docs/fh3-fh5.md, decision 9): their liveries are shown, not
/// moved, until moving them is checked on real saves (Steam Cloud, the Xbox cloud). Everything else is the inner store's
/// </summary>
internal sealed class ReadOnlyLiveryStore(ILiveryStore inner) : ILiveryStore
{
    /// <summary>The store that reads the saves</summary>
    public ILiveryStore Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <inheritdoc/>
    public bool IsReadOnly(string savePath) => true;

    /// <inheritdoc/>
    public bool IsLiveryKey(string key) => Inner.IsLiveryKey(key);

    /// <inheritdoc/>
    public string? GetLiveryFolder(string savePath, string key) => Inner.GetLiveryFolder(savePath, key);

    /// <inheritdoc/>
    public IReadOnlyList<LiveryFileRef> ListFiles(string savePath, string key) => Inner.ListFiles(savePath, key);

    /// <inheritdoc/>
    public string? GetRestorePath(string savePath, string key, string fileName) => Inner.GetRestorePath(savePath, key, fileName);

    /// <inheritdoc/>
    public IReadOnlyList<string> ListLiveries(string savePath) => Inner.ListLiveries(savePath);

    /// <inheritdoc/>
    public LiveryFiles GetFiles(string savePath, string key) => Inner.GetFiles(savePath, key);

    /// <inheritdoc/>
    public bool IsExternalListing(string key) => Inner.IsExternalListing(key);

    /// <inheritdoc/>
    public ILiveryPreviewFinder BeginScan(string savePath, IReadOnlyCollection<string> keys) => Inner.BeginScan(savePath, keys);
}
