using LiveryGallery.Configuration;
using LiveryGallery.Enums;

namespace LiveryGallery.Services;

internal static class AppSettingsService
{
    // ForzaToolkit.AppHost: a broken file is kept as .corrupted, writes are atomic (now or debounced)
    private static readonly JsonSettingsFile<AppSettingsData> File = new(Path.Combine(AppSettings.BaseCachePath, "settings.json"));

    /// <summary>Writes the settings a moment after the last of several changes</summary>
    public static void Save(AppSettingsData data) => File.Schedule(data);

    public static Task<bool> SaveImmediateAsync(AppSettingsData data) => File.SaveNowAsync(data);

    private static bool NormalizeEnums(AppSettingsData data)
    {
        bool changed = false;
        if (!Enum.IsDefined(data.SortMode)) { data.SortMode = SortMode.Manufacture; changed = true; }
        if (!Enum.IsDefined(data.FavoriteMode)) { data.FavoriteMode = FavoriteMode.None; changed = true; }
        if (!Enum.IsDefined(data.MineMode)) { data.MineMode = MineMode.None; changed = true; }
        if (!Enum.IsDefined(data.DuplicatesFilterMode)) { data.DuplicatesFilterMode = DuplicatesFilterMode.All; changed = true; }
        if (!Enum.IsDefined(data.GeneratedFilterMode)) { data.GeneratedFilterMode = GeneratedFilterMode.All; changed = true; }
        if (data.ThemeMode is { } theme && !Enum.IsDefined(theme)) { data.ThemeMode = AppThemeMode.System; changed = true; }
        if (!Enum.IsDefined(data.MyDesignsRowsMode)) { data.MyDesignsRowsMode = MyDesignsRowsMode.Auto; changed = true; }
        if (data.ServerLiveries is < 0 or > MyDesignsGrid.MaxServerLiveries)
        {
            data.ServerLiveries = Math.Clamp(data.ServerLiveries, 0, MyDesignsGrid.MaxServerLiveries);
            changed = true;
        }
        return changed;
    }

    private static string? ReadText()
    {
        try
        {
            return System.IO.File.ReadAllText(File.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogError("Failed to read the settings file for its game settings", ex);
            return null;
        }
    }

    public static AppSettingsData Load()
    {
        if (File.TryLoad() is { } data)
        {
            bool changed = AppSettingsMigration.Apply(data, data.SettingsVersion < AppSettingsData.CurrentVersion ? ReadText() : null);
            changed |= NormalizeEnums(data);
            if (changed) Save(data);
            return data;
        }

        return new AppSettingsData
        {
            SettingsVersion = AppSettingsData.CurrentVersion,
            Language = AppLanguages.System(),
            ThemeMode = AppThemeMode.System
        };
    }
}
