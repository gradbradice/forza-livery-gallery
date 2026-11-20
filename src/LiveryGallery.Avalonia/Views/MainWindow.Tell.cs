using Avalonia.Controls;
using ForzaToolkit.UI.Dialogs;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>
/// How the main window tells the user something without a dialog to close (docs/plans.md, «Упрощение»): why what was
/// asked cannot be done goes in a short message over the gallery (<see cref="Tell"/>); a failure to keep in view goes in
/// the bar of notices and under the bell (<see cref="Warn"/>). Over another window of the gallery (the livery window, the
/// settings) neither would be seen: there it is a dialog of that window, as before. Questions stay dialogs
/// </summary>
internal partial class MainWindow
{
    /// <summary>Why it cannot be done, for a moment</summary>
    private Task Tell(string text, Window? owner = null, string? title = null)
    {
        if (owner is not null && !ReferenceEquals(owner, this))
            return InfoDialog.ShowAsync(owner, title ?? Configuration.AppSettings.Identity.Name, text);
        _mainViewModel.Notices.ShowToast(text);
        return Task.CompletedTask;
    }

    /// <summary>Something failed: in the bar of notices until closed, and under the bell</summary>
    private Task Warn(string text, Window? owner = null, string? title = null)
    {
        if (owner is not null && !ReferenceEquals(owner, this))
            return InfoDialog.ShowAsync(owner, title ?? Configuration.AppSettings.Identity.Name, text);
        _mainViewModel.Notices.Add(NoticeKind.Warning, title ?? Strings.NoticeProblemTitle, text);
        return Task.CompletedTask;
    }
}
