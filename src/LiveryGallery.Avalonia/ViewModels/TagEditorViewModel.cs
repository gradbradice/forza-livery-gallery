using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;

namespace LiveryGallery.ViewModels;

/// <summary>A tag of the liveries edited: on every one of them (solid), or on some of them (dashed, "3 of 5")</summary>
internal sealed class PropertyTag(string tag, int count, int total)
{
    public string Tag { get; } = tag;

    public int Count { get; } = count;

    public bool OnAll => Count >= total;

    /// <summary>"3 of 5" for a tag on some of them; empty for one on all</summary>
    public string Part => OnAll ? "" : string.Format(CultureInfo.CurrentCulture, Strings.NoticeCounterFormat, Count, total);
}

/// <summary>A tag offered as the user types: a tag some livery has, with how many have it; or a new one to make</summary>
internal sealed class TagSuggestion(string tag, int count, bool isNew)
{
    public string Tag { get; } = tag;

    public int Count { get; } = count;

    public bool IsNew { get; } = isNew;

    public string Text => IsNew ? string.Format(CultureInfo.CurrentCulture, Strings.PropertiesCreateTagFormat, Tag) : Tag;

    public string CountText => IsNew ? "" : Count.ToString(CultureInfo.CurrentCulture);
}

/// <summary>
/// The tags of one livery or of several, as the properties panel and the livery window edit them (one editor: the tag
/// dialog it replaces is gone). The tags on all of them are solid, those on some dashed with "3 of 5" (a click gives
/// the tag to all); a cross takes a tag off all of them. The field offers the gallery's tags: with nothing typed the
/// most used ones, else those holding what is typed (those starting with it first), with how many liveries have each,
/// and last a new tag; Enter takes the first, and "a, b" adds both
/// </summary>
internal sealed partial class TagEditorViewModel(
    Func<IReadOnlyList<LiveryEntry>> entries,
    Func<IEnumerable<LiveryEntry>> gallery,
    Action<IReadOnlyList<LiveryEntry>, IReadOnlyCollection<string>, IReadOnlyCollection<string>> apply) : ObservableObject
{
    private const int MostSuggestions = 6;

    private Dictionary<string, int>? _galleryCounts;
    private bool _inputFocused;

    /// <summary>The tags of the liveries: those on all first, then those on some, the more the earlier</summary>
    public ObservableCollection<PropertyTag> Tags { get; } = [];

    public bool IsMulti => entries().Count > 1;

    /// <summary>The tag being typed</summary>
    [ObservableProperty]
    private string _tagInput = "";

    public ObservableCollection<TagSuggestion> Suggestions { get; } = [];

    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>
    /// The list under the field is shown: there are suggestions and the field (or the list) has the focus. Text typed
    /// and left behind keeps its suggestions, but the list does not stay over the window once the field lost the focus
    /// or its panel was closed (it is a popup over the list of cards, not a part of the panel)
    /// </summary>
    public bool ShowSuggestions => HasSuggestions && _inputFocused;

    /// <summary>The liveries or their tags changed: the chips again, and the gallery's counts counted anew when asked</summary>
    public void Refresh()
    {
        var edited = entries();
        Tags.Clear();
        var counts = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var entry in edited)
            foreach (string tag in entry.Tags)
                counts[tag] = counts.GetValueOrDefault(tag) + 1;
        foreach (var (tag, count) in counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase))
            Tags.Add(new PropertyTag(tag, count, edited.Count));
        _galleryCounts = null;
        OnPropertyChanged(nameof(IsMulti));
        RefreshSuggestions();
    }

    /// <summary>The field took or lost the focus: with nothing typed it offers the most used tags while it has it</summary>
    public void SetInputFocused(bool focused)
    {
        if (_inputFocused == focused) return;
        _inputFocused = focused;
        RefreshSuggestions();
    }

    partial void OnTagInputChanged(string value) => RefreshSuggestions();

    // Never one all of them already have
    private void RefreshSuggestions()
    {
        Suggestions.Clear();
        string typed = LastTyped();
        if (entries().Count > 0 && (typed.Length > 0 || _inputFocused))
        {
            _galleryCounts ??= CountGallery();
            var onAll = Tags.Where(t => t.OnAll).Select(t => t.Tag).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            var found = _galleryCounts
                .Where(p => !onAll.Contains(p.Key) && p.Key.Contains(typed, StringComparison.CurrentCultureIgnoreCase))
                .OrderByDescending(p => typed.Length > 0 && p.Key.StartsWith(typed, StringComparison.CurrentCultureIgnoreCase))
                .ThenByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase)
                .Take(MostSuggestions);
            foreach (var (tag, count) in found) Suggestions.Add(new TagSuggestion(tag, count, isNew: false));
            if (typed.Length > 0 && !_galleryCounts.ContainsKey(typed)) Suggestions.Add(new TagSuggestion(typed, 0, isNew: true));
        }
        OnPropertyChanged(nameof(HasSuggestions));
        OnPropertyChanged(nameof(ShowSuggestions));
    }

    // "drift, cl": the suggestions are for the last one
    private string LastTyped() => TagInput.Split(',').Last().Trim();

    private Dictionary<string, int> CountGallery()
    {
        var counts = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var entry in gallery())
            foreach (string tag in entry.Tags)
                counts[tag] = counts.GetValueOrDefault(tag) + 1;
        return counts;
    }

    /// <summary>
    /// Enter in the field: each tag typed onto every livery, the last one as the first suggestion has it (its case as
    /// the gallery already writes it)
    /// </summary>
    [RelayCommand]
    private void AddTypedTag()
    {
        var typed = TagInput.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        if (typed.Count == 0) return;
        if (Suggestions.FirstOrDefault() is { } first && first.Tag.Contains(typed[^1], StringComparison.CurrentCultureIgnoreCase))
            typed[^1] = first.Tag;
        AddTags(typed);
    }

    /// <summary>A tag onto every livery (a dashed one: onto those that do not have it yet)</summary>
    [RelayCommand]
    private void AddTag(string tag) => AddTags([tag]);

    private void AddTags(IReadOnlyList<string> tags)
    {
        var edited = entries();
        var add = tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        if (add.Count == 0 || edited.Count == 0) return;
        apply(edited, add, []);
        TagInput = "";
        Refresh();
    }

    [RelayCommand]
    private void ChooseSuggestion(TagSuggestion suggestion) => AddTag(suggestion.Tag);

    /// <summary>A tag off every livery</summary>
    [RelayCommand]
    private void RemoveTag(string tag)
    {
        var edited = entries();
        if (edited.Count == 0) return;
        apply(edited, [], [tag]);
        Refresh();
    }
}
