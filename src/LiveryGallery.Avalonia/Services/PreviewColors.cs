using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using ForzaToolkit.Core.IO;

namespace LiveryGallery.Services;

/// <summary>
/// The average colour of each preview, so a card shows it while its picture loads instead of a grey box: the list
/// scrolls calmer. Learnt when a preview is made (ThumbnailService) or first decoded (ThumbnailCacheService), kept per
/// game in <c>games\&lt;game&gt;\preview-colors.json</c> by the preview's path; the colours of previews no longer there are
/// dropped when the file is read
/// </summary>
internal static class PreviewColors
{
    /// <summary>The file's name in the game's folder</summary>
    public const string FileName = "preview-colors.json";

    private static readonly ConcurrentDictionary<string, uint> _colors = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<uint, IBrush> _brushes = new();
    private static string? _file;

    /// <summary>Reads the game's colours (GameSessions, as the game opens); the previews gone are dropped</summary>
    public static void Load(string gameRoot)
    {
        _file = Path.Combine(gameRoot, FileName);
        _colors.Clear();
        var read = JsonFiles.ReadOrSetAside(_file, text => JsonSerializer.Deserialize<Dictionary<string, uint>>(text), null, "preview colours");
        if (read is null) return;
        int dropped = 0;
        foreach (var (path, color) in read)
        {
            if (File.Exists(path)) _colors[path] = color;
            else dropped++;
        }
        if (dropped > 0) Save();
    }

    /// <summary>The brush of a preview's average colour; null when it is not known yet</summary>
    public static IBrush? BrushOf(string? previewPath)
    {
        if (previewPath is null || !_colors.TryGetValue(previewPath, out uint argb)) return null;
        return _brushes.GetOrAdd(argb, c => new ImmutableSolidColorBrush(Color.FromUInt32(c)));
    }

    /// <summary>True when the preview's colour is known</summary>
    public static bool Knows(string previewPath) => _colors.ContainsKey(previewPath);

    /// <summary>Learns the average colour of a preview from its decoded picture; nothing when it is known already</summary>
    public static void Learn(string previewPath, Bitmap bitmap)
    {
        if (_file is null || _colors.ContainsKey(previewPath)) return;
        if (Average(bitmap) is not { } color) return;
        _colors[previewPath] = color;
        Save();
    }

    /// <summary>
    /// The picture's average colour, opaque, from a few of its rows (every 8th pixel of 8 rows spread over it): a few
    /// kilobytes copied, not the whole picture. Null when its pixels cannot be read
    /// </summary>
    internal static uint? Average(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        if (size.Width <= 0 || size.Height <= 0) return null;
        int stride = size.Width * 4;
        var row = new byte[stride];
        long r = 0, g = 0, b = 0, n = 0;
        var handle = GCHandle.Alloc(row, GCHandleType.Pinned);
        try
        {
            int rows = Math.Min(8, size.Height);
            for (int k = 0; k < rows; k++)
            {
                int y = (int)((k + 0.5) * size.Height / rows);
                bitmap.CopyPixels(new PixelRect(0, y, size.Width, 1), handle.AddrOfPinnedObject(), stride, stride);
                bool bgra = bitmap.Format is not { } format || format == PixelFormat.Bgra8888;
                for (int x = 0; x < size.Width; x += 8)
                {
                    int i = x * 4;
                    byte a = row[i + 3];
                    if (a == 0) continue;
                    // Premultiplied or not, a near-opaque preview averages the same; transparent pixels are left out
                    r += bgra ? row[i + 2] : row[i];
                    g += row[i + 1];
                    b += bgra ? row[i] : row[i + 2];
                    n++;
                }
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
        finally
        {
            handle.Free();
        }
        if (n == 0) return null;
        return 0xFF000000u | (uint)(r / n) << 16 | (uint)(g / n) << 8 | (uint)(b / n);
    }

    private static int _saveWaiting;

    // Once every two seconds at most: the first scroll through a list learns a colour a card, and the file is written
    // once for all of them (a colour lost when the app closes in between is learnt again)
    private static void Save()
    {
        if (_file is null || Interlocked.Exchange(ref _saveWaiting, 1) == 1) return;
        _ = Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(_ =>
        {
            Interlocked.Exchange(ref _saveWaiting, 0);
            if (_file is not { } file) return;
            var snapshot = new Dictionary<string, uint>(_colors, StringComparer.OrdinalIgnoreCase);
            DebouncedFileWriter.Shared.Schedule(file, JsonSerializer.Serialize(snapshot));
        }, TaskScheduler.Default);
    }
}
