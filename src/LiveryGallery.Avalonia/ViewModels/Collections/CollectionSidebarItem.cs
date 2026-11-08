using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LiveryGallery.ViewModels.Collections;

/// <summary>
/// A row of the side panel of a collection (<see cref="Views.Collections.CollectionSidebar"/>): "All" and "Without"
/// rows the app has, then the user's own (the albums of the screenshots, the folders of the storage), which can be
/// renamed, deleted and dropped on
/// </summary>
internal sealed partial class CollectionSidebarItem : ObservableObject
{
    /// <param name="id">The album's or folder's id; for a row of the app, its own key</param>
    /// <param name="isUsers">The user's own: renamed, deleted, dropped on</param>
    /// <param name="acceptsDrops">Something can be dropped on it; the user's own rows when null ("No folder" takes liveries too)</param>
    public CollectionSidebarItem(string id, string name, Geometry? icon, bool isUsers, bool? acceptsDrops = null)
    {
        Id = id;
        _name = name;
        Icon = icon;
        IsUsers = isUsers;
        AcceptsDrops = acceptsDrops ?? isUsers;
    }

    public string Id { get; }
    public Geometry? Icon { get; }
    public bool IsUsers { get; }

    /// <summary>Something dragged can be dropped on the row (<see cref="Views.Collections.CollectionSidebar.CanDrop"/>)</summary>
    public bool AcceptsDrops { get; }

    /// <summary>The first of the user's own rows: a line above it parts them from the app's</summary>
    [ObservableProperty]
    private bool _isFirstOfUsers;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Something is dragged over the row and can be dropped on it</summary>
    [ObservableProperty]
    private bool _isDropTarget;

    /// <summary>A row dragged to a new place goes before this one: a line above it</summary>
    [ObservableProperty]
    private bool _isInsertBefore;

    /// <summary>A row dragged to a new place goes after this one (the last): a line below it</summary>
    [ObservableProperty]
    private bool _isInsertAfter;
}
