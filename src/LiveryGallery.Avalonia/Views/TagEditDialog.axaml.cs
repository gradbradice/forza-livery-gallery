using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;

namespace LiveryGallery.Views;

/// <summary>A tag in the dialog for several liveries: on all of them (ticked), some (a square) or none</summary>
internal sealed partial class TagCheckRow : ObservableObject
{
    public required string Tag { get; init; }

    /// <summary>The square only when some of the liveries have the tag: "leave it as it is"</summary>
    public bool IsThreeState { get; init; }

    public string? Tooltip { get; init; }

    [ObservableProperty]
    private bool? _isChecked;
}

/// <summary>
/// The tags of one or several liveries: every tag of the gallery as a box, ticked when the livery has it (with several
/// liveries: on all, some or none of them), and new tags typed as a list
/// </summary>
internal partial class TagEditDialog : Window
{
    private readonly List<TagCheckRow> _rows;

    /// <summary>The tags to put on every livery: the ticked ones and the typed ones</summary>
    public List<string> TagsToAdd { get; private set; } = new();

    /// <summary>The tags to take off every livery: the cleared ones (a typed tag is never taken off)</summary>
    public List<string> TagsToRemove { get; private set; } = new();

    /// <summary>A tag on some of several liveries stays as it is unless the user ticks or clears it</summary>
    /// <param name="tagsOfEach">The tags of each livery (one list for one livery)</param>
    /// <param name="knownTags">Every tag of the gallery</param>
    public TagEditDialog(IReadOnlyList<IReadOnlyList<string>> tagsOfEach, IEnumerable<string> knownTags)
    {
        InitializeComponent();
        int count = tagsOfEach.Count;
        bool one = count == 1;

        Title = Strings.TagsDialogTitle;
        TitleBarText.Text = Strings.TagsDialogTitle;
        TitleText.Text = one ? Strings.TagsDialogTitle : string.Format(Strings.TagsDialogManyTitleFormat, count);
        SaveButton.Content = Strings.ButtonSave;
        CancelButton.Content = Strings.ButtonCancel;
        TagsBox.PlaceholderText = Strings.TagsDialogHint;

        var having = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tags in tagsOfEach)
            foreach (string tag in tags.Distinct(StringComparer.OrdinalIgnoreCase))
                having[tag] = having.GetValueOrDefault(tag) + 1;

        _rows = [.. knownTags.Concat(having.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(tag =>
            {
                int n = having.GetValueOrDefault(tag);
                bool some = n > 0 && n < count;
                return new TagCheckRow
                {
                    Tag = tag,
                    IsThreeState = some,
                    IsChecked = some ? null : n == count,
                    Tooltip = some ? string.Format(Strings.TagsDialogPartialTooltipFormat, n, count) : null,
                };
            })];
        ManyTagsList.ItemsSource = _rows;
        ManyTagsScroll.IsVisible = _rows.Count > 0;

        // No tag in the whole gallery yet: only the field, with its hint
        HintText.Text = _rows.Count == 0 ? Strings.TagsDialogHint : one ? Strings.TagsDialogOneHint : Strings.TagsDialogManyHint;
        NewTagsLabel.Text = one ? Strings.TagsDialogOneNewTagsLabel : Strings.TagsDialogNewTagsLabel;
        NewTagsLabel.IsVisible = _rows.Count > 0;

        Loaded += (_, _) => TagsBox.Focus();
    }

    /// <summary>The tags typed in the field: split by commas, trimmed, each once</summary>
    internal static List<string> ParseTyped(string? text) => [.. (text ?? "")
        .Split(',')
        .Select(t => t.Trim())
        .Where(t => t.Length > 0)
        .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())];

    /// <summary>What the boxes and the field ask for: the ticked and typed tags go on, the cleared ones off</summary>
    internal static (List<string> Add, List<string> Remove) Changes(IEnumerable<TagCheckRow> rows, IReadOnlyCollection<string> typed)
    {
        var typedSet = new HashSet<string>(typed, StringComparer.OrdinalIgnoreCase);
        var add = rows.Where(r => r.IsChecked == true).Select(r => r.Tag).Concat(typed)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var remove = rows.Where(r => r.IsChecked == false && !typedSet.Contains(r.Tag)).Select(r => r.Tag).ToList();
        return (add, remove);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        (TagsToAdd, TagsToRemove) = Changes(_rows, ParseTyped(TagsBox.Text));
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void TagsBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SaveButton_Click(sender, e);
        else if (e.Key == Key.Escape) CancelButton_Click(sender, e);
    }
}
