using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Views;
using System.Collections.ObjectModel;

namespace LiveryGallery.ViewModels;

internal sealed partial class FilterBarViewModel : ObservableObject
{
    private readonly AppSettingsData _settings;

    public event Action? FiltersChanged;

    /// <param name="profile">The game: the auction filter and the game-order sort only for a game that has them</param>
    public FilterBarViewModel(AppSettingsData settings, GameProfile profile)
    {
        _settings = settings;
        IsAuctionAvailable = profile.Features.Auction;
        IsGameOrderAvailable = profile.MyDesigns is not null;
        _sortMode = settings.SortMode;
        _favoriteMode = settings.FavoriteMode;
        _mineMode = settings.MineMode;
        _installedMode = settings.InstalledMode;
        _serverEnabled = settings.ServerConnectionEnabled;
        _duplicatesFilterMode = settings.DuplicatesFilterMode;
        _generatedFilterMode = settings.GeneratedFilterMode;
        _copiesFilterMode = settings.CopiesFilterMode;
        _paintFilterMode = settings.PaintFilterMode;
        _auctionFilterMode = settings.AuctionFilterMode;
        _groupingEnabled = settings.GroupingEnabled;
    }

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSortManufacturer))]
    [NotifyPropertyChangedFor(nameof(IsSortAuthor))]
    [NotifyPropertyChangedFor(nameof(IsSortDownloadTime))]
    [NotifyPropertyChangedFor(nameof(IsSortGameOrder))]
    [NotifyPropertyChangedFor(nameof(IsSortCountry))]
    [NotifyPropertyChangedFor(nameof(IsSortCarType))]
    [NotifyPropertyChangedFor(nameof(SortValueText))]
    [NotifyPropertyChangedFor(nameof(GroupingValueText))]
    [NotifyPropertyChangedFor(nameof(GroupingByKeyText))]
    private SortMode _sortMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFavNone))]
    [NotifyPropertyChangedFor(nameof(IsFavFirst))]
    [NotifyPropertyChangedFor(nameof(IsFavOnly))]
    [NotifyPropertyChangedFor(nameof(IsFavSeparate))]
    [NotifyPropertyChangedFor(nameof(FavoriteValueText))]
    private FavoriteMode _favoriteMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMineNone))]
    [NotifyPropertyChangedFor(nameof(IsMineFirst))]
    [NotifyPropertyChangedFor(nameof(IsMineOnly))]
    [NotifyPropertyChangedFor(nameof(IsMineSeparate))]
    [NotifyPropertyChangedFor(nameof(MineValueText))]
    private MineMode _mineMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalledNone))]
    [NotifyPropertyChangedFor(nameof(IsInstalledFirst))]
    [NotifyPropertyChangedFor(nameof(IsInstalledOnly))]
    [NotifyPropertyChangedFor(nameof(IsNotInstalledFirst))]
    [NotifyPropertyChangedFor(nameof(IsNotInstalledOnly))]
    [NotifyPropertyChangedFor(nameof(InstalledValueText))]
    private InstalledMode _installedMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstalledValueText))]
    [NotifyPropertyChangedFor(nameof(InstalledMenuTooltip))]
    private bool _isInstalledFilterAvailable;

    public InstalledMode EffectiveInstalledMode => IsInstalledFilterAvailable && !IsStorageMode ? InstalledMode : InstalledMode.None;

    public string? InstalledMenuTooltip => IsInstalledFilterAvailable ? null
        : _serverEnabled ? Strings.InstalledFilterWaitsForServer : Strings.InstalledFilterNeedsServer;

    // The livery server is on for the game; its list of the liveries on cars is there (from it or its cache)
    private bool _serverEnabled, _installedKnown;

    /// <summary>The livery server is on (or off) for the game: "On cars" works only with it</summary>
    public void SetInstalledFilterAvailable(bool serverEnabled)
    {
        _serverEnabled = serverEnabled;
        UpdateInstalledAvailable();
    }

    /// <summary>
    /// The list of the liveries on cars is there (or gone): "On cars" works only with it, so that "not on cars" does not
    /// take every livery for one no car uses before the list comes
    /// </summary>
    public void SetInstalledDataKnown(bool known)
    {
        _installedKnown = known;
        UpdateInstalledAvailable();
    }

    private void UpdateInstalledAvailable()
    {
        bool available = _serverEnabled && _installedKnown;
        OnPropertyChanged(nameof(InstalledMenuTooltip));
        if (IsInstalledFilterAvailable == available) return;
        IsInstalledFilterAvailable = available;
        if (InstalledMode != InstalledMode.None) FiltersChanged?.Invoke();
    }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDupAll))]
    [NotifyPropertyChangedFor(nameof(IsDupAndPossible))]
    [NotifyPropertyChangedFor(nameof(IsDupOnly))]
    [NotifyPropertyChangedFor(nameof(DuplicatesValueText))]
    private DuplicatesFilterMode _duplicatesFilterMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGenAll))]
    [NotifyPropertyChangedFor(nameof(IsGenOnly))]
    [NotifyPropertyChangedFor(nameof(GeneratedValueText))]
    private GeneratedFilterMode _generatedFilterMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaintAll))]
    [NotifyPropertyChangedFor(nameof(IsPaintHidden))]
    [NotifyPropertyChangedFor(nameof(IsPaintOnly))]
    [NotifyPropertyChangedFor(nameof(PaintValueText))]
    private PaintFilterMode _paintFilterMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuctionAll))]
    [NotifyPropertyChangedFor(nameof(IsAuctionSeparate))]
    [NotifyPropertyChangedFor(nameof(IsAuctionOnly))]
    [NotifyPropertyChangedFor(nameof(IsAuctionHidden))]
    [NotifyPropertyChangedFor(nameof(AuctionValueText))]
    private AuctionFilterMode _auctionFilterMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFavSeparateEnabled))]
    [NotifyPropertyChangedFor(nameof(IsMineSeparateEnabled))]
    [NotifyPropertyChangedFor(nameof(IsAuctionSeparateEnabled))]
    [NotifyPropertyChangedFor(nameof(IsGroupingNone))]
    [NotifyPropertyChangedFor(nameof(IsGroupingByKey))]
    [NotifyPropertyChangedFor(nameof(GroupingValueText))]
    private bool _groupingEnabled;

    public ObservableCollection<QuickFilterChipViewModel> QuickFilters { get; } = [];
    public bool HasQuickFilters => QuickFilters.Count > 0;
    public IReadOnlyList<QuickFilter> ActiveQuickFilters => [.. QuickFilters.Select(c => c.Filter)];

    [RelayCommand]
    private void ShowOnlyCar(LiveryEntry? entry)
    {
        if (entry is not null) SetQuickFilter(QuickFilter.ForCar(entry));
    }

    [RelayCommand]
    private void ShowOnlyManufacturer(LiveryEntry? entry)
    {
        if (entry is not null) SetQuickFilter(QuickFilter.ForManufacturer(entry));
    }

    [RelayCommand]
    private void ShowOnlyAuthor(LiveryEntry? entry)
    {
        if (entry is not null) SetQuickFilter(QuickFilter.ForAuthor(entry));
    }

    [RelayCommand]
    private void ShowOnlyCountry(LiveryEntry? entry)
    {
        if (entry is not null) SetQuickFilter(QuickFilter.ForCountry(entry));
    }

    [RelayCommand]
    private void ShowOnlyCarType(LiveryEntry? entry)
    {
        if (entry is not null) SetQuickFilter(QuickFilter.ForCarType(entry));
    }

    [RelayCommand]
    private void RemoveQuickFilter(QuickFilterChipViewModel? chip)
    {
        if (chip is null || !QuickFilters.Remove(chip)) return;
        // A car belongs to its manufacturer. Dropping the manufacturer drops the car as well
        if (chip.Filter.Kind == QuickFilterKind.Manufacturer)
            RemoveChips(c => c.Filter.Kind == QuickFilterKind.Car);
        OnQuickFiltersChanged();
    }

    public QuickFilter? ActiveManufacturerFilter =>
        QuickFilters.FirstOrDefault(c => c.Filter.Kind == QuickFilterKind.Manufacturer)?.Filter;

    [RelayCommand]
    private void ClearQuickFilters()
    {
        if (QuickFilters.Count == 0) return;
        QuickFilters.Clear();
        OnQuickFiltersChanged();
    }

    /// <summary>
    /// Every livery shows again (the statistics' "Liveries" tile): no search, no quick filters, no "only …" filter. How
    /// the gallery is ordered, and what it puts first or apart, stays
    /// </summary>
    public void ShowEverything()
    {
        SearchText = null;
        if (FavoriteMode == FavoriteMode.OnlyFavorites) FavoriteMode = FavoriteMode.None;
        if (MineMode == MineMode.OnlyMine) MineMode = MineMode.None;
        if (InstalledMode is InstalledMode.OnlyInstalled or InstalledMode.OnlyNotInstalled) InstalledMode = InstalledMode.None;
        if (AuctionFilterMode is AuctionFilterMode.OnlyAuction or AuctionFilterMode.HideAuction) AuctionFilterMode = AuctionFilterMode.All;
        DuplicatesFilterMode = DuplicatesFilterMode.All;
        GeneratedFilterMode = GeneratedFilterMode.All;
        PaintFilterMode = PaintFilterMode.All;
        CopiesFilterMode = CopiesFilterMode.All;
        ClearQuickFilters();
    }

    public void ApplyQuickFilter(QuickFilter filter) => SetQuickFilter(filter);

    private void SetQuickFilter(QuickFilter filter)
    {
        switch (filter.Kind)
        {
            case QuickFilterKind.Author:
            case QuickFilterKind.Country:
            case QuickFilterKind.CarType:
                // One of each: a new country replaces the old one; country, car type and author narrow each other
                RemoveChips(c => c.Filter.Kind == filter.Kind);
                QuickFilters.Add(new QuickFilterChipViewModel(filter));
                break;

            case QuickFilterKind.Manufacturer:
                RemoveChips(c => c.Filter.Kind == QuickFilterKind.Manufacturer
                    || (c.Filter.Kind == QuickFilterKind.Car && !QuickFilter.SameManufacturer(c.Filter.Manufacturer, filter)));
                QuickFilters.Insert(0, new QuickFilterChipViewModel(filter));
                break;

            case QuickFilterKind.Selection:
                // A set named by the statistics stands alone: other quick filters would hide some of it
                QuickFilters.Clear();
                QuickFilters.Add(new QuickFilterChipViewModel(filter));
                break;

            case QuickFilterKind.Car:
                RemoveChips(c => c.Filter.Kind == QuickFilterKind.Car
                    || (c.Filter.Kind == QuickFilterKind.Manufacturer && !QuickFilter.SameManufacturer(c.Filter, filter.Manufacturer)));
                if (filter.Manufacturer is { } manufacturer && ActiveManufacturerFilter is null)
                    QuickFilters.Insert(0, new QuickFilterChipViewModel(manufacturer));
                int afterManufacturer = ActiveManufacturerFilter is null ? 0 : 1;
                QuickFilters.Insert(afterManufacturer, new QuickFilterChipViewModel(filter));
                break;
        }
        OnQuickFiltersChanged();
    }

    private void RemoveChips(Func<QuickFilterChipViewModel, bool> predicate)
    {
        foreach (var chip in QuickFilters.Where(predicate).ToList())
            QuickFilters.Remove(chip);
    }

    private void OnQuickFiltersChanged()
    {
        if (_restoring) return;
        OnPropertyChanged(nameof(HasQuickFilters));
        OnPropertyChanged(nameof(HasActiveFilters));
        FiltersChanged?.Invoke();
    }

    /// <summary>
    /// Something hides liveries now: a search, a quick filter or an "only …" filter. Exactly what
    /// <see cref="ShowEverything"/> undoes, so the "Reset filters" button shows while there is something to reset.
    /// The order, and what is put first or apart, is not a filter
    /// </summary>
    public bool HasActiveFilters =>
        !string.IsNullOrEmpty(SearchText)
        || QuickFilters.Count > 0
        || FavoriteMode == FavoriteMode.OnlyFavorites
        || MineMode == MineMode.OnlyMine
        || EffectiveInstalledMode is InstalledMode.OnlyInstalled or InstalledMode.OnlyNotInstalled
        || EffectiveAuctionFilterMode is AuctionFilterMode.OnlyAuction or AuctionFilterMode.HideAuction
        || DuplicatesFilterMode != DuplicatesFilterMode.All
        || GeneratedFilterMode != GeneratedFilterMode.All
        || PaintFilterMode != PaintFilterMode.All
        || CopiesFilterMode != CopiesFilterMode.All;

    /// <summary>
    /// The chips under the filter bar: each "only …" filter that hides liveries now, named as the filter menu names it
    /// ("Favorites: Only"), and after them the quick filters (a car, an author…). A chip's cross takes that one filter
    /// off; "Reset filters" takes them all. The search is not a chip: its field shows it and has its own cross
    /// </summary>
    public IReadOnlyList<object> FilterChips => [.. ModeChips(), .. QuickFilters];


    /// <summary>There is a chip to show</summary>
    public bool HasFilterChips => QuickFilters.Count > 0 || ModeChips().Count > 0;

    private List<ModeFilterChipViewModel> ModeChips()
    {
        var chips = new List<ModeFilterChipViewModel>(4);
        void Add(bool on, string label, string value, Action off)
        {
            if (on) chips.Add(new ModeFilterChipViewModel($"{label}: {value}", off));
        }
        Add(FavoriteMode == FavoriteMode.OnlyFavorites, Strings.FilterFavoritesLabel, FavoriteValueText, () => FavoriteMode = FavoriteMode.None);
        Add(MineMode == MineMode.OnlyMine, Strings.FilterMineLabel, MineValueText, () => MineMode = MineMode.None);
        Add(EffectiveInstalledMode is InstalledMode.OnlyInstalled or InstalledMode.OnlyNotInstalled, Strings.FilterInstalledLabel,
            InstalledValueText, () => InstalledMode = InstalledMode.None);
        Add(EffectiveAuctionFilterMode is AuctionFilterMode.OnlyAuction or AuctionFilterMode.HideAuction, Strings.FilterAuctionLabel,
            AuctionValueText, () => AuctionFilterMode = AuctionFilterMode.All);
        Add(DuplicatesFilterMode != DuplicatesFilterMode.All, Strings.FilterDuplicatesLabel, DuplicatesValueText,
            () => DuplicatesFilterMode = DuplicatesFilterMode.All);
        Add(GeneratedFilterMode != GeneratedFilterMode.All, Strings.FilterGeneratedLabel, GeneratedValueText,
            () => GeneratedFilterMode = GeneratedFilterMode.All);
        Add(PaintFilterMode != PaintFilterMode.All, Strings.FilterPaintLabel, PaintValueText, () => PaintFilterMode = PaintFilterMode.All);
        Add(CopiesFilterMode != CopiesFilterMode.All, CopiesLabel, CopiesValueText, () => CopiesFilterMode = CopiesFilterMode.All);
        return chips;
    }

    /// <summary>The "Reset filters" button: every livery shows again, as <see cref="ShowEverything"/> does</summary>
    [RelayCommand]
    private void ResetFilters() => ShowEverything();

    // HasActiveFilters follows every property it reads
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(SearchText) or nameof(FavoriteMode) or nameof(MineMode) or nameof(InstalledMode)
            or nameof(AuctionFilterMode) or nameof(DuplicatesFilterMode) or nameof(GeneratedFilterMode)
            or nameof(PaintFilterMode) or nameof(CopiesFilterMode) or nameof(IsStorageMode) or nameof(IsInstalledFilterAvailable) or nameof(IsAuctionAvailable))
            OnPropertyChanged(nameof(HasActiveFilters));
        // The chips follow what HasActiveFilters follows, the quick filters, and the language of the value texts
        if (e.PropertyName is nameof(HasActiveFilters) or nameof(HasQuickFilters) or nameof(FavoriteValueText))
        {
            OnPropertyChanged(nameof(FilterChips));
            OnPropertyChanged(nameof(HasFilterChips));
        }
    }

    public bool IsSortManufacturer => SortMode == SortMode.Manufacture;
    public bool IsSortAuthor => SortMode == SortMode.Author;
    public bool IsSortDownloadTime => SortMode == SortMode.DownloadTime;
    public bool IsSortGameOrder => SortMode == SortMode.GameOrder;
    public bool IsSortCountry => SortMode == SortMode.Country;
    public bool IsSortCarType => SortMode == SortMode.CarType;

    [RelayCommand]
    private void SetSortMode(SortMode mode) => SortMode = mode;

    public bool IsFavNone => FavoriteMode == FavoriteMode.None;
    public bool IsFavFirst => FavoriteMode == FavoriteMode.FavoritesFirst;
    public bool IsFavOnly => FavoriteMode == FavoriteMode.OnlyFavorites;
    public bool IsFavSeparate => FavoriteMode == FavoriteMode.FavoritesSeparately;

    public bool IsFavSeparateEnabled => GroupingEnabled;

    [RelayCommand]
    private void SetFavoriteMode(FavoriteMode mode) => FavoriteMode = mode;

    public bool IsInstalledNone => InstalledMode == InstalledMode.None;
    public bool IsInstalledFirst => InstalledMode == InstalledMode.InstalledFirst;
    public bool IsInstalledOnly => InstalledMode == InstalledMode.OnlyInstalled;
    public bool IsNotInstalledFirst => InstalledMode == InstalledMode.NotInstalledFirst;
    public bool IsNotInstalledOnly => InstalledMode == InstalledMode.OnlyNotInstalled;

    [RelayCommand]
    private void SetInstalledMode(InstalledMode mode) => InstalledMode = mode;

    public bool IsMineNone => MineMode == MineMode.None;
    public bool IsMineFirst => MineMode == MineMode.MineFirst;
    public bool IsMineOnly => MineMode == MineMode.OnlyMine;
    public bool IsMineSeparate => MineMode == MineMode.MineSeparately;

    public bool IsMineSeparateEnabled => GroupingEnabled;

    [RelayCommand]
    private void SetMineMode(MineMode mode) => MineMode = mode;

    public bool IsDupAll => DuplicatesFilterMode == DuplicatesFilterMode.All;
    public bool IsDupAndPossible => DuplicatesFilterMode == DuplicatesFilterMode.DuplicatesAndPossible;
    public bool IsDupOnly => DuplicatesFilterMode == DuplicatesFilterMode.DuplicatesOnly;

    [RelayCommand]
    private void SetDuplicatesFilterMode(DuplicatesFilterMode mode) => DuplicatesFilterMode = mode;

    public bool IsGenAll => GeneratedFilterMode == GeneratedFilterMode.All;
    public bool IsGenOnly => GeneratedFilterMode == GeneratedFilterMode.GeneratedOnly;

    [RelayCommand]
    private void SetGeneratedFilterMode(GeneratedFilterMode mode) => GeneratedFilterMode = mode;

    public bool IsPaintAll => PaintFilterMode == PaintFilterMode.All;
    public bool IsPaintHidden => PaintFilterMode == PaintFilterMode.HidePaint;
    public bool IsPaintOnly => PaintFilterMode == PaintFilterMode.PaintOnly;

    [RelayCommand]
    private void SetPaintFilterMode(PaintFilterMode mode) => PaintFilterMode = mode;

    /// <summary>The game has an auction house: the auction filter is shown</summary>
    public bool IsAuctionAvailable { get; }

    /// <summary>The game has a livery menu grid: sorting in the game's order is offered</summary>
    public bool IsGameOrderAvailable { get; }

    /// <summary>The auction filter that applies: none for a game without an auction house (the saved choice stays)</summary>
    public AuctionFilterMode EffectiveAuctionFilterMode => IsAuctionAvailable && !IsStorageMode ? AuctionFilterMode : AuctionFilterMode.All;

    /// <summary>The sort that applies: the game's order only where there is one (the saved choice stays)</summary>
    public SortMode EffectiveSortMode =>
        SortMode == SortMode.GameOrder && !ShowGameOrderSort ? SortMode.Manufacture : SortMode;

    /// <summary>
    /// The storage is shown (docs/storage.md): no auction liveries, no cars, no place in the game's grid there, so
    /// those filters and the game's order do not apply. Its choices are not saved as the settings, which stay the game's
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAuctionFilter), nameof(ShowGameOrderSort), nameof(ShowInstalledFilter))]
    [NotifyPropertyChangedFor(nameof(CopyBadge), nameof(SimilarBadge), nameof(CopiesLabel))]
    private bool _isStorageMode;

    /// <summary>
    /// The filter of copies (<see cref="CopiesFilterMode"/>): the game shown, "In the storage"; the storage shown, "In
    /// the game"
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCopiesAll), nameof(IsCopiesSame), nameof(IsCopiesSameOrSimilar), nameof(IsCopiesSimilar))]
    [NotifyPropertyChangedFor(nameof(IsCopiesNone), nameof(CopiesValueText))]
    private CopiesFilterMode _copiesFilterMode;

    public bool IsCopiesAll => CopiesFilterMode == CopiesFilterMode.All;
    public bool IsCopiesSame => CopiesFilterMode == CopiesFilterMode.Same;
    public bool IsCopiesSameOrSimilar => CopiesFilterMode == CopiesFilterMode.SameOrSimilar;
    public bool IsCopiesSimilar => CopiesFilterMode == CopiesFilterMode.Similar;
    public bool IsCopiesNone => CopiesFilterMode == CopiesFilterMode.None;

    [RelayCommand]
    private void SetCopiesFilterMode(CopiesFilterMode mode) => CopiesFilterMode = mode;

    /// <summary>The badge of the same livery in the other place, as the cards show it</summary>
    public CardBadge CopyBadge => IsStorageMode ? CardBadge.CopyInGame : CardBadge.CopyInStorage;

    /// <summary>The badge of a similar one in the other place</summary>
    public CardBadge SimilarBadge => IsStorageMode ? CardBadge.SimilarInGame : CardBadge.SimilarInStorage;

    /// <summary>The filter's name: the other place</summary>
    public string CopiesLabel => IsStorageMode ? Strings.FilterCopiesInGameLabel : Strings.FilterCopiesInStorageLabel;

    public string CopiesValueText => CopiesValueTextOf(CopiesFilterMode);

    internal static string CopiesValueTextOf(CopiesFilterMode mode) => mode switch
    {
        CopiesFilterMode.Same => Strings.FilterValueCopySame,
        CopiesFilterMode.SameOrSimilar => Strings.FilterValueCopySameOrSimilar,
        CopiesFilterMode.Similar => Strings.FilterValueCopySimilar,
        CopiesFilterMode.None => Strings.FilterValueCopyNone,
        _ => Strings.FilterValueAll,
    };

    /// <summary>The auction filter is in the menu: the game has an auction house and the game is shown</summary>
    public bool ShowAuctionFilter => IsAuctionAvailable && !IsStorageMode;

    /// <summary>"In the game's order" is in the menu: the game has a grid and the game is shown</summary>
    public bool ShowGameOrderSort => IsGameOrderAvailable && !IsStorageMode;

    /// <summary>"Installed" is in the menu: only the game's liveries can be on a car</summary>
    public bool ShowInstalledFilter => !IsStorageMode;

    /// <summary>The sets of filters saved for this game (<see cref="FilterPresetData"/>)</summary>
    public IReadOnlyList<FilterPresetData> Presets => _settings.Game.FilterPresets;

    /// <summary>A set was saved or deleted: the menu that lists them is built again</summary>
    public event Action? PresetsChanged;

    /// <summary>Why <paramref name="name"/> cannot name a set; null when it can (a name already taken is replaced)</summary>
    public static string? PresetNameProblem(string name) => string.IsNullOrWhiteSpace(name) ? "" : null;

    /// <summary>
    /// Saves what the bar shows now, but the search, under <paramref name="name"/>; a set of that name is replaced, in
    /// its place in the list
    /// </summary>
    public void SavePreset(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return;
        var preset = new FilterPresetData
        {
            Name = name, SortMode = SortMode, GroupingEnabled = GroupingEnabled, FavoriteMode = FavoriteMode, MineMode = MineMode,
            InstalledMode = InstalledMode, DuplicatesFilterMode = DuplicatesFilterMode, GeneratedFilterMode = GeneratedFilterMode,
            PaintFilterMode = PaintFilterMode, AuctionFilterMode = AuctionFilterMode, CopiesFilterMode = CopiesFilterMode,
            QuickFilters = [.. ActiveQuickFilters.Select(QuickFilterData.From).OfType<QuickFilterData>()],
        };
        var presets = _settings.Game.FilterPresets;
        int same = presets.FindIndex(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (same >= 0) presets[same] = preset;
        else presets.Add(preset);
        AppSettingsService.Save(_settings);
        PresetsChanged?.Invoke();
    }

    /// <summary>
    /// What a saved set holds, a line for each thing, as the filter menu names them: the order, the grouping, every
    /// filter that is not at "All", and the quick filters. For the tooltip of the set in the menu, for one who forgot
    /// what the set was
    /// </summary>
    public static IReadOnlyList<string> DescribePreset(FilterPresetData preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var lines = new List<string>
        {
            $"{Strings.FilterSortLabel}: {SortValueTextOf(preset.SortMode)}",
            $"{Strings.FilterGroupingLabel}: {(preset.GroupingEnabled ? GroupingByKeyTextOf(preset.SortMode) : Strings.GroupingOptionNone)}",
        };
        void Add(bool set, string label, string value)
        {
            if (set) lines.Add($"{label}: {value}");
        }
        Add(preset.FavoriteMode != FavoriteMode.None, Strings.FilterFavoritesLabel, FavoriteValueTextOf(preset.FavoriteMode));
        Add(preset.MineMode != MineMode.None, Strings.FilterMineLabel, MineValueTextOf(preset.MineMode));
        Add(preset.InstalledMode != InstalledMode.None, Strings.FilterInstalledLabel, InstalledValueTextOf(preset.InstalledMode));
        Add(preset.AuctionFilterMode != AuctionFilterMode.All, Strings.FilterAuctionLabel, AuctionValueTextOf(preset.AuctionFilterMode));
        Add(preset.DuplicatesFilterMode != DuplicatesFilterMode.All, Strings.FilterDuplicatesLabel, DuplicatesValueTextOf(preset.DuplicatesFilterMode));
        Add(preset.GeneratedFilterMode != GeneratedFilterMode.All, Strings.FilterGeneratedLabel, GeneratedValueTextOf(preset.GeneratedFilterMode));
        Add(preset.PaintFilterMode != PaintFilterMode.All, Strings.FilterPaintLabel, PaintValueTextOf(preset.PaintFilterMode));
        Add(preset.CopiesFilterMode != CopiesFilterMode.All, Strings.FilterCopiesInStorageLabel, CopiesValueTextOf(preset.CopiesFilterMode));
        foreach (var filter in preset.QuickFilters) lines.Add(new QuickFilterChipViewModel(filter.ToFilter()).Text);
        return lines;
    }

    public void DeletePreset(FilterPresetData preset)
    {
        if (!_settings.Game.FilterPresets.Remove(preset)) return;
        AppSettingsService.Save(_settings);
        PresetsChanged?.Invoke();
    }

    /// <summary>
    /// Shows a saved set: its order, grouping, filters and quick filters, in one step (the gallery is shown again once).
    /// The search stays as it is
    /// </summary>
    public void ApplyPreset(FilterPresetData preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        _restoring = true; // no saving and no FiltersChanged for each property: once, below
        try
        {
            SortMode = preset.SortMode;
            GroupingEnabled = preset.GroupingEnabled;
            FavoriteMode = preset.FavoriteMode;
            MineMode = preset.MineMode;
            InstalledMode = preset.InstalledMode;
            DuplicatesFilterMode = preset.DuplicatesFilterMode;
            GeneratedFilterMode = preset.GeneratedFilterMode;
            PaintFilterMode = preset.PaintFilterMode;
            AuctionFilterMode = preset.AuctionFilterMode;
            CopiesFilterMode = preset.CopiesFilterMode;
            QuickFilters.Clear();
            foreach (var filter in preset.QuickFilters) QuickFilters.Add(new QuickFilterChipViewModel(filter.ToFilter()));
        }
        finally
        {
            _restoring = false;
        }
        OnPropertyChanged(nameof(HasQuickFilters));
        OnPropertyChanged(nameof(HasActiveFilters));
        Persist(s =>
        {
            s.SortMode = SortMode;
            s.GroupingEnabled = GroupingEnabled;
            s.FavoriteMode = FavoriteMode;
            s.MineMode = MineMode;
            s.InstalledMode = InstalledMode;
            s.DuplicatesFilterMode = DuplicatesFilterMode;
            s.GeneratedFilterMode = GeneratedFilterMode;
            s.PaintFilterMode = PaintFilterMode;
            s.AuctionFilterMode = AuctionFilterMode;
            s.CopiesFilterMode = CopiesFilterMode;
        });
    }

    private bool _restoring;

    /// <summary>What the bar shows now, to bring back when its mode is shown again</summary>
    public FilterBarState Capture() => new(
        SearchText, SortMode, FavoriteMode, MineMode, InstalledMode, DuplicatesFilterMode, GeneratedFilterMode,
        PaintFilterMode, AuctionFilterMode, GroupingEnabled, ActiveQuickFilters, CopiesFilterMode);

    /// <summary>
    /// Shows <paramref name="state"/> (another mode's) without saving it and without <see cref="FiltersChanged"/>: the
    /// caller shows the gallery again once the mode is in place
    /// </summary>
    public void Restore(FilterBarState state, bool storageMode)
    {
        ArgumentNullException.ThrowIfNull(state);
        _restoring = true;
        try
        {
            IsStorageMode = storageMode;
            SearchText = state.SearchText;
            SortMode = state.SortMode;
            FavoriteMode = state.FavoriteMode;
            MineMode = state.MineMode;
            InstalledMode = state.InstalledMode;
            DuplicatesFilterMode = state.DuplicatesFilterMode;
            GeneratedFilterMode = state.GeneratedFilterMode;
            PaintFilterMode = state.PaintFilterMode;
            AuctionFilterMode = state.AuctionFilterMode;
            CopiesFilterMode = state.CopiesFilterMode;
            GroupingEnabled = state.GroupingEnabled;
            QuickFilters.Clear();
            foreach (var filter in state.QuickFilters) QuickFilters.Add(new QuickFilterChipViewModel(filter));
            OnPropertyChanged(nameof(HasQuickFilters));
        }
        finally
        {
            _restoring = false;
        }
        OnPropertyChanged(nameof(HasActiveFilters)); // the quick filters were put back without their notice
    }

    public bool IsAuctionAll => AuctionFilterMode == AuctionFilterMode.All;
    public bool IsAuctionSeparate => AuctionFilterMode == AuctionFilterMode.AuctionSeparately;
    public bool IsAuctionOnly => AuctionFilterMode == AuctionFilterMode.OnlyAuction;
    public bool IsAuctionHidden => AuctionFilterMode == AuctionFilterMode.HideAuction;

    // Like favorites/mine. A separate group only makes sense when the gallery is grouped
    public bool IsAuctionSeparateEnabled => GroupingEnabled;

    [RelayCommand]
    private void SetAuctionFilterMode(AuctionFilterMode mode) => AuctionFilterMode = mode;

    public bool IsGroupingNone => !GroupingEnabled;
    public bool IsGroupingByKey => GroupingEnabled;

    [RelayCommand]
    private void DisableGrouping() => GroupingEnabled = false;

    [RelayCommand]
    private void EnableGrouping() => GroupingEnabled = true;

    public string SortValueText => SortValueTextOf(SortMode);

    internal static string SortValueTextOf(SortMode mode) => mode switch
    {
        SortMode.Author => Strings.SortOptionAuthor,
        SortMode.DownloadTime => Strings.SortOptionDownloadDate,
        SortMode.GameOrder => Strings.SortOptionGameOrder,
        SortMode.Country => Strings.SortOptionCountry,
        SortMode.CarType => Strings.SortOptionCarType,
        _ => Strings.SortOptionManufacturer
    };

    public string GroupingByKeyText => GroupingByKeyTextOf(SortMode);

    internal static string GroupingByKeyTextOf(SortMode mode) => mode switch
    {
        SortMode.Author => Strings.GroupingOptionByAuthor,
        SortMode.DownloadTime => Strings.GroupingOptionByMonth,
        SortMode.GameOrder => Strings.GroupingOptionByCar,
        SortMode.Country => Strings.GroupingOptionByCountry,
        SortMode.CarType => Strings.GroupingOptionByCarType,
        _ => Strings.GroupingOptionByManufacturer
    };

    public string GroupingValueText => GroupingEnabled ? GroupingByKeyText : Strings.GroupingOptionNone;

    public string FavoriteValueText => FavoriteValueTextOf(FavoriteMode);

    internal static string FavoriteValueTextOf(FavoriteMode mode) => mode switch
    {
        FavoriteMode.FavoritesFirst => Strings.FilterValueFirst,
        FavoriteMode.OnlyFavorites => Strings.FilterValueOnly,
        FavoriteMode.FavoritesSeparately => Strings.FilterValueSeparately,
        _ => Strings.FilterValueAll
    };

    public string InstalledValueText => !IsInstalledFilterAvailable ? Strings.FilterValueUnavailable : InstalledValueTextOf(InstalledMode);

    internal static string InstalledValueTextOf(InstalledMode mode) => mode switch
        {
            InstalledMode.InstalledFirst => Strings.FilterValueFirst,
            InstalledMode.OnlyInstalled => Strings.FilterValueOnly,
            InstalledMode.NotInstalledFirst => Strings.FilterValueNotInstalledFirst,
            InstalledMode.OnlyNotInstalled => Strings.FilterValueNotInstalledOnly,
            _ => Strings.FilterValueAll
        };

    public string MineValueText => MineValueTextOf(MineMode);

    internal static string MineValueTextOf(MineMode mode) => mode switch
    {
        MineMode.MineFirst => Strings.FilterValueFirst,
        MineMode.OnlyMine => Strings.FilterValueOnly,
        MineMode.MineSeparately => Strings.FilterValueSeparately,
        _ => Strings.FilterValueAll
    };

    public string DuplicatesValueText => DuplicatesValueTextOf(DuplicatesFilterMode);

    internal static string DuplicatesValueTextOf(DuplicatesFilterMode mode) => mode switch
    {
        DuplicatesFilterMode.DuplicatesAndPossible => Strings.FilterValueWithPossible,
        DuplicatesFilterMode.DuplicatesOnly => Strings.FilterValueOnly,
        _ => Strings.FilterValueAll
    };

    public string GeneratedValueText => GeneratedValueTextOf(GeneratedFilterMode);

    internal static string GeneratedValueTextOf(GeneratedFilterMode mode) => mode switch
    {
        GeneratedFilterMode.GeneratedOnly => Strings.FilterValueOnly,
        _ => Strings.FilterValueAll
    };

    public string PaintValueText => PaintValueTextOf(PaintFilterMode);

    internal static string PaintValueTextOf(PaintFilterMode mode) => mode switch
    {
        PaintFilterMode.HidePaint => Strings.FilterValueHide,
        PaintFilterMode.PaintOnly => Strings.FilterValueOnly,
        _ => Strings.FilterValueAll
    };

    public string AuctionValueText => AuctionValueTextOf(AuctionFilterMode);

    internal static string AuctionValueTextOf(AuctionFilterMode mode) => mode switch
    {
        AuctionFilterMode.AuctionSeparately => Strings.FilterValueSeparately,
        AuctionFilterMode.OnlyAuction => Strings.FilterValueOnly,
        AuctionFilterMode.HideAuction => Strings.FilterValueHide,
        _ => Strings.FilterValueAll
    };

    public void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(SortValueText));
        OnPropertyChanged(nameof(GroupingByKeyText));
        OnPropertyChanged(nameof(GroupingValueText));
        OnPropertyChanged(nameof(FavoriteValueText));
        OnPropertyChanged(nameof(MineValueText));
        OnPropertyChanged(nameof(InstalledValueText));
        OnPropertyChanged(nameof(InstalledMenuTooltip));
        OnPropertyChanged(nameof(DuplicatesValueText));
        OnPropertyChanged(nameof(GeneratedValueText));
        OnPropertyChanged(nameof(PaintValueText));
        OnPropertyChanged(nameof(AuctionValueText));
        OnPropertyChanged(nameof(CopiesLabel));
        OnPropertyChanged(nameof(CopiesValueText));
        foreach (var chip in QuickFilters) chip.RefreshLocalizedText();
    }

    partial void OnSortModeChanged(SortMode value) => Persist(s => s.SortMode = value);
    partial void OnFavoriteModeChanged(FavoriteMode value) => Persist(s => s.FavoriteMode = value);
    partial void OnMineModeChanged(MineMode value) => Persist(s => s.MineMode = value);
    partial void OnInstalledModeChanged(InstalledMode value) => Persist(s => s.InstalledMode = value);
    partial void OnDuplicatesFilterModeChanged(DuplicatesFilterMode value) => Persist(s => s.DuplicatesFilterMode = value);
    partial void OnGeneratedFilterModeChanged(GeneratedFilterMode value) => Persist(s => s.GeneratedFilterMode = value);
    partial void OnPaintFilterModeChanged(PaintFilterMode value) => Persist(s => s.PaintFilterMode = value);
    partial void OnAuctionFilterModeChanged(AuctionFilterMode value) => Persist(s => s.AuctionFilterMode = value);
    partial void OnGroupingEnabledChanged(bool value) => Persist(s => s.GroupingEnabled = value);
    partial void OnCopiesFilterModeChanged(CopiesFilterMode value) => Persist(s => s.CopiesFilterMode = value);

    private void Persist(Action<AppSettingsData> apply)
    {
        if (_restoring) return;
        // The settings are the game's: the storage's choices live while the app runs
        if (!IsStorageMode)
        {
            apply(_settings);
            AppSettingsService.Save(_settings);
        }
        FiltersChanged?.Invoke();
    }
}

/// <summary>What the bar of filters shows in one mode of the main window (the game, the storage)</summary>
internal sealed record FilterBarState(
    string? SearchText,
    SortMode SortMode,
    FavoriteMode FavoriteMode,
    MineMode MineMode,
    InstalledMode InstalledMode,
    DuplicatesFilterMode DuplicatesFilterMode,
    GeneratedFilterMode GeneratedFilterMode,
    PaintFilterMode PaintFilterMode,
    AuctionFilterMode AuctionFilterMode,
    bool GroupingEnabled,
    IReadOnlyList<QuickFilter> QuickFilters,
    CopiesFilterMode CopiesFilterMode = CopiesFilterMode.All)
{
    /// <summary>
    /// The storage the first time it is shown: ordered and grouped as the game, but with no search, no quick
    /// filters and no "only …" filter, so every stored livery shows
    /// </summary>
    public FilterBarState ForAnotherMode() => this with
    {
        SearchText = null,
        QuickFilters = [],
        FavoriteMode = FavoriteMode == FavoriteMode.OnlyFavorites ? FavoriteMode.None : FavoriteMode,
        MineMode = MineMode == MineMode.OnlyMine ? MineMode.None : MineMode,
        InstalledMode = InstalledMode.None,
        DuplicatesFilterMode = DuplicatesFilterMode.All,
        GeneratedFilterMode = GeneratedFilterMode.All,
        PaintFilterMode = PaintFilterMode.All,
        AuctionFilterMode = AuctionFilterMode.All,
        CopiesFilterMode = CopiesFilterMode.All,
        SortMode = SortMode == SortMode.GameOrder ? SortMode.DownloadTime : SortMode,
    };
}
