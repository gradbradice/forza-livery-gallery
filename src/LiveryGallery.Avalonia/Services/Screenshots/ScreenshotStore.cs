using SkiaSharp;

namespace LiveryGallery.Services.Screenshots;

/// <summary>How the gallery keeps a screenshot it is given</summary>
internal enum ScreenshotFormat
{
    /// <summary>The file as it is (a pasted picture: PNG)</summary>
    Original,

    /// <summary>Re-encoded as WebP at the chosen quality</summary>
    WebP,
}

/// <summary>One screenshot of a livery</summary>
/// <param name="LiveryKey">The livery's key: its folder name, as for notes and tags</param>
/// <param name="Path">The file</param>
/// <param name="Bytes">Its size</param>
/// <param name="AddedUtc">When it was added</param>
internal sealed record LiveryScreenshot(string LiveryKey, string Path, long Bytes, DateTime AddedUtc)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>What a livery has: how many screenshots, and the one on its card instead of the game's preview</summary>
internal readonly record struct ScreenshotSummary(int Count, string? CoverPath);

/// <summary>How much the screenshots of a game take</summary>
internal readonly record struct ScreenshotUsage(long Bytes, int Count, int Liveries);

/// <summary>
/// The user's screenshots of each livery, kept on this PC only: <c>games\&lt;game&gt;\screenshots\&lt;livery key&gt;\</c>,
/// one file per screenshot named by when it was added. The key is the livery's folder name, as for notes and tags, so
/// the screenshots stay with the livery through the archive. The cover (a screenshot shown on the card instead of the
/// game's preview) is the file named in <c>cover.txt</c> beside them. What each livery has is kept in memory after the
/// first look at the folder. Thread-safe
/// </summary>
internal sealed partial class ScreenshotStore
{
    /// <summary>The file that names the cover</summary>
    public const string CoverFileName = "cover.txt";

    /// <summary>The pictures the gallery takes, by extension; the decoder decides in the end</summary>
    public static readonly IReadOnlyList<string> Extensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    private readonly Lock _lock = new();
    private Dictionary<string, ScreenshotSummary>? _index;

    public ScreenshotStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = root;
    }

    /// <summary>The folder with one folder per livery</summary>
    public string Root { get; }

    /// <summary>A livery's screenshots were added, removed or got another cover (the livery's key)</summary>
    public event Action<string>? Changed;

    public static bool IsPicture(string path) =>
        Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>The folder of a livery's screenshots; the key is a folder name already, anything else is replaced</summary>
    public string FolderOf(string liveryKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(liveryKey);
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        string safe = new([.. liveryKey.Select(c => invalid.Contains(c) ? '_' : c)]);
        return System.IO.Path.Combine(Root, safe.Trim('.', ' ') is { Length: > 0 } s ? s : "_");
    }

    /// <summary>A livery's screenshots, the oldest first</summary>
    public IReadOnlyList<LiveryScreenshot> List(string liveryKey)
    {
        string folder = FolderOf(liveryKey);
        if (!Directory.Exists(folder)) return [];
        try
        {
            return [.. new DirectoryInfo(folder).EnumerateFiles()
                .Where(f => IsPicture(f.Name))
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .Select(f => new LiveryScreenshot(liveryKey, f.FullName, f.Length, AddedOf(f)))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogErrorThrottled("screenshots|" + folder, $"Failed to list the screenshots in '{folder}'", ex);
            return [];
        }
    }

    // The name says when (yyyyMMdd-HHmmss-fff, UTC); a file put there by hand: its time
    private static DateTime AddedOf(FileInfo file) =>
        DateTime.TryParseExact(System.IO.Path.GetFileNameWithoutExtension(file.Name)[..Math.Min(19, System.IO.Path.GetFileNameWithoutExtension(file.Name).Length)],
            "yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var at)
            ? at
            : file.LastWriteTimeUtc;

    /// <summary>How many screenshots a livery has and its cover</summary>
    public ScreenshotSummary Summary(string liveryKey)
    {
        lock (_lock) return Index().GetValueOrDefault(Normalize(liveryKey));
    }

    /// <summary>Every livery that has screenshots, by key (the folder names as they are on disk)</summary>
    public IReadOnlyDictionary<string, ScreenshotSummary> All()
    {
        lock (_lock) return new Dictionary<string, ScreenshotSummary>(Index(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The screenshots of every livery, the newest first: the gallery's "Screenshots" view</summary>
    public IReadOnlyList<LiveryScreenshot> ListAll()
    {
        var keys = All().Keys.ToList();
        return [.. keys.SelectMany(List).OrderByDescending(s => s.AddedUtc)];
    }

    /// <summary>How much the screenshots take</summary>
    public ScreenshotUsage Usage()
    {
        long bytes = 0;
        int count = 0, liveries = 0;
        foreach (string key in All().Keys)
        {
            var shots = List(key);
            if (shots.Count == 0) continue;
            liveries++;
            count += shots.Count;
            bytes += shots.Sum(s => s.Bytes);
        }
        return new ScreenshotUsage(bytes, count, liveries);
    }

    /// <summary>
    /// Adds a picture file as a screenshot of the livery. The file is copied (as it is, or as WebP), so it stays even if
    /// the original goes. Null when the file is not a picture the gallery can read
    /// </summary>
    public LiveryScreenshot? AddFile(string liveryKey, string sourcePath, ScreenshotFormat format, int quality)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(sourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogError($"Failed to read the screenshot '{sourcePath}'", ex);
            return null;
        }
        return Add(liveryKey, bytes, System.IO.Path.GetExtension(sourcePath), format, quality);
    }

    /// <summary>
    /// Adds a picture (its bytes; <paramref name="extension"/> says what it is, ".png" for a pasted one). Null when it is
    /// not a picture or cannot be written (a full disk, a folder that cannot be made): told in the log
    /// </summary>
    public LiveryScreenshot? Add(string liveryKey, byte[] picture, string extension, ScreenshotFormat format, int quality)
    {
        ArgumentNullException.ThrowIfNull(picture);
        if (!CanDecode(picture)) return null;
        extension = extension.ToLowerInvariant() == ".jpeg" ? ".jpg" : extension.ToLowerInvariant();
        if (!Extensions.Contains(extension)) extension = ".png";
        if (format == ScreenshotFormat.WebP && extension != ".webp" && EncodeWebP(picture, quality) is { } webp)
        {
            picture = webp;
            extension = ".webp";
        }

        string folder = FolderOf(liveryKey);
        string path;
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(folder);
                var now = DateTime.UtcNow;
                string stem = now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
                path = System.IO.Path.Combine(folder, stem + extension);
                for (int n = 2; File.Exists(path); n++) path = System.IO.Path.Combine(folder, $"{stem}-{n}{extension}");
                File.WriteAllBytes(path, picture);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.LogError($"Failed to keep a screenshot of '{liveryKey}' in '{folder}'", ex);
                return null;
            }
            finally
            {
                Forget(liveryKey);
            }
        }
        Changed?.Invoke(liveryKey);
        return new LiveryScreenshot(liveryKey, path, picture.LongLength, DateTime.UtcNow);
    }

    /// <summary>The files a backup takes of a livery: its screenshots and the file naming the cover</summary>
    public IReadOnlyList<string> FilesForBackup(string liveryKey)
    {
        var files = List(liveryKey).Select(s => s.Path).ToList();
        string cover = System.IO.Path.Combine(FolderOf(liveryKey), CoverFileName);
        if (files.Count > 0 && File.Exists(cover)) files.Add(cover);
        string albums = System.IO.Path.Combine(FolderOf(liveryKey), AlbumsFileName);
        if (files.Count > 0 && File.Exists(albums)) files.Add(albums);
        return files;
    }

    /// <summary>
    /// A livery's screenshots back from a backup (pictures and the cover's name, by their names), only when it has none
    /// here: a restore never mixes two sets. How many pictures were taken
    /// </summary>
    public int RestoreFromBackup(string liveryKey, IEnumerable<(string Name, Func<Stream> Open)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        string folder = FolderOf(liveryKey);
        int taken = 0;
        lock (_lock)
        {
            if (Index().GetValueOrDefault(Normalize(liveryKey)).Count > 0) return 0;
            foreach (var (name, open) in files)
            {
                bool cover = string.Equals(name, CoverFileName, StringComparison.OrdinalIgnoreCase);
                bool albums = string.Equals(name, AlbumsFileName, StringComparison.OrdinalIgnoreCase);
                if (!cover && !albums && !IsPicture(name)) continue;
                if (System.IO.Path.GetFileName(name) != name) continue;
                Directory.CreateDirectory(folder);
                string path = System.IO.Path.Combine(folder, cover ? CoverFileName : albums ? AlbumsFileName : name);
                if (File.Exists(path)) continue;
                try
                {
                    using var source = open();
                    using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                    source.CopyTo(target);
                    if (!cover && !albums) taken++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    AppLog.LogError($"Failed to put back the screenshot '{path}' from a backup", ex);
                    try { File.Delete(path); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
                }
            }
            DeleteFolderIfEmpty(folder);
            Forget(liveryKey);
            if (taken > 0) AdoptAlbums(liveryKey);
        }
        if (taken > 0)
        {
            Changed?.Invoke(liveryKey);
            AlbumsChanged?.Invoke();
        }
        return taken;
    }

    /// <summary>Removes a screenshot; when it was the cover, the card goes back to the game's preview</summary>
    public bool Delete(LiveryScreenshot screenshot)
    {
        ArgumentNullException.ThrowIfNull(screenshot);
        lock (_lock)
        {
            try
            {
                if (!File.Exists(screenshot.Path)) return false;
                File.Delete(screenshot.Path);
                if (string.Equals(CoverNameOf(screenshot.LiveryKey), screenshot.FileName, StringComparison.OrdinalIgnoreCase))
                    File.Delete(System.IO.Path.Combine(FolderOf(screenshot.LiveryKey), CoverFileName));
                ForgetInAlbums(screenshot.LiveryKey, screenshot.FileName, renamedTo: null);
                DeleteFolderIfEmpty(FolderOf(screenshot.LiveryKey));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.LogError($"Failed to delete the screenshot '{screenshot.Path}'", ex);
                return false;
            }
            Forget(screenshot.LiveryKey);
        }
        Changed?.Invoke(screenshot.LiveryKey);
        return true;
    }

    /// <summary>Every screenshot of the livery (it was deleted for good)</summary>
    public bool DeleteAll(string liveryKey)
    {
        string folder = FolderOf(liveryKey);
        lock (_lock)
        {
            if (!Directory.Exists(folder)) return true;
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.LogError($"Failed to delete the screenshots in '{folder}'", ex);
                return false;
            }
            Forget(liveryKey);
        }
        Changed?.Invoke(liveryKey);
        return true;
    }

    /// <summary>Makes a screenshot the card's picture; null puts the game's preview back</summary>
    public void SetCover(string liveryKey, LiveryScreenshot? cover)
    {
        string file = System.IO.Path.Combine(FolderOf(liveryKey), CoverFileName);
        lock (_lock)
        {
            try
            {
                if (cover is null)
                {
                    if (File.Exists(file)) File.Delete(file);
                }
                else
                {
                    Directory.CreateDirectory(FolderOf(liveryKey));
                    File.WriteAllText(file, cover.FileName);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.LogError($"Failed to write the cover of '{liveryKey}'", ex);
                return;
            }
            Forget(liveryKey);
        }
        Changed?.Invoke(liveryKey);
    }

    /// <summary>The screenshot on the card, when one was chosen and is still there</summary>
    public string? CoverPath(string liveryKey) => Summary(liveryKey).CoverPath;

    /// <summary>
    /// Re-encodes the screenshots that are not WebP yet, for the setting turned on after they were added. Each file is
    /// replaced only when the WebP is smaller; the cover follows its file. How many were re-encoded
    /// </summary>
    public int ConvertAllToWebP(int quality, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var todo = ListAll().Where(s => !s.Path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)).ToList();
        int done = 0, converted = 0;
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var shot in todo)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((done++, todo.Count));
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(shot.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.LogError($"Failed to read the screenshot '{shot.Path}'", ex);
                continue;
            }
            if (EncodeWebP(bytes, quality) is not { } webp || webp.LongLength >= bytes.LongLength) continue;
            string target = System.IO.Path.ChangeExtension(shot.Path, ".webp");
            lock (_lock)
            {
                try
                {
                    if (File.Exists(target)) continue;
                    File.WriteAllBytes(target, webp);
                    File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(shot.Path));
                    File.Delete(shot.Path);
                    if (string.Equals(CoverNameOf(shot.LiveryKey), shot.FileName, StringComparison.OrdinalIgnoreCase))
                        File.WriteAllText(System.IO.Path.Combine(FolderOf(shot.LiveryKey), CoverFileName), System.IO.Path.GetFileName(target));
                    ForgetInAlbums(shot.LiveryKey, shot.FileName, renamedTo: System.IO.Path.GetFileName(target));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLog.LogError($"Failed to re-encode the screenshot '{shot.Path}'", ex);
                    continue;
                }
                Forget(shot.LiveryKey);
            }
            converted++;
            touched.Add(shot.LiveryKey);
        }
        progress?.Report((todo.Count, todo.Count));
        foreach (string key in touched) Changed?.Invoke(key);
        return converted;
    }

    /// <summary>
    /// A picture made smaller for the screen: at most <paramref name="width"/> pixels wide, never wider than the file, scaled
    /// down with high quality (smooth, no shimmer from a 4K screenshot). Null when the file cannot be read. Throws for IO
    /// errors, as <see cref="Avalonia.Media.Imaging.Bitmap"/> does
    /// </summary>
    public static Avalonia.Media.Imaging.Bitmap? DecodeSmaller(string path, int width)
    {
        if (SizeOf(path) is not { } size) return null;
        using var stream = File.OpenRead(path);
        return size.Width <= width
            ? new Avalonia.Media.Imaging.Bitmap(stream)
            : Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, width, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
    }

    /// <summary>The pixels a picture needs to stay sharp when it is <paramref name="logicalWidth"/> wide on a screen scaled by <paramref name="scaling"/></summary>
    public static int PixelWidth(double logicalWidth, double scaling) => (int)Math.Ceiling(logicalWidth * Math.Max(1, scaling));

    /// <summary>The width and height of a screenshot without decoding it; null when it cannot be read</summary>
    public static (int Width, int Height)? SizeOf(string path)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            return codec is null ? null : (codec.Info.Width, codec.Info.Height);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool CanDecode(byte[] picture)
    {
        using var data = SKData.CreateCopy(picture);
        using var codec = SKCodec.Create(data);
        return codec is not null && codec.Info.Width > 0 && codec.Info.Height > 0;
    }

    /// <summary>The picture as WebP; null when it cannot be decoded or encoded</summary>
    public static byte[]? EncodeWebP(byte[] picture, int quality)
    {
        using var bitmap = SKBitmap.Decode(picture);
        if (bitmap is null) return null;
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, Math.Clamp(quality, 1, 100));
        return data?.ToArray();
    }

    private string? CoverNameOf(string liveryKey)
    {
        string file = System.IO.Path.Combine(FolderOf(liveryKey), CoverFileName);
        try
        {
            return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void DeleteFolderIfEmpty(string folder)
    {
        if (!Directory.Exists(folder)) return;
        if (Directory.EnumerateFiles(folder).All(f => System.IO.Path.GetFileName(f) is CoverFileName or AlbumsFileName))
            Directory.Delete(folder, recursive: true);
    }

    private string Normalize(string liveryKey) => System.IO.Path.GetFileName(FolderOf(liveryKey));

    /// <summary>Reads every livery's folder again at the next question (pictures put there or taken away by hand)</summary>
    public void Reload()
    {
        lock (_lock) Forget();
    }

    private void Forget()
    {
        _index = null;
        _members = null;
    }

    // One livery's folder changed: only its entries are read again (the whole index reads every livery's folder, and
    // the cards ask for it on the UI thread). Under the lock
    private void Forget(string liveryKey)
    {
        string key = Normalize(liveryKey);
        if (_index is { } index)
        {
            if (SummaryOf(new DirectoryInfo(System.IO.Path.Combine(Root, key))) is { } summary) index[key] = summary;
            else index.Remove(key);
        }
        if (_members is { } members)
        {
            if (ReadLiveryAlbums(key) is { } livery) members[key] = livery;
            else members.Remove(key);
        }
    }

    // Called under the lock. A listing that failed is not kept: the next asks again
    private Dictionary<string, ScreenshotSummary> Index()
    {
        if (_index is not null) return _index;
        var index = new Dictionary<string, ScreenshotSummary>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(Root))
                foreach (var dir in new DirectoryInfo(Root).EnumerateDirectories())
                    if (SummaryOf(dir) is { } summary) index[dir.Name] = summary;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogErrorThrottled("screenshots|" + Root, $"Failed to list the screenshots in '{Root}'", ex);
            return index;
        }
        return _index = index;
    }

    // A livery's folder: how many pictures and its cover; null with none (or when it cannot be read)
    private ScreenshotSummary? SummaryOf(DirectoryInfo dir)
    {
        try
        {
            if (!dir.Exists) return null;
            var pictures = dir.EnumerateFiles().Where(f => IsPicture(f.Name)).ToList();
            if (pictures.Count == 0) return null;
            string? coverName = CoverNameOf(dir.Name);
            string? cover = coverName is null ? null : pictures.FirstOrDefault(p => string.Equals(p.Name, coverName, StringComparison.OrdinalIgnoreCase))?.FullName;
            return new ScreenshotSummary(pictures.Count, cover);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogErrorThrottled("screenshots|" + dir.FullName, $"Failed to list the screenshots in '{dir.FullName}'", ex);
            return null;
        }
    }
}
