using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>
/// A livery in a list of the archive or a backup (Views/LiveryRow.axaml): its picture, name, number, author and car as
/// the file has them, a box to tick it, and what the list adds (when it was archived, its folder, another account)
/// </summary>
internal abstract partial class LiveryRowViewModel(LiveryData data) : ObservableObject, IThumbnailHost
{
    public LiveryData Data { get; } = data;

    public string FolderName => Data.FolderName;

    public string LiveryName => Data.DisplayLiveryName;

    public string LiveryIdText => LiveryRestoreConflicts.FormatLiveryId(Data.LiveryId);

    public string AuthorRaw => Data.DisplayAuthorRaw;

    /// <summary>"Nissan Skyline GT-R": the make and the model as the file has them</summary>
    public string CarText => string.Join(' ', new[] { Data.CarManufacturerRaw, Data.CarModelNameRaw }.Where(p => !string.IsNullOrWhiteSpace(p)));

    public abstract string? ThumbnailPath { get; }

    /// <summary>The row has a box to tick (a livery to restore); a row only shown has none</summary>
    public virtual bool CanCheck => true;

    /// <summary>The number's tooltip (with the folder when folder names are shown); null for none</summary>
    public virtual string? LiveryIdTooltip => null;

    /// <summary>A line under the car ("Archived on 12/11/2026"); empty for none</summary>
    public virtual string DetailText => "";

    public bool HasDetail => DetailText.Length > 0;

    /// <summary>The folder under the rest, as on a gallery card, while folder names are shown</summary>
    public virtual bool ShowFolderName => false;

    /// <summary>The livery is another account's: the line that says whose</summary>
    public virtual bool IsFromOtherAccount => false;

    public virtual string OtherAccountText => "";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Bitmap? _thumbnail;
}

/// <summary>
/// The ticked rows of a list (the archive's, a backup's): how many, all or none, told when that changes. It follows the
/// list: rows added are watched, rows gone are not
/// </summary>
internal sealed class CheckedRows<T> where T : LiveryRowViewModel
{
    private readonly ObservableCollection<T> _rows;
    private readonly Action _changed;
    private readonly HashSet<T> _watched = [];

    /// <param name="rows">The rows shown</param>
    /// <param name="changed">Told when the ticked rows change</param>
    public CheckedRows(ObservableCollection<T> rows, Action changed)
    {
        _rows = rows;
        _changed = changed;
        rows.CollectionChanged += Rows_CollectionChanged;
        Watch();
    }

    public IReadOnlyList<T> Selected => [.. _rows.Where(r => r.IsSelected)];

    public bool Any => _rows.Any(r => r.IsSelected);

    public int Count => _rows.Count(r => r.IsSelected);

    /// <summary>"Selected: 3"</summary>
    public string CountText => string.Format(Strings.ArchiveSelectedCountFormat, Count);

    public void SelectAll()
    {
        foreach (var row in _rows) if (row.CanCheck) row.IsSelected = true;
    }

    public void Clear()
    {
        foreach (var row in _rows) row.IsSelected = false;
    }

    private void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Watch();
        _changed();
    }

    // Every row shown watched once, the ones gone let go
    private void Watch()
    {
        var shown = _rows.ToHashSet();
        foreach (var gone in _watched.Where(r => !shown.Contains(r)).ToList())
        {
            gone.PropertyChanged -= Row_PropertyChanged;
            _watched.Remove(gone);
        }
        foreach (var row in shown)
            if (_watched.Add(row)) row.PropertyChanged += Row_PropertyChanged;
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LiveryRowViewModel.IsSelected)) _changed();
    }
}
