using LiveryGallery.Enums;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LiveryGallery.ViewModels;

internal class LiveryGroup : INotifyPropertyChanged, IDisposable
{
    private string _key = "";
    public required string Key
    {
        get => _key;
        set
        {
            if (_key == value) return;
            _key = value;
            OnPropertyChanged();
        }
    }

    private List<LiveryEntry> _items = [];
    public required List<LiveryEntry> Items
    {
        get => _items;
        init
        {
            _items = value;
            foreach (var entry in _items)
                entry.PropertyChanged += OnEntryPropertyChanged;
        }
    }

    public int Count => Items.Count;
    public int FavoriteCount => Items.Count(x => x.IsFavorite);
    public int MineCount => Items.Count(x => x.IsMine);
    public int DuplicateCount => Items.Count(x => x.IsDuplicate);
    public int PossibleDuplicateCount => Items.Count(x => x.IsPossibleDuplicate);

    public bool HasFavorites => FavoriteCount > 0;
    public bool HasMine => MineCount > 0;
    public bool HasDuplicates => DuplicateCount > 0;
    public bool HasPossibleDuplicates => PossibleDuplicateCount > 0;

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LiveryEntry.IsFavorite)) return;
        OnPropertyChanged(nameof(FavoriteCount));
        OnPropertyChanged(nameof(HasFavorites));
    }

    public void Dispose()
    {
        foreach (var entry in _items)
            entry.PropertyChanged -= OnEntryPropertyChanged;
    }

    public bool IsFavoritesGroup { get; init; }
    public bool IsMineGroup { get; init; }
    public bool IsAuctionGroup { get; init; }

    public QuickFilter? GroupFilter { get; init; }
    public bool HasGroupFilter => GroupFilter is not null;

    public LiveryGroupSpecialKind SpecialKind { get; init; } = LiveryGroupSpecialKind.None;
    public DateTime? SpecialMonth { get; init; }
    private double _groupWidth = 1200;
    public double GroupWidth
    {
        get => _groupWidth;
        set
        {
            if (_groupWidth == value) return;
            _groupWidth = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Rows));
        }
    }

    /// <summary>The room of one card, its margins with it (<see cref="Models.CardLayout.CardStep"/>)</summary>
    public double CardStep { get; set; } = Models.CardLayout.NormalWidth + 2 * Models.CardLayout.Margin;

    private LazyRowList? _rows;
    private int _rowsBuiltForColumns = -1;

    /// <summary>The group's cards by rows: as many in a row as its width holds. Kept while that number is the same (a
    /// wider window with as many columns only makes the cards wider)</summary>
    public IReadOnlyList<GalleryRow> Rows
    {
        get
        {
            int columns = Math.Max(1, (int)Math.Round(GroupWidth / CardStep));
            if (_rows is not null && _rowsBuiltForColumns == columns) return _rows;
            _rows = new LazyRowList(this, Items, columns);
            _rowsBuiltForColumns = columns;
            return _rows;
        }
    }

    public string CountText => $"({Count})";

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
