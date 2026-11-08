using Avalonia.Threading;

namespace LiveryGallery.Controller;

/// <summary>
/// Watches the folder of the storage shown (docs/storage.md, "Own folder"): a livery the user puts there by hand, or
/// takes away, is looked for a moment after the folder last changed, as a scan follows the save. While the folder is
/// not there (a disk not plugged in), it is looked for every few seconds, so the storage comes back by itself
/// </summary>
internal sealed class StorageFolderWatcher : IDisposable
{
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LookAgain = TimeSpan.FromSeconds(5);

    private readonly Func<Task> _look;
    private readonly Func<Task> _lookForFolder;
    private readonly DispatcherTimer _quiet = new() { Interval = Quiet };
    private readonly DispatcherTimer _again = new() { Interval = LookAgain };
    private FileSystemWatcher? _watcher;
    private string? _folder;
    private bool _running, _changedWhileRunning, _disposed;

    /// <param name="look">Looks into the folder (liveries put there or taken away)</param>
    /// <param name="lookForFolder">Looks whether a folder that was not there is back</param>
    public StorageFolderWatcher(Func<Task> look, Func<Task> lookForFolder)
    {
        _look = look;
        _lookForFolder = lookForFolder;
        _quiet.Tick += (_, _) =>
        {
            _quiet.Stop();
            _ = RunAsync(_look);
        };
        _again.Tick += (_, _) => _ = RunAsync(_lookForFolder);
    }

    /// <summary>
    /// Watches <paramref name="folder"/> (null: nothing); <paramref name="missing"/> looks for the storage's folder
    /// every few seconds instead
    /// </summary>
    public void Configure(string? folder, bool missing)
    {
        if (_disposed) return;
        if (missing) _again.Start();
        else _again.Stop();
        if (string.Equals(folder, _folder, StringComparison.OrdinalIgnoreCase) && (folder is null || _watcher is not null)) return;
        Stop();
        _folder = folder;
        if (folder is null || !Directory.Exists(folder)) return;
        try
        {
            _watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Created += OnEvent;
            _watcher.Deleted += OnEvent;
            _watcher.Renamed += (s, e) => OnEvent(s, e);
            _watcher.Error += (_, e) =>
            {
                AppLog.LogErrorThrottled("storage-watcher", $"The watch of the storage folder '{folder}' failed", e.GetException());
                Dispatcher.UIThread.Post(() => _ = RunAsync(_lookForFolder));
            };
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            AppLog.LogErrorThrottled("storage-watcher", $"Failed to watch the storage folder '{folder}'", ex);
            _watcher = null;
        }
    }

    private void OnEvent(object? sender, FileSystemEventArgs e)
    {
        // The storage's own lists and a copy in progress tell nothing new
        string name = Path.GetFileName(e.FullPath);
        if (name.StartsWith('.') || name is LiveryArchive.IndexFileName or LiveryArchive.FoldersFileName
            || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            if (_running) _changedWhileRunning = true;
            _quiet.Stop();
            _quiet.Start();
        });
    }

    private async Task RunAsync(Func<Task> what)
    {
        if (_running || _disposed) return;
        _running = true;
        try
        {
            await what();
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to look into the storage folder", ex);
        }
        finally
        {
            _running = false;
        }
        if (_changedWhileRunning && !_disposed)
        {
            _changedWhileRunning = false;
            _quiet.Start();
        }
    }

    private void Stop()
    {
        _quiet.Stop();
        if (_watcher is null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _again.Stop();
        Stop();
    }
}
