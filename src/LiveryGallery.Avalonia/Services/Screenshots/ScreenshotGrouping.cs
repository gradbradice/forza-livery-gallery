using System.Globalization;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Services.Screenshots;

/// <summary>How the "Screenshots" view groups the screenshots</summary>
internal enum ScreenshotGroupMode
{
    /// <summary>A group per livery, the livery with the newest screenshot first</summary>
    ByLivery,

    /// <summary>The groups of the liveries' grid as it is sorted now (months, makers, authors…)</summary>
    LikeLiveries,

    /// <summary>By the day each screenshot was added: today, yesterday, the days of the last month, then months</summary>
    ByDate,
}

/// <summary>A group of the "Screenshots" view: its heading and its screenshots in order</summary>
/// <param name="Key">Kept while the view is open, for the groups folded</param>
/// <param name="Livery">The livery of a group <see cref="ScreenshotGroupMode.ByLivery"/>; null otherwise</param>
/// <param name="LiveryCount">How many liveries its screenshots are of</param>
internal sealed record ScreenshotGroupPlan(string Key, string Title, string? Subtitle, LiveryEntry? Livery, int LiveryCount, IReadOnlyList<ScreenshotTile> Tiles);

/// <summary>The words of the groups, from the user's language</summary>
internal sealed record ScreenshotGroupTexts(string Today, string Yesterday, CultureInfo Culture, string MonthYearFormat);

/// <summary>
/// The groups of the "Screenshots" view, and each tile's line (<see cref="ScreenshotTile.Caption"/>): the time in a
/// livery's group, the livery elsewhere. Pure but for the captions it sets
/// </summary>
internal static class ScreenshotGrouping
{
    /// <summary>A day older than this is shown in its month's group</summary>
    public const int DaysShownByDay = 30;

    /// <param name="tiles">The screenshots shown (their liveries pass the filters and the search)</param>
    /// <param name="liveryGroups">The liveries' grid's groups in their order (for <see cref="ScreenshotGroupMode.LikeLiveries"/>)</param>
    /// <param name="todayLocal">The local date now</param>
    public static List<ScreenshotGroupPlan> Group(IReadOnlyList<ScreenshotTile> tiles, ScreenshotGroupMode mode, bool newestFirst,
        IReadOnlyList<(string Key, IReadOnlyList<LiveryEntry> Items)> liveryGroups, DateTime todayLocal, ScreenshotGroupTexts texts)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(liveryGroups);
        ArgumentNullException.ThrowIfNull(texts);
        IEnumerable<ScreenshotTile> Ordered(IEnumerable<ScreenshotTile> t) =>
            newestFirst ? t.OrderByDescending(s => s.Screenshot.AddedUtc) : t.OrderBy(s => s.Screenshot.AddedUtc);
        string Time(ScreenshotTile t) => t.Screenshot.AddedUtc.ToLocalTime().ToString("g", texts.Culture);

        switch (mode)
        {
            case ScreenshotGroupMode.ByLivery:
            {
                var groups = tiles.GroupBy(t => t.Entry.FolderName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => (Entry: g.First().Entry, Tiles: Ordered(g).ToList()));
                groups = newestFirst
                    ? groups.OrderByDescending(g => g.Tiles.Max(t => t.Screenshot.AddedUtc))
                    : groups.OrderBy(g => g.Tiles.Min(t => t.Screenshot.AddedUtc));
                var result = new List<ScreenshotGroupPlan>();
                foreach (var (entry, list) in groups)
                {
                    foreach (var t in list) t.Caption = Time(t);
                    result.Add(new ScreenshotGroupPlan("livery|" + entry.FolderName, entry.LiveryName, CarText(entry), entry, 1, list));
                }
                return result;
            }
            case ScreenshotGroupMode.LikeLiveries:
            {
                var byKey = tiles.ToLookup(t => t.Entry.FolderName, StringComparer.OrdinalIgnoreCase);
                var result = new List<ScreenshotGroupPlan>();
                var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, items) in liveryGroups)
                {
                    var list = new List<ScreenshotTile>();
                    int liveries = 0;
                    foreach (var entry in items)
                    {
                        if (!placed.Add(entry.FolderName) || !byKey.Contains(entry.FolderName)) continue;
                        liveries++;
                        list.AddRange(Ordered(byKey[entry.FolderName]));
                    }
                    if (list.Count == 0) continue;
                    foreach (var t in list) t.Caption = t.Entry.LiveryName;
                    result.Add(new ScreenshotGroupPlan("group|" + key, key, null, null, liveries, list));
                }
                return result;
            }
            default:
            {
                DateTime today = todayLocal.Date;
                string Day(DateTime local)
                {
                    var day = local.Date;
                    if (day == today) return texts.Today;
                    if (day == today.AddDays(-1)) return texts.Yesterday;
                    if (day > today.AddDays(-DaysShownByDay)) return day.ToString("D", texts.Culture);
                    string month = day.ToString(texts.MonthYearFormat, texts.Culture);
                    return month.Length > 0 ? char.ToUpper(month[0], texts.Culture) + month[1..] : month;
                }
                var result = new List<ScreenshotGroupPlan>();
                foreach (var g in Ordered(tiles).GroupBy(t => Day(t.Screenshot.AddedUtc.ToLocalTime())))
                {
                    var list = g.ToList();
                    foreach (var t in list) t.Caption = $"{t.Entry.LiveryName} · {t.Screenshot.AddedUtc.ToLocalTime().ToString("t", texts.Culture)}";
                    result.Add(new ScreenshotGroupPlan("day|" + g.Key, g.Key, null, null,
                        list.Select(t => t.Entry.FolderName).Distinct(StringComparer.OrdinalIgnoreCase).Count(), list));
                }
                return result;
            }
        }
    }

    /// <summary>"Toyota Sera (1991)"</summary>
    public static string CarText(LiveryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string car = $"{entry.CarManufacturer} {entry.CarModelName}".Trim();
        return entry.CarYear is { } year ? $"{car} ({year})" : car;
    }
}
