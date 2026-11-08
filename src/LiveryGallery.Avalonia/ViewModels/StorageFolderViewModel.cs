using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>Where the window "Storage folder" puts the storage</summary>
internal enum StorageFolderOption
{
    /// <summary>The program's folder (games\&lt;game&gt;\storage\&lt;account&gt;)</summary>
    Program,

    /// <summary>A common folder of the user's, inside by game and account: &lt;common&gt;\FH6\bradice (…CDEF)</summary>
    Common,

    /// <summary>Exactly the folder chosen: this account of this game only</summary>
    Exact,
}

/// <summary>
/// The window "Storage folder" (docs/storage.md, "Own folder"): the program's folder, a common folder of the user's
/// with the gallery's own layout by game and account (offered first when the storage is in the program's folder), or
/// exactly a folder. Each choice is checked (not in a save, the program's folder or another account's storage), and
/// the window says what the folder holds already (liveries put there by hand, folders of liveries), and what the move
/// will do (a rename on one disk, a copy checked between two)
/// </summary>
internal sealed partial class StorageFolderViewModel : ObservableObject
{
    private readonly StorageFolderContext _context;
    private readonly DispatcherTimer _surveyLater;
    private int _surveyVersion;

    public StorageFolderViewModel(StorageFolderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _surveyLater = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => SurveyNow());
        // In a folder of its own: the choice as it is now; in the program's folder: a common folder is offered
        if (context.IsCustom)
        {
            string current = context.CurrentFolder;
            string? common = Path.GetDirectoryName(Path.GetDirectoryName(current));
            bool isCommonLayout = common is not null && context.CommonFolder is { } known && StoragePlaces.Same(common, known)
                && StoragePlaces.Same(current, CommonTarget(common));
            _option = isCommonLayout ? StorageFolderOption.Common : StorageFolderOption.Exact;
            _commonFolder = isCommonLayout ? common! : context.CommonFolder ?? "";
            _exactFolder = isCommonLayout ? "" : current;
        }
        else
        {
            _option = StorageFolderOption.Common;
            _commonFolder = context.CommonFolder ?? "";
            _exactFolder = "";
        }
        Refresh();
    }

    public string TitleText => string.Format(Strings.StorageFolderHeaderFormat, _context.AccountTitle);

    public string SubtitleText => string.Format(Strings.StorageFolderNowFormat, _context.GameName,
        _context.IsCustom ? _context.CurrentFolder : Strings.StorageFolderNowProgram);

    /// <summary>The program's folder, shortened from its start ("…\AppData\Roaming\ForzaLiveryGallery\games\fh6\storage\…")</summary>
    public string ProgramFolderText => Shorten(_context.ProgramFolder);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgram), nameof(IsCommon), nameof(IsExact))]
    private StorageFolderOption _option;

    public bool IsProgram
    {
        get => Option == StorageFolderOption.Program;
        set
        {
            if (value) Option = StorageFolderOption.Program;
        }
    }

    public bool IsCommon
    {
        get => Option == StorageFolderOption.Common;
        set
        {
            if (value) Option = StorageFolderOption.Common;
        }
    }

    public bool IsExact
    {
        get => Option == StorageFolderOption.Exact;
        set
        {
            if (value) Option = StorageFolderOption.Exact;
        }
    }

    [ObservableProperty]
    private string _commonFolder;

    [ObservableProperty]
    private string _exactFolder;

    partial void OnOptionChanged(StorageFolderOption value) => Refresh();

    partial void OnCommonFolderChanged(string value) => Refresh();

    partial void OnExactFolderChanged(string value) => Refresh();

    /// <summary>"The account's liveries will be in D:\Forza liveries\FH6\bradice (…CDEF)"</summary>
    [ObservableProperty]
    private string _commonResultText = "";

    /// <summary>The folder the storage would go to; null while none is given</summary>
    public string? Target { get; private set; }

    /// <summary>Why the folder does not fit (the reason, the rule); empty when it does</summary>
    [ObservableProperty]
    private string _problemText = "";

    [ObservableProperty]
    private bool _hasProblem;

    /// <summary>What the move does: how much, one disk or two, what stays with the liveries</summary>
    [ObservableProperty]
    private string _summaryText = "";

    /// <summary>What the folder holds already (liveries put there by hand)</summary>
    [ObservableProperty]
    private string _existingText = "";

    [ObservableProperty]
    private bool _hasExisting;

    [ObservableProperty]
    private string _applyText = Strings.StorageMoveButton;

    [ObservableProperty]
    private bool _canApply;

    /// <summary>The move is on: the choices wait, the bar fills</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isMoving;

    public bool IsIdle => !IsMoving;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "";

    /// <summary>The folder chosen for "a common folder" ("D:\Forza liveries"), remembered for the next account</summary>
    public string? ChosenCommonFolder => Option == StorageFolderOption.Common && Target is not null ? CommonFolder.Trim() : null;

    /// <summary>The program's folder is chosen: the storage goes back there (no folder of its own)</summary>
    public bool IsProgramChosen => Option == StorageFolderOption.Program;

    private string CommonTarget(string common) =>
        StoragePlaces.InCommonFolder(common, _context.GameFolderName,
            StoragePlaces.AccountFolderName(_context.AccountKey, _context.DisplayName, name => _context.IsNameTaken(common, name)));

    private void Refresh()
    {
        string? target = Option switch
        {
            StorageFolderOption.Program => _context.ProgramFolder,
            StorageFolderOption.Common => CommonFolder.Trim() is { Length: > 0 } common && Path.IsPathFullyQualified(common) ? CommonTarget(common) : null,
            _ => ExactFolder.Trim() is { Length: > 0 } exact ? exact : null,
        };
        Target = target;
        CommonResultText = Option == StorageFolderOption.Common && target is not null
            ? string.Format(Strings.StorageOptionCommonResultFormat, target)
            : "";

        // The program's folder is always right; any other is checked (in a common folder: the account's folder in it)
        var (problem, with) = (StoragePlaceProblem.None, (StorageForbidden?)null);
        var forbidden = _context.Forbidden.Append(new StorageForbidden(_context.CurrentFolder, StorageForbiddenKind.Current));
        if (Option == StorageFolderOption.Common && CommonFolder.Trim().Length > 0)
            (problem, with) = target is null ? (StoragePlaceProblem.NotAFullPath, null) : StoragePlaces.Check(target, forbidden);
        else if (Option == StorageFolderOption.Exact && target is not null)
            (problem, with) = StoragePlaces.Check(target, forbidden);
        ProblemText = problem == StoragePlaceProblem.None ? "" : Describe(problem, with);
        HasProblem = ProblemText.Length > 0;

        bool here = target is not null && StoragePlaces.Same(target, _context.CurrentFolder);
        SummaryText = target is null || HasProblem ? ""
            : here ? Strings.StorageAlreadyHere
            : MoveSummary(target);
        ExistingText = "";
        HasExisting = false;
        ApplyText = _context.Liveries == 0 || !_context.IsAvailable ? Strings.StorageChooseButton : Strings.StorageMoveButton;
        CanApply = target is not null && !HasProblem && !here && !IsMoving;
        // What the folder holds is looked at a moment after the typing stops
        _surveyVersion++;
        _surveyLater.Stop();
        if (CanApply && Option != StorageFolderOption.Program) _surveyLater.Start();
    }

    private static string Describe(StoragePlaceProblem problem, StorageForbidden? with)
    {
        string reason = StoragePlaces.Describe(problem, with);
        return problem is StoragePlaceProblem.InSave or StoragePlaceProblem.HoldsSave or StoragePlaceProblem.InProgram
            or StoragePlaceProblem.HoldsProgram or StoragePlaceProblem.OtherStorage
            ? $"{reason} {Strings.StoragePlaceRule}"
            : reason;
    }

    private string MoveSummary(string target)
    {
        if (!_context.IsAvailable) return Strings.StorageMoveUnreachable;
        if (_context.Liveries == 0) return Strings.StorageMoveNothing;
        string what = string.Format(Strings.StorageMoveSummaryFormat, StatisticsViewModel.LiveryCount(_context.Liveries),
            ByteSizeFormatter.Format(_context.Bytes));
        string how = StoragePlaces.SameDisk(target, _context.CurrentFolder)
            ? Strings.StorageMoveSameDisk
            : string.Format(Strings.StorageMoveOtherDiskFormat, StoragePlaces.CopyMinutes(_context.Bytes));
        return $"{what} {how} {Strings.StorageMoveKeeps}";
    }

    // Off the window's thread: the liveries the folder holds already
    private void SurveyNow()
    {
        _surveyLater.Stop();
        if (Target is not { } target || !CanApply) return;
        int version = _surveyVersion;
        var store = _context.Store;
        _ = Task.Run(() => StoragePlaces.Survey(target, store)).ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (version != _surveyVersion) return;
                ShowSurvey(t.Result);
            });
        }, TaskScheduler.Default);
    }

    private void ShowSurvey(StorageSurvey survey)
    {
        if (survey.Liveries == 0)
        {
            HasExisting = false;
            ExistingText = "";
            return;
        }
        var parts = new List<string>(3) { string.Format(Strings.StorageExistingFormat, StatisticsViewModel.LiveryCount(survey.Liveries)) };
        if (survey.InFolders > 0)
            parts.Add(string.Format(Strings.StorageExistingFoldersFormat, StatisticsViewModel.LiveryCount(survey.InFolders), string.Join(", ", survey.Folders)));
        if (_context.Liveries > 0 && _context.IsAvailable)
            parts.Add(string.Format(Strings.StorageExistingAddFormat, StatisticsViewModel.LiveryCount(_context.Liveries)));
        ExistingText = string.Join(" ", parts);
        HasExisting = true;
        if (_context.Liveries > 0 && _context.IsAvailable) ApplyText = Strings.StorageMoveAndAddButton;
    }

    /// <summary>How far the move is</summary>
    public void Report(LiveryArchiveMoveProgress progress)
    {
        Progress = progress.TotalBytes > 0 ? (double)progress.DoneBytes / progress.TotalBytes : progress.TotalLiveries > 0 ? (double)progress.DoneLiveries / progress.TotalLiveries : 0;
        ProgressText = string.Format(Strings.StorageMovingFormat, ByteSizeFormatter.Format(progress.DoneBytes), ByteSizeFormatter.Format(progress.TotalBytes));
    }

    public void MoveStarted()
    {
        IsMoving = true;
        CanApply = false;
        Progress = 0;
        ProgressText = "";
    }

    public void MoveEnded()
    {
        IsMoving = false;
        Refresh();
    }

    /// <summary>A long path shown by its end: the program's folder is deep in the user's profile</summary>
    private static string Shorten(string path)
    {
        const int Most = 64;
        if (path.Length <= Most) return path;
        string tail = path[^Most..];
        int cut = tail.IndexOf(Path.DirectorySeparatorChar);
        return "…" + (cut > 0 ? tail[cut..] : tail);
    }
}
