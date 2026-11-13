using ForzaToolkit.Liveries.Gallery;
using LiveryGallery.Enums;
using LiveryGallery.Services;

namespace LiveryGallery.Models;

/// <summary>
/// A set of filters the user saved under a name (the filter bar's "Saved filter sets"), kept with the game's settings:
/// everything the bar shows but the search — the order, the grouping, each filter of the menu and the quick filters
/// (a car, a manufacturer, an author, a country, a car type)
/// </summary>
internal sealed class FilterPresetData
{
    public string Name { get; set; } = "";
    public SortMode SortMode { get; set; } = SortMode.Manufacture;
    public bool GroupingEnabled { get; set; } = true;
    public FavoriteMode FavoriteMode { get; set; }
    public MineMode MineMode { get; set; }
    public InstalledMode InstalledMode { get; set; }
    public DuplicatesFilterMode DuplicatesFilterMode { get; set; }
    public GeneratedFilterMode GeneratedFilterMode { get; set; }
    public PaintFilterMode PaintFilterMode { get; set; }
    public AuctionFilterMode AuctionFilterMode { get; set; }
    public CopiesFilterMode CopiesFilterMode { get; set; }
    public List<QuickFilterData> QuickFilters { get; set; } = [];
}

/// <summary>A quick filter as it is written to the settings (<see cref="QuickFilter"/> without what is not kept)</summary>
internal sealed class QuickFilterData
{
    public QuickFilterKind Kind { get; set; }
    public int CarId { get; set; }
    public string? Value { get; set; }
    public bool CarKnown { get; set; } = true;
    public string DisplayValue { get; set; } = "";
    public string? ManufacturerValue { get; set; }
    public string? ManufacturerDisplayValue { get; set; }

    /// <summary>Null for a filter that is not kept: a set of liveries named by the statistics lives only while it is shown</summary>
    public static QuickFilterData? From(QuickFilter filter) => filter.Kind == QuickFilterKind.Selection || filter.Keys is not null
        ? null
        : new QuickFilterData
        {
            Kind = filter.Kind, CarId = filter.CarId, Value = filter.Value, CarKnown = filter.CarKnown, DisplayValue = filter.DisplayValue,
            ManufacturerValue = filter.Manufacturer?.Value, ManufacturerDisplayValue = filter.Manufacturer?.DisplayValue,
        };

    public QuickFilter ToFilter() => new()
    {
        Kind = Kind, CarId = CarId, Value = Value, CarKnown = CarKnown, DisplayValue = DisplayValue,
        Manufacturer = ManufacturerDisplayValue is null ? null
            : new QuickFilter { Kind = QuickFilterKind.Manufacturer, Value = ManufacturerValue, DisplayValue = ManufacturerDisplayValue },
    };
}
