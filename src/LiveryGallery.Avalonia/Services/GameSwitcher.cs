using LiveryGallery.Profiles;

namespace LiveryGallery.Services;

/// <summary>What the gallery knows about a game's saves on this PC</summary>
internal enum GameSaveState
{
    /// <summary>The gallery found the game's saves itself</summary>
    Found,

    /// <summary>The user chose the save folder, and it is there</summary>
    Chosen,

    /// <summary>No save of the game was found, and none was chosen</summary>
    NotFound,
}

/// <summary>A game in the switcher: every supported game is listed, found or not</summary>
/// <param name="Id">The game (ForzaToolkit.Games id)</param>
/// <param name="Name">Its name</param>
/// <param name="State">What is known about its saves</param>
/// <param name="SaveCount">How many save folders were found</param>
internal sealed record GameChoice(string Id, string Name, GameSaveState State, int SaveCount);

/// <summary>The games the switcher lists, with the state of their saves</summary>
internal static class GameChoices
{
    /// <summary>Each profile with its saves: looks at the disk, so off the UI thread</summary>
    public static IReadOnlyList<GameChoice> Of(IEnumerable<GameProfile> profiles, AppSettingsData settings)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(settings);
        var choices = new List<GameChoice>();
        foreach (var profile in profiles)
        {
            int found = 0;
            try
            {
                found = profile.Saves.FindAllSaveFolders().Count;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.LogErrorThrottled("games|" + profile.Game.Id, $"Failed to look for the saves of {profile.Game.Name}", ex);
            }
            string? chosen = settings.Games.TryGetValue(profile.Game.Id, out var game) ? game.SavePath : null;
            var state = found > 0 ? GameSaveState.Found
                : chosen is { Length: > 0 } && Directory.Exists(chosen) ? GameSaveState.Chosen
                : GameSaveState.NotFound;
            choices.Add(new GameChoice(profile.Game.Id, profile.Game.Name, state, found));
        }
        return choices;
    }
}

/// <summary>
/// The game switcher of the main window's title bar: the games the program supports and the switch to one. The
/// switch itself is the host's: it builds the other game's services and window
/// </summary>
/// <param name="currentGameId">The game the window shows</param>
/// <param name="list">The supported games with their saves (looks at the disk)</param>
/// <param name="switchTo">Opens the other game's window; called once this window has shut down</param>
internal sealed class GameSwitcher(string currentGameId, Func<IReadOnlyList<GameChoice>> list, Func<string, Task> switchTo)
{
    public string CurrentGameId { get; } = currentGameId;

    public Task<IReadOnlyList<GameChoice>> ListAsync() => Task.Run(list);

    public Task SwitchToAsync(string gameId) => switchTo(gameId);

    public bool IsCurrent(string gameId) => string.Equals(gameId, CurrentGameId, StringComparison.OrdinalIgnoreCase);
}
