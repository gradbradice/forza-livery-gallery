using ForzaToolkit.Core.IO;
using ForzaToolkit.Liveries.UserData;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>
/// The files of the user's own data (tags, notes, favourites, locks, livery numbers) that did not read at start: said
/// once, as the window opens. One another program held is left as it is and not written over until the next start
/// (ForzaToolkit's JsonFiles.Read); a broken one was kept aside as a dated copy and started again
/// </summary>
internal partial class MainWindow
{
    /// <summary>The files of the user's data that did not read when the game's services started (GameSessions)</summary>
    public IReadOnlyList<UserDataLoadProblem> UserDataProblems { get; init; } = [];

    /// <summary>What to tell the user of <paramref name="problems"/>; null when there is nothing to tell</summary>
    internal static string? UserDataProblemText(IReadOnlyList<UserDataLoadProblem> problems)
    {
        var parts = new List<string>();
        var held = problems.Where(p => p.ChangesNotSaved).Select(p => p.FilePath).ToList();
        if (held.Count > 0) parts.Add(string.Format(Strings.Culture, Strings.UserDataUnreadableMessageFormat, string.Join("\n", held)));
        var broken = problems.Where(p => p.Status == JsonFileReadStatus.SetAside)
            .Select(p => p.CopyPath is { } copy ? $"{p.FilePath}\n→ {copy}" : p.FilePath).ToList();
        if (broken.Count > 0) parts.Add(string.Format(Strings.Culture, Strings.UserDataSetAsideMessageFormat, string.Join("\n", broken)));
        return parts.Count > 0 ? string.Join("\n\n", parts) : null;
    }

    // In the notices' bar, with a button to the file (the first one: the others are named in the text)
    private void ShowUserDataProblems()
    {
        if (UserDataProblemText(UserDataProblems) is not { } text) return;
        string file = UserDataProblems[0].CopyPath ?? UserDataProblems[0].FilePath;
        _mainViewModel.Notices.Add(NoticeKind.Warning, Strings.UserDataProblemTitle, text,
            new NoticeAction(Strings.NoticeShowFile, () => Services.Diagnostics.ExplorerLauncher.ShowInFolder(file)));
    }
}
