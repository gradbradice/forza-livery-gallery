using Avalonia.Threading;
using LiveryGallery.Services;

namespace LiveryGallery.Controller;

internal sealed class SaveFolderWatcher : IDisposable
{
    private const string ContainersRootName = "ContainersRoot";
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FallbackPollInterval = TimeSpan.FromSeconds(30);
    private static readonly char[] PathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
    private readonly Func<CancellationToken, Task<bool>> _refreshAsync;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private Task _currentRefresh = Task.CompletedTask;
    private readonly DispatcherTimer _debounceTimer = new() { Interval = DebounceInterval };
    private readonly DispatcherTimer _fallbackTimer = new() { Interval = FallbackPollInterval };
    private FileSystemWatcher? _saveWatcher;
    private FileSystemWatcher? _thumbnailCacheWatcher;
    private string? _savePath;
    private readonly GameProfile _profile;
    private bool _enabled;
    private bool _refreshRunning;
    private bool _changedWhileRefreshing;
    private bool _disposed;

    /// <param name="refreshAsync">Rescans the save</param>
    /// <param name="profile">The game: which names in the save are liveries, and its cache of listed liveries' previews</param>
    public SaveFolderWatcher(Func<CancellationToken, Task<bool>> refreshAsync, GameProfile profile)
    {
        _refreshAsync = refreshAsync;
        _profile = profile;
        _debounceTimer.Tick += (_, __) =>
        {
            _debounceTimer.Stop();
            if (!_refreshRunning) _currentRefresh = RunRefreshAsync(_lifetimeCts.Token);
        };
        _fallbackTimer.Tick += (_, __) =>
        {
            WatchIfAppeared();
            RequestRefresh();
        };
    }

    public void Configure(string? savePath, bool enabled)
    {
        if (_disposed) return;
        bool unchanged = enabled == _enabled
            && string.Equals(savePath, _savePath, StringComparison.OrdinalIgnoreCase)
            && (!enabled || _saveWatcher is not null);
        if (unchanged) return;

        StopWatching();
        _enabled = enabled;
        _savePath = savePath;
        if (!enabled) return;

        _saveWatcher = TryCreateWatcher(savePath, filter: null, includeSubdirectories: true, OnSaveFolderEvent);
        if (_profile.ListingPreviews is { } cache)
            _thumbnailCacheWatcher = TryCreateWatcher(cache.FindDirectory(), cache.IndexFileName,
                includeSubdirectories: false, OnThumbnailCacheEvent);

        if (_saveWatcher is null) _fallbackTimer.Start();
    }

    /// <summary>
    /// The save folder was missing, or could not be watched, when the watching began (the game not run yet, OneDrive not
    /// synced, a drive not plugged in): tried again on each poll, and once it is watched, the poll stops. Before, the
    /// gallery polled every 30 seconds until it was started again (code review, finding 16). The game's preview cache,
    /// when missing, is tried again on each poll and refresh. True when the save folder is watched now
    /// </summary>
    internal bool WatchIfAppeared()
    {
        if (_disposed || !_enabled) return false;
        if (_thumbnailCacheWatcher is null && _profile.ListingPreviews is { } cache)
            _thumbnailCacheWatcher = TryCreateWatcher(cache.FindDirectory(), cache.IndexFileName,
                includeSubdirectories: false, OnThumbnailCacheEvent);
        if (_saveWatcher is not null) return true;
        _saveWatcher = TryCreateWatcher(_savePath, filter: null, includeSubdirectories: true, OnSaveFolderEvent);
        if (_saveWatcher is null) return false;
        _fallbackTimer.Stop();
        return true;
    }

    /// <summary>The save folder is watched (tests)</summary>
    internal bool IsWatching => _saveWatcher is not null;

    /// <summary>The save folder is polled instead (tests)</summary>
    internal bool IsPolling => _fallbackTimer.IsEnabled;

    private FileSystemWatcher? TryCreateWatcher(
        string? directory, string? filter, bool includeSubdirectories, FileSystemEventHandler onEvent)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;
        try
        {
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = includeSubdirectories,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            if (filter is not null) watcher.Filter = filter;
            watcher.Created += onEvent;
            watcher.Deleted += onEvent;
            watcher.Changed += onEvent;
            watcher.Renamed += (sender, e) => onEvent(sender, e);
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex)
        {
            AppLog.LogErrorThrottled(directory, $"Failed to watch '{directory}' for changes", ex);
            return null;
        }
    }

    private void OnSaveFolderEvent(object sender, FileSystemEventArgs e)
    {
        bool relevant = IsRelevantSavePath(e.FullPath)
            || (e is RenamedEventArgs renamed && IsRelevantSavePath(renamed.OldFullPath));
        if (relevant) Dispatcher.UIThread.Post(RequestRefresh);
        bool profile = IsProfileData(e.FullPath) || (e is RenamedEventArgs r && IsProfileData(r.OldFullPath));
        if (profile) Dispatcher.UIThread.Post(() => { if (!_disposed && _enabled) ProfileChanged?.Invoke(); });
    }

    public event Action? ProfileChanged;

    // Only <containerId>\ContainersRoot\User_<hex>\C_ProfileData counts as the profile (FH6's: the livery server's)
    private bool IsProfileData(string? path)
    {
        if (path is null || _savePath is null || _profile.SaveLayout != GameSaveLayout.LiveryFolders) return false;
        if (!string.Equals(Path.GetFileName(path), ProfileDataLocator.ProfileDataFileName, StringComparison.OrdinalIgnoreCase))
            return false;

        string relative;
        try { relative = Path.GetRelativePath(_savePath, path); }
        catch (ArgumentException) { return false; } // not a path below the save folder

        var parts = relative.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4
            && parts[0].All(char.IsDigit)
            && string.Equals(parts[1], ContainersRootName, StringComparison.OrdinalIgnoreCase)
            && parts[2].StartsWith(ProfileDataLocator.ProfileUserFolderPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private void OnThumbnailCacheEvent(object sender, FileSystemEventArgs e) => Dispatcher.UIThread.Post(RequestRefresh);

    internal bool IsRelevantSavePath(string? fullPath)
    {
        if (fullPath is null || _savePath is null) return false;

        string relative;
        try { relative = Path.GetRelativePath(_savePath, fullPath); }
        catch (ArgumentException) { return false; } // not a path below the save folder

        var parts = relative.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        // FH3–FH5: a livery's own file next to the others (Livery_….header), or the containers' index the game rewrites
        if (_profile.SaveLayout == GameSaveLayout.SaveItems)
            return parts.Length == 1
                && (string.Equals(parts[0], ForzaToolkit.Saves.WgsContainerIndex.FileName, StringComparison.OrdinalIgnoreCase)
                    || _profile.Liveries.IsLiveryKey(parts[0].Split('.')[0]));
        return parts.Length switch
        {
            0 => false,
            1 => parts[0].All(char.IsDigit),
            2 => string.Equals(parts[1], ContainersRootName, StringComparison.OrdinalIgnoreCase),
            _ => _profile.Liveries.IsLiveryKey(parts[2]),
        };
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        AppLog.LogErrorThrottled("SaveFolderWatcher.Error",
            "The save folder watcher reported an error (events may have been lost) — restarting it",
            e.GetException());
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            string? path = _savePath;
            bool enabled = _enabled;
            StopWatching();
            _savePath = null; // forces Configure to rebuild the watchers
            Configure(path, enabled);
            RequestRefresh();
        });
    }

    private void RequestRefresh()
    {
        if (_disposed || !_enabled) return;
        if (_refreshRunning)
        {
            _changedWhileRefreshing = true;
            return;
        }
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async Task RunRefreshAsync(CancellationToken ct)
    {
        if (_disposed || !_enabled || _refreshRunning || ct.IsCancellationRequested) return;

        _refreshRunning = true;
        _changedWhileRefreshing = false;
        if (_thumbnailCacheWatcher is null) WatchIfAppeared();
        bool completed;
        try
        {
            completed = await _refreshAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return; // stopped, nothing to repeat
        }
        catch (Exception ex)
        {
            AppLog.LogError("Automatic refresh after a save folder change failed", ex);
            completed = true;
        }
        finally
        {
            _refreshRunning = false;
        }
        if (ct.IsCancellationRequested) return;

        // Busy or more changes arrived meanwhile
        // go again once things settle
        if (!completed || _changedWhileRefreshing)
        {
            _changedWhileRefreshing = false;
            RequestRefresh();
        }
    }

    private void StopWatching()
    {
        _debounceTimer.Stop();
        _fallbackTimer.Stop();
        DisposeWatcher(ref _saveWatcher);
        DisposeWatcher(ref _thumbnailCacheWatcher);
    }

    private static void DisposeWatcher(ref FileSystemWatcher? watcher)
    {
        if (watcher is null) return;
        try
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to dispose a file system watcher", ex);
        }
        watcher = null;
    }

    public async Task StopAsync()
    {
        Dispose();
        await _currentRefresh; // never faults
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetimeCts.Cancel();
        StopWatching();
    }
}
