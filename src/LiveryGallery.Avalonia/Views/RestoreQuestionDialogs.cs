using Avalonia.Controls;
using ForzaToolkit.UI.Dialogs;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>The questions of a restore (<see cref="RestoreQuestions"/>) as dialogs over a window: the same in every window</summary>
internal static class RestoreQuestionDialogs
{
    /// <summary>Asks <paramref name="ask"/>'s questions over <paramref name="owner"/></summary>
    /// <param name="gameName">The game's name, for "Forza Horizon 6 is running"</param>
    public static void AskOver(this RestoreQuestions ask, Window owner, string gameName)
    {
        ask.Duplicates = message => ConfirmDialog.AskAsync(
            owner, Strings.RestoreDuplicateTitle, message, Strings.RestoreAnywayButton, Strings.RestoreSkipDuplicatesButton);
        ask.IntoCurrentAccount = message => ConfirmDialog.AskAsync(
            owner, Strings.RestoreFromOtherAccountTitle, message, Strings.RestoreIntoCurrentAccountButton, Strings.ButtonCancel);
        ask.WhileGameRunning = () => ConfirmDialog.AskAsync(
            owner, string.Format(Strings.GameRunningTitleFormat, gameName), Strings.GameRunningFileChangeMessage,
            Strings.GameRunningContinueButton, Strings.ButtonCancel);
    }
}
