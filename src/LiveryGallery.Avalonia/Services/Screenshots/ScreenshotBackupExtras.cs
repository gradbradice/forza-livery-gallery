using ForzaToolkit.Liveries.Storage;

namespace LiveryGallery.Services.Screenshots;

/// <summary>
/// The screenshots in the backups (<c>extras/screenshots/&lt;key&gt;/</c>): taken along with their livery while the
/// setting "Include screenshots in backups" is on, and put back with it on restore when the livery has none here,
/// whatever the setting is now
/// </summary>
/// <param name="store">The screenshots of the game's liveries</param>
/// <param name="include">Whether a backup made now takes them (the setting)</param>
internal sealed class ScreenshotBackupExtras(ScreenshotStore store, Func<bool> include) : ILiveryBackupExtras
{
    public string Name => "screenshots";

    public IReadOnlyList<string> FilesOf(string key) => include() ? store.FilesForBackup(key) : [];

    public void Restore(string key, IReadOnlyList<LiveryBackupExtraFile> files) =>
        store.RestoreFromBackup(key, files.Select(f => (f.Name, (Func<Stream>)f.Open)));
}
