namespace LiveryGallery.ViewModels;

/// <summary>
/// What putting liveries back into the save may have to ask (the archive, a backup, the storage's return to the game):
/// a copy of one in the game; liveries of another account; the game running. One set for every place that restores,
/// asked by the window it happens in (Views/RestoreQuestionDialogs.cs). Nothing set: nothing asked, as "no"
/// </summary>
internal sealed class RestoreQuestions
{
    /// <summary>A livery to restore is a copy of one in the game: true to restore it anyway</summary>
    public Func<string, Task<bool>>? Duplicates { get; set; }

    /// <summary>Liveries to restore came from another account: true to bring them into this one</summary>
    public Func<string, Task<bool>>? IntoCurrentAccount { get; set; }

    /// <summary>The game is running: true to change its files anyway</summary>
    public Func<Task<bool>>? WhileGameRunning { get; set; }
}
