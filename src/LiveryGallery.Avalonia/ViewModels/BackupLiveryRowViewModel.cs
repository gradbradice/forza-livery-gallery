using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>A livery of a backup that the save no longer has (to restore), or one the save has that the backup has not</summary>
internal sealed class BackupLiveryRowViewModel : LiveryRowViewModel
{
    private readonly string? _thumbnailPath;
    private readonly bool _canCheck;

    private BackupLiveryRowViewModel(LiveryData data, string? thumbnailPath, bool canCheck) : base(data)
    {
        _thumbnailPath = thumbnailPath;
        _canCheck = canCheck;
    }

    /// <summary>A livery of the backup, to restore: its preview from the backup's previews when made already</summary>
    public static BackupLiveryRowViewModel FromBackup(LiveryData data, BackupPreviews previews, string? previewPath = null) =>
        new(data, previewPath ?? previews.FindExisting(data.ThumbnailPath), canCheck: true);

    /// <summary>A livery of the save the backup has not: only shown</summary>
    public static BackupLiveryRowViewModel FromCurrent(LiveryData data) =>
        new(data, data.ThumbnailPath is { Length: > 0 } path ? path : null, canCheck: false);

    /// <summary>Its preview is still to be made from the backup</summary>
    public bool NeedsPreview => ThumbnailPath is null && !string.IsNullOrEmpty(Data.ThumbnailPath);

    public override string? ThumbnailPath => _thumbnailPath;

    public override bool CanCheck => _canCheck;
}
