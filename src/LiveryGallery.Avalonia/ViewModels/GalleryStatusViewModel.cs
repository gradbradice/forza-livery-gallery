using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

internal sealed partial class GalleryStatusViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _loadingText;

    [ObservableProperty]
    private string _countText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFavorites))]
    private int _favoritesCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDuplicates))]
    private int _duplicatesCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPossibleDuplicates))]
    private int _possibleDuplicatesCount;

    [ObservableProperty]
    private string _emptyStateText = string.Empty;

    [ObservableProperty]
    private bool _isEmptyStateVisible;

    /// <summary>The empty gallery is because the game's save was not found: its buttons are shown</summary>
    [ObservableProperty]
    private bool _isSaveMissing;

    /// <summary>Nothing shows because of the search: «Clear the search» under the text</summary>
    [ObservableProperty]
    private bool _canClearSearch;

    /// <summary>Nothing shows because of the filters or the tags: «Reset the filters» under the text</summary>
    [ObservableProperty]
    private bool _canResetFilters;

    /// <summary>The game's save has no livery: «Choose another folder» and «Look again» under the text</summary>
    [ObservableProperty]
    private bool _isGameEmpty;

    /// <summary>The storage has no livery: «Show the game» under the text</summary>
    [ObservableProperty]
    private bool _isStorageEmpty;

    /// <summary>The empty gallery has a button to show</summary>
    public bool HasEmptyActions => IsSaveMissing || CanClearSearch || CanResetFilters || IsGameEmpty || IsStorageEmpty;

    partial void OnIsSaveMissingChanged(bool value) => OnPropertyChanged(nameof(HasEmptyActions));
    partial void OnCanClearSearchChanged(bool value) => OnPropertyChanged(nameof(HasEmptyActions));
    partial void OnCanResetFiltersChanged(bool value) => OnPropertyChanged(nameof(HasEmptyActions));
    partial void OnIsGameEmptyChanged(bool value) => OnPropertyChanged(nameof(HasEmptyActions));
    partial void OnIsStorageEmptyChanged(bool value) => OnPropertyChanged(nameof(HasEmptyActions));

    private void ClearEmptyActions()
    {
        IsSaveMissing = false;
        CanClearSearch = false;
        CanResetFilters = false;
        IsGameEmpty = false;
        IsStorageEmpty = false;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefresh))]
    private bool _isScanRunning;

    public bool CanRefresh => !IsScanRunning;

    public bool HasFavorites => FavoritesCount > 0;
    public bool HasDuplicates => DuplicatesCount > 0;
    public bool HasPossibleDuplicates => PossibleDuplicatesCount > 0;

    public void RenderScanStatus(LiveryScanEntry? lastScanResult)
    {
        if (lastScanResult is null) return;

        string text = string.Format(Strings.StatusProcessed, lastScanResult.Parsed, lastScanResult.ReusedFromCache);
        if (lastScanResult.Errors > 0) text += string.Format(Strings.StatusErrors, lastScanResult.Errors);
        if (lastScanResult.Removed > 0) text += string.Format(Strings.StatusRemoved, lastScanResult.Removed);
        StatusText = text;
    }

    public void SetStatusText(string text) => StatusText = text;

    public void ShowEmptyState(string text)
    {
        EmptyStateText = text;
        IsEmptyStateVisible = true;
        ClearEmptyActions();
    }

    /// <summary>The game's save was not found: said in place of the gallery, with a way to choose it</summary>
    public void ShowSaveMissing(string text)
    {
        EmptyStateText = text;
        IsEmptyStateVisible = true;
        ClearEmptyActions();
        IsSaveMissing = true;
    }

    public void SetLoading(bool loading, string? text = null)
    {
        IsLoading = loading;
        if (text is not null) LoadingText = text;
    }

    /// <param name="storage">The storage is shown: an empty one says how liveries get there</param>
    /// <param name="emptyText">What an empty view says instead (an empty folder of the storage)</param>
    /// <param name="otherFilters">A filter or a tag other than the search hides liveries now</param>
    public void UpdateCountsAndEmptyState(List<LiveryEntry> filtered, int allEntriesCount, string? searchText, bool storage = false,
        string? emptyText = null, bool otherFilters = false)
    {
        string search = searchText?.Trim() ?? string.Empty;
        ClearEmptyActions();
        var stats = GalleryStatisticsService.Calculate(filtered);

        CountText = allEntriesCount == 0 ? string.Empty : string.Format(Strings.CountShowing, filtered.Count, allEntriesCount);
        FavoritesCount = stats.FavoritesShown;
        DuplicatesCount = stats.DuplicatesShown;
        PossibleDuplicatesCount = stats.PossibleDuplicatesShown;

        if (allEntriesCount == 0)
        {
            EmptyStateText = emptyText ?? (storage ? Strings.StorageEmptyHint : Strings.EmptyNoLiveries);
            IsEmptyStateVisible = true;
            IsStorageEmpty = storage && emptyText is null;
            IsGameEmpty = !storage;
        }
        else if (filtered.Count == 0)
        {
            // What hides them, and the way back: the search, the filters, or both
            EmptyStateText = search.Length == 0 ? Strings.EmptyNoFilterResults
                : otherFilters ? string.Format(Strings.EmptyNoResultsWithFilters, search)
                : string.Format(Strings.EmptyNoResults, search);
            IsEmptyStateVisible = true;
            CanClearSearch = search.Length > 0;
            CanResetFilters = otherFilters || search.Length == 0;
        }
        else
        {
            IsEmptyStateVisible = false;
        }
    }
}
