using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;

namespace LiveryGallery.ViewModels;

/// <summary>
/// The row of every tag in the gallery, each shown, hidden or off. The tags a filter is on come first (marked), the rest
/// by name: a strip made low (<see cref="Views.TagsStrip"/>) never hides what filters the gallery. The order changes only
/// while the pointer is away (<see cref="HoldOrder"/>): a tag clicked does not jump from under it, nor its neighbours.
/// From <see cref="SearchFrom"/> tags on, a search narrows them (a user with some 200 tags, after 1.7.1)
/// </summary>
internal sealed partial class TagsBarViewModel : ObservableObject
{
    /// <summary>The search's place in the strip's flow (<see cref="Views.TagsStrip"/>): before the first tag</summary>
    public TagsSearchItem SearchItem => _searchItem ??= new TagsSearchItem(this);
    private TagsSearchItem? _searchItem;

    /// <summary>The search is offered from this many tags on</summary>
    public const int SearchFrom = 30;

    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;

    private readonly Func<string, TagFilterState> _stateOf;
    private readonly Action<string, TagFilterState> _onStateChanged;
    private HashSet<string>? _lastAllTags;
    private bool _holdOrder;

    public ObservableCollection<TagChipViewModel> AllTags { get; } = [];

    [ObservableProperty]
    private bool _hasTags;

    /// <summary>Enough tags to search among (<see cref="SearchFrom"/>)</summary>
    [ObservableProperty]
    private bool _showsSearch;

    /// <summary>The text looked for in the tags' names (case, kana and width aside); empty: every tag</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>A search that no tag matches</summary>
    [ObservableProperty]
    private bool _nothingFound;

    public TagsBarViewModel(Func<string, TagFilterState> stateOf, Action<string, TagFilterState> onStateChanged)
    {
        _stateOf = stateOf;
        _onStateChanged = onStateChanged;
    }

    /// <summary>
    /// The pointer is over the strip: the order stays as it is (a tag just marked keeps its place under the pointer);
    /// once it leaves, the marked tags move first
    /// </summary>
    public bool HoldOrder
    {
        get => _holdOrder;
        set
        {
            if (_holdOrder == value) return;
            _holdOrder = value;
            if (!value) Arrange();
        }
    }

    public bool RecomputeAllTags(IEnumerable<LiveryEntry> allEntries)
    {
        var allTagsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in allEntries)
            foreach (var tag in entry.Tags)
                allTagsSet.Add(tag);

        if (_lastAllTags is not null && _lastAllTags.SetEquals(allTagsSet)) return false;
        _lastAllTags = allTagsSet;

        AllTags.Clear();
        foreach (var chip in Ordered(allTagsSet.Select(tag => new TagChipViewModel(tag, _stateOf(tag), _onStateChanged))))
            AllTags.Add(chip);

        HasTags = AllTags.Count > 0;
        ShowsSearch = AllTags.Count >= SearchFrom;
        // Too few tags left to search among: the search goes, and every tag shows again
        if (!ShowsSearch && SearchText.Length > 0) SearchText = "";
        else ApplySearch();
        return true;
    }

    public void SyncSelection()
    {
        foreach (var chip in AllTags)
            chip.State = _stateOf(chip.Tag);
        if (!HoldOrder) Arrange();
    }

    /// <summary>The marked tags first, the rest after; each by name. Only the chips out of place move</summary>
    public void Arrange()
    {
        var target = Ordered(AllTags).ToList();
        for (int i = 0; i < target.Count; i++)
        {
            int at = AllTags.IndexOf(target[i]);
            if (at != i) AllTags.Move(at, i);
        }
    }

    /// <summary>Does the tag match the search (an empty search matches every tag)</summary>
    public static bool Matches(string tag, string search)
    {
        string text = search.Trim();
        return text.Length == 0 || CultureInfo.CurrentCulture.CompareInfo.IndexOf(tag, text, Loose) >= 0;
    }

    partial void OnSearchTextChanged(string value) => ApplySearch();

    private void ApplySearch()
    {
        bool any = false;
        foreach (var chip in AllTags)
        {
            chip.Matches = Matches(chip.Tag, SearchText);
            any |= chip.Matches;
        }
        NothingFound = AllTags.Count > 0 && !any;
    }

    private static IEnumerable<TagChipViewModel> Ordered(IEnumerable<TagChipViewModel> chips) =>
        chips.OrderBy(c => c.State == TagFilterState.Off ? 1 : 0).ThenBy(c => c.Tag, StringComparer.OrdinalIgnoreCase);
}

/// <summary>The search field among the tags, laid out as the first of them (<see cref="TagsBarViewModel.SearchItem"/>)</summary>
internal sealed class TagsSearchItem(TagsBarViewModel bar)
{
    public TagsBarViewModel Bar { get; } = bar;
}
