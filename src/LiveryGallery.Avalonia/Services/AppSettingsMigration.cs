using System.Text.Json;
using System.Text.Json.Nodes;
using ForzaToolkit.AppHost;

namespace LiveryGallery.Services;

internal static class AppSettingsMigration
{
    /// <param name="data">The settings as read</param>
    /// <param name="fileText">The file they were read from: 1.6.0's game settings are taken from it</param>
    public static bool Apply(AppSettingsData data, string? fileText = null)
    {
        bool changed = false;

        // DarkTheme (<= 1.1.0) → ThemeMode
        if (data.ThemeMode is null)
        {
            data.ThemeMode = data.DarkTheme ? AppThemeMode.Dark : AppThemeMode.Light;
            changed = true;
        }

        changed |= ToVersion2(data, fileText);
        return changed;
    }

    /// <summary>
    /// 1.6.0 and earlier: one game, its settings at the top of the file. They are read as FH6's (<c>Games["fh6"]</c>):
    /// their names are those of <see cref="GameSettingsData"/>, so the file's own text is read into it
    /// </summary>
    private static bool ToVersion2(AppSettingsData data, string? fileText)
    {
        if (data.SettingsVersion >= AppSettingsData.CurrentVersion) return false;

        // The game properties of AppSettingsData stand for the current game's and are not read from the top of the
        // file, so 1.6.0's are read from its text
        var names = typeof(GameSettingsData).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        JsonObject? file = null;
        try
        {
            file = fileText is null ? null : JsonNode.Parse(fileText) as JsonObject;
        }
        catch (JsonException)
        {
            // Read as settings already, so this does not happen; the game's settings start as the defaults then
        }
        if (file is not null && !data.Games.ContainsKey(AppSettingsData.DefaultGameId))
        {
            var game = new JsonObject();
            foreach (var (name, value) in file)
                if (names.Contains(name)) game[name] = value?.DeepClone();
            if (game.Count > 0)
                data.Games[AppSettingsData.DefaultGameId] = game.Deserialize<GameSettingsData>(AppJson.Indented) ?? new GameSettingsData();
        }
        // Not kept as unknown: they are in Games now
        if (data.Unknown is { } unknown)
        {
            foreach (string name in unknown.Keys.Where(names.Contains).ToList()) unknown.Remove(name);
            if (unknown.Count == 0) data.Unknown = null;
        }
        data.LastGame ??= AppSettingsData.DefaultGameId;
        data.SettingsVersion = AppSettingsData.CurrentVersion;
        return true;
    }
}
