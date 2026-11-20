using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>What can be done with the selected liveries (<see cref="GalleryActions"/>)</summary>
internal enum GalleryActionId
{
    OpenDetails,
    View3D,
    View3DNewWindow,
    Favorite,
    Lock,
    Tags,
    FindInGame,
    AssignAuthor,
    UnassignAuthor,
    ReturnToGame,
    MoveToFolder,
    MoveToStorage,
    MoveToArchive,
}

/// <summary>Where an action is offered besides its key</summary>
[Flags]
internal enum GalleryActionPlaces
{
    None = 0,
    /// <summary>The cards' context menu</summary>
    Menu = 1,
    /// <summary>The bar over the gallery when several are selected</summary>
    Bar = 2,
    /// <summary>The properties panel's buttons</summary>
    Panel = 4,
}

/// <summary>
/// One action on the selected liveries, as every place shows it: its words (the menu's, with the count; a short one for
/// the bar and the panel), its icon, its key, whether it is offered now and, when it is offered but cannot be done, why
/// </summary>
internal sealed partial class GalleryAction : ObservableObject
{
    private readonly Action _run;

    internal GalleryAction(GalleryActionId id, GalleryActionPlaces places, int group, string iconKey, double iconStroke, Action run)
    {
        Id = id;
        Places = places;
        Group = group;
        _iconKey = iconKey;
        IconStroke = iconStroke;
        _run = run;
        Command = new RelayCommand(Run, () => IsEnabled);
    }

    public GalleryActionId Id { get; }

    public GalleryActionPlaces Places { get; }

    /// <summary>The menu's section: a line between two sections</summary>
    public int Group { get; }

    /// <summary>The key that does it (in the game or in the storage, as shown now); null when none</summary>
    [ObservableProperty]
    private GalleryShortcut? _key;

    /// <summary>The menu's words: "Add to favourites (5)"</summary>
    [ObservableProperty]
    private string _label = "";

    /// <summary>The bar's and the panel's words: "Favourite"</summary>
    [ObservableProperty]
    private string _shortLabel = "";

    /// <summary>The key of its 24-grid icon among the app's resources</summary>
    [ObservableProperty]
    private string _iconKey;

    /// <summary>A line icon's thickness; 0 for a filled shape</summary>
    public double IconStroke { get; }

    /// <summary>The star and the lock: every selected livery has it (the star filled, the lock closed)</summary>
    [ObservableProperty]
    private bool _isOn;

    /// <summary>Offered for this selection, here</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>Why it cannot be done now; null when it can</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnabled))]
    [NotifyPropertyChangedFor(nameof(Tooltip))]
    private string? _blockedReason;

    public bool IsEnabled => BlockedReason is null;

    /// <summary>"To the game": in the storage's colour, as on a stored card</summary>
    public bool IsToGame => Id == GalleryActionId.ReturnToGame;

    /// <summary>Why not, when it cannot be done; else its words and its key</summary>
    public string Tooltip => BlockedReason
        ?? (Key is { } key ? $"{Label}  ({GalleryShortcuts.GestureText(key)})" : Label);

    partial void OnBlockedReasonChanged(string? value) => Command.NotifyCanExecuteChanged();

    partial void OnLabelChanged(string value) => OnPropertyChanged(nameof(Tooltip));

    partial void OnKeyChanged(GalleryShortcut? value) => OnPropertyChanged(nameof(Tooltip));

    public IRelayCommand Command { get; }

    private void Run()
    {
        if (IsVisible && IsEnabled) _run();
    }
}

/// <summary>
/// The gallery's actions on the selected liveries, in one place (docs/plans.md, «Упрощение»). The cards' menu, the bar
/// over the gallery, the properties panel and the keys show and run them from here, so each has the same words, the
/// same checks and the same reason when it cannot be done. Every one of them works on the selection: the menu selects
/// its card first. A card's own buttons are that card's alone, and are not here
/// </summary>
internal sealed class GalleryActions
{
    private readonly MainViewModel _main;
    private readonly Dictionary<GalleryActionId, GalleryAction> _byId = [];
    private bool _refreshing;

    public GalleryActions(MainViewModel main)
    {
        _main = main;
        var gallery = main.Gallery;
        Add(GalleryActionId.OpenDetails, GalleryActionPlaces.Menu | GalleryActionPlaces.Panel, 0, "IconPreview", 0,
            () => gallery.OpenDetailsCommand.Execute(Single));
        Add(GalleryActionId.View3D, GalleryActionPlaces.Panel, 0, "Icon3D", 0,
            () => { if (Single is { } entry) gallery.View3DCommand.Execute(entry); });
        Add(GalleryActionId.View3DNewWindow, GalleryActionPlaces.Menu, 0, "Icon3D", 0,
            () => gallery.View3DNewWindowCommand.Execute(null));
        Add(GalleryActionId.Favorite, GalleryActionPlaces.Menu | GalleryActionPlaces.Bar, 1, "IconStar", 2,
            () => gallery.ToggleFavoritesCommand.Execute(null));
        Add(GalleryActionId.Lock, GalleryActionPlaces.Menu | GalleryActionPlaces.Bar, 1, "IconLock", 2,
            () => gallery.ToggleLocksCommand.Execute(null));
        Add(GalleryActionId.Tags, GalleryActionPlaces.Menu | GalleryActionPlaces.Bar, 1, "IconBookmark", 0,
            () => gallery.EditTagsCommand.Execute(null));
        Add(GalleryActionId.FindInGame, GalleryActionPlaces.Menu | GalleryActionPlaces.Panel, 3, "IconLocate", 0,
            () => main.NavigateInGameCommand.Execute(Single));
        Add(GalleryActionId.AssignAuthor, GalleryActionPlaces.Menu, 4, "IconUser", 0,
            () => gallery.AssignAuthorCommand.Execute(null));
        Add(GalleryActionId.UnassignAuthor, GalleryActionPlaces.Menu, 4, "", 0,
            () => gallery.UnassignAuthorCommand.Execute(null));
        Add(GalleryActionId.ReturnToGame, GalleryActionPlaces.Menu | GalleryActionPlaces.Bar | GalleryActionPlaces.Panel, 5, "IconToGame", 2,
            () => main.ReturnSelectedToGameCommand.Execute(null));
        // Its folders are listed when the menu opens (MainWindow.StorageFolders.cs)
        Add(GalleryActionId.MoveToFolder, GalleryActionPlaces.Menu, 5, "IconFolder", 0, () => { });
        Add(GalleryActionId.MoveToStorage, GalleryActionPlaces.Menu | GalleryActionPlaces.Bar | GalleryActionPlaces.Panel, 5, "IconStorage", 2,
            () => main.MoveSelectedToStorageCommand.Execute(null));
        Add(GalleryActionId.MoveToArchive, GalleryActionPlaces.Menu | GalleryActionPlaces.Panel, 5, "IconTrash", 2,
            () => main.MoveSelectedToArchiveCommand.Execute(null));

        All = [.. _byId.Values];
        Bar = [.. All.Where(a => a.Places.HasFlag(GalleryActionPlaces.Bar))];
        Panel = [.. All.Where(a => a.Places.HasFlag(GalleryActionPlaces.Panel))];

        gallery.SelectedEntries.CollectionChanged += Changed;
        gallery.FlagsChanged += (_, _) => Refresh();
        gallery.SelectionStateChanged += Refresh;
        main.PropertyChanged += Main_PropertyChanged;
        main.MoveSelectedToStorageCommand.CanExecuteChanged += (_, _) => Refresh();
        main.MoveSelectedToArchiveCommand.CanExecuteChanged += (_, _) => Refresh();
        main.ReturnSelectedToGameCommand.CanExecuteChanged += (_, _) => Refresh();
        main.NavigateInGameCommand.CanExecuteChanged += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Every action, in the menu's order</summary>
    public IReadOnlyList<GalleryAction> All { get; }

    /// <summary>The bar's buttons</summary>
    public IReadOnlyList<GalleryAction> Bar { get; }

    /// <summary>The properties panel's buttons</summary>
    public IReadOnlyList<GalleryAction> Panel { get; }

    public GalleryAction this[GalleryActionId id] => _byId[id];

    private GalleryViewModel Gallery => _main.Gallery;

    private LiveryEntry? Single => Gallery.SelectedCount == 1 ? Gallery.SelectedEntries[0] : null;

    private void Add(GalleryActionId id, GalleryActionPlaces places, int group, string icon, double stroke, Action run) =>
        _byId[id] = new GalleryAction(id, places, group, icon, stroke, run);

    private void Changed(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    private void Main_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsStorageMode) or nameof(MainViewModel.StorageBlockedTooltip)
            or nameof(MainViewModel.ArchiveBlockedTooltip) or nameof(MainViewModel.IsSaveReadOnly))
            Refresh();
    }

    /// <summary>The key's action: the keys of the selection (F, L, T, Del, Ins, Enter, 3, G…); null for any other</summary>
    public static GalleryActionId? OfKey(GalleryShortcut key) => key switch
    {
        GalleryShortcut.OpenDetails => GalleryActionId.OpenDetails,
        GalleryShortcut.View3D => GalleryActionId.View3D,
        GalleryShortcut.View3DNewWindow => GalleryActionId.View3DNewWindow,
        GalleryShortcut.Favorite => GalleryActionId.Favorite,
        GalleryShortcut.Lock => GalleryActionId.Lock,
        GalleryShortcut.Tags => GalleryActionId.Tags,
        GalleryShortcut.FindInGame => GalleryActionId.FindInGame,
        GalleryShortcut.ReturnToGame => GalleryActionId.ReturnToGame,
        GalleryShortcut.MoveToStorage => GalleryActionId.MoveToStorage,
        GalleryShortcut.MoveToArchive or GalleryShortcut.StoredToArchive => GalleryActionId.MoveToArchive,
        _ => null,
    };

    /// <summary>
    /// The key of a selection's action: done when it can be; when it cannot, the reason in a short message (and the key
    /// is taken: nothing else happens). False when the key is not one of them, nothing is selected or the action is not
    /// offered for this selection (Enter with several): the key goes on to the control in focus
    /// </summary>
    public bool RunKey(GalleryShortcut key)
    {
        if (OfKey(key) is not { } id || Gallery.SelectedCount == 0) return false;
        Refresh();
        var action = this[id];
        if (!action.IsVisible) return false;
        if (action.BlockedReason is { Length: > 0 } why) _main.Notices.ShowToast(why);
        else if (action.BlockedReason is null) action.Command.Execute(null);
        return true;
    }

    /// <summary>Each action's words, icon and state for the selection as it is now</summary>
    public void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            RefreshAll();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshAll()
    {
        var selected = Gallery.SelectedEntries;
        int count = selected.Count;
        var single = Single;
        bool any = count > 0;
        bool storage = _main.IsStorageMode;
        string WithCount(string text, int n) => n > 1 ? $"{text} ({n.ToString(CultureInfo.CurrentCulture)})" : text;

        Set(GalleryActionId.OpenDetails, single is not null, null, Strings.ContextMenuOpenDetails, Strings.PropertiesDetails);
        Set(GalleryActionId.View3D, single is not null, single is { CanView3D: false } ? single.View3DTooltip : null, "3D", "3D");
        Set(GalleryActionId.View3DNewWindow, any,
            any && !selected.Any(e => e.CanView3D) ? selected[0].View3DTooltip : null,
            Gallery.View3DNewWindowMenuHeader, "3D");

        bool allFavorite = any && selected.All(e => e.IsFavorite);
        Set(GalleryActionId.Favorite, any, null,
            WithCount(allFavorite ? Strings.ContextMenuRemoveFromFavorites : Strings.ContextMenuAddToFavorites, count), Strings.PropertiesFavorite);
        this[GalleryActionId.Favorite].IsOn = allFavorite;

        bool allLocked = any && selected.All(e => e.IsLocked);
        var lockAction = this[GalleryActionId.Lock];
        Set(GalleryActionId.Lock, any && !storage, null,
            WithCount(allLocked ? Strings.ContextMenuUnlock : Strings.ContextMenuLock, count), Strings.PropertiesLock);
        lockAction.IsOn = allLocked;
        lockAction.IconKey = allLocked ? "IconLockOpen" : "IconLock";

        Set(GalleryActionId.Tags, any, null, WithCount(Strings.ContextMenuEditTags, count), Strings.SelectionBarTags);

        // One livery of the game: where it is in the game's list. With several the place would be anyone's
        bool findable = !storage && selected.Any(e => e.ShowFindInGame);
        Set(GalleryActionId.FindInGame, findable,
            count > 1 ? Strings.NavigateInGameMultipleSelectedTooltip
            : single is null ? null
            : !single.CanNavigateInGame ? single.NavigateInGameTooltip
            : !_main.NavigateInGameCommand.CanExecute(single) ? ""
            : null,
            Strings.ContextMenuNavigateInGame, Strings.PropertiesFindInGame);

        int assignable = Gallery.AssignableCount;
        Set(GalleryActionId.AssignAuthor, assignable > 0, null, WithCount(Strings.ContextMenuAssignAuthor, assignable), Strings.ContextMenuAssignAuthor);
        int assigned = Gallery.AssignedCount;
        Set(GalleryActionId.UnassignAuthor, assigned > 0, null, WithCount(Strings.ContextMenuUnassignAuthor, assigned), Strings.ContextMenuUnassignAuthor);

        Set(GalleryActionId.ReturnToGame, any && storage,
            _main.ReturnSelectedToGameCommand.CanExecute(null) ? null : _main.ReadOnlySaveText ?? "",
            Strings.ContextMenuReturnToGame, Strings.PropertiesToGame);
        Set(GalleryActionId.MoveToFolder, any && storage, null, Strings.ContextMenuMoveToFolder, Strings.ContextMenuMoveToFolder);
        Set(GalleryActionId.MoveToStorage, any && !storage,
            _main.MoveSelectedToStorageCommand.CanExecute(null) ? null : _main.StorageBlockedTooltip ?? "",
            Strings.ContextMenuMoveToStorage, Strings.PropertiesToStorage);
        Set(GalleryActionId.MoveToArchive, any,
            _main.MoveSelectedToArchiveCommand.CanExecute(null) ? null : _main.ArchiveBlockedTooltip ?? _main.ReadOnlySaveText ?? "",
            Strings.ContextMenuMoveToArchive, Strings.ContextMenuMoveToArchive);

        // The keys follow the place shown: Del is "to the storage" in the game and "to the archive" in the storage
        this[GalleryActionId.OpenDetails].Key = GalleryShortcut.OpenDetails;
        this[GalleryActionId.View3D].Key = GalleryShortcut.View3D;
        this[GalleryActionId.View3DNewWindow].Key = GalleryShortcut.View3DNewWindow;
        this[GalleryActionId.Favorite].Key = GalleryShortcut.Favorite;
        this[GalleryActionId.Lock].Key = GalleryShortcut.Lock;
        this[GalleryActionId.Tags].Key = GalleryShortcut.Tags;
        this[GalleryActionId.FindInGame].Key = GalleryShortcut.FindInGame;
        this[GalleryActionId.ReturnToGame].Key = GalleryShortcut.ReturnToGame;
        this[GalleryActionId.MoveToStorage].Key = GalleryShortcut.MoveToStorage;
        this[GalleryActionId.MoveToArchive].Key = storage ? GalleryShortcut.StoredToArchive : GalleryShortcut.MoveToArchive;
    }

    private void Set(GalleryActionId id, bool visible, string? blocked, string label, string shortLabel)
    {
        var action = this[id];
        action.IsVisible = visible;
        action.Label = label;
        action.ShortLabel = shortLabel;
        // An empty reason is still a reason: the action is greyed out, with no tooltip of its own
        action.BlockedReason = visible ? blocked : null;
    }
}
