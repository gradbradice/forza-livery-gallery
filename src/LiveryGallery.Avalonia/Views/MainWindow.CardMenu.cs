using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using ForzaToolkit.UI.Controls;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>
/// The cards' context menu, built each time it opens from the gallery's actions (<see cref="GalleryActions"/>): the same
/// words, keys, checks and reasons as the bar, the panel and the keys. It works on the selection (it selects its card
/// first); only "Show only this car…" is about the card it was opened on
/// </summary>
internal partial class MainWindow
{
    private const int QuickFilterGroup = 2;

    private void FillCardMenu(ContextMenu menu, LiveryEntry card)
    {
        var actions = _mainViewModel.Actions;
        actions.Refresh();
        menu.Items.Clear();
        int? lastGroup = null;
        void Section(int group)
        {
            if (lastGroup is { } last && last != group) menu.Items.Add(new Separator());
            lastGroup = group;
        }

        bool filtersAdded = false;
        foreach (var action in actions.All.Where(a => a.Places.HasFlag(GalleryActionPlaces.Menu) && a.IsVisible))
        {
            if (!filtersAdded && action.Group > QuickFilterGroup)
            {
                AddQuickFilters(menu, card, Section);
                filtersAdded = true;
            }
            Section(action.Group);
            var item = ActionItem(action);
            menu.Items.Add(item);
            if (action.Id == GalleryActionId.MoveToFolder) FillMoveToFolderMenu(item);
        }
        if (!filtersAdded) AddQuickFilters(menu, card, Section);
    }

    private static MenuItem ActionItem(GalleryAction action)
    {
        var item = new MenuItem
        {
            Header = action.Label,
            Icon = ActionIcon(action, 16),
            Tag = action.Id,
        };
        if (action.Id != GalleryActionId.MoveToFolder)
        {
            item.Command = action.Command;
            item.IsEnabled = action.IsEnabled;
        }
        if (action.Key is { } key) ShortcutHint.SetAction(item, key);
        if (!string.IsNullOrEmpty(action.BlockedReason))
        {
            ToolTip.SetTip(item, action.BlockedReason);
            ToolTip.SetShowOnDisabled(item, true);
        }
        return item;
    }

    /// <summary>An action's icon: the toolkit's star for the favourites, else its 24-grid shape; null when it has none</summary>
    internal static Control? ActionIcon(GalleryAction action, double size)
    {
        if (action.Id == GalleryActionId.Favorite)
            return new FavoriteStar { IsOn = action.IsOn, Width = size, Height = size, StrokeThickness = 1.4 };
        if (string.IsNullOrEmpty(action.IconKey) || Avalonia.Application.Current?.FindResource(action.IconKey) is not Geometry shape) return null;
        var icon = new GridIcon { Data = shape, Width = size, Height = size, StrokeThickness = action.IconStroke };
        icon.Bind(GridIcon.ForegroundProperty, icon.GetResourceObservable("TextPrimaryBrush"));
        return icon;
    }

    // "Show only this car / make / author / country / type": the card the menu was opened on
    private void AddQuickFilters(ContextMenu menu, LiveryEntry card, Action<int> section)
    {
        var filters = _mainViewModel.FilterBar;
        section(QuickFilterGroup);
        void Add(string header, System.Windows.Input.ICommand command, string? icon = null, bool parameter = true)
        {
            var item = new MenuItem { Header = header, Command = command, CommandParameter = parameter ? card : null };
            if (icon is not null && Avalonia.Application.Current?.FindResource(icon) is Geometry shape)
            {
                var path = new Avalonia.Controls.Shapes.Path { Data = shape, Width = 13, Height = 12, Stretch = Stretch.Uniform };
                path.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, path.GetResourceObservable("TextPrimaryBrush"));
                item.Icon = path;
            }
            menu.Items.Add(item);
        }
        Add(Strings.ContextMenuOnlyThisCar, filters.ShowOnlyCarCommand);
        Add(Strings.ContextMenuOnlyThisManufacturer, filters.ShowOnlyManufacturerCommand);
        Add(Strings.ContextMenuOnlyThisAuthor, filters.ShowOnlyAuthorCommand);
        if (card.HasCarCountry) Add(Strings.ContextMenuOnlyThisCountry, filters.ShowOnlyCountryCommand, "IconFlag");
        if (card.HasCarType) Add(Strings.ContextMenuOnlyThisCarType, filters.ShowOnlyCarTypeCommand, "IconCarSide");
        if (filters.HasQuickFilters) Add(Strings.ContextMenuClearQuickFilters, filters.ClearQuickFiltersCommand, parameter: false);
    }
}
