using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using LiveryGallery.Localisation;
using LiveryGallery.Services.Diagnostics;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>
/// What the gallery found in the storage's folder, put there by hand, and what it did with each livery: added as it
/// was, added into the storage's folder of its subfolder, given a game name, left where it lay (and why), or gone
/// </summary>
internal partial class StorageFoundDialog : Window
{
    private const string Mono = "Cascadia Mono, Consolas, monospace";

    public StorageFoundDialog() => InitializeComponent(); // the designer

    public StorageFoundDialog(StorageFoundDetails details)
    {
        InitializeComponent();
        Title = Strings.StorageFoundDialogTitle;
        TitleBarText.Text = Strings.StorageFoundDialogTitle;
        DoneButton.Content = Strings.StorageFoundDone;
        Build(details);
    }

    private void Build(StorageFoundDetails details)
    {
        var taken = details.Items.Where(i => i.IsTaken).ToList();
        var plain = taken.Where(i => i.FolderName is null && i.Renamed == LiveryIntakeRename.None).ToList();
        var inFolders = taken.Where(i => i.FolderName is not null && i.Renamed == LiveryIntakeRename.None).ToList();
        var renamed = taken.Where(i => i.Renamed != LiveryIntakeRename.None).ToList();
        var skipped = details.Items.Where(i => !i.IsTaken).ToList();
        var removed = details.RemovedFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (plain.Count > 0)
            Sections.Children.Add(Section("✓", "SuccessBrush", Strings.StorageFoundAddedSection, N(plain.Count),
                plain.Select(i => Row(i.FoundAt, null, null)), folded: true));
        if (inFolders.Count > 0)
        {
            string counts = string.Join(", ", inFolders.GroupBy(i => i.FolderName!, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => $"{g.Key} {N(g.Count())}"));
            Sections.Children.Add(Section("✓", "SuccessBrush", Strings.StorageFoundFoldersSection, $"{N(inFolders.Count)}: {counts}",
                inFolders.Select(i =>
                {
                    string what = string.Format(Strings.StorageFoundIntoFolderFormat, i.FolderName);
                    string? group = i.FoundAt.Split(Path.DirectorySeparatorChar, 2)[0];
                    if (removed.Contains(group)) what += " " + Strings.StorageFoundFolderRemoved;
                    return Row(i.FoundAt, what, null);
                }), folded: false));
        }
        if (renamed.Count > 0)
            Sections.Children.Add(Section("●", "WarningBrush", Strings.StorageFoundRenamedSection, N(renamed.Count),
                renamed.Select(i => Row(i.FoundAt, "→ " + (i.Renamed == LiveryIntakeRename.KeyTaken
                    ? string.Format(Strings.StorageFoundKeyTakenFormat, i.Key)
                    : string.Format(Strings.StorageFoundCarFormat, i.Key, i.CarId)), null)), folded: false));
        if (skipped.Count > 0)
            Sections.Children.Add(Section("✕", "DangerBrush", Strings.StorageFoundSkippedSection,
                $"{N(skipped.Count)}, {Strings.StorageFoundSkippedNote}",
                skipped.Select(i => Row(i.FoundAt, Reason(i), i.Path)), folded: false));
        if (details.Gone.Count > 0)
            Sections.Children.Add(Section("–", "TextSecondaryBrush", Strings.StorageFoundGoneSection,
                $"{N(details.Gone.Count)}, {Strings.StorageFoundGoneNote}", details.Gone.Select(k => Row(k, null, null)), folded: true));
    }

    private static string Reason(LiveryIntakeItem item) => item.Problem switch
    {
        LiveryIntakeProblem.Incomplete => Strings.StorageFoundIncomplete,
        LiveryIntakeProblem.Auction => Strings.StorageFoundAuction,
        LiveryIntakeProblem.SameAsStored => string.Format(Strings.StorageFoundSameFormat, item.SameAs),
        LiveryIntakeProblem.NoCar => Strings.StorageFoundNoCar,
        LiveryIntakeProblem.TooDeep => Strings.StorageFoundTooDeep,
        _ => Strings.StorageFoundFailed,
    };

    // A brush of the theme shown (the window is made fresh each time it opens)
    private static IBrush? Brush(string key) =>
        Avalonia.Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var value) ? value as IBrush : null;

    private static string N(int value) => value.ToString("N0", Services.AppLocalisationService.Culture);

    // A section: its mark, its name, its count, and its rows (folded behind "show")
    private Control Section(string mark, string brush, string name, string count, IEnumerable<Control> rows, bool folded)
    {
        var list = new StackPanel { Spacing = 0, Margin = new Avalonia.Thickness(0, 6, 0, 0), IsVisible = !folded };
        foreach (var row in rows) list.Children.Add(row);
        var header = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        header.Inlines!.Add(new Run(mark + "  ") { FontWeight = FontWeight.Bold, Foreground = Brush(brush) });
        header.Inlines.Add(new Run(name) { FontWeight = FontWeight.SemiBold });
        header.Inlines.Add(new Run("  " + count) { Foreground = Brush("TextSecondaryBrush") });
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        top.Children.Add(header);
        if (folded)
        {
            var toggle = new Button { Classes = { "rowLink" }, Content = "▸ " + Strings.StorageFoundShow, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            toggle.Click += (_, _) =>
            {
                list.IsVisible = !list.IsVisible;
                toggle.Content = (list.IsVisible ? "▾ " + Strings.StorageFoundHide : "▸ " + Strings.StorageFoundShow);
            };
            top.Children.Add(toggle);
        }
        return new StackPanel { Children = { top, list } };
    }

    // A row: where it was found, what became of it, and "Show in folder" for one that stayed
    private Control Row(string foundAt, string? what, string? path)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*,Auto"), Margin = new Avalonia.Thickness(18, 0, 0, 0), MinHeight = 30 };
        var border = new Border
        {
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            Padding = new Avalonia.Thickness(0, 5),
            BorderBrush = Brush("BorderBrush2"),
            Child = grid,
        };
        var where = new TextBlock
        {
            Text = foundAt, FontFamily = new FontFamily(Mono), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, [ToolTip.TipProperty] = foundAt,
            Foreground = Brush("TextSecondaryBrush"),
        };
        grid.Children.Add(where);
        if (what is not null)
        {
            var text = new TextBlock { Text = what, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(12, 0, 0, 0) };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
        }
        if (path is not null)
        {
            var show = new Button { Classes = { "secondary" }, Content = Strings.ScreenshotShowInFolder, Padding = new Avalonia.Thickness(10, 3), Margin = new Avalonia.Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            show.Click += (_, _) => ExplorerLauncher.ShowInFolder(path);
            Grid.SetColumn(show, 2);
            grid.Children.Add(show);
        }
        return border;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    /// <summary>Shows the details over <paramref name="owner"/></summary>
    public static Task ShowAsync(Window owner, StorageFoundDetails details) => new StorageFoundDialog(details).ShowDialog(owner);
}
