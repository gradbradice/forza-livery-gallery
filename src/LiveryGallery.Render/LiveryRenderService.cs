using Avalonia.Threading;
using ForzaToolkit.Games;
using ForzaToolkit.Games.Installation;
using ForzaToolkit.LiveryRender;
using ForzaToolkit.LiveryRender.Assets;
using ForzaToolkit.LiveryRender.Livery;
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

    // The game of the liveries. Its own files draw them; FH3 to FH5, when their own cannot be read (encrypted, not
    // installed), borrow another game's (GameDrawing.SubstitutesFor). The caches are named by the game whose files draw: cars-<game>.tsv,
    // vinyls-<game>.bin (LiveryRender names them so)
    private readonly ForzaGame _game;
    // Another game's folder, for a game that borrows: the one set in that game's settings, or found on this PC
    private readonly Func<ForzaGame, string?>? _otherFolder;
    // The logo numbers of the liveries' game → files (its published list); null — the renderer's own table (FH6)
    private readonly Func<DecalIdMap?>? _decalIds;

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

    /// <summary>The check of the game's own folder (the settings show it)</summary>
    public GameFolderReport? FolderCheck { get; private set; }
    public bool IsChecking { get; private set; } = true;

    /// <summary>The folder whose files draw: the game's own, or the borrowed game's (<see cref="DrawnWith"/>)</summary>
    public string? GameDirectory { get; private set; }

    /// <summary>
    /// The game whose files draw when they are not the liveries' game's own (FH3 drawn with FH4's…), and why;
    /// null when the game's own files draw. The 3D window and the settings say so every time
    /// </summary>
    public (ForzaGame Game, BorrowReason Reason)? DrawnWith { get; private set; }

    public bool IsAvailable => !IsChecking && GameDirectory is not null;
    public LiveryRenderer? CurrentRenderer => _renderer;

    public event Action? StateChanged;

    /// <param name="game">The game of the liveries drawn</param>
    /// <param name="wheels">A car's stock wheels from the car list; the renderer places the wheels from the game files
    /// for a car it gives none for</param>
    /// <param name="otherFolder">Another game's folder, when <paramref name="game"/> borrows (FH3 to FH5): its setting, or found</param>
    /// <param name="decalIds">The liveries' game's logo numbers → files; null — the renderer's own (FH6's)</param>
    public LiveryRenderService(ForzaGame game, Func<int, CarWheels?>? wheels = null, Func<ForzaGame, string?>? otherFolder = null,
        Func<DecalIdMap?>? decalIds = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        _game = game;
        _wheels = wheels;
        _otherFolder = otherFolder;
        _decalIds = decalIds;
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
            // A borrowed game's cars, shapes and paints; the stock wheels of the car list are the liveries' game's, so
            // only for its own files
            _renderer = new LiveryRenderer(new LiveryRendererOptions
            {
                GameDirectory = root,
                CacheDirectory = CacheDirectory,
                GameId = DrawingGameId,
                DecalIds = _decalIds?.Invoke(),
                MaxCachedCars = 1,
                WheelSizes = _wheels is null || DrawnWith is not null ? null : CarWheelSizes.From(_wheels),
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

        foreach (var finding in check.Findings)
            AppLog.LogError($"Game folder '{path}': {finding.Code} ({(finding.IsError ? "error" : "warning")}): {finding.Path}",
                new InvalidOperationException(finding.Code.ToString()));

        // The game's own files, else (FH3 to FH5) the first game of GameDrawing.SubstitutesFor whose folder is right
        string? ownRoot = check is { IsValid: true, GameDirectory: { } own } ? own : null;
        (string Root, ForzaGame Game, BorrowReason Reason)? borrowed = ownRoot is null ? await FindBorrowedAsync(path) : null;
        if (version != _version || _disposed) return;

        string? previousRoot = GameDirectory;
        FolderCheck = check;
        _currentPath = path;
        GameDirectory = ownRoot ?? borrowed?.Root;
        DrawnWith = borrowed is { } b ? (b.Game, b.Reason) : null;
        IsChecking = false;
        if (borrowed is { } with)
            AppLog.LogWarning($"Liveries of {_game.ShortName} are drawn with {with.Game.ShortName}'s files ('{with.Root}'): "
                + $"{_game.ShortName}'s own cannot be read ({with.Reason}); cars, logos and paints may differ");

        string? root = GameDirectory;
        bool rootChanged = !string.Equals(previousRoot, root, StringComparison.OrdinalIgnoreCase);
        var obsolete = rootChanged ? _renderer : null;
        if (obsolete is not null) ClearRenderer();
        if (rootChanged) _preparationTarget = root;
        StateChanged?.Invoke();
        if (obsolete is not null) DisposeRenderer(obsolete);
        if (root is not null && rootChanged) StartCarIndexPreparation(root);
    }

    // The id of the game whose files draw: the caches are named by it
    private string DrawingGameId => DrawnWith?.Game.Id ?? _game.Id;

    /// <summary>
    /// For a game whose own files cannot draw (FH3 to FH5): the first of <see cref="GameDrawing.SubstitutesFor"/> whose folder,
    /// set in its settings or found, passes the same check; null when none does or the game borrows nothing
    /// </summary>
    private async Task<(string Root, ForzaGame Game, BorrowReason Reason)?> FindBorrowedAsync(string? ownPath)
    {
        var substitutes = GameDrawing.SubstitutesFor(_game);
        if (substitutes.Count == 0 || _otherFolder is null) return null;
        var reason = GameDrawing.IsLockedPackageFolder(ownPath) ? BorrowReason.Locked
            : string.IsNullOrWhiteSpace(ownPath) || !Directory.Exists(ownPath) ? BorrowReason.NotFound
            : BorrowReason.NotDrawable;
        foreach (var other in substitutes)
        {
            string? folder;
            try
            {
                // Off the UI thread: a game not set in the settings is looked for on this PC (GameDrawing.FindOnThisPc:
                // the Steam libraries, every drive's XboxGames, the UWP packages), which a sleeping or network drive can
                // hold for seconds; the window went on meanwhile (code review 19.11.2026, finding 3)
                folder = await Task.Run(() => _otherFolder(other) is { } found && GameDrawing.DrawingFolder(other, found) is not null ? found : null)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppLog.LogError($"Failed to find {other.ShortName}'s folder to draw {_game.ShortName}'s liveries", ex);
                continue;
            }
            if (folder is null) continue;
            if (_disposed) return null;
            try
            {
                if (await RenderGameFolder.CheckAsync(folder) is { IsValid: true, GameDirectory: { } root })
                    return (root, other, reason);
            }
            catch (Exception ex)
            {
                AppLog.LogError($"Game folder check failed for '{folder}' ({other.ShortName})", ex);
            }
        }
        AppLog.LogWarning($"Liveries of {_game.ShortName} are not drawn: its own files cannot be read ({reason}), "
            + $"and no folder of {string.Join(", ", substitutes.Select(g => g.ShortName))} was found");
        return null;
    }

    private void StartCarIndexPreparation(string root)
    {
        var previousPreparation = _carIndexPreparation;
        string gameId = DrawingGameId;
        _carIndexPreparation = Task.Run(async () =>
        {
            await previousPreparation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (!string.Equals(_preparationTarget, root, StringComparison.OrdinalIgnoreCase)) return;
            PrepareRenderCaches(root, gameId);
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
