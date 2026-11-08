using System.Text.Json;
using ForzaToolkit.Core.IO;
using LiveryGallery.Services.Collections;

namespace LiveryGallery.Services.Screenshots;

/// <summary>An album of screenshots: a name the user gave, a label that a screenshot of any livery can have</summary>
internal sealed record ScreenshotAlbum(string Id, string Name, int Order, DateTime CreatedUtc);

/// <summary>An album and how many screenshots it has</summary>
internal readonly record struct ScreenshotAlbumCount(ScreenshotAlbum Album, int Count);

/// <summary>
/// The albums. A screenshot can be in several, and leaving an album never deletes it. The list of albums is
/// <c>screenshots\albums.json</c>; which screenshots of a livery are in which album is in the livery's own folder
/// (<c>albums.json</c> beside <c>cover.txt</c>, with the albums' names), so it goes with the livery into the archive,
/// the storage and the backups, and a restore makes an album again that is no longer there
/// </summary>
internal sealed partial class ScreenshotStore
{
    /// <summary>The list of albums at the root, and a livery's albums in its folder</summary>
    public const string AlbumsFileName = "albums.json";

    private static readonly JsonSerializerOptions AlbumJson = new() { WriteIndented = true };

    private List<ScreenshotAlbum>? _albums;
    // A livery's album file, by livery key (the folder names); read again after a change (Forget)
    private Dictionary<string, LiveryAlbums>? _members;

    /// <summary>An album was made, renamed or deleted, or a screenshot went in or out of one</summary>
    public event Action? AlbumsChanged;

    private sealed class AlbumListFile
    {
        public int Version { get; set; } = 1;
        public List<ScreenshotAlbum> Albums { get; set; } = [];
    }

    /// <summary>A livery's albums: which of its screenshots (file names) are in which album, and the albums' names</summary>
    private sealed class LiveryAlbums
    {
        public Dictionary<string, string> Names { get; set; } = [];
        public Dictionary<string, List<string>> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The albums, in their order</summary>
    public IReadOnlyList<ScreenshotAlbum> Albums()
    {
        lock (_lock) return [.. AlbumList()];
    }

    /// <summary>Each album with the number of its screenshots that are still there</summary>
    public IReadOnlyList<ScreenshotAlbumCount> AlbumCounts()
    {
        lock (_lock)
        {
            var counts = AlbumList().ToDictionary(a => a.Id, _ => 0);
            foreach (var (key, livery) in Members())
            {
                var present = PicturesOf(key);
                foreach (var (file, ids) in livery.Files)
                {
                    if (!present.Contains(file)) continue;
                    foreach (string id in ids)
                        if (counts.TryGetValue(id, out int n)) counts[id] = n + 1;
                }
            }
            return [.. AlbumList().Select(a => new ScreenshotAlbumCount(a, counts[a.Id]))];
        }
    }

    /// <summary>The ids of the albums a screenshot is in</summary>
    public IReadOnlySet<string> AlbumsOf(LiveryScreenshot screenshot)
    {
        ArgumentNullException.ThrowIfNull(screenshot);
        lock (_lock)
        {
            return Members().TryGetValue(Normalize(screenshot.LiveryKey), out var livery) && livery.Files.TryGetValue(screenshot.FileName, out var ids)
                ? ids.ToHashSet()
                : new HashSet<string>();
        }
    }

    /// <summary>The albums with screenshots of a livery, and how many of them each has (the livery window)</summary>
    public IReadOnlyList<ScreenshotAlbumCount> AlbumsOfLivery(string liveryKey)
    {
        lock (_lock)
        {
            if (!Members().TryGetValue(Normalize(liveryKey), out var livery)) return [];
            var present = PicturesOf(Normalize(liveryKey));
            var counts = new Dictionary<string, int>();
            foreach (var (file, ids) in livery.Files)
            {
                if (!present.Contains(file)) continue;
                foreach (string id in ids) counts[id] = counts.GetValueOrDefault(id) + 1;
            }
            return [.. AlbumList().Where(a => counts.ContainsKey(a.Id)).Select(a => new ScreenshotAlbumCount(a, counts[a.Id]))];
        }
    }

    /// <summary>Makes an album; null when the name cannot be one or another album has it</summary>
    public ScreenshotAlbum? CreateAlbum(string name)
    {
        if (CollectionNames.Clean(name) is not { } clean) return null;
        ScreenshotAlbum album;
        lock (_lock)
        {
            var list = AlbumList();
            if (CollectionNames.IsTaken(clean, list.Select(a => (a.Id, a.Name)))) return null;
            album = new ScreenshotAlbum(NewAlbumId(list), clean, list.Count == 0 ? 0 : list.Max(a => a.Order) + 1, DateTime.UtcNow);
            list.Add(album);
            if (!SaveAlbumList(list)) { _albums = null; return null; }
        }
        AlbumsChanged?.Invoke();
        return album;
    }

    /// <summary>Gives an album another name; false when the name cannot be one or another album has it</summary>
    public bool RenameAlbum(string id, string name)
    {
        if (CollectionNames.Clean(name) is not { } clean) return false;
        lock (_lock)
        {
            var list = AlbumList();
            int i = list.FindIndex(a => a.Id == id);
            if (i < 0 || CollectionNames.IsTaken(clean, list.Select(a => (a.Id, a.Name)), except: id)) return false;
            list[i] = list[i] with { Name = clean };
            if (!SaveAlbumList(list)) { _albums = null; return false; }
            // The liveries' files keep the name too, for a restore
            foreach (var (key, livery) in Members())
                if (livery.Names.ContainsKey(id)) { livery.Names[id] = clean; SaveLiveryAlbums(key, livery); }
        }
        AlbumsChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// The albums in a new order (the side panel's drag, "Move up", "Move down"): <paramref name="ids"/> first in their
    /// order, an album they leave out after them as it was
    /// </summary>
    public void ReorderAlbums(IReadOnlyList<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        lock (_lock)
        {
            var list = AlbumList();
            var ordered = ids.Select(id => list.FirstOrDefault(a => a.Id == id)).OfType<ScreenshotAlbum>().Distinct()
                .Concat(list.Where(a => !ids.Contains(a.Id)))
                .Select((a, i) => a with { Order = i })
                .ToList();
            if (ordered.Select(a => a.Id).SequenceEqual(list.Select(a => a.Id))) return;
            if (!SaveAlbumList(ordered)) return;
            _albums = ordered;
        }
        AlbumsChanged?.Invoke();
    }

    /// <summary>Deletes an album; its screenshots stay, only out of it</summary>
    public void DeleteAlbum(string id)
    {
        lock (_lock)
        {
            var list = AlbumList();
            if (list.RemoveAll(a => a.Id == id) == 0) return;
            SaveAlbumList(list);
            foreach (var (key, livery) in Members())
            {
                if (!livery.Names.Remove(id) && !livery.Files.Values.Any(ids => ids.Contains(id))) continue;
                foreach (var ids in livery.Files.Values) ids.Remove(id);
                SaveLiveryAlbums(key, livery);
            }
            Forget();
        }
        AlbumsChanged?.Invoke();
    }

    /// <summary>Puts screenshots into an album, or takes them out of it</summary>
    public void SetInAlbum(IEnumerable<LiveryScreenshot> screenshots, string albumId, bool inAlbum)
    {
        ArgumentNullException.ThrowIfNull(screenshots);
        lock (_lock)
        {
            if (AlbumList().FirstOrDefault(a => a.Id == albumId) is not { } album) return;
            foreach (var group in screenshots.GroupBy(s => Normalize(s.LiveryKey), StringComparer.OrdinalIgnoreCase))
            {
                var livery = Members().GetValueOrDefault(group.Key) ?? new LiveryAlbums();
                foreach (var shot in group)
                {
                    if (!livery.Files.TryGetValue(shot.FileName, out var ids)) livery.Files[shot.FileName] = ids = [];
                    if (inAlbum && !ids.Contains(albumId)) ids.Add(albumId);
                    else if (!inAlbum) ids.Remove(albumId);
                    if (ids.Count == 0) livery.Files.Remove(shot.FileName);
                }
                if (livery.Files.Values.Any(ids => ids.Contains(albumId))) livery.Names[albumId] = album.Name;
                else livery.Names.Remove(albumId);
                SaveLiveryAlbums(group.Key, livery);
            }
            Forget();
        }
        AlbumsChanged?.Invoke();
    }

    // A screenshot was deleted (renamedTo null) or re-encoded under another name: its albums follow. Under the lock
    private void ForgetInAlbums(string liveryKey, string fileName, string? renamedTo)
    {
        string key = Normalize(liveryKey);
        if (!Members().TryGetValue(key, out var livery) || !livery.Files.Remove(fileName, out var ids)) return;
        if (renamedTo is not null) livery.Files[renamedTo] = ids;
        else
            foreach (string id in livery.Names.Keys.Where(id => !livery.Files.Values.Any(f => f.Contains(id))).ToList())
                livery.Names.Remove(id);
        SaveLiveryAlbums(key, livery);
    }

    // After a restore: an album the livery's file names that the list no longer has is made again (same id), or joins
    // the album of the same name. Under the lock
    private void AdoptAlbums(string liveryKey)
    {
        string key = Normalize(liveryKey);
        var livery = ReadLiveryAlbums(key);
        if (livery is null || livery.Names.Count == 0) return;
        var list = AlbumList();
        bool listChanged = false, liveryChanged = false;
        foreach (var (id, name) in livery.Names.ToList())
        {
            if (list.Any(a => a.Id == id)) continue;
            if (list.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) is { } same)
            {
                livery.Names.Remove(id);
                livery.Names[same.Id] = same.Name;
                foreach (var ids in livery.Files.Values)
                    if (ids.Remove(id) && !ids.Contains(same.Id)) ids.Add(same.Id);
                liveryChanged = true;
            }
            else if (CollectionNames.Clean(name) is { } clean)
            {
                list.Add(new ScreenshotAlbum(id, clean, list.Count == 0 ? 0 : list.Max(a => a.Order) + 1, DateTime.UtcNow));
                listChanged = true;
            }
        }
        if (listChanged) SaveAlbumList(list);
        if (liveryChanged) SaveLiveryAlbums(key, livery);
        Forget();
    }

    private static string NewAlbumId(List<ScreenshotAlbum> list)
    {
        string id;
        do id = "a_" + Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4));
        while (list.Any(a => a.Id == id));
        return id;
    }

    // The file names of a livery's pictures that are there. Under the lock
    private HashSet<string> PicturesOf(string key)
    {
        string folder = System.IO.Path.Combine(Root, key);
        try
        {
            return Directory.Exists(folder)
                ? new HashSet<string>(Directory.EnumerateFiles(folder).Select(System.IO.Path.GetFileName).OfType<string>().Where(IsPicture), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    // Under the lock
    private List<ScreenshotAlbum> AlbumList()
    {
        if (_albums is not null) return _albums;
        string path = System.IO.Path.Combine(Root, AlbumsFileName);
        var albums = new List<ScreenshotAlbum>();
        try
        {
            if (File.Exists(path))
                albums = JsonSerializer.Deserialize<AlbumListFile>(File.ReadAllText(path), AlbumJson)?.Albums
                    .Where(a => !string.IsNullOrEmpty(a.Id) && CollectionNames.Clean(a.Name) is not null)
                    .DistinctBy(a => a.Id)
                    .ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken list is kept aside as a copy; the albums come back from the names in the liveries' files
            AppLog.LogError($"Failed to read the albums '{path}'", ex);
            AtomicFile.TryBackupCorruptedFile(path);
            int order = 0;
            albums = [.. Members().Values.SelectMany(l => l.Names)
                .DistinctBy(n => n.Key)
                .Where(n => CollectionNames.Clean(n.Value) is not null)
                .DistinctBy(n => n.Value, StringComparer.OrdinalIgnoreCase)
                .Select(n => new ScreenshotAlbum(n.Key, n.Value.Trim(), order++, DateTime.UtcNow))];
        }
        return _albums = [.. albums.OrderBy(a => a.Order)];
    }

    private bool SaveAlbumList(List<ScreenshotAlbum> list)
    {
        try
        {
            Directory.CreateDirectory(Root);
            AtomicFile.WriteAllBytes(System.IO.Path.Combine(Root, AlbumsFileName),
                JsonSerializer.SerializeToUtf8Bytes(new AlbumListFile { Albums = list }, AlbumJson));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogError("Failed to write the albums of the screenshots", ex);
            return false;
        }
    }

    // Every livery's album file, by key. Under the lock
    private Dictionary<string, LiveryAlbums> Members()
    {
        if (_members is not null) return _members;
        var members = new Dictionary<string, LiveryAlbums>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(Root))
                foreach (var dir in new DirectoryInfo(Root).EnumerateDirectories())
                    if (ReadLiveryAlbums(dir.Name) is { } livery) members[dir.Name] = livery;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogErrorThrottled("albums|" + Root, $"Failed to read the albums in '{Root}'", ex);
            // Not kept: the next asks again
            return members;
        }
        return _members = members;
    }

    private LiveryAlbums? ReadLiveryAlbums(string key)
    {
        string path = System.IO.Path.Combine(Root, key, AlbumsFileName);
        try
        {
            if (!File.Exists(path)) return null;
            var livery = JsonSerializer.Deserialize<LiveryAlbums>(File.ReadAllText(path), AlbumJson);
            if (livery is null) return null;
            livery.Files = new Dictionary<string, List<string>>(livery.Files ?? [], StringComparer.OrdinalIgnoreCase);
            livery.Names ??= [];
            return livery;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.LogErrorThrottled(path, $"Failed to read the albums of '{key}'", ex);
            return null;
        }
    }

    private void SaveLiveryAlbums(string key, LiveryAlbums livery)
    {
        string folder = System.IO.Path.Combine(Root, key);
        string path = System.IO.Path.Combine(folder, AlbumsFileName);
        try
        {
            if (livery.Files.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            if (!Directory.Exists(folder)) return; // the livery's screenshots are gone
            AtomicFile.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(livery, AlbumJson));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogError($"Failed to write the albums of '{key}'", ex);
        }
    }
}
