
namespace LiveryGallery.Services;

/// <summary>The save folder the gallery shows, of the game in <see cref="Profile"/></summary>
internal sealed class SavePathService(AppSettingsData settings, GameProfile profile)
{
    /// <summary>The game whose save this is</summary>
    public GameProfile Profile => profile;

    public string? SavePath { get; private set; }
    private bool _lostNotified;

    /// <summary>Whose the save shown is (its XUID, as the save bears it out); null when it cannot be told</summary>
    public ulong? CurrentUserId
    {
        get
        {
            if (SavePath is null)
            {
                AppLog.LogErrorThrottled("CurrentIdentity.NoSavePath",
                    "SavePathService.CurrentUserId: SavePath has not been set yet. The current UserId cannot be determined.",
                    new InvalidOperationException("SavePath is null"));
                return null;
            }

            int? containerId = settings.LastKnownContainerSavePath == SavePath ? settings.LastKnownContainerId : null;
            var userId = profile.Saves.ResolveUserId(SavePath, containerId);
            if (userId is null)
            {
                AppLog.LogErrorThrottled("CurrentIdentity.Unresolved",
                    $"SavePathService.CurrentUserId: Failed to tell whose the save '{Path.GetFileName(SavePath)}' is from its folder name or its files. " +
                    $"The MINE badge will not be shown for any livery.",
                    new InvalidOperationException("ResolveUserId returned null"));
            }
            return userId;
        }
    }

    public bool WasSavedPathMissing =>
        !string.IsNullOrWhiteSpace(settings.SavePath) && !Directory.Exists(settings.SavePath);

    public void InitializeFromSettings()
    {
        SavePath = !string.IsNullOrWhiteSpace(settings.SavePath) && Directory.Exists(settings.SavePath)
            ? settings.SavePath
            : profile.Saves.FindMostRecentSaveFolder();
    }

    public void SyncFromSettings() => SavePath = settings.SavePath;

    public string? ResolveSaveDataPath()
    {
        if (SavePath is null) return null;

        var root = profile.Saves.FindDataRoot(SavePath);
        string? path = root?.Path;
        int? containerId = root?.ContainerId;
        if (containerId is not null
            && (containerId != settings.LastKnownContainerId || settings.LastKnownContainerSavePath != SavePath))
        {
            settings.LastKnownContainerId = containerId;
            settings.LastKnownContainerSavePath = SavePath;
            AppSettingsService.Save(settings);
        }
        return path;
    }

    public void SetSavePath(string path)
    {
        SavePath = path;
        settings.SavePath = path;
        AppSettingsService.Save(settings);
    }

    public bool ShouldNotifyLost()
    {
        if (_lostNotified) return false;
        _lostNotified = true;
        return true;
    }

    public void ResetLostNotification() => _lostNotified = false;
}