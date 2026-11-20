using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;

namespace LiveryGallery.ViewModels;

/// <summary>How important a notice is: a warning (amber) or a piece of news</summary>
internal enum NoticeKind
{
    Info,
    Warning,
}

/// <summary>A button of a notice: its words and what it does</summary>
internal sealed class NoticeAction(string label, Action run)
{
    public string Label { get; } = label;

    public IRelayCommand Command { get; } = new RelayCommand(run);
}

/// <summary>One notice of this launch: shown in the bar over the gallery until closed, kept under the bell</summary>
internal sealed partial class Notice(NoticeKind kind, string title, string text, IReadOnlyList<NoticeAction> actions) : ObservableObject
{
    public NoticeKind Kind { get; } = kind;

    public string Title { get; } = title;

    public string Text { get; } = text;

    public IReadOnlyList<NoticeAction> Actions { get; } = actions;

    public bool IsWarning => Kind == NoticeKind.Warning;

    /// <summary>Closed in the bar (still under the bell)</summary>
    [ObservableProperty]
    private bool _isDismissed;
}

/// <summary>
/// A short message after an action on liveries (moved, tags given to many), with "Undo" while it shows; it goes by itself
/// after <see cref="NoticesViewModel.ToastTime"/>
/// </summary>
internal sealed partial class Toast(string text, string? detail, Action? undo) : ObservableObject
{
    public string Text { get; } = text;

    public string? Detail { get; } = detail;

    public bool CanUndo => undo is not null;

    public void Undo() => undo?.Invoke();

    /// <summary>The time left, from 1 to 0 (the line under it)</summary>
    [ObservableProperty]
    private double _timeLeft = 1;
}

/// <summary>
/// The notices of the gallery without windows over it (docs/plans.md, «Уведомления»): what needs telling at start goes
/// to a bar over the gallery, one at a time ("1 of 2"), and the gallery works at once; all of this launch's notices stay
/// under the bell in the title bar. A short message with "Undo" follows an action on liveries
/// </summary>
internal sealed partial class NoticesViewModel : ObservableObject
{
    /// <summary>How long a short message shows</summary>
    public static readonly TimeSpan ToastTime = TimeSpan.FromSeconds(8);

    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private DateTime _toastShownUtc;
    private TimeSpan _toastTime = ToastTime;

    /// <summary>How long a message stays: 8 seconds, and a little more for a long one (about 15 characters a second)</summary>
    internal static TimeSpan TimeFor(string text) =>
        TimeSpan.FromSeconds(Math.Clamp(text.Length / 15.0, ToastTime.TotalSeconds, 20));

    public NoticesViewModel() => _toastTimer.Tick += (_, _) => TickToast();

    /// <summary>
    /// The most notices the bell keeps: a long session told of many things kept them all, each with its buttons' actions
    /// (and what those hold). The oldest closed ones go first; open ones only when there are more than this
    /// </summary>
    public const int MostKept = 50;

    /// <summary>This launch's notices, the newest first (at most <see cref="MostKept"/>)</summary>
    public ObservableCollection<Notice> All { get; } = [];

    /// <summary>The notice in the bar; null when the bar is hidden</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBanner), nameof(BannerCounter), nameof(HasMoreThanOne))]
    private Notice? _banner;

    public bool HasBanner => Banner is not null;

    private List<Notice> Open => [.. All.Where(n => !n.IsDismissed).Reverse()];

    /// <summary>"1 of 2" when more than one waits</summary>
    public string BannerCounter => Banner is { } shown && Open is { Count: > 1 } open
        ? string.Format(CultureInfo.CurrentCulture, Strings.NoticeCounterFormat, open.IndexOf(shown) + 1, open.Count)
        : "";

    public bool HasMoreThanOne => Open.Count > 1;

    /// <summary>The notices not closed yet (the bell's number)</summary>
    public int OpenCount => Open.Count;

    public bool HasAny => All.Count > 0;

    /// <summary>Adds a notice; the bar shows it when nothing else is shown</summary>
    public Notice Add(NoticeKind kind, string title, string text, params NoticeAction[] actions)
    {
        var notice = new Notice(kind, title, text, actions);
        All.Insert(0, notice);
        while (All.Count > MostKept)
        {
            int oldestClosed = -1;
            for (int i = All.Count - 1; i > 0 && oldestClosed < 0; i--)
                if (All[i].IsDismissed) oldestClosed = i;
            var dropped = All[oldestClosed >= 0 ? oldestClosed : All.Count - 1];
            All.Remove(dropped);
            if (ReferenceEquals(Banner, dropped)) Banner = Open.FirstOrDefault();
        }
        Banner ??= notice;
        Changed();
        return notice;
    }

    /// <summary>Closes the notice in the bar; the next open one shows</summary>
    [RelayCommand]
    public void Dismiss()
    {
        if (Banner is not { } shown) return;
        shown.IsDismissed = true;
        Banner = Open.FirstOrDefault();
        Changed();
    }

    /// <summary>The next open notice in the bar</summary>
    [RelayCommand]
    public void Next()
    {
        var open = Open;
        if (Banner is not { } shown || open.Count < 2) return;
        Banner = open[(open.IndexOf(shown) + 1) % open.Count];
        Changed();
    }

    /// <summary>Shows a notice of the bell's list in the bar again</summary>
    public void Show(Notice notice)
    {
        notice.IsDismissed = false;
        Banner = notice;
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(BannerCounter));
        OnPropertyChanged(nameof(HasMoreThanOne));
        OnPropertyChanged(nameof(OpenCount));
        OnPropertyChanged(nameof(HasAny));
    }

    /// <summary>The short message shown; null when none</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasToast))]
    private Toast? _toast;

    public bool HasToast => Toast is not null;

    /// <summary>Shows a short message (a newer one takes its place)</summary>
    public void ShowToast(string text, string? detail = null, Action? undo = null)
    {
        Toast = new Toast(text, detail, undo);
        _toastTime = TimeFor(text);
        _toastShownUtc = DateTime.UtcNow;
        _toastTimer.Start();
    }

    /// <summary>"Undo" on the message: undone, and the message goes</summary>
    [RelayCommand]
    public void UndoToast()
    {
        var toast = Toast;
        CloseToast();
        toast?.Undo();
    }

    [RelayCommand]
    public void CloseToast()
    {
        _toastTimer.Stop();
        Toast = null;
    }

    /// <summary>The pointer over the message holds it: its time starts again when the pointer leaves</summary>
    public void HoldToast(bool hold)
    {
        if (Toast is null) return;
        if (hold) _toastTimer.Stop();
        else
        {
            _toastShownUtc = DateTime.UtcNow;
            Toast.TimeLeft = 1;
            _toastTimer.Start();
        }
    }

    private void TickToast()
    {
        if (Toast is not { } toast)
        {
            _toastTimer.Stop();
            return;
        }
        double left = 1 - (DateTime.UtcNow - _toastShownUtc) / _toastTime;
        if (left <= 0) CloseToast();
        else toast.TimeLeft = left;
    }
}
