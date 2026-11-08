using Avalonia.Controls;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

/// <summary>Asks the user for the save folder of the profile's game</summary>
internal sealed class SavePathPrompter(Window owner, GameProfile profile) : ISavePathPrompter
{
    public async Task<string?> PromptForSavePathAsync(bool initial, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested) return null;
        if (initial)
        {
            bool yes = await ConfirmDialog.AskAsync(owner, Strings.FolderNotFoundTitle,
                FolderNotFoundMessage(Strings.FolderNotFoundMessageFormat, profile.Game));
            if (!yes || ct.IsCancellationRequested) return null;
        }

        return await BrowseAsync(ct);
    }

    /// <summary>
    /// "The save folder of {0} was not found", with the usual path of the saves on its last line: the game id of the
    /// save folder goes in as {1}. A game whose saves are not in a PC GameSave folder has no such path: the line goes
    /// </summary>
    internal static string FolderNotFoundMessage(string format, ForzaGame game)
    {
        if (game.GetSaveLocation<PcGameSaveLocation>()?.GameId is { } id) return string.Format(format, game.Name, id);
        string text = format.TrimEnd();
        int lastLine = text.LastIndexOf('\n');
        if (lastLine >= 0 && text.AsSpan(lastLine).Contains("{1}", StringComparison.Ordinal)) text = text[..lastLine].TrimEnd();
        return string.Format(text, game.Name);
    }

    private Task<string?> BrowseAsync(CancellationToken ct) =>
        FolderPicker.PickValidAsync(owner, Strings.SelectFolderDialogTitle, profile.Saves.IsValidSaveFolder,
            () => ConfirmDialog.AskAsync(owner, Strings.SaveFolderValidationFailedTitle, Strings.SaveFolderValidationFailed,
                yesText: Strings.ButtonRetry, noText: Strings.ButtonCancel), ct);
}
