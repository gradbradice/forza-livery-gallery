using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ForzaToolkit.Liveries.History;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>What the livery window needs from the gallery: its data and the actions that stay in this window</summary>
internal sealed record LiveryDetailsSource(
    Func<IReadOnlyList<LiveryEntry>> AllEntries,
    Func<LiveryEntry, string?> PreviewPath,
    Func<IReadOnlyList<LiveryHistoryEvent>> History,
    Func<Task<IReadOnlyList<LiveryBackupManifest?>>> BackupManifests,
    Func<LiveryEntry, AuthorCard?> AuthorCard,
    Action<LiveryEntry, string?> SetNote,
    Action<LiveryEntry> ToggleFavorite,
    Action<LiveryEntry> ToggleLock,
    Services.Screenshots.ScreenshotStore? Screenshots = null,
    Func<(Services.Screenshots.ScreenshotFormat Format, int Quality)>? ScreenshotFormat = null);

/// <summary>A small card of another livery (the author's, a similar one): a click opens it in the same window</summary>
internal sealed partial class LiveryThumbItem(LiveryEntry entry, string detail, bool isExact) : ObservableObject
{
    public LiveryEntry Entry { get; } = entry;
    public string Name => Entry.LiveryName;
    public string Detail { get; } = detail;
    public bool IsExact { get; } = isExact;
    public bool HasDetail => Detail.Length > 0;

    [ObservableProperty]
    private Bitmap? _thumbnail;
}

internal sealed record LiveryHistoryItem(string DateText, string Text);

/// <summary>
/// The livery window: one livery with everything the gallery knows about it, the neighbours on screen a key away. The
/// note is saved on its own a moment after typing stops, and when the window moves on or closes
/// </summary>
internal sealed partial class LiveryDetailsViewModel : ObservableObject, IDisposable
{
    private const int OtherByAuthorShown = 4;
    private const int SimilarShown = 4;

    private readonly LiveryDetailsSource _source;
    private readonly IReadOnlyList<LiveryEntry> _shown;
    private readonly DispatcherTimer _noteTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private CancellationTokenSource _loadCts = new();
    private bool _noteDirty;
    private bool _loadingNote;
    private IReadOnlyList<LiveryBackupManifest?>? _manifests;

    public LiveryDetailsViewModel(LiveryDetailsSource source, IReadOnlyList<LiveryEntry> shown, LiveryEntry start)
    {
        _source = source;
        _shown = shown;
        _noteTimer.Tick += (_, _) => SaveNote();
        _entry = start;
        Load(start);
    }

    [ObservableProperty] private LiveryEntry _entry;
    [ObservableProperty] private Bitmap? _preview;
    [ObservableProperty] private bool _isPreviewLoading;
    [ObservableProperty] private string _positionText = "";
    [ObservableProperty] private bool _hasPosition;
    [ObservableProperty] private bool _canGoPrevious;
    [ObservableProperty] private bool _canGoNext;
    [ObservableProperty] private string _carLine = "";
    [ObservableProperty] private string _authorText = "";
    /// <summary>The livery's badges, the card's (Views/LiveryBadges.cs): each with its name and what it means here</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadges))]
    private IReadOnlyList<Views.LiveryBadge> _badges = [];

    public bool HasBadges => Badges.Count > 0;
    [ObservableProperty] private string _idText = "";
    [ObservableProperty] private bool _hasId;
    [ObservableProperty] private string _datesText = "";
    [ObservableProperty] private string _layersText = "";
    [ObservableProperty] private string _backupsText = Strings.LiveryDetailsCounting;
    [ObservableProperty] private bool _backupsMissing;
    [ObservableProperty] private string _noteStatus = "";
    [ObservableProperty] private string _noteCount = "";
    [ObservableProperty] private bool _hasAuthorCard;
    [ObservableProperty] private string _authorNoteText = "";
    [ObservableProperty] private bool _hasAuthorNote;
    [ObservableProperty] private string _authorNoteSource = "";
    [ObservableProperty] private IReadOnlyList<LiveryHistoryItem> _history = [];
    [ObservableProperty] private bool _hasHistory;
    [ObservableProperty] private IReadOnlyList<LiveryThumbItem> _otherByAuthor = [];
    [ObservableProperty] private bool _hasOtherByAuthor;
    [ObservableProperty] private string _allByAuthorText = "";
    [ObservableProperty] private bool _hasMoreByAuthor;
    [ObservableProperty] private string _sameCarText = "";
    [ObservableProperty] private bool _hasSameCar;
    [ObservableProperty] private IReadOnlyList<LiveryThumbItem> _similar = [];
    [ObservableProperty] private bool _hasSimilar;

    private string _noteText = "";
    /// <summary>The note as typed; saved a moment after typing stops</summary>
    public string NoteText
    {
        get => _noteText;
        set
        {
            value ??= "";
            if (value.Length > NoteService.MaxLength) value = value[..NoteService.MaxLength];
            if (_noteText == value) return;
            _noteText = value;
            OnPropertyChanged();
            NoteCount = $"{value.Length} / {NoteService.MaxLength}";
            if (_loadingNote) return;
            _noteDirty = true;
            NoteStatus = "";
            _noteTimer.Stop();
            _noteTimer.Start();
        }
    }

    public int NoteMaxLength => NoteService.MaxLength;

    /// <summary>The window title</summary>
    public string Title => string.Format(Strings.LiveryDetailsTitleFormat, Entry.LiveryName);

    /// <summary>The keys of every livery by the author, for "all in the gallery"</summary>
    public IReadOnlyList<string> AuthorLiveryKeys { get; private set; } = [];

    public void GoPrevious() => Step(-1);

    public void GoNext() => Step(+1);

    private void Step(int delta)
    {
        int index = IndexOf(Entry);
        if (index < 0) return;
        int next = index + delta;
        if (next < 0 || next >= _shown.Count) return;
        Show(_shown[next]);
    }

    /// <summary>Shows another livery in the same window (a neighbour, the author's, a similar one)</summary>
    public void Show(LiveryEntry entry)
    {
        if (ReferenceEquals(entry, Entry)) return;
        SaveNote();
        Entry = entry;
        Load(entry);
    }

    /// <summary>What changed outside (favorite, lock, tags, the author card): the texts are made again</summary>
    public void Refresh() => Load(Entry, reloadImages: false);

    public void ToggleFavorite()
    {
        _source.ToggleFavorite(Entry);
    }

    public void ToggleLock()
    {
        _source.ToggleLock(Entry);
    }


    /// <summary>The author's card (to edit its note); null when nothing tells the author</summary>
    public AuthorCard? AuthorCard() => _source.AuthorCard(Entry);

    /// <summary>Saves a note typed and not saved yet</summary>
    public void SaveNote()
    {
        _noteTimer.Stop();
        if (!_noteDirty) return;
        _noteDirty = false;
        _source.SetNote(Entry, _noteText);
        NoteStatus = Strings.LiveryDetailsNoteSaved;
    }

    private int IndexOf(LiveryEntry entry)
    {
        for (int i = 0; i < _shown.Count; i++)
            if (ReferenceEquals(_shown[i], entry) || _shown[i].FolderName == entry.FolderName) return i;
        return -1;
    }

    private void Load(LiveryEntry entry, bool reloadImages = true)
    {
        var all = _source.AllEntries();
        int index = IndexOf(entry);
        HasPosition = index >= 0;
        PositionText = index >= 0 ? string.Format(Strings.LiveryDetailsPositionFormat, index + 1, _shown.Count) : "";
        CanGoPrevious = index > 0;
        CanGoNext = index >= 0 && index < _shown.Count - 1;
        OnPropertyChanged(nameof(Title));

        CarLine = string.Join(" · ", new[]
        {
            entry.CarYear is { } year ? $"{entry.CarManufacturer} {entry.CarModelName} {year}" : $"{entry.CarManufacturer} {entry.CarModelName}",
            entry.CarCountry, entry.CarType,
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
        AuthorText = entry.Author;
        Badges = Views.LiveryBadges.Of(entry);

        HasId = entry.LiveryId > 0;
        IdText = HasId ? entry.LiveryId.ToString("N0", AppLocalisationService.Culture) : Strings.LiveryDetailsUnknown;
        string created = entry.CreatedYear is > 0 && entry.CreatedMonth is >= 1 and <= 12
            ? new DateTime(entry.CreatedYear.Value, entry.CreatedMonth.Value, 1).ToString(AppLocalisationService.MonthYearFormat, AppLocalisationService.Culture)
            : "—";
        string downloaded = entry.DownloadDate is { } d ? d.ToString("d", AppLocalisationService.Culture) : "—";
        DatesText = $"{created} · {downloaded}";
        LayersText = entry.LayerCount is { } layers ? layers.ToString("N0", AppLocalisationService.Culture) : "—";
        ShowBackups(entry);

        _loadingNote = true;
        NoteText = entry.Note ?? "";
        _loadingNote = false;
        _noteDirty = false;
        NoteStatus = "";
        NoteCount = $"{NoteText.Length} / {NoteService.MaxLength}";

        var card = _source.AuthorCard(entry);
        HasAuthorCard = card is not null;
        HasAuthorNote = !string.IsNullOrWhiteSpace(card?.Description);
        AuthorNoteText = HasAuthorNote ? card!.Description!.Trim() : Strings.LiveryDetailsAuthorNoteEmpty;
        AuthorNoteSource = card is null ? "" : string.Format(Strings.LiveryDetailsAuthorNoteFromCardFormat, card.DisplayName);

        var history = LiveryDetails.History(entry, _source.History());
        History = [.. history.Select(h => new LiveryHistoryItem(h.AtLocal.ToString("d", AppLocalisationService.Culture), h.Text))];
        HasHistory = History.Count > 0;
        LoadScreenshots(entry);

        var byAuthor = LiveryDetails.OtherByAuthor(entry, all);
        AuthorLiveryKeys = [entry.FolderName, .. byAuthor.Select(e => e.FolderName)];
        var other = byAuthor.Take(OtherByAuthorShown).Select(e => new LiveryThumbItem(e, "", false)).ToList();
        HasOtherByAuthor = other.Count > 0;
        HasMoreByAuthor = byAuthor.Count > 0;
        AllByAuthorText = string.Format(Strings.LiveryDetailsAllByAuthorFormat, byAuthor.Count + 1);
        int sameCar = LiveryDetails.SameCarCount(entry, all);
        HasSameCar = sameCar > 0;
        SameCarText = string.Format(Strings.LiveryDetailsSameCarFormat, entry.CarModelName, sameCar);

        var similar = LiveryDetails.Similar(entry, all, LiveryEntry.PossibleDuplicateThresholdPercent)
            .Take(SimilarShown)
            .Select(s => new LiveryThumbItem(s.Entry,
                string.Format(s.IsExact ? Strings.LiveryDetailsExactCopyFormat : Strings.LiveryDetailsPossibleCopyFormat, s.Percent), s.IsExact))
            .ToList();
        HasSimilar = similar.Count > 0;

        var oldOther = OtherByAuthor;
        var oldSimilar = Similar;
        OtherByAuthor = other;
        Similar = similar;
        if (reloadImages)
        {
            _loadCts.Cancel();
            _loadCts.Dispose();
            _loadCts = new CancellationTokenSource();
            var oldPreview = Preview;
            Preview = null;
            oldPreview?.Dispose();
            foreach (var item in oldOther.Concat(oldSimilar)) item.Thumbnail?.Dispose();
            _ = LoadImagesAsync(entry, other.Concat(similar).ToList(), _loadCts.Token);
        }
        else
        {
            // Same livery: the thumbnails stay, moved over to the new rows; those of rows no longer shown go
            var kept = new HashSet<Bitmap>(ReferenceEqualityComparer.Instance);
            foreach (var item in other.Concat(similar))
            {
                item.Thumbnail = oldOther.Concat(oldSimilar).FirstOrDefault(o => o.Entry == item.Entry)?.Thumbnail;
                if (item.Thumbnail is { } thumb) kept.Add(thumb);
            }
            foreach (var item in oldOther.Concat(oldSimilar))
                if (item.Thumbnail is { } thumb && !kept.Contains(thumb)) thumb.Dispose();
        }
    }

    private void ShowBackups(LiveryEntry entry)
    {
        if (_manifests is null)
        {
            BackupsText = Strings.LiveryDetailsCounting;
            BackupsMissing = false;
            _ = CountBackupsAsync(entry);
            return;
        }
        var presence = LiveryDetails.Backups(entry, _manifests);
        BackupsMissing = presence.With == 0;
        BackupsText = presence is { With: > 0, LastUtc: { } last }
            ? string.Format(Strings.LiveryDetailsBackupsFormat, presence.With, presence.Total,
                last.ToLocalTime().ToString("d", AppLocalisationService.Culture))
            : Strings.LiveryDetailsBackupsNone;
    }

    private async Task CountBackupsAsync(LiveryEntry entry)
    {
        try
        {
            _manifests = await _source.BackupManifests();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.LogError("Livery window: failed to read the backups", ex);
            _manifests = [];
        }
        if (ReferenceEquals(Entry, entry)) ShowBackups(entry);
    }

    // The decodes are not given the token: one cancelled before it starts would end the task with an exception nobody
    // waits for (an error in the log for each livery skipped quickly). The token is looked at after each
    private async Task LoadImagesAsync(LiveryEntry entry, IReadOnlyList<LiveryThumbItem> items, CancellationToken ct)
    {
        IsPreviewLoading = true;
        string? previewPath = _source.PreviewPath(entry);
        var preview = await Task.Run(() => Decode(previewPath, 1280), CancellationToken.None).ConfigureAwait(true);
        if (ct.IsCancellationRequested) { preview?.Dispose(); return; }
        Preview = preview;
        IsPreviewLoading = false;
        foreach (var item in items)
        {
            string? path = item.Entry.ThumbnailPath is { } own && File.Exists(own) ? own : _source.PreviewPath(item.Entry);
            var thumb = await Task.Run(() => Decode(path, 240), CancellationToken.None).ConfigureAwait(true);
            if (ct.IsCancellationRequested) { thumb?.Dispose(); return; }
            if (thumb is null) continue;
            // The rows may have been made again meanwhile (Refresh): the picture goes to the row shown now, or away
            var row = OtherByAuthor.Concat(Similar).FirstOrDefault(r => ReferenceEquals(r.Entry, item.Entry) && r.Thumbnail is null);
            if (row is null) thumb.Dispose();
            else row.Thumbnail = thumb;
        }
    }

    private static Bitmap? Decode(string? path, int width)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.MediumQuality);
        }
#pragma warning disable CA1031 // a broken or unknown picture is no preview, not a failure of the window
        catch (Exception ex)
#pragma warning restore CA1031
        {
            AppLog.LogErrorThrottled(path, $"Livery window: failed to read the picture '{path}'", ex);
            return null;
        }
    }

    public void Dispose()
    {
        SaveNote();
        _noteTimer.Stop();
        _loadCts.Cancel();
        _loadCts.Dispose();
        Preview?.Dispose();
        foreach (var item in OtherByAuthor.Concat(Similar)) item.Thumbnail?.Dispose();
        DisposeScreenshots();
    }
}
