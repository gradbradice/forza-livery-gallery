using Avalonia.Threading;
using ForzaToolkit.Games.Installation;
using ForzaToolkit.LiveryRender;
using ForzaToolkit.LiveryRender.Assets;
using Microsoft.Extensions.Logging;

namespace LiveryGallery.Render;

internal sealed class LiveryRenderService : IDisposable
{
    // Disk caches of the render library: car index (cars-fh6.v1.tsv), vinyl shapes (vinyls-fh6.v1.bin), shader
    // defaults (shaders-fh6.v2.tsv). The library validates them against the game files and rebuilds whatever is stale
    // when archives change or new ones appear. Shared with the toolkit's other programs (the converter, Forza Livery
    // Craft): %LOCALAPPDATA%\ForzaToolkit\render-cache, so the game's files are scanned once for all of them
    private static readonly string CacheDirectory = RenderCacheFolders.Shared;

    private static readonly TimeSpan MemoryReleaseDelay = TimeSpan.FromSeconds(2);

    // The game whose cars and shapes the renderer draws: its caches are cars-<game>.tsv and vinyls-<game>.bin
    // (LiveryRender names them by the game)
    private readonly string _gameId;

    private readonly DispatcherTimer _memoryReleaseTimer = new() { Interval = MemoryReleaseDelay };
    private readonly Func<int, CarWheels?>? _wheels;
    private Task _pendingPathChange = Task.CompletedTask;
    private Task _carIndexPreparation = Task.CompletedTask;
    private volatile string? _preparationTarget;

    private int _version;
    private int _users;
    private string? _currentPath;
    private LiveryRenderer? _renderer;
    private volatile bool _hasRenderer;
    private bool _disposed;

    public GameFolderReport? FolderCheck { get; private set; }
    public bool IsChecking { get; private set; } = true;
    public string? GameDirectory => FolderCheck is { IsValid: true, GameDirectory: { } root } ? root : null;
    public bool IsAvailable => !IsChecking && GameDirectory is not null;
    public LiveryRenderer? CurrentRenderer => _renderer;

    public event Action? StateChanged;

    /// <param name="wheels">A car's stock wheels from the car list; the renderer places the wheels from the game files
    /// for a car it gives none for</param>
    public LiveryRenderService(string gameId, Func<int, CarWheels?>? wheels = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        _gameId = gameId;
        _wheels = wheels;
        _memoryReleaseTimer.Tick += (_, _) =>
        {
            _memoryReleaseTimer.Stop();
            if (_renderer is null) RequestMemoryRelease();
        };
    }

    public async Task<LiveryRenderer?> AcquireAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        while (!_disposed)
        {
            var pathChange = _pendingPathChange;
            var preparation = _carIndexPreparation;
            if (pathChange.IsCompleted && preparation.IsCompleted) break;
            await Task.WhenAll(pathChange, preparation).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext
                | ConfigureAwaitOptions.SuppressThrowing);
        }

        if (_disposed || IsChecking || GameDirectory is not { } root) return null;
        _memoryReleaseTimer.Stop();
        _users++;
        if (_renderer is null)
        {
            _renderer = new LiveryRenderer(new LiveryRendererOptions
            {
                GameDirectory = root,
                CacheDirectory = CacheDirectory,
                GameId = _gameId,
                MaxCachedCars = 1,
                WheelSizes = _wheels is null ? null : CarWheelSizes.From(_wheels),
            });
            _hasRenderer = true;
        }
        return _renderer;
    }

    public void Release()
    {
        if (_users > 0) _users--;
        if (_users == 0 && _renderer is not null)
            Dispatcher.UIThread.Post(ReleaseIfUnused, DispatcherPriority.Background);
    }

    private void ReleaseIfUnused()
    {
        if (_users == 0) ReleaseRenderer();
    }

    public Task SetGamePathAsync(string? path)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return Task.CompletedTask;
        if (!IsChecking && FolderCheck is not null && string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        var change = SetGamePathCoreAsync(path, ++_version);
        _pendingPathChange = change;
        return change;
    }

    private async Task SetGamePathCoreAsync(string? path, int version)
    {
        IsChecking = true;
        StateChanged?.Invoke();

        GameFolderReport check;
        try
        {
            check = await RenderGameFolder.CheckAsync(path);
        }
        catch (Exception ex)
        {
            AppLog.LogError($"Game folder check failed for '{path}'", ex);
            check = new GameFolderReport
            {
                InputPath = path,
                Findings = [new GameFolderFinding(GameFolderIssueCode.NotFound, true, path)],
            };
        }

        if (version != _version || _disposed) return;

        string? previousRoot = GameDirectory;
        FolderCheck = check;
        _currentPath = path;
        IsChecking = false;

        foreach (var finding in check.Findings)
            AppLog.LogError($"Game folder '{path}': {finding.Code} ({(finding.IsError ? "error" : "warning")}): {finding.Path}",
                new InvalidOperationException(finding.Code.ToString()));

        string? root = GameDirectory;
        bool rootChanged = !string.Equals(previousRoot, root, StringComparison.OrdinalIgnoreCase);
        var obsolete = rootChanged ? _renderer : null;
        if (obsolete is not null) ClearRenderer();
        if (rootChanged) _preparationTarget = root;
        StateChanged?.Invoke();
        if (obsolete is not null) DisposeRenderer(obsolete);
        if (root is not null && rootChanged) StartCarIndexPreparation(root);
    }

    private void StartCarIndexPreparation(string root)
    {
        var previousPreparation = _carIndexPreparation;
        _carIndexPreparation = Task.Run(async () =>
        {
            await previousPreparation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (!string.Equals(_preparationTarget, root, StringComparison.OrdinalIgnoreCase)) return;
            PrepareRenderCaches(root, _gameId);
        });
        _ = _carIndexPreparation.ContinueWith(
            _ => Dispatcher.UIThread.Post(ScheduleMemoryRelease),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>
    /// Builds the render caches in the background once the game folder is known. The first run scans every
    /// car archive and parses Vinyls.zip; later runs only compare sizes and dates and rebuild what changed
    /// (game update, new cars), so the 3D viewer opens without rescanning the archives.
    /// </summary>
    private static void PrepareRenderCaches(string root, string gameId)
    {
        try
        {
            using var renderer = new LiveryRenderer(new LiveryRendererOptions
            {
                GameDirectory = root,
                CacheDirectory = CacheDirectory,
                GameId = gameId,
            });
            var result = renderer.PrepareCaches();
            if (result.Warnings.Count > 0)
                AppLog.LogWarning($"Render caches prepared in {result.Elapsed.TotalSeconds:0.0} s ({result.Cars} cars, "
                    + $"{result.VinylShapes} vinyl shapes, shader defaults: {result.ShaderDefaults}) with {result.Warnings.Count} warning(s): "
                    + string.Join("; ", result.Warnings.Take(10)) + (result.Warnings.Count > 10 ? "; …" : ""));
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to prepare the livery renderer caches", ex);
        }
    }

    /// <summary>
    /// Deletes, in the background, the render caches the gallery kept in its own folder before they were shared:
    /// 1.6.0's (<c>%LOCALAPPDATA%\FH6LiveryGallery\render-cache</c>) and the test builds' of 1.7.0
    /// (<c>%LOCALAPPDATA%\ForzaLiveryGallery\render-cache</c>). What another running copy holds open goes on the next start
    /// </summary>
    internal static void RemoveOldCaches()
    {
        var logger = AppLog.LoggerFactory.CreateLogger("LiveryGallery.Rendering");
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (string? folder in new[] { LiveryGallery.Configuration.AppSettings.Identity.PreviousFolderName, LiveryGallery.Configuration.AppSettings.Identity.FolderName })
            if (!string.IsNullOrWhiteSpace(folder))
                _ = RenderCacheFolders.RemoveOld(Path.Combine(local, folder, "render-cache"), logger);
    }

    private void ClearRenderer()
    {
        _renderer = null;
        _hasRenderer = false;
    }

    private void ReleaseRenderer()
    {
        var renderer = _renderer;
        ClearRenderer();
        if (renderer is not null) DisposeRenderer(renderer);
    }

    private void DisposeRenderer(LiveryRenderer renderer)
    {
        try
        {
            renderer.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to dispose the livery renderer", ex);
        }
        ScheduleMemoryRelease();
    }

    private void ScheduleMemoryRelease()
    {
        if (_disposed) return;
        _memoryReleaseTimer.Stop();
        _memoryReleaseTimer.Start();
    }

    private void RequestMemoryRelease()
    {
        // The paints' decoded textures and the livery canvases kept for the next livery outlive the renderer (shared by
        // the whole process): with no renderer left they go too, and the next one makes them again
        if (!_hasRenderer) LiveryRenderer.TrimSharedMemory();
        MemoryReclaimer.ReclaimAfterLargeRelease("3D renderer released", () => !_hasRenderer && !_disposed);
    }

    public async Task ShutdownAsync(TimeSpan timeout)
    {
        Dispose();
        try
        {
            await _carIndexPreparation.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            AppLog.LogWarning($"The car index preparation was still running {timeout.TotalSeconds:0.#} s after shutdown began");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _version++;
        _preparationTarget = null;
        _memoryReleaseTimer.Stop();
        var renderer = _renderer;
        ClearRenderer();
        if (renderer is null) return;
        try
        {
            renderer.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to dispose the livery renderer", ex);
        }
    }
}
