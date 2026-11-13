using System.Globalization;
using Avalonia.Input;
using ForzaToolkit.UI.Dialogs;
using LiveryGallery.Localisation;

namespace LiveryGallery.Services;

/// <summary>What a key of the gallery does</summary>
internal enum GalleryShortcut
{
    Search,
    SelectAll,
    Escape,
    Refresh,
    /// <summary>Del: the selected liveries into the storage (kept, beyond the game's limit)</summary>
    MoveToStorage,
    /// <summary>Shift+Del: the selected liveries to the archive (to delete)</summary>
    MoveToArchive,
    Favorite,
    Lock,
    Tags,
    /// <summary>Space: the card under the cursor into the selection or out of it (the arrows move the cursor)</summary>
    ToggleSelection,
    View3D,
    /// <summary>Shift+3: one more 3D window</summary>
    View3DNewWindow,
    FindInGame,
    Settings,
    Archive,
    Backups,
    Statistics,
    Authors,
    ShortcutList,
    OpenDetails,
    /// <summary>Del in the storage: the selected liveries to the archive (Shift+Del does the same there)</summary>
    StoredToArchive,
    /// <summary>Ins in the storage: the selected liveries back into the game</summary>
    ReturnToGame,
    /// <summary>Ctrl+1: the main window shows the game</summary>
    ShowGame,
    /// <summary>Ctrl+2: the main window shows the storage</summary>
    ShowStorage,
    /// <summary>F2 in the storage: the folder shown gets another name</summary>
    RenameFolder,
    Undo,
    SaveFilterSet,
}

/// <summary>In which modes of the main window a key works (docs/storage.md)</summary>
[Flags]
internal enum GalleryShortcutModes
{
    Game = 1,
    Storage = 2,
    Both = Game | Storage,
}

/// <summary>Groups of the list of keys (F1)</summary>
internal enum GalleryShortcutGroup
{
    SearchAndSelection,
    SelectedLiveries,
    Windows,
    Storage,
}

/// <param name="WhileTyping">
/// The key works while the cursor is in a text field. Letters, digits, the space, Delete and Ctrl+A belong to the field
/// </param>
/// <param name="Modes">The modes of the main window the key works in: Del sends to the storage in the game, to the archive in the storage</param>
internal sealed record GalleryShortcutKey(
    GalleryShortcut Action, GalleryShortcutGroup Group, Key Key, KeyModifiers Modifiers, bool WhileTyping,
    GalleryShortcutModes Modes = GalleryShortcutModes.Both);

/// <summary>
/// The gallery's keys: a fixed set. The main window asks <see cref="Match"/> for each key it gets, the menus, the
/// tooltips and the list of keys (F1) show <see cref="GestureText"/>
/// </summary>
internal static class GalleryShortcuts
{
    private const KeyModifiers Ctrl = KeyModifiers.Control;
    private const KeyModifiers CtrlShift = KeyModifiers.Control | KeyModifiers.Shift;

    public static IReadOnlyList<GalleryShortcutKey> All { get; } =
    [
        new(GalleryShortcut.Search, GalleryShortcutGroup.SearchAndSelection, Key.F, Ctrl, WhileTyping: true),
        new(GalleryShortcut.SelectAll, GalleryShortcutGroup.SearchAndSelection, Key.A, Ctrl, WhileTyping: false),
        new(GalleryShortcut.Escape, GalleryShortcutGroup.SearchAndSelection, Key.Escape, KeyModifiers.None, WhileTyping: true),
        new(GalleryShortcut.Refresh, GalleryShortcutGroup.SearchAndSelection, Key.F5, KeyModifiers.None, WhileTyping: true),
        new(GalleryShortcut.ToggleSelection, GalleryShortcutGroup.SearchAndSelection, Key.Space, KeyModifiers.None, WhileTyping: false),
        // Not while typing: a text field undoes its own typing with it
        new(GalleryShortcut.Undo, GalleryShortcutGroup.SelectedLiveries, Key.Z, Ctrl, WhileTyping: false),
        // In the search box too: the filters are often set from there
        new(GalleryShortcut.SaveFilterSet, GalleryShortcutGroup.SearchAndSelection, Key.S, Ctrl, WhileTyping: true),

        new(GalleryShortcut.Favorite, GalleryShortcutGroup.SelectedLiveries, Key.F, KeyModifiers.None, WhileTyping: false),
        new(GalleryShortcut.Lock, GalleryShortcutGroup.SelectedLiveries, Key.L, KeyModifiers.None, WhileTyping: false),
        new(GalleryShortcut.Tags, GalleryShortcutGroup.SelectedLiveries, Key.T, KeyModifiers.None, WhileTyping: false),
        new(GalleryShortcut.OpenDetails, GalleryShortcutGroup.SelectedLiveries, Key.Enter, KeyModifiers.None, WhileTyping: false),
        new(GalleryShortcut.View3D, GalleryShortcutGroup.SelectedLiveries, Key.D3, KeyModifiers.None, WhileTyping: false),
        new(GalleryShortcut.View3DNewWindow, GalleryShortcutGroup.SelectedLiveries, Key.D3, KeyModifiers.Shift, WhileTyping: false),
        new(GalleryShortcut.FindInGame, GalleryShortcutGroup.SelectedLiveries, Key.G, KeyModifiers.None, WhileTyping: false),
        new(GalleryShortcut.MoveToStorage, GalleryShortcutGroup.SelectedLiveries, Key.Delete, KeyModifiers.None, WhileTyping: false,
            GalleryShortcutModes.Game),
        new(GalleryShortcut.MoveToArchive, GalleryShortcutGroup.SelectedLiveries, Key.Delete, KeyModifiers.Shift, WhileTyping: false),

        new(GalleryShortcut.Settings, GalleryShortcutGroup.Windows, Key.OemComma, Ctrl, WhileTyping: true),
        new(GalleryShortcut.Archive, GalleryShortcutGroup.Windows, Key.A, CtrlShift, WhileTyping: true),
        new(GalleryShortcut.Backups, GalleryShortcutGroup.Windows, Key.B, CtrlShift, WhileTyping: true),
        new(GalleryShortcut.Statistics, GalleryShortcutGroup.Windows, Key.S, CtrlShift, WhileTyping: true),
        new(GalleryShortcut.Authors, GalleryShortcutGroup.Windows, Key.U, CtrlShift, WhileTyping: true),
        new(GalleryShortcut.ShortcutList, GalleryShortcutGroup.Windows, Key.F1, KeyModifiers.None, WhileTyping: true),
        new(GalleryShortcut.ShowGame, GalleryShortcutGroup.Storage, Key.D1, Ctrl, WhileTyping: true),
        new(GalleryShortcut.ShowStorage, GalleryShortcutGroup.Storage, Key.D2, Ctrl, WhileTyping: true),
        new(GalleryShortcut.ReturnToGame, GalleryShortcutGroup.Storage, Key.Insert, KeyModifiers.None, WhileTyping: false,
            GalleryShortcutModes.Storage),
        new(GalleryShortcut.StoredToArchive, GalleryShortcutGroup.Storage, Key.Delete, KeyModifiers.None, WhileTyping: false,
            GalleryShortcutModes.Storage),
        new(GalleryShortcut.RenameFolder, GalleryShortcutGroup.Storage, Key.F2, KeyModifiers.None, WhileTyping: false,
            GalleryShortcutModes.Storage),
    ];

    public static GalleryShortcutKey Of(GalleryShortcut action) => All.First(s => s.Action == action);

    /// <summary>
    /// The action of a pressed key, or null. <paramref name="physicalKey"/> stands in for a letter or digit the
    /// layout gives no Latin key for, so F is F on any layout. The digits of the number pad count as digits
    /// </summary>
    /// <param name="typing">The cursor is in a text field</param>
    /// <param name="storage">The main window shows the storage, not the game</param>
    public static GalleryShortcut? Match(Key key, PhysicalKey physicalKey, KeyModifiers modifiers, bool typing, bool storage = false)
    {
        var mode = storage ? GalleryShortcutModes.Storage : GalleryShortcutModes.Game;
        key = Normalize(key, physicalKey);
        // Meta (the Windows key) is not a modifier of these keys: Win+L locks the computer, it does not lock a livery
        if ((modifiers & KeyModifiers.Meta) != 0) return null;
        foreach (var shortcut in All)
            if (shortcut.Key == key && shortcut.Modifiers == modifiers && (shortcut.WhileTyping || !typing) && shortcut.Modes.HasFlag(mode))
                return shortcut.Action;
        return null;
    }

    private static Key Normalize(Key key, PhysicalKey physicalKey)
    {
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return Key.D0 + (key - Key.NumPad0);
        if (key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9) return key;
        if (physicalKey is >= PhysicalKey.A and <= PhysicalKey.Z) return Key.A + (physicalKey - PhysicalKey.A);
        if (physicalKey is >= PhysicalKey.Digit0 and <= PhysicalKey.Digit9) return Key.D0 + (physicalKey - PhysicalKey.Digit0);
        if (physicalKey == PhysicalKey.Comma && key is Key.None or Key.ImeProcessed) return Key.OemComma;
        return key;
    }

    /// <summary>
    /// The key as the keyboard of the interface's language names it, as in the menus of Windows: "Ctrl+Shift+A", "Del",
    /// "3"; in German "Strg+Umschalt+A" and "Entf"
    /// </summary>
    public static string GestureText(GalleryShortcut action) => GestureText(action, Strings.Culture);

    /// <param name="culture">The language of the key names; null, the interface's</param>
    public static string GestureText(GalleryShortcut action, CultureInfo? culture)
    {
        string Text(string name) => Strings.ResourceManager.GetString(name, culture) ?? name;
        var shortcut = Of(action);
        string key = shortcut.Key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (shortcut.Key - Key.D0))).ToString(),
            Key.OemComma => ",",
            Key.Delete => Text(nameof(Strings.KeyDelete)),
            Key.Insert => Text(nameof(Strings.KeyInsert)),
            Key.Escape => "Esc",
            Key.Enter => "Enter",
            Key.Space => Text(nameof(Strings.KeySpace)),
            _ => shortcut.Key.ToString(),
        };
        string modifiers = (shortcut.Modifiers.HasFlag(KeyModifiers.Control) ? Text(nameof(Strings.KeyCtrl)) + "+" : "")
            + (shortcut.Modifiers.HasFlag(KeyModifiers.Shift) ? Text(nameof(Strings.KeyShift)) + "+" : "");
        return modifiers + key;
    }

    /// <summary>
    /// The list of keys (F1) for ForzaToolkit's <see cref="ShortcutsDialog"/>, as Forza Livery Craft shows its own: search
    /// and selection (with the arrows and the clicks on a card) and the windows on the left, the selected liveries on the
    /// right
    /// </summary>
    public static IReadOnlyList<ShortcutGroup> Groups()
    {
        static ShortcutGroup Group(GalleryShortcutGroup group, bool right, IEnumerable<(string Gesture, string Description)> more) =>
            new(GroupTitle(group), [.. All.Where(s => s.Group == group)
                .Select(s => (GestureText(s.Action), Description(s.Action))).Concat(more).Select(l => Line(l.Item1, l.Item2))], right);
        return
        [
            Group(GalleryShortcutGroup.SearchAndSelection, false, CursorMoves().Concat(CardClicks())),
            Group(GalleryShortcutGroup.Windows, false, []),
            Group(GalleryShortcutGroup.SelectedLiveries, true, []),
            Group(GalleryShortcutGroup.Storage, true, []),
        ];
    }

    /// <summary>The note under the list: what the keys do not do while typing, those that need one livery selected</summary>
    public static string Note(CultureInfo? culture = null)
    {
        culture ??= Strings.Culture;
        string Text(string name) => Strings.ResourceManager.GetString(name, culture) ?? name;
        return string.Format(culture, Text(nameof(Strings.ShortcutsNoteFormat)), Text(nameof(Strings.KeySpace)), Text(nameof(Strings.KeyDelete)));
    }

    /// <summary>A line of the list from the keys as written here: "+" between the keys of one gesture, "|" between gestures that do the same</summary>
    public static ShortcutLine Line(string gesture, string description)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        var gestures = gesture.Split('|').Select(g => (IReadOnlyList<string>)g.Split('+')).ToList();
        return new ShortcutLine(gestures[0], description) { OtherKeys = gestures.Skip(1).ToList() };
    }

    /// <summary>
    /// The clicks on a card, for the list of keys under "Search and selection": the keys as <see cref="GestureText"/>
    /// writes them ("Ctrl+Click"), and what the click does
    /// </summary>
    public static IReadOnlyList<(string Gesture, string Description)> CardClicks(CultureInfo? culture = null)
    {
        culture ??= Strings.Culture;
        string Text(string name) => Strings.ResourceManager.GetString(name, culture) ?? name;
        string click = Text(nameof(Strings.KeyClick)), ctrl = Text(nameof(Strings.KeyCtrl)), shift = Text(nameof(Strings.KeyShift));
        return
        [
            (click, Text(nameof(Strings.ShortcutClick))),
            (Text(nameof(Strings.KeyDoubleClick)), Text(nameof(Strings.ContextMenuOpenDetails))),
            ($"{ctrl}+{click}", Text(nameof(Strings.ShortcutCtrlClick))),
            ($"{shift}+{click}", Text(nameof(Strings.ShortcutShiftClick))),
            ($"{ctrl}+{shift}+{click}", Text(nameof(Strings.ShortcutCtrlShiftClick))),
        ];
    }

    /// <summary>
    /// The arrows and the keys of pages (<see cref="GalleryKeyboard"/>), for the list of keys under "Search and
    /// selection": the keys ("|" between keys that do the same, "+" in a combination) and what they do
    /// </summary>
    public static IReadOnlyList<(string Gesture, string Description)> CursorMoves(CultureInfo? culture = null)
    {
        culture ??= Strings.Culture;
        string Text(string name) => Strings.ResourceManager.GetString(name, culture) ?? name;
        const string arrows = "← → ↑ ↓";
        string ctrl = Text(nameof(Strings.KeyCtrl)), shift = Text(nameof(Strings.KeyShift));
        return
        [
            (arrows, Text(nameof(Strings.ShortcutArrows))),
            ($"{Text(nameof(Strings.KeyHome))}|{Text(nameof(Strings.KeyEnd))}", Text(nameof(Strings.ShortcutHomeEnd))),
            ($"{Text(nameof(Strings.KeyPageUp))}|{Text(nameof(Strings.KeyPageDown))}", Text(nameof(Strings.ShortcutPage))),
            ($"{shift}+{arrows}", Text(nameof(Strings.ShortcutShiftArrows))),
            ($"{ctrl}+{arrows}", string.Format(culture, Text(nameof(Strings.ShortcutCtrlArrows)), Text(nameof(Strings.KeySpace)))),
        ];
    }

    /// <summary>What the key does, for the list of keys</summary>
    public static string Description(GalleryShortcut action) => action switch
    {
        GalleryShortcut.Undo => Strings.ShortcutUndo,
        GalleryShortcut.SaveFilterSet => Strings.ShortcutSaveFilterSet,
        GalleryShortcut.Search => Strings.ShortcutSearch,
        GalleryShortcut.SelectAll => Strings.ShortcutSelectAll,
        GalleryShortcut.Escape => Strings.ShortcutEscape,
        GalleryShortcut.Refresh => Strings.RefreshTooltip,
        GalleryShortcut.MoveToStorage => Strings.ContextMenuMoveToStorage,
        GalleryShortcut.MoveToArchive => Strings.ContextMenuMoveToArchive,
        GalleryShortcut.Favorite => Strings.FavoriteToggleTooltip,
        GalleryShortcut.Lock => Strings.ShortcutLock,
        GalleryShortcut.Tags => Strings.ShortcutTags,
        GalleryShortcut.ToggleSelection => Strings.ShortcutToggleSelection,
        GalleryShortcut.View3D => Strings.View3DTooltip,
        GalleryShortcut.View3DNewWindow => Strings.ShortcutView3DNewWindow,
        GalleryShortcut.FindInGame => Strings.ContextMenuNavigateInGame,
        GalleryShortcut.Settings => Strings.SettingsDialogTitle,
        GalleryShortcut.Archive => Strings.MenuOpenArchive,
        GalleryShortcut.Backups => Strings.MenuOpenBackups,
        GalleryShortcut.Statistics => Strings.StatsToggleTooltip,
        GalleryShortcut.Authors => Strings.AuthorsButtonTooltip,
        GalleryShortcut.ShortcutList => Strings.ShortcutsTitle,
        GalleryShortcut.OpenDetails => Strings.ContextMenuOpenDetails,
        GalleryShortcut.StoredToArchive => Strings.ShortcutStoredToArchive,
        GalleryShortcut.ReturnToGame => Strings.ContextMenuReturnToGame,
        GalleryShortcut.ShowGame => Strings.ShortcutShowGame,
        GalleryShortcut.ShowStorage => Strings.ShortcutShowStorage,
        GalleryShortcut.RenameFolder => Strings.ShortcutRenameFolder,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    public static string GroupTitle(GalleryShortcutGroup group) => group switch
    {
        GalleryShortcutGroup.SearchAndSelection => Strings.ShortcutsGroupSearch,
        GalleryShortcutGroup.SelectedLiveries => Strings.ShortcutsGroupSelected,
        GalleryShortcutGroup.Windows => Strings.ShortcutsGroupWindows,
        GalleryShortcutGroup.Storage => Strings.ShortcutsGroupStorage,
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
    };
}

/// <summary>A tooltip with the key of its action shown beside the text (the main window's data template draws it)</summary>
internal sealed record ShortcutTip(string Text, GalleryShortcut Action)
{
    public string Gesture => GalleryShortcuts.GestureText(Action);

    public override string ToString() => Text;
}
