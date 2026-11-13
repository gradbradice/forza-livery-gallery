using Avalonia.Threading;
using ForzaToolkit.Liveries.Scanning;
using LiveryGallery.ViewModels;
using LiveryGallery.Viewer3D;

namespace LiveryGallery.Services;

/// <summary>
/// The previews the gallery draws from the 3D model for cards whose save has none (<see cref="ILiveryThumbnails"/>).
/// A drawn preview is written into the shared preview folder under <see cref="LiveryDrawnPreviews.FileName"/>, and the
/// next scan makes it the livery's preview: from then on it goes into the archive and the backups with the livery, and
/// it is deleted with the livery or once the game writes a preview of its own. Nothing is written to the save. A stored
/// livery (one of the storage, which no scan of the save sees) is drawn from its file in the storage, and its preview
/// is given to it in the storage's index at once (<see cref="KeepStored"/>).
/// One preview is drawn at a time, in the order the cards asked; a preview whose cards all left the screen before it
/// started is not drawn. A livery that could not be drawn is not tried again until the game folder changes.
/// Used from the UI thread.
/// </summary>
internal sealed class RenderedThumbnails
{
    private sealed class Job(string fileName, LiveryThumbnailRequest request)
    {
        public string FileName { get; } = fileName;
        public LiveryThumbnailRequest Request { get; } = request;
        public TaskCompletionSource<string?> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Waiters;
    }

    private readonly ILiveryThumbnails _thumbnails;
    private readonly Func<LiveryEntry, string?> _liveryFileOf;
    private readonly string _previewDirectory;
    private readonly ClosableAsyncLock _saveFolderLock;
    private readonly Dictionary<string, Job> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<Job> _queue = new();
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
    private int _generation;
    private bool _running;
    private bool _shuttingDown;

    /// <param name="thumbnails">Draws the previews</param>
    /// <param name="liveryFileOf">A card → its livery file: in the current save, a stored livery's in the storage; null when it cannot be read</param>
    /// <param name="previewDirectory">The shared preview folder the scanner reads (<c>LiveryCacheStore.ThumbsDir</c>)</param>
    /// <param name="saveFolderLock">Held while a drawn preview is written, so a scan does not take it for an orphan</param>
    public RenderedThumbnails(ILiveryThumbnails thumbnails, Func<LiveryEntry, string?> liveryFileOf, string previewDirectory, ClosableAsyncLock saveFolderLock)
    {
        _thumbnails = thumbnails;
        _liveryFileOf = liveryFileOf;
        _previewDirectory = previewDirectory;
        _saveFolderLock = saveFolderLock;
    }

    /// <summary>
    /// The way previews are drawn now. 1: LiveryRender 0.19.0 places a livery on a car whose section masks are 1024 wide
    /// (22 cars of FH6, those without windows: buggies, trophy trucks, BAC Mono) at its width; before, it was drawn half
    /// as wide. 2: LiveryRender 0.21.0 paints the parts of a model whose paint groups are slot masks (as FH4's and FH5's
    /// are) from the livery's records of those parts. 3: LiveryRender 0.22.0 reads the car library's additions
    /// (<c>Materials_pri_&lt;N&gt;.zip</c>) over its base archive. 4: LiveryRender 0.26.0 paints a part set back to the
    /// factory paint in the car's factory colour, not in the colour last picked for it that FH6 keeps in the record
    /// </summary>
    public const int DrawingRevision = 4;

    /// <summary>
    /// Deletes the previews the gallery drew in an older way (<paramref name="revision"/> below
    /// <see cref="DrawingRevision"/>): they are drawn again when their cards show. The game's previews stay. Returns
    /// how many were deleted
    /// </summary>
    public static int DeleteOutdated(string previewDirectory, int revision)
    {
        if (revision >= DrawingRevision || !Directory.Exists(previewDirectory)) return 0;
        int deleted = 0;
        foreach (var file in Directory.EnumerateFiles(previewDirectory, LiveryDrawnPreviews.FilePrefix + "*.png"))
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.LogErrorThrottled(file, $"Failed to delete the outdated drawn preview '{file}'", ex);
            }
        }
        return deleted;
    }

    /// <summary>A preview was drawn and written: a scan makes it the livery's preview</summary>
    public event Action? Drawn;

    /// <summary>
    /// Gives a stored livery its drawn preview in the storage's index (no scan does): kept from the preview cleanup, and
    /// it goes back into the game with the livery. Called on the UI thread with each drawn preview a stored card gets
    /// </summary>
    public Action<LiveryEntry, string>? KeepStored { get; set; }

    /// <summary>The card needs a preview of the gallery's own and one can be drawn now</summary>
    public bool CanRender(LiveryEntry entry) =>
        !_shuttingDown && entry.CanRenderThumbnail && _thumbnails.IsAvailable
        && FileNameOf(entry) is { } name && !_failed.Contains(name);

    /// <summary>
    /// The drawn preview of <paramref name="entry"/>, drawn when there is none yet; null when it cannot be drawn or
    /// <paramref name="ct"/> (the card left the screen) was cancelled
    /// </summary>
    public async Task<string?> GetPathAsync(LiveryEntry entry, CancellationToken ct)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!CanRender(entry) || FileNameOf(entry) is not { } name) return null;

        string path = Path.Combine(_previewDirectory, name);
        if (File.Exists(path)) return Kept(entry, path); // drawn before; the next scan attaches it

        bool queued = false;
        if (!_jobs.TryGetValue(name, out var job))
        {
            if (_liveryFileOf(entry) is not { } liveryFile) return null;
            job = new Job(name, new LiveryThumbnailRequest(entry.CarId, liveryFile));
            _jobs[name] = job;
            _queue.Enqueue(job);
            queued = true;
        }

        // Counted before the queue runs: a job nobody waits for is skipped
        job.Waiters++;
        if (queued) _ = RunAsync();
        try
        {
            return Kept(entry, await job.Done.Task.WaitAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            job.Waiters--;
        }
    }

    /// <summary>The game folder changed: liveries that failed may draw now</summary>
    public void Reset()
    {
        Dispatcher.UIThread.VerifyAccess();
        _generation++;
        _failed.Clear();
    }

    /// <summary>The app is closing: nothing more is drawn</summary>
    public void BeginShutdown()
    {
        _shuttingDown = true;
        while (_queue.TryDequeue(out var job)) Finish(job, null);
    }

    private async Task RunAsync()
    {
        if (_running) return;
        _running = true;
        int written = 0;
        try
        {
            while (!_shuttingDown && _queue.TryDequeue(out var job))
            {
                if (job.Waiters == 0)
                {
                    Finish(job, null);
                    continue;
                }

                int generation = _generation;
                string? path = null;
                try
                {
                    if (await _thumbnails.DrawAsync(job.Request, CancellationToken.None) is { } png)
                        path = await WriteAsync(job.FileName, png);
                }
                catch (Exception ex)
                {
                    AppLog.LogErrorThrottled("RenderedThumbnails", $"Failed to draw a preview of '{job.Request.LiveryFilePath}' from 3D", ex);
                }

                if (path is not null) written++;
                // Not tried again while the game folder stays the same; a failure because it went away is not remembered
                else if (generation == _generation && !_shuttingDown && _thumbnails.IsAvailable) _failed.Add(job.FileName);
                Finish(job, path);
            }
        }
        finally
        {
            _running = false;
            if (written > 0 && !_shuttingDown) Drawn?.Invoke();
        }
    }

    private async Task<string?> WriteAsync(string fileName, byte[] png)
    {
        string path = Path.Combine(_previewDirectory, fileName);
        try
        {
            // Under the save folder lock: a scan running now would delete a preview no livery refers to yet
            using (await _saveFolderLock.AcquireAsync())
            {
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(_previewDirectory);
                    AtomicFile.WriteAllBytes(path, png);
                });
            }
            return path;
        }
        catch (OperationCanceledException) when (_saveFolderLock.IsClosing)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogErrorThrottled(path, $"Failed to write the drawn preview '{path}'", ex);
            return null;
        }
    }

    private void Finish(Job job, string? path)
    {
        if (_jobs.TryGetValue(job.FileName, out var current) && ReferenceEquals(current, job)) _jobs.Remove(job.FileName);
        job.Done.TrySetResult(path);
    }

    // A stored livery's preview is attached now: the scan that attaches the game's does not see the storage
    private string? Kept(LiveryEntry entry, string? path)
    {
        if (path is not null && entry.IsStored) KeepStored?.Invoke(entry, path);
        return path;
    }

    private static string? FileNameOf(LiveryEntry entry) => LiveryDrawnPreviews.FileName(entry.CarId, entry.CLiveryHash);
}
