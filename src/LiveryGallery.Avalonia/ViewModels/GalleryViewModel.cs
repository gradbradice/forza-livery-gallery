using ForzaToolkit.Core.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using System.Collections.ObjectModel;

namespace LiveryGallery.ViewModels;

internal sealed partial class GalleryViewModel : ObservableObject, ISelectionChange<LiveryEntry>
{
    private readonly FavoriteService favoriteService;
    private readonly LockService lockService;
    private List<LiveryEntry> _allEntries = [];
    public IReadOnlyList<LiveryEntry> AllEntries => _allEntries;
    public ObservableCollection<LiveryGroup> DisplayedGroups { get; } = [];
    private readonly HashSet<string> _selectedTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _excludedTags = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Tags that hide a livery</summary>
    public IReadOnlySet<string> ExcludedTags => _excludedTags;
    public bool HasTagFilter => _selectedTags.Count > 0 || _excludedTags.Count > 0;
    private readonly HashSet<string> _selectedFolderNames = new(StringComparer.Ordinal);
    public SelectedLiveries SelectedEntries { get; } = [];
    public bool HasSelection => SelectedEntries.Count > 0;
    public int SelectedCount => SelectedEntries.Count;
    public string SelectionSummaryText => string.Format(Strings.SelectedCountLabel, SelectedCount);
    private GalleryFilterState _filter;

    private double _groupWidth = 1200;
    public double GroupWidth
    {
        get => _groupWidth;
        set
        {
            if (_groupWidth == value) return;
            _groupWidth = value;
            ApplyGroupWidthToDisplayedGroups();
        }
    }

    private CardLayout _cardLayout = CardLayout.For(1200, CardSize.Normal);

    /// <summary>
    /// The cards' layout (<see cref="CardLayout"/>): the window sets it from the list's width and the cards' size. The
    /// groups take its width and step; the cards bind <see cref="CardWidth"/> and <see cref="CardPictureHeight"/>
    /// </summary>
    /// <returns>True when the number of columns changed: the rows are to be built again</returns>
    public bool SetCardLayout(CardLayout layout)
    {
        if (_cardLayout == layout && _groupWidth == layout.RowWidth) return false;
        bool columnsChanged = layout.Columns != _cardLayout.Columns;
        bool widthChanged = layout.CardWidth != _cardLayout.CardWidth;
        _cardLayout = layout;
        _groupWidth = layout.RowWidth;
        ApplyGroupWidthToDisplayedGroups();
        if (widthChanged)
        {
            OnPropertyChanged(nameof(CardWidth));
            OnPropertyChanged(nameof(CardPictureHeight));
        }
        return columnsChanged;
    }

    /// <summary>The cards' layout now</summary>
    public CardLayout CardLayout => _cardLayout;

    /// <summary>A card's width (without its margins)</summary>
    public double CardWidth => _cardLayout.CardWidth;

    /// <summary>A card's picture's height: the game preview's proportions at <see cref="CardWidth"/></summary>
    public double CardPictureHeight => _cardLayout.PictureHeight;

    private CardSize _cardSize;

    /// <summary>How big the cards are (the setting); the window lays them out again when it changes</summary>
    public CardSize CardSize
    {
        get => _cardSize;
        set
        {
            if (_cardSize == value) return;
            _cardSize = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSmallCards));
        }
    }

    /// <summary>The small cards: their buttons only under the pointer, no date or tags</summary>
    public bool IsSmallCards => _cardSize == CardSize.Small;

    public event Action? GroupsReplaced;
    public event Action<LiveryEntry>? AuthorRowRequested;
    /// <summary>The liveries whose tags to edit: one, or every selected one</summary>
    public event Action<IReadOnlyList<LiveryEntry>>? EditTagsRequested;
    public event Action<LiveryEntry>? ViewPreviewRequested;
    public event Action<LiveryEntry>? View3DRequested;
    /// <summary>One more 3D window, to compare (Shift+3, the context menu)</summary>
    public event Action<LiveryEntry>? View3DNewWindowRequested;

    /// <summary>The livery window: a double click on a card, Enter, the context menu, the note's mark</summary>
    public event Action<LiveryEntry>? OpenDetailsRequested;

    [RelayCommand]
    private void OpenDetails(LiveryEntry? entry)
    {
        var target = entry ?? (SelectedEntries.Count == 1 ? SelectedEntries[0] : null);
        if (target is not null) OpenDetailsRequested?.Invoke(target);
    }
    public event Action<GalleryCountsSnapshot>? CountsUpdated;
    public event Action? SelectedTagsChanged;
    /// <summary>A livery was locked or unlocked: what may go to the archive changed</summary>
    public event Action? LocksChanged;

    public GalleryViewModel(FavoriteService favoriteService, LockService lockService)
    {
        this.favoriteService = favoriteService;
        this.lockService = lockService;
        SelectedEntries.CollectionChanged += (_, __) =>
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectionSummaryText));
            OnSelectionMenuChanged();
        };
    }

    /// <summary>
    /// What an action on the selection works on (the menu, the keys, the bar, the panel: <see cref="GalleryActions"/>):
    /// the selection. A livery that is not selected, given by a caller of its own, alone. A card's own buttons are
    /// that card's: they have commands of their own (the star, the lock, the tags of one card)
    /// </summary>
    public IReadOnlyList<LiveryEntry> TargetsOf(LiveryEntry? entry) =>
        entry is null || (entry.IsSelected && SelectedEntries.Count > 1) ? [.. SelectedEntries] : [entry];

    /// <summary>
    /// The selected liveries' stars, locks or authors changed, or the selection did: the actions' words follow
    /// (<see cref="GalleryActions"/>)
    /// </summary>
    public event Action? SelectionStateChanged;

    private void OnSelectionMenuChanged() => SelectionStateChanged?.Invoke();

    /// <summary>
    /// A part of the liveries the grid shows before any filter (the storage's folder); all when null. The caller shows
    /// the gallery again after setting it
    /// </summary>
    public Func<LiveryEntry, bool>? Scope { get; set; }

    public List<LiveryEntry> GetFilteredEntries() =>
        GalleryFilterService.Apply(
            Scope is { } scope ? [.. _allEntries.Where(scope)] : _allEntries, _filter.SearchText, _selectedTags, _excludedTags,
            _filter.FavoriteMode == FavoriteMode.OnlyFavorites, _filter.MineMode == MineMode.OnlyMine,
            _filter.DuplicatesFilterMode, _filter.GeneratedFilterMode, _filter.PaintFilterMode,
            _filter.SearchByFolderName, _filter.QuickFilters,
            auctionFilterMode: _filter.AuctionFilterMode,
            onlyInstalled: _filter.InstalledMode == InstalledMode.OnlyInstalled,
            onlyNotInstalled: _filter.InstalledMode == InstalledMode.OnlyNotInstalled)
        .Where(e => MatchesCopies(e, _filter.CopiesFilterMode)).ToList();

    /// <summary>The filter of copies: the mark of the other place (<see cref="LiveryEntry.CopyKind"/>) the livery has</summary>
    internal static bool MatchesCopies(LiveryEntry entry, CopiesFilterMode mode) => mode switch
    {
        CopiesFilterMode.Same => entry.CopyKind == CopyKind.Same,
        CopiesFilterMode.SameOrSimilar => entry.CopyKind != CopyKind.None,
        CopiesFilterMode.Similar => entry.CopyKind == CopyKind.Similar,
        CopiesFilterMode.None => entry.CopyKind == CopyKind.None,
        _ => true,
    };

    /// <summary>The star on a card: that livery alone</summary>
    [RelayCommand]
    private void ToggleFavorite(LiveryEntry entry) => SetFavorites([entry], !entry.IsFavorite);

    /// <summary>
    /// The context menu: every selected livery becomes a favorite, or, when all of them are already, none of them is
    /// </summary>
    [RelayCommand]
    private void ToggleFavorites(LiveryEntry? entry)
    {
        var targets = TargetsOf(entry);
        if (targets.Count == 0) return;
        SetFavorites(targets, targets.Any(e => !e.IsFavorite));
    }

    /// <summary>
    /// Stars or locks were changed by the user: which liveries (their keys) and what each had before, so the change
    /// can be undone (<see cref="RestoreFlags"/>). True: locks
    /// </summary>
    public event Action<bool, IReadOnlyList<(string Key, bool Was)>>? FlagsChanged;

    /// <summary>
    /// Puts stars or locks back as they were (undo). The liveries are found by their keys among the ones shown and
    /// <paramref name="others"/> (the game's, while the storage is shown); one that is nowhere now still gets its value
    /// kept, for when it is back
    /// </summary>
    public void RestoreFlags(bool locks, IReadOnlyList<(string Key, bool Value)> values, IEnumerable<LiveryEntry> others)
    {
        var byKey = new Dictionary<string, List<LiveryEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _allEntries.Concat(others))
        {
            if (!byKey.TryGetValue(entry.FolderName, out var same)) byKey[entry.FolderName] = same = [];
            if (!same.Contains(entry)) same.Add(entry);
        }
        foreach (var (key, value) in values)
        {
            if (locks) lockService.SetLocked(key, value);
            else favoriteService.SetFavorite(key, value);
            foreach (var entry in byKey.GetValueOrDefault(key) ?? [])
            {
                if (locks) entry.IsLocked = value;
                else entry.IsFavorite = value;
            }
        }
        OnSelectionMenuChanged();
        if (locks) LocksChanged?.Invoke();
        else if (_filter.FavoriteMode != FavoriteMode.None) Refresh(_filter);
        else RefreshCountsOnly();
    }

    private void SetFavorites(IReadOnlyList<LiveryEntry> entries, bool favorite)
    {
        var was = new List<(string, bool)>();
        foreach (var entry in entries)
        {
            if (entry.IsFavorite == favorite) continue;
            was.Add((entry.FolderName, entry.IsFavorite));
            entry.IsFavorite = favorite;
            favoriteService.SetFavorite(entry.FolderName, favorite);
        }
        bool changed = was.Count > 0;
        if (!changed) return;
        FlagsChanged?.Invoke(false, was);
        OnSelectionMenuChanged();

        if (_filter.FavoriteMode != FavoriteMode.None)
            Refresh(_filter);
        else
            RefreshCountsOnly();
    }

    /// <summary>The lock on a card: that livery alone</summary>
    [RelayCommand]
    private void ToggleLock(LiveryEntry entry) => SetLocks([entry], !entry.IsLocked);

    /// <summary>The context menu: every selected livery gets locked, or, when all of them already are, unlocked</summary>
    [RelayCommand]
    private void ToggleLocks(LiveryEntry? entry)
    {
        var targets = TargetsOf(entry);
        if (targets.Count == 0) return;
        SetLocks(targets, targets.Any(e => !e.IsLocked));
    }

    private void SetLocks(IReadOnlyList<LiveryEntry> entries, bool locked)
    {
        var was = new List<(string, bool)>();
        foreach (var entry in entries)
        {
            if (entry.IsLocked == locked) continue;
            was.Add((entry.FolderName, entry.IsLocked));
            entry.IsLocked = locked;
            lockService.SetLocked(entry.FolderName, locked);
        }
        if (was.Count == 0) return;
        FlagsChanged?.Invoke(true, was);
        OnSelectionMenuChanged();
        LocksChanged?.Invoke();
    }

    [RelayCommand]
    private void ShowAuthorRow(LiveryEntry entry) => AuthorRowRequested?.Invoke(entry);

    /// <summary>The menu, T, the bar: the tags of every selected livery</summary>
    [RelayCommand]
    private void EditTags(LiveryEntry? entry)
    {
        var targets = TargetsOf(entry);
        if (targets.Count > 0) EditTagsRequested?.Invoke(targets);
    }

    /// <summary>The card's tag button: that card's tags, the card alone selected (as a click on it selects it)</summary>
    [RelayCommand]
    private void EditCardTags(LiveryEntry entry)
    {
        SelectOnly(entry);
        EditTagsRequested?.Invoke([entry]);
    }

    /// <summary>The liveries whose author the user may say: each selected one, or the card alone (never empty)</summary>
    public event Action<IReadOnlyList<LiveryEntry>>? AssignAuthorRequested;

    /// <summary>The liveries whose author the user said, to forget it (never empty)</summary>
    public event Action<IReadOnlyList<LiveryEntry>>? UnassignAuthorRequested;

    private static bool CanAssign(LiveryEntry entry) => entry.CanAssignAuthor && entry.CLiveryHash is not null;

    /// <summary>
    /// The context menu or the card: every selected livery whose own id does not tell the author (one author for all of
    /// them), or the card alone. Liveries with an account's id are left out
    /// </summary>
    [RelayCommand]
    private void AssignAuthor(LiveryEntry? entry)
    {
        var targets = TargetsOf(entry).Where(CanAssign).ToList();
        if (targets.Count > 0) AssignAuthorRequested?.Invoke(targets);
    }

    [RelayCommand]
    private void UnassignAuthor(LiveryEntry? entry)
    {
        var targets = TargetsOf(entry).Where(e => e.IsAuthorAssigned).ToList();
        if (targets.Count > 0) UnassignAuthorRequested?.Invoke(targets);
    }

    /// <summary>The selected liveries whose author the user may say</summary>
    public int AssignableCount => SelectedEntries.Count(CanAssign);

    /// <summary>The selected liveries with an author the user said</summary>
    public int AssignedCount => SelectedEntries.Count(e => e.IsAuthorAssigned);

    /// <summary>The menu's author items after the user changed whose liveries they are</summary>
    public void RefreshAuthorMenu() => OnSelectionMenuChanged();

    public void RefreshDuplicateTooltips()
    {
        foreach (var entry in AllEntries) entry.RefreshDuplicateTooltip();
    }

    public void RefreshPossibleDuplicateThreshold()
    {
        foreach (var entry in AllEntries) entry.RefreshPossibleDuplicateThreshold();
    }

    [RelayCommand]
    private void ViewPreview(LiveryEntry entry) => ViewPreviewRequested?.Invoke(entry);

    [RelayCommand]
    private void View3D(LiveryEntry entry)
    {
        if (entry.CanView3D) View3DRequested?.Invoke(entry);
    }

    /// <summary>
    /// One more 3D window for the card, or, when it is one of several selected, a window for each selected livery that
    /// can be seen in 3D (<see cref="View3DSeveralRequested"/>)
    /// </summary>
    [RelayCommand]
    private void View3DNewWindow(LiveryEntry entry)
    {
        var targets = TargetsOf(entry).Where(e => e.CanView3D).ToList();
        if (targets.Count > 1) View3DSeveralRequested?.Invoke(targets);
        else if (targets.Count == 1) View3DNewWindowRequested?.Invoke(targets[0]);
    }

    /// <summary>Several liveries, each in a 3D window of its own (Shift+3 or the context menu on a selection)</summary>
    public event Action<IReadOnlyList<LiveryEntry>>? View3DSeveralRequested;

    /// <summary>The context menu's «one more 3D window»: with several selected, how many open each in its own</summary>
    public string View3DNewWindowMenuHeader => SelectedEntries.Count(e => e.CanView3D) is var n and > 1
        ? string.Format(AppLocalisationService.Culture, Strings.ContextMenuView3DSeveralFormat, n)
        : Strings.ContextMenuView3DNewWindow;

    public void RefreshView3DAvailability()
    {
        foreach (var entry in _allEntries) entry.RefreshView3D();
    }

    public TagFilterState TagState(string tag) =>
        _selectedTags.Contains(tag) ? TagFilterState.Show
        : _excludedTags.Contains(tag) ? TagFilterState.Hide
        : TagFilterState.Off;

    /// <summary>Shows only the tag's liveries, hides them, or stops filtering by the tag</summary>
    public void SetTagState(string tag, TagFilterState state)
    {
        bool changed = state == TagFilterState.Show ? _selectedTags.Add(tag) : _selectedTags.Remove(tag);
        changed |= state == TagFilterState.Hide ? _excludedTags.Add(tag) : _excludedTags.Remove(tag);
        if (!changed) return;
        SelectedTagsChanged?.Invoke();
        Refresh(_filter);
    }

    public void SetTagSelected(string tag, bool selected) => SetTagState(tag, selected ? TagFilterState.Show : TagFilterState.Off);

    /// <summary>No tag shows or hides anything</summary>
    public void ClearTagFilter()
    {
        if (!HasTagFilter) return;
        _selectedTags.Clear();
        _excludedTags.Clear();
        SelectedTagsChanged?.Invoke();
        Refresh(_filter);
    }

    /// <summary>A tag on a card: shows only its liveries, or stops</summary>
    [RelayCommand]
    private void ToggleTag(string tag) => SetTagSelected(tag, TagState(tag) != TagFilterState.Show);

    /// <summary>
    /// Stops filtering by tags no livery has any more (the last livery with one lost it): the row has no chip left to
    /// turn them off. When one was dropped the gallery is shown again, and true is returned
    /// </summary>
    public bool SyncKnownTags(IReadOnlyCollection<string> knownTags)
    {
        bool dropped = _selectedTags.RemoveWhere(t => !knownTags.Contains(t)) > 0;
        dropped |= _excludedTags.RemoveWhere(t => !knownTags.Contains(t)) > 0;
        if (!dropped) return false;
        SelectedTagsChanged?.Invoke();
        Refresh(_filter);
        return true;
    }

    public void Refresh(GalleryFilterState filter)
    {
        _filter = filter;
        foreach (var entry in _allEntries)
            entry.ShowMineBadge = entry.IsMine;

        var filtered = GetFilteredEntries();
        var groups = GalleryGroupingService.Group(
            filtered, filter.SortMode, filter.FavoriteMode, filter.MineMode, filter.AuctionFilterMode,
            filter.GroupingEnabled, GroupWidth, filter.InstalledMode, _cardLayout.CardStep);
        ReplaceGroups(groups);
        CountsUpdated?.Invoke(new GalleryCountsSnapshot(filtered, ScopedCount, filter.SearchText));
    }

    private void ApplyGroupWidthToDisplayedGroups()
    {
        foreach (var group in DisplayedGroups)
        {
            group.CardStep = _cardLayout.CardStep;
            group.GroupWidth = GroupWidth;
        }
    }

    public void RefreshCountsOnly() =>
        CountsUpdated?.Invoke(new GalleryCountsSnapshot(GetFilteredEntries(), ScopedCount, _filter.SearchText));

    // "Shown: 12 of 40": of the scope (the storage's folder), not of every livery
    private int ScopedCount => Scope is { } scope ? _allEntries.Count(scope) : _allEntries.Count;

    public void ReplaceGroups(List<LiveryGroup> newGroups)
    {
        bool samePositionalOrder = DisplayedGroups.Count == newGroups.Count;
        if (samePositionalOrder)
        {
            for (int i = 0; i < newGroups.Count; i++)
            {
                if (DisplayedGroups[i].Key != newGroups[i].Key)
                {
                    samePositionalOrder = false;
                    break;
                }
            }
        }

        if (!samePositionalOrder)
        {
            foreach (var oldGroup in DisplayedGroups)
                oldGroup.Dispose();
            DisplayedGroups.Clear();
            foreach (var newGroup in newGroups)
                DisplayedGroups.Add(newGroup);

            GroupsReplaced?.Invoke();
            return;
        }

        for (int i = 0; i < newGroups.Count; i++)
        {
            var oldGroup = DisplayedGroups[i];
            var newGroup = newGroups[i];

            if (AreGroupsEquivalent(oldGroup, newGroup))
            {
                newGroup.Dispose();
                continue;
            }

            oldGroup.Dispose();
            DisplayedGroups[i] = newGroup;
        }

        GroupsReplaced?.Invoke();
    }

    private static bool AreGroupsEquivalent(LiveryGroup oldGroup, LiveryGroup newGroup)
    {
        if (oldGroup.Items.Count != newGroup.Items.Count) return false;
        for (int i = 0; i < oldGroup.Items.Count; i++)
        {
            if (!HasEquivalentPresentationData(oldGroup.Items[i], newGroup.Items[i])) return false;
        }
        return true;
    }

    /// <summary>What the gallery shows of one mode of the main window: the selection, the tags that filter, the cursor</summary>
    public GalleryViewState CaptureView() =>
        new([.. _selectedFolderNames], [.. _selectedTags], [.. _excludedTags], _cursorFolderName, _rules.Anchor?.FolderName);

    /// <summary>
    /// Shows another mode's liveries (the game's, the storage's) with what was selected and filtered by tags there
    /// (<paramref name="view"/>; nothing when null). The caller shows the groups again
    /// </summary>
    public void SwapEntries(List<LiveryEntry> entries, GalleryViewState? view)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in SelectedEntries) entry.IsSelected = false;
        SelectedEntries.Clear();
        _selectedFolderNames.Clear();
        ShowCursorOn(null);
        _selectedTags.Clear();
        _excludedTags.Clear();
        _cursorFolderName = view?.Cursor;
        _rules.Anchor = view?.Anchor is { } anchor ? entries.FirstOrDefault(e => string.Equals(e.FolderName, anchor, StringComparison.Ordinal)) : null;
        _cursorRow = -1;
        if (view is not null)
        {
            _selectedTags.UnionWith(view.ShownTags);
            _excludedTags.UnionWith(view.HiddenTags);
            _selectedFolderNames.UnionWith(view.Selected);
        }
        _allEntries = entries;
        ApplyInstalledCounts();
        ReconcileSelection();
        SelectedTagsChanged?.Invoke();
    }

    public void ReplaceEntries(List<LiveryEntry> entries)
    {
        _allEntries = entries;
        ApplyInstalledCounts();
        ReconcileSelection();
    }

    private IReadOnlyDictionary<string, int>? _installedCounts;

    public bool IsInstalledDataKnown => _installedCounts is not null;

    public void SetInstalledLiveries(IReadOnlyDictionary<string, int>? installedCounts)
    {
        _installedCounts = installedCounts is null
            ? null
            : new Dictionary<string, int>(installedCounts, StringComparer.OrdinalIgnoreCase);
        ApplyInstalledCounts();
    }

    public event Action? InstalledCountsChanged;

    private void ApplyInstalledCounts()
    {
        foreach (var entry in _allEntries)
            entry.InstalledCarCount = !entry.IsStored && _installedCounts is not null && _installedCounts.TryGetValue(entry.FolderName, out int n) ? n : 0;
        InstalledCountsChanged?.Invoke();
    }

    private void ReconcileSelection()
    {
        if (_selectedFolderNames.Count == 0) return;

        var byName = _allEntries.ToDictionary(e => e.FolderName);
        _selectedFolderNames.RemoveWhere(name => !byName.ContainsKey(name));

        var selected = new List<LiveryEntry>(_selectedFolderNames.Count);
        foreach (var name in _selectedFolderNames)
        {
            var entry = byName[name];
            entry.IsSelected = true;
            selected.Add(entry);
        }
        SelectedEntries.ReplaceAll(selected);
    }

    [RelayCommand]
    private void ToggleSelection(LiveryEntry entry)
    {
        if (_selectedFolderNames.Add(entry.FolderName))
        {
            entry.IsSelected = true;
            SelectedEntries.Add(entry);
            return;
        }

        _selectedFolderNames.Remove(entry.FolderName);
        entry.IsSelected = false;
        var existing = SelectedEntries.FirstOrDefault(e => e.FolderName == entry.FolderName);
        if (existing is not null) SelectedEntries.Remove(existing);
    }

    [RelayCommand]
    private void SelectOnly(LiveryEntry entry)
    {
        foreach (var e in SelectedEntries) e.IsSelected = false;
        _selectedFolderNames.Clear();
        SelectedEntries.Clear();

        entry.IsSelected = true;
        _selectedFolderNames.Add(entry.FolderName);
        SelectedEntries.Add(entry);
    }

    // The rules of clicks and keys, as in Explorer and as the screenshots' tiles have them; they keep the anchor (where a
    // Shift range starts: the last card clicked without Shift), kept when the selection is cleared. Cards are the same
    // livery by folder: a range from a card no longer shown is the clicked card alone
    private readonly ExplorerSelection<LiveryEntry> _rules = new(GallerySelection.SameLivery);

    void ISelectionChange<LiveryEntry>.Choose(IReadOnlyList<LiveryEntry> items, bool add) => SelectMany(items, add);

    void ISelectionChange<LiveryEntry>.Toggle(LiveryEntry item) => ToggleSelection(item);

    /// <summary>The liveries on screen, in their order: the groups as shown, a folded group's liveries left out</summary>
    public List<LiveryEntry> ShownEntries()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var shown = new List<LiveryEntry>();
        foreach (var group in DisplayedGroups)
        {
            if (!group.IsExpanded) continue;
            foreach (var entry in group.Items)
                if (seen.Add(entry.FolderName)) shown.Add(entry);
        }
        return shown;
    }

    /// <summary>
    /// A left click on a card, as in Explorer: alone it selects only this card; with Ctrl it adds the card to the
    /// selection or takes it out; with Shift it selects every card on screen from the last one clicked to this one, and
    /// with Ctrl+Shift adds them to the selection
    /// </summary>
    public void ClickSelect(LiveryEntry entry, bool ctrl, bool shift)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!shift)
        {
            // The keys go on from the card clicked; the cursor is shown again once they move it
            _cursorFolderName = entry.FolderName;
            _cursorRow = -1;
            ShowCursorOn(null);
        }
        _rules.Click(ShownEntries(), entry, ctrl, shift, this);
    }

    // The cursor of the keys: the card last clicked or moved to, and its row on screen (a livery in two groups is on
    // two rows); the card that shows it
    private string? _cursorFolderName;
    private int _cursorRow = -1;
    private LiveryEntry? _cursorShownOn;

    /// <summary>The first row on screen with <paramref name="entry"/>; null when it is not shown</summary>
    public GalleryRow? RowOf(LiveryEntry entry) =>
        ShownRows().FirstOrDefault(r => r.Items.Any(e => string.Equals(e.FolderName, entry.FolderName, StringComparison.Ordinal)));

    /// <summary>The rows on screen, top first: each unfolded group's</summary>
    public List<GalleryRow> ShownRows() => [.. DisplayedGroups.Where(g => g.IsExpanded).SelectMany(g => g.Rows)];

    /// <summary>
    /// An arrow, Home, End, Page Up or Page Down (<see cref="GalleryKeyboard"/>): moves the cursor and the selection as
    /// in Explorer. Returns the row moved to, for the window to scroll to it; null when there is no card
    /// </summary>
    /// <param name="pageRows">How many rows a page holds</param>
    /// <param name="firstInView">The first row in view: a cursor not on screen starts there</param>
    public GalleryRow? MoveCursor(GalleryMove move, SelectionMove selection, int pageRows, GalleryRow? firstInView)
    {
        var rows = ShownRows();
        var (row, column) = FindCursor(rows);
        int start = firstInView is null ? -1 : rows.IndexOf(firstInView);
        if (GalleryKeyboard.Target([.. rows.Select(r => r.Items.Count)], row, column, move, pageRows, start) is not { } target
            || rows[target.Row].Items.Count == 0)
            return null;
        var entry = rows[target.Row].Items[target.Column];
        var from = row >= 0 ? rows[row].Items[column] : null;
        _cursorFolderName = entry.FolderName;
        _cursorRow = target.Row;
        ShowCursorOn(entry);
        _rules.Move(ShownEntries(), entry, from, selection, this);
        return rows[target.Row];
    }

    /// <summary>
    /// Space: the card under the cursor goes into the selection or out of it (a cursor not on screen starts at the first
    /// row in view). Returns its row; null when there is no card
    /// </summary>
    public GalleryRow? ToggleCursor(GalleryRow? firstInView)
    {
        var rows = ShownRows();
        var (row, column) = FindCursor(rows);
        if (row < 0)
        {
            row = firstInView is null ? 0 : Math.Max(0, rows.IndexOf(firstInView));
            column = 0;
            if (row >= rows.Count || rows[row].Items.Count == 0) return null;
        }
        var entry = rows[row].Items[column];
        _cursorFolderName = entry.FolderName;
        _cursorRow = row;
        ShowCursorOn(entry);
        _rules.Toggle(entry, this);
        return rows[row];
    }

    // The cursor's place in the rows: its own row when it is still there, else the first with its card; -1 for none
    private (int Row, int Column) FindCursor(List<GalleryRow> rows)
    {
        if (_cursorFolderName is not { } name) return (-1, 0);
        int Column(int r)
        {
            var items = rows[r].Items;
            for (int c = 0; c < items.Count; c++)
                if (string.Equals(items[c].FolderName, name, StringComparison.Ordinal)) return c;
            return -1;
        }
        if (_cursorRow >= 0 && _cursorRow < rows.Count && Column(_cursorRow) is >= 0 and var own) return (_cursorRow, own);
        for (int r = 0; r < rows.Count; r++)
            if (Column(r) is >= 0 and var c) return (r, c);
        return (-1, 0);
    }

    private void ShowCursorOn(LiveryEntry? entry)
    {
        if (ReferenceEquals(_cursorShownOn, entry)) return;
        if (_cursorShownOn is not null) _cursorShownOn.IsCurrent = false;
        _cursorShownOn = entry;
        if (entry is not null) entry.IsCurrent = true;
        OnPropertyChanged(nameof(CursorEntry));
    }

    /// <summary>The card the keys' cursor is on (the grey ring); null when none is</summary>
    public LiveryEntry? CursorEntry => _cursorShownOn;

    /// <summary>
    /// The selection becomes <paramref name="entries"/> (with <paramref name="add"/>, gets them too), as one change of
    /// <see cref="SelectedEntries"/>: a card stays selected without going out and in again
    /// </summary>
    /// <summary>
    /// The selection while a rectangle is dragged over the cards: what was selected when it started (kept with Ctrl or
    /// Shift, empty without) and the cards it touches now. Nothing happens while the rectangle touches the same cards
    /// </summary>
    public void SelectBand(IReadOnlyCollection<LiveryEntry> before, IReadOnlyCollection<LiveryEntry> touched)
    {
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in before) wanted.Add(entry.FolderName);
        foreach (var entry in touched) wanted.Add(entry.FolderName);
        if (wanted.SetEquals(_selectedFolderNames)) return;
        SelectMany(before.Concat(touched), add: false);
    }

    private void SelectMany(IEnumerable<LiveryEntry> entries, bool add)
    {
        var names = add ? _selectedFolderNames : new HashSet<string>(StringComparer.Ordinal);
        var next = add ? new List<LiveryEntry>(SelectedEntries) : [];
        foreach (var entry in entries)
            if (names.Add(entry.FolderName)) next.Add(entry);
        if (!add)
        {
            var kept = new HashSet<LiveryEntry>(next, ReferenceEqualityComparer.Instance);
            foreach (var old in SelectedEntries)
                if (!kept.Contains(old)) old.IsSelected = false;
            _selectedFolderNames.Clear();
            _selectedFolderNames.UnionWith(names);
        }
        foreach (var entry in next) entry.IsSelected = true;
        SelectedEntries.ReplaceAll(next);
    }

    [RelayCommand]
    private void ClearSelection()
    {
        if (_selectedFolderNames.Count == 0) return;
        foreach (var entry in SelectedEntries) entry.IsSelected = false;
        _selectedFolderNames.Clear();
        SelectedEntries.Clear();
    }

    [RelayCommand]
    private void SelectAll()
    {
        SelectMany(GetFilteredEntries(), add: false);
    }

    /// <param name="freshEntries">The liveries as just built</param>
    /// <param name="replaced">The cards they replace; the shown ones when null (the game's, while the storage is shown)</param>
    public List<LiveryEntry> MergeWithLocalState(List<LiveryEntry> freshEntries, IReadOnlyList<LiveryEntry>? replaced = null)
    {
        var previousByName = (replaced ?? _allEntries).ToDictionary(e => e.FolderName);
        var mergedEntries = new List<LiveryEntry>(freshEntries.Count);
        foreach (var newEntry in freshEntries)
        {
            if (previousByName.TryGetValue(newEntry.FolderName, out var previous))
            {
                newEntry.IsFavorite = previous.IsFavorite;
                newEntry.IsLocked = previous.IsLocked;
                newEntry.Tags = previous.Tags;

                if (HasEquivalentPresentationData(previous, newEntry))
                {
                    mergedEntries.Add(previous);
                    continue;
                }
            }
            mergedEntries.Add(newEntry);
        }
        return mergedEntries;
    }

    private static bool HasEquivalentPresentationData(LiveryEntry a, LiveryEntry b)
    {
        return a.FolderName == b.FolderName
            && a.LiveryName == b.LiveryName
            && a.Author == b.Author
            && a.AuthorRaw == b.AuthorRaw
            && a.Data.IsAuthorInferred == b.Data.IsAuthorInferred
            && a.CarId == b.CarId
            && a.CarManufacturerRaw == b.CarManufacturerRaw
            && a.CarModelNameRaw == b.CarModelNameRaw
            && a.CarYear == b.CarYear
            && a.CarKnown == b.CarKnown
            && a.CreatedYear == b.CreatedYear
            && a.CreatedMonth == b.CreatedMonth
            && a.DownloadDate == b.DownloadDate
            && a.ThumbnailPath == b.ThumbnailPath
            && a.Data.ExternalPreviewPath == b.Data.ExternalPreviewPath
            && a.DuplicateStatus == b.DuplicateStatus
            && PossibleDuplicateOfEqual(a.PossibleDuplicateOf, b.PossibleDuplicateOf)
            && a.IsMine == b.IsMine
            && a.IsPossiblyGenerated == b.IsPossiblyGenerated
            && a.HasNoLayers == b.HasNoLayers
            && a.HasParseError == b.HasParseError
            && ParseIssuesEqual(a.Data.ParseIssues, b.Data.ParseIssues)
            && a.LiveryId == b.LiveryId
            && a.CLiveryHash == b.CLiveryHash
            && a.AssignedCardId == b.AssignedCardId
            && a.Data.ArtworkSketch == b.Data.ArtworkSketch
            && a.Data.AuthorIdentityTagHex == b.Data.AuthorIdentityTagHex;
    }

    private static bool ParseIssuesEqual(IReadOnlyList<LiveryParseIssue>? a, IReadOnlyList<LiveryParseIssue>? b)
    {
        if (a is null || b is null) return ReferenceEquals(a, b);
        return a.SequenceEqual(b);
    }

    private static bool PossibleDuplicateOfEqual(IReadOnlyList<DuplicateRelation>? a, IReadOnlyList<DuplicateRelation>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || a.Count == 0) return b is null || b.Count == 0;
        if (b is null || a.Count != b.Count) return false;
        return a.ToHashSet().SetEquals(b);
    }
}

/// <summary>The selection, the tags that show or hide liveries, and the keys' cursor of one mode of the main window</summary>
internal sealed record GalleryViewState(
    IReadOnlyList<string> Selected, IReadOnlyList<string> ShownTags, IReadOnlyList<string> HiddenTags, string? Cursor, string? Anchor);
