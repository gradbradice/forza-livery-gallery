using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;

namespace LiveryGallery.ViewModels;

/// <summary>
/// The properties panel on the right of the gallery (docs/plans.md, «Панель свойств»): the selected livery — its picture,
/// car, author, star and lock, tags with a field to add one (the tags there are offered as one types), its note saved as
/// it is typed, its facts — or what the selected liveries have in common: the tags on all of them and on some, the
/// stars "2 of 5", a note to add to all or to put in place of theirs. Its actions are the gallery's own (the same as the
/// keys and the context menu), so the checks and the undo are theirs
/// </summary>
internal sealed partial class PropertiesViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly List<LiveryEntry> _watched = [];

    public PropertiesViewModel(MainViewModel main)
    {
        _main = main;
        Note = new NoteEditorViewModel(main.SetNote);
        TagEditor = new TagEditorViewModel(() => Entries, () => main.Gallery.AllEntries, main.ApplyTagsChange);
        main.Gallery.SelectedEntries.CollectionChanged += (_, _) =>
        {
            // Only while the panel is open: it is brought up to date when it opens
            if (main.IsPropertiesOpen) Update();
        };
    }

    private GalleryViewModel Gallery => _main.Gallery;

    /// <summary>The selected liveries, as the panel shows them</summary>
    public IReadOnlyList<LiveryEntry> Entries { get; private set; } = [];

    /// <summary>The one livery selected; null when none or several are</summary>
    public LiveryEntry? Single => Entries.Count == 1 ? Entries[0] : null;

    public bool IsSingle => Entries.Count == 1;

    public bool IsMulti => Entries.Count > 1;

    public bool IsEmpty => Entries.Count == 0;

    /// <summary>"Selected: 5"</summary>
    public string MultiTitle => string.Format(CultureInfo.CurrentCulture, Strings.SelectedCountLabel, Entries.Count);

    /// <summary>"Nissan ×3, Porsche ×2 · authors: 3"</summary>
    public string MultiSummary
    {
        get
        {
            var makes = Entries.GroupBy(e => e.CarManufacturer, StringComparer.CurrentCultureIgnoreCase)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.CurrentCulture)
                .Select(g => $"{g.Key} ×{g.Count()}").Take(3).ToList();
            int authors = Entries.Select(e => e.AuthorDisplayText).Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.CurrentCultureIgnoreCase).Count();
            string cars = string.Join(", ", makes);
            string who = string.Format(CultureInfo.CurrentCulture, Strings.PropertiesAuthorsFormat, authors);
            return cars.Length > 0 ? $"{cars} · {who}" : who;
        }
    }

    public bool IsStorageMode => _main.IsStorageMode;

    /// <summary>The tags of the selection, with a field to add one (the same editor as the livery window's)</summary>
    public TagEditorViewModel TagEditor { get; }

    /// <summary>The panel's buttons: the gallery's actions on the selection (GalleryActions), the same as the menu's</summary>
    public IReadOnlyList<GalleryAction> Actions => _main.Actions.Panel;

    /// <summary>The star: on all of them, on none, or "2 of 5"</summary>
    public bool AllFavorite => Entries.Count > 0 && Entries.All(e => e.IsFavorite);

    public string FavoritePart => PartOf(Entries.Count(e => e.IsFavorite));

    /// <summary>The star's tooltip: "Favourite", or "Favourite: 2 of 5" when only some are</summary>
    public string FavoriteTooltip => WithPart(Strings.PropertiesFavorite, FavoritePart);

    public bool AllLocked => Entries.Count > 0 && Entries.All(e => e.IsLocked);

    public string LockPart => PartOf(Entries.Count(e => e.IsLocked));

    /// <summary>The lock's tooltip: "Lock", or "Lock: 2 of 5" when only some are locked</summary>
    public string LockTooltip => WithPart(Strings.PropertiesLock, LockPart);

    private static string WithPart(string name, string part) => part.Length > 0 ? $"{name}: {part}" : name;

    private string PartOf(int count) => Entries.Count > 1 && count > 0 && count < Entries.Count
        ? string.Format(CultureInfo.CurrentCulture, Strings.NoticeCounterFormat, count, Entries.Count)
        : "";

    /// <summary>The one livery's note as it is typed, saved by itself (the same editor as the livery window's)</summary>
    public NoteEditorViewModel Note { get; }

    /// <summary>A note for all the selected liveries, added to theirs or put in their place</summary>
    [ObservableProperty]
    private string _noteForAll = "";

    /// <summary>"2 of 5 have a note of their own."</summary>
    public string NotesOfTheirOwn
    {
        get
        {
            int with = Entries.Count(e => e.HasNote);
            return with == 0 ? "" : string.Format(CultureInfo.CurrentCulture, Strings.PropertiesNotesOwnFormat, with, Entries.Count);
        }
    }

    /// <summary>
    /// The panel closed: the note typed is saved, and the panel lets go of the liveries it showed (their handlers, the
    /// note's livery). Closed, it is not told of a new selection or scan, so it held them until it opened again: after a
    /// scan, liveries the gallery no longer has. It is brought up to date when it opens (<see cref="Update"/>)
    /// </summary>
    public void Release()
    {
        SaveNote();
        foreach (var entry in _watched) entry.PropertyChanged -= Entry_PropertyChanged;
        _watched.Clear();
        Entries = [];
        Note.Show(null);
        NoteForAll = "";
        TagEditor.Refresh();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>The selection changed: the panel shows it (a note being typed is saved first)</summary>
    public void Update()
    {
        foreach (var entry in _watched) entry.PropertyChanged -= Entry_PropertyChanged;
        _watched.Clear();
        Entries = [.. Gallery.SelectedEntries];
        // Each of them is watched (its star, lock, tags, note) while selected: a large selection is watched only
        // through the gallery's own refresh
        if (Entries.Count <= 500)
            foreach (var entry in Entries)
            {
                entry.PropertyChanged += Entry_PropertyChanged;
                _watched.Add(entry);
            }
        Note.Show(Single);
        NoteForAll = "";
        TagEditor.Refresh();
        OnPropertyChanged(string.Empty);
    }

    private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LiveryEntry.IsFavorite):
                OnPropertyChanged(nameof(AllFavorite));
                OnPropertyChanged(nameof(FavoritePart));
                OnPropertyChanged(nameof(FavoriteTooltip));
                break;
            case nameof(LiveryEntry.IsLocked):
                OnPropertyChanged(nameof(AllLocked));
                OnPropertyChanged(nameof(LockPart));
                OnPropertyChanged(nameof(LockTooltip));
                break;
            case nameof(LiveryEntry.Tags):
                TagEditor.Refresh();
                break;
            case nameof(LiveryEntry.Note):
                OnPropertyChanged(nameof(NotesOfTheirOwn));
                if (ReferenceEquals(sender, Note.Entry)) Note.Refresh();
                break;
        }
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (Entries.Count > 0) Gallery.ToggleFavoritesCommand.Execute(null);
    }

    [RelayCommand]
    private void ToggleLock()
    {
        if (Entries.Count > 0 && !IsStorageMode) Gallery.ToggleLocksCommand.Execute(null);
    }

    /// <summary>The author's card of the one livery</summary>
    [RelayCommand]
    private void ShowAuthor()
    {
        if (Single is { } entry) Gallery.ShowAuthorRowCommand.Execute(entry);
    }

    /// <summary>Saves the one livery's note now (the panel closes)</summary>
    public void SaveNote() => Note.Save();

    /// <summary>The note for all after each one's own (a line between), or alone where there is none</summary>
    [RelayCommand]
    private void AppendNoteToAll() => SetNotes(own => string.IsNullOrWhiteSpace(own) ? NoteForAll : own + "\n" + NoteForAll);

    /// <summary>The note for all in place of each one's own</summary>
    [RelayCommand]
    private void ReplaceNotes() => SetNotes(_ => NoteForAll);

    private void SetNotes(Func<string?, string> note)
    {
        if (string.IsNullOrWhiteSpace(NoteForAll)) return;
        foreach (var entry in Entries) _main.SetNote(entry, note(entry.Note));
        NoteForAll = "";
        OnPropertyChanged(nameof(NotesOfTheirOwn));
    }
}
