using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>A livery of the game offered to make room: ticked, it goes to the storage</summary>
internal sealed partial class ReturnSwapRowViewModel : ObservableObject, IThumbnailHost
{
    private readonly Action _changed;

    public ReturnSwapRowViewModel(LiveryEntry entry, bool goes, Action changed)
    {
        Entry = entry;
        _goes = goes;
        _changed = changed;
    }

    public LiveryEntry Entry { get; }
    public string LiveryName => Entry.LiveryName;
    public string CarText => Entry.CarYear is { } year ? $"{Entry.CarManufacturer} {Entry.CarModelName} ({year})".Trim() : $"{Entry.CarManufacturer} {Entry.CarModelName}".Trim();
    public string? ThumbnailPath => Entry.Data.ThumbnailPath;

    public string SinceText => Entry.DownloadDate is { } date
        ? string.Format(Strings.ReturnRoomInGameSinceFormat, date.ToString("d", AppLocalisationService.Culture))
        : Strings.UnknownDownloadDate;

    [ObservableProperty]
    private bool _goes;

    partial void OnGoesChanged(bool value) => _changed();

    [ObservableProperty]
    private Bitmap? _thumbnail;
}

/// <summary>A storage folder the liveries that make room may go into: a new one, none, or one the storage has</summary>
internal sealed record ReturnFolderChoice(string Name, string? Id, bool IsNew)
{
    public override string ToString() => Name;
}

/// <summary>
/// The window of "Return to the game" when the game has too little room (docs/storage.md, stage 3): swap (the game's
/// liveries ticked go to the storage, all come back), return all beyond the limit, return as many as fit
/// </summary>
internal sealed partial class ReturnRoomViewModel : ObservableObject
{
    private readonly int _counted;

    /// <param name="toReturn">How many liveries come back</param>
    /// <param name="counted">How many the game's limit counts now</param>
    /// <param name="limit">The game's limit</param>
    /// <param name="candidates">The game's liveries that may make room, in the order to offer (<see cref="ReturnRoom.Candidates"/>)</param>
    /// <param name="folders">The storage's folders</param>
    /// <param name="today">For the name of the new folder</param>
    public ReturnRoomViewModel(int toReturn, int counted, int limit, IReadOnlyList<LiveryEntry> candidates,
        IReadOnlyList<LiveryArchiveFolder> folders, DateTime today)
    {
        ToReturn = toReturn;
        _counted = counted;
        Limit = limit;
        Free = Math.Max(0, limit - counted);
        Missing = ReturnRoom.Missing(toReturn, Free);
        Rows = [.. candidates.Select((e, i) => new ReturnSwapRowViewModel(e, i < Missing, Recount))];

        string swapName = string.Format(Strings.ReturnRoomExchangeFolderFormat, today.ToString("d", AppLocalisationService.Culture));
        var same = folders.FirstOrDefault(f => string.Equals(f.Name, swapName, StringComparison.OrdinalIgnoreCase));
        Folders =
        [
            same is null ? new ReturnFolderChoice(swapName, null, IsNew: true) : new ReturnFolderChoice(same.Name, same.Id, IsNew: false),
            new ReturnFolderChoice(Strings.StorageNoFolder, null, IsNew: false),
            .. folders.Where(f => f != same).OrderBy(f => f.Order).Select(f => new ReturnFolderChoice(f.Name, f.Id, IsNew: false)),
        ];
        _folder = Folders[0];

        _mode = CanExchange ? ReturnRoomMode.Exchange : CanFit ? ReturnRoomMode.Fit : ReturnRoomMode.All;
        Recount();
    }

    public int ToReturn { get; }
    public int Limit { get; }
    public int Free { get; }
    public int Missing { get; }

    public IReadOnlyList<ReturnSwapRowViewModel> Rows { get; }
    public IReadOnlyList<ReturnFolderChoice> Folders { get; }

    /// <summary>Enough of the game's liveries may make room</summary>
    public bool CanExchange => Rows.Count >= Missing;

    /// <summary>Some fit: a free slot at least</summary>
    public bool CanFit => Free > 0;

    [ObservableProperty]
    private ReturnFolderChoice _folder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExchange), nameof(IsAll), nameof(IsFit), nameof(ApplyText), nameof(CanApply), nameof(NeedMoreText))]
    private ReturnRoomMode _mode;

    public bool IsExchange
    {
        get => Mode == ReturnRoomMode.Exchange;
        set { if (value && CanExchange) Mode = ReturnRoomMode.Exchange; }
    }

    public bool IsAll
    {
        get => Mode == ReturnRoomMode.All;
        set { if (value) Mode = ReturnRoomMode.All; }
    }

    public bool IsFit
    {
        get => Mode == ReturnRoomMode.Fit;
        set { if (value && CanFit) Mode = ReturnRoomMode.Fit; }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply), nameof(NeedMoreText), nameof(CheckedText))]
    private int _checkedCount;

    /// <summary>The swap needs as many ticked as are missing (more frees more)</summary>
    public bool CanApply => Mode != ReturnRoomMode.Exchange || CheckedCount >= Missing;

    public string? NeedMoreText => Mode == ReturnRoomMode.Exchange && CheckedCount < Missing
        ? string.Format(Strings.ReturnRoomNeedMoreFormat, N(Missing - CheckedCount))
        : null;

    private static string N(int value) => value.ToString("N0", AppLocalisationService.Culture);
    private static string Liveries(int count) => StatisticsViewModel.LiveryCount(count);

    public string TitleText => string.Format(Strings.ReturnRoomTitleFormat, Liveries(ToReturn));

    public string InfoText => string.Format(Strings.ReturnRoomInfoFormat, N(_counted), N(Limit), N(Free), N(Missing));

    public string ExchangeText => string.Format(Strings.ReturnRoomExchangeFormat, Liveries(Missing));

    public string ExchangeNoteText => CanExchange
        ? string.Format(Strings.ReturnRoomExchangeNoteFormat, N(_counted - Missing + ToReturn), N(Limit))
        : string.Format(Strings.ReturnRoomTooFewFormat, Liveries(Rows.Count));

    public string CheckedText => string.Format(Strings.ReturnRoomCheckedFormat, N(CheckedCount), N(Missing), N(Rows.Count));

    public string CandidatesNote => Strings.ReturnRoomCandidatesNote;

    public string AllText => string.Format(Strings.ReturnRoomAllFormat, N(ToReturn));

    public string AllNoteText => string.Format(Strings.ReturnRoomAllNoteFormat, N(_counted + ToReturn), N(Limit));

    public string FitText => string.Format(Strings.ReturnRoomFitFormat, N(Math.Min(Free, ToReturn)));

    public string FitNoteText => string.Format(Strings.ReturnRoomFitNoteFormat, N(Math.Min(Free, ToReturn)), N(ToReturn - Math.Min(Free, ToReturn)));

    public string ApplyText => Mode switch
    {
        ReturnRoomMode.Exchange => Strings.ReturnRoomExchangeButton,
        ReturnRoomMode.All => Strings.ReturnBeyondLimitButton,
        _ => string.Format(Strings.ReturnRoomFitButtonFormat, N(Math.Min(Free, ToReturn))),
    };

    private void Recount() => CheckedCount = Rows.Count(r => r.Goes);

    /// <summary>What the user chose; null while the swap lacks ticks</summary>
    public ReturnRoomChoice? Choice() => !CanApply ? null : Mode switch
    {
        ReturnRoomMode.Exchange => new ReturnRoomChoice(Mode, [.. Rows.Where(r => r.Goes).Select(r => r.Entry)],
            Folder.IsNew ? null : Folder.Id, Folder.IsNew ? Folder.Name : null),
        _ => new ReturnRoomChoice(Mode, [], null, null),
    };
}
