using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ForzaToolkit.Liveries.UserData;
using LiveryGallery.Localisation;

namespace LiveryGallery.ViewModels;

/// <summary>
/// The note of one livery as it is typed, in the livery window and the properties panel (one editor for both): kept to
/// the note's length, saved a moment after the typing stops, when the field is left and before another livery is shown;
/// "Saved" after a save, the count of characters as it is typed. A note changed elsewhere (the other editor, an undo)
/// is taken while nothing typed waits to be saved
/// </summary>
internal sealed partial class NoteEditorViewModel : ObservableObject
{
    private readonly Action<LiveryEntry, string?> _save;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private LiveryEntry? _entry;
    private string _text = "";
    private bool _dirty;

    /// <param name="save">Saves a livery's note (MainViewModel.SetNote: the search finds it, the undo keeps it)</param>
    public NoteEditorViewModel(Action<LiveryEntry, string?> save)
    {
        _save = save;
        _timer.Tick += (_, _) => Save();
    }

    public int MaxLength => NoteService.MaxLength;

    /// <summary>The livery whose note it is; null when none</summary>
    public LiveryEntry? Entry => _entry;

    /// <summary>The note as typed</summary>
    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (value.Length > MaxLength) value = value[..MaxLength];
            if (!SetProperty(ref _text, value)) return;
            OnPropertyChanged(nameof(Count));
            if (_entry is null) return;
            _dirty = true;
            Status = "";
            _timer.Stop();
            _timer.Start();
        }
    }

    /// <summary>"12 / 2000"</summary>
    public string Count => $"{_text.Length} / {MaxLength}";

    /// <summary>"Saved" after a save, empty while typing</summary>
    [ObservableProperty]
    private string _status = "";

    /// <summary>Another livery's note (or none): what was typed for the one before is saved first</summary>
    public void Show(LiveryEntry? entry)
    {
        Save();
        _entry = entry;
        _dirty = false;
        SetProperty(ref _text, entry?.Note ?? "", nameof(Text));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(Entry));
        Status = "";
    }

    /// <summary>The note changed elsewhere: taken unless something typed here waits to be saved</summary>
    public void Refresh()
    {
        if (_dirty || _entry is null) return;
        SetProperty(ref _text, _entry.Note ?? "", nameof(Text));
        OnPropertyChanged(nameof(Count));
    }

    /// <summary>What was typed, saved now (the field was left, the window closes)</summary>
    public void Save()
    {
        _timer.Stop();
        if (!_dirty || _entry is not { } entry) return;
        _dirty = false;
        if (string.Equals(entry.Note ?? "", _text, StringComparison.Ordinal)) return;
        _save(entry, _text);
        Status = Strings.LiveryDetailsNoteSaved;
    }
}
