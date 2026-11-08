using Avalonia;
using Avalonia.Controls;
using ForzaToolkit.Formats;
using ForzaToolkit.Games.Installation;
using ForzaToolkit.LiveryRender;
using ForzaToolkit.LiveryRender.Formats;
using ForzaToolkit.LiveryRender.Avalonia;
using ForzaToolkit.LiveryRender.Livery;
using Microsoft.Extensions.Logging;
using LiveryGallery.Viewer3D;
using LiveryGallery.Views;

namespace LiveryGallery.Render;

/// <summary>
/// <see cref="ILivery3DViewer"/> over LiveryRender: the renderer (<see cref="LiveryRenderService"/>), the viewer windows
/// (up to <see cref="Livery3DWindows.Max"/>, each holding the renderer while open, turning together when their switch is
/// on), the camera left on each car (kept in the game's settings), and the livery file read through Formats
/// (<see cref="LiveryDrawings"/> of ForzaToolkit.LiveryRender.Formats). Used from the UI thread.
/// </summary>
internal sealed class Livery3DViewer : ILivery3DViewer, IDisposable
{
    // A window closed while a car was loading keeps the renderer until the load ends, at most this long
    private static readonly TimeSpan LoadDrainTimeout = TimeSpan.FromSeconds(30);

    // A window opened beside another starts this far from it, so both are seen
    private const int CascadeStep = 40;

    private readonly LiveryRenderService _service;
    private readonly LiveryThumbnails _thumbnails;
    private readonly AppSettingsData _settings;
    private readonly string _gameId;
    private readonly LiveryUpgradeNames? _upgradeNames;
    private readonly ViewerCameraMemory _cameras;
    // The open windows, the one used last at the end
    private readonly List<ViewerSlot> _slots = [];
    private int _nextWindowId = 1;
    private bool _camerasChanged;
    private bool _syncing;
    private Task _pendingRelease = Task.CompletedTask;
    private GameFolderReport? _report;
    private bool _shuttingDown;

    private sealed class ViewerSlot(LiveryViewerWindow window, int id)
    {
        public LiveryViewerWindow Window { get; } = window;
        public int Id { get; } = id;
        public string? Key { get; set; }

        // How many liveries were asked for this window: one read more slowly than the next is not shown after it
        public int Asked { get; set; }
    }

    /// <param name="settings">The viewer windows remember their quality setting there, and the game's cars' cameras</param>
    /// <param name="cars">The car list: the stock wheels of each car (sizes and stance from the game database)</param>
    /// <param name="gameId">The game whose cars and shapes are drawn (ForzaToolkit.Games id): its game folder, its caches</param>
    /// <param name="upgrades">The game's names of the cars' tuning parts (car-upgrades.json); null — the window's own</param>
    /// <param name="language">The game language the car texts are shown in (<c>CarTextProvider.LanguageCode</c>), asked each time</param>
    public Livery3DViewer(AppSettingsData settings, CarDatabaseService cars, string gameId, CarUpgradesService? upgrades = null,
        Func<string>? language = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(cars);
        _settings = settings;
        _gameId = gameId;
        if (upgrades is not null)
            _upgradeNames = new LiveryUpgradeNames(
                part => upgrades.Catalog.PartName(part, language?.Invoke()),
                (car, part, option) => upgrades.Catalog.OptionName(car, part, option, language?.Invoke()))
            {
                RimName = rim => upgrades.Catalog.RimName(rim, language?.Invoke()),
                TakesRims = car => upgrades.Catalog.TakesRims(car),
            };
        var kept = settings.Games.TryGetValue(gameId, out var game) ? game.ViewerCameras : null;
        _cameras = new ViewerCameraMemory(kept?.Select(c => new ViewerCameraEntry(c.Car, new ViewerCamera(c.Yaw, c.Pitch, c.Zoom, c.PanX, c.PanY))));
        _cameras.Changed += (_, _) => _camerasChanged = true;
        _service = new LiveryRenderService(gameId, carId => cars.Get(carId)?.Wheels);
        _thumbnails = new LiveryThumbnails(_service);
        _service.StateChanged += OnServiceStateChanged;
    }

    public bool IsChecking => _service.IsChecking;
    public bool IsAvailable => _service.IsAvailable;
    public string? GameDirectory => _service.GameDirectory;
    public GameFolderReport? FolderReport => _report;
    public ILiveryThumbnails Thumbnails => _thumbnails;

    public event Action? StateChanged;

    public event Action<Livery3DNavigation>? NavigateRequested;

    /// <summary>How many viewer windows are open</summary>
    internal int WindowCount => _slots.Count;

    private void OnServiceStateChanged()
    {
        _report = _service.FolderCheck;
        StateChanged?.Invoke();
        // Another game folder: the open windows still draw with the old renderer
        if (_service.IsChecking) return;
        foreach (var slot in _slots.ToList())
            if (!ReferenceEquals(slot.Window.Renderer, _service.CurrentRenderer)) slot.Window.Close();
    }

    public Task SetGamePathAsync(string? path) => _service.SetGamePathAsync(path);

    public async Task<Livery3DResult> ShowAsync(Window owner, Livery3DRequest request)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);
        if (_shuttingDown) return Livery3DResult.Shown;
        if (!_service.IsAvailable) return new Livery3DResult(Livery3DProblem.NotAvailable);

        // An open window takes the livery at once as the one it shows: ← → pressed again go on from it, and a livery
        // asked before it that is read later is not shown over it
        var target = SlotFor(request.Window);
        int asked = 0;
        if (target is not null)
        {
            target.Key = request.Key;
            asked = ++target.Asked;
        }
        // A newer livery was asked for the same window meanwhile: this one is let be, its problems too
        bool Superseded() => target is not null && target.Asked != asked && _slots.Contains(target);

        ParseResult<LiveryDrawing> drawing;
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(request.LiveryFilePath);
            drawing = await Task.Run(() => LiveryDrawings.FromCLivery((uint)Math.Max(0, request.CarId), bytes));
        }
        catch (Exception ex)
        {
            AppLog.LogError($"3D viewer: failed to read '{request.LiveryFilePath}'", ex);
            return Superseded() ? Livery3DResult.Shown : new Livery3DResult(Livery3DProblem.ReadFailed);
        }

        if (Superseded()) return Livery3DResult.Shown;
        if (drawing.Error is { } parseError)
            AppLog.LogError($"3D viewer: '{request.LiveryFilePath}' parsed with status {drawing.Status}: {parseError.Message}",
                new InvalidDataException(parseError.Message));
        if (!drawing.HasValue)
            return new Livery3DResult(Livery3DProblem.ParseFailed, drawing.Error is { } e ? $"{e.Code} ({(int)e.Code})" : null);

        var acquired = SlotFor(request.Window) is null ? await _service.AcquireAsync() : null;
        if (_shuttingDown)
        {
            if (acquired is not null) _service.Release();
            return Livery3DResult.Shown;
        }
        // Another press may have opened a window while the renderer was coming
        var slot = SlotFor(request.Window);
        ViewerSlot? beside = null;
        if (slot is null)
        {
            if (acquired is null) return new Livery3DResult(Livery3DProblem.NotAvailable);
            beside = _slots.LastOrDefault();
            slot = Open(owner, acquired);
        }
        else
        {
            if (acquired is not null) _service.Release();
            Touch(slot);
            slot.Window.Activate();
        }

        slot.Key = request.Key;
        var shown = slot.Window.ShowLiveryAsync(request.LiveryName, request.CarText, request.GameName, drawing.Value!);
        // A window opened beside others that turn together starts at their camera
        if (beside is not null && slot.Window.SyncCamera && beside.Window.SyncCamera) slot.Window.SetView(beside.Window.View);
        await shown;
        return Livery3DResult.Shown;
    }

    // The window a livery goes to; null — a new one
    private ViewerSlot? SlotFor(int window) => window switch
    {
        Livery3DWindows.New => _slots.Count >= Livery3DWindows.Max ? _slots[^1] : null,
        > 0 => _slots.Find(s => s.Id == window) ?? _slots.LastOrDefault(),
        _ => _slots.LastOrDefault(),
    };

    private void Touch(ViewerSlot slot)
    {
        if (_slots.Count > 0 && ReferenceEquals(_slots[^1], slot)) return;
        if (_slots.Remove(slot)) _slots.Add(slot);
    }

    private ViewerSlot Open(Window owner, LiveryRenderer renderer)
    {
        var window = new LiveryViewerWindow(renderer, LiveryViewerTexts.Current, _settings.ViewerQualityIndex, index =>
        {
            _settings.ViewerQualityIndex = index;
            LiveryGallery.Services.AppSettingsService.Save(_settings);
        }, AppLog.LoggerFactory.CreateLogger("Viewer3D"))
        {
            UpgradeNames = _upgradeNames,
            ShowPlates = _settings.ViewerShowPlates,
            Cameras = _cameras,
            SyncCamera = _settings.ViewerSyncCamera,
        };
        var slot = new ViewerSlot(window, _nextWindowId++);
        // Beside the window used last, a step down and right of it, so both are seen
        if (_slots.LastOrDefault()?.Window is { } last)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(last.Position.X + CascadeStep, last.Position.Y + CascadeStep);
        }
        window.PlatesChanged += (_, shown) =>
        {
            _settings.ViewerShowPlates = shown;
            LiveryGallery.Services.AppSettingsService.Save(_settings);
            foreach (var other in Others(slot)) other.Window.ShowPlates = shown;
        };
        window.SyncCameraChanged += (_, on) => SyncChanged(slot, on);
        window.ViewChanged += (_, view) => FollowView(slot, view);
        window.NavigateRequested += (_, step) =>
        {
            if (slot.Key is { } key) NavigateRequested?.Invoke(new Livery3DNavigation(slot.Id, key, step));
        };
        window.Activated += (_, _) => Touch(slot);
        // A closed window something still holds is told in the log by ClosedWindowWatch, as every window
        window.Closed += (_, _) =>
        {
            _slots.Remove(slot);
            UpdateSyncSwitches();
            SaveCameras();
            _pendingRelease = ReleaseAfterLoadsAsync(window, _pendingRelease);
        };
        _slots.Add(slot);
        UpdateSyncSwitches();
        window.Show(owner);
        return slot;
    }

    private IEnumerable<ViewerSlot> Others(ViewerSlot slot) => _slots.Where(s => !ReferenceEquals(s, slot));

    // The switch is seen when there is a window to turn with
    private void UpdateSyncSwitches()
    {
        foreach (var slot in _slots) slot.Window.CanSyncCamera = _slots.Count > 1;
    }

    // One switch for every window: on in one, on in all, the others turned to its camera
    private void SyncChanged(ViewerSlot slot, bool on)
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            _settings.ViewerSyncCamera = on;
            LiveryGallery.Services.AppSettingsService.Save(_settings);
            foreach (var other in Others(slot))
            {
                other.Window.SyncCamera = on;
                if (on) other.Window.SetView(slot.Window.View);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void FollowView(ViewerSlot slot, ViewerCamera view)
    {
        if (_syncing || !slot.Window.SyncCamera) return;
        _syncing = true;
        try
        {
            foreach (var other in Others(slot))
                if (other.Window.SyncCamera) other.Window.SetView(view);
        }
        finally
        {
            _syncing = false;
        }
    }

    // The cars' cameras into the game's settings, when one changed
    private void SaveCameras()
    {
        if (!_camerasChanged) return;
        _camerasChanged = false;
        if (!_settings.Games.TryGetValue(_gameId, out var game)) return;
        game.ViewerCameras = [.. _cameras.Entries.Select(c => new ViewerCameraData(c.CarId, c.View.Yaw, c.View.Pitch, c.View.Zoom, c.View.PanX, c.View.PanY))];
        LiveryGallery.Services.AppSettingsService.Save(_settings);
    }

    private async Task ReleaseAfterLoadsAsync(LiveryViewerWindow window, Task previous)
    {
        await previous.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        try
        {
            if (!await window.WaitForLoadsAsync(LoadDrainTimeout))
                AppLog.LogWarning($"3D viewer: a load was still running {LoadDrainTimeout.TotalSeconds:0} s after the window closed; releasing the renderer anyway");
        }
        catch (Exception ex)
        {
            AppLog.LogError("3D viewer: waiting for the loads of a closed window failed", ex);
        }
        finally
        {
            _service.Release();
        }
    }

    public void BeginShutdown()
    {
        _shuttingDown = true;
        _thumbnails.BeginShutdown();
        _service.StateChanged -= OnServiceStateChanged;
        foreach (var slot in _slots.ToList()) slot.Window.Close();
        SaveCameras();
    }

    /// <summary>The last resort when the app exits without <see cref="ShutdownAsync"/>: frees the renderer</summary>
    public void Dispose()
    {
        BeginShutdown();
        _service.Dispose();
    }

    public async Task ShutdownAsync(TimeSpan timeout)
    {
        BeginShutdown();
        try
        {
            await _pendingRelease.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            AppLog.LogWarning($"3D viewer: the renderer was still in use {timeout.TotalSeconds:0.#} s after shutdown began");
        }
        await _service.ShutdownAsync(timeout);
    }
}
