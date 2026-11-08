using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaToolkit.Liveries.Statistics;
using LiveryGallery.Controls;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

internal sealed partial class StatisticsViewModel
{
    private const int CalendarLength = 371; // 53 weeks

    private IReadOnlyList<HistoryMonth> _months = [];
    private IReadOnlyList<MonthCount> _createdMonths = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRangeYear), nameof(IsRangeTwoYears), nameof(IsRangeAll))]
    private HistoryRange _range = HistoryRange.TwoYears;

    public bool IsRangeYear => Range == HistoryRange.Year;
    public bool IsRangeTwoYears => Range == HistoryRange.TwoYears;
    public bool IsRangeAll => Range == HistoryRange.All;

    partial void OnRangeChanged(HistoryRange value) => ApplyRange();

    [RelayCommand]
    private void SelectRange(HistoryRange range) => Range = range;

    [ObservableProperty]
    private IReadOnlyList<StatTileViewModel> _recentTiles = [];

    [ObservableProperty]
    private string _journalNote = "";

    [ObservableProperty]
    private IReadOnlyList<ChartColumn> _flowColumns = [];

    [ObservableProperty]
    private IReadOnlyList<ChartColumn> _sizeColumns = [];

    [ObservableProperty]
    private IReadOnlyList<ChartColumn> _authorColumns = [];

    [ObservableProperty]
    private IReadOnlyList<ChartColumn> _createdColumns = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeAndAuthorsColumns))]
    private bool _hasSize;

    /// <summary>The size and new-authors cards share a row; without the size chart the new authors take all of it</summary>
    public int SizeAndAuthorsColumns => HasSize ? 2 : 1;

    [ObservableProperty]
    private bool _hasCreated;

    [ObservableProperty]
    private string _ageText = "";

    [ObservableProperty]
    private IReadOnlyList<DayCount> _calendarDays = [];

    [ObservableProperty]
    private string _calendarNote = "";

    [ObservableProperty]
    private IReadOnlyList<int> _weekdayValues = [];

    [ObservableProperty]
    private IReadOnlyList<string> _weekdayTooltips = [];

    public IReadOnlyList<string> FlowUpSeries { get; } = ["faint", "accent", "aqua"];
    public IReadOnlyList<string> FlowDownSeries { get; } = ["orange", "blue"];
    public IReadOnlyList<string> SingleSeries { get; } = ["accent"];

    private static readonly IReadOnlyList<ChartLegendItem> GameFlowLegend =
    [
        new("accent", Strings.StatsLegendNew),
        new("faint", Strings.StatsLegendEstimated),
        new("aqua", Strings.StatsLegendReturned),
        new("orange", Strings.StatsLegendDeleted),
        new("blue", Strings.StatsLegendArchived),
    ];

    private static readonly IReadOnlyList<ChartLegendItem> StorageFlowLegend = [new("accent", Strings.StatsLegendStored)];

    /// <summary>The storage's flow is the liveries that went there, month by month</summary>
    public IReadOnlyList<ChartLegendItem> FlowLegend => IsStorage ? StorageFlowLegend : GameFlowLegend;

    /// <summary>The storage's time page: its liveries by the day each went there (<see cref="LiveryEntry.StoredAtUtc"/>)</summary>
    private void BuildStorageTime()
    {
        var dates = _items.Select(e => e.StoredAtUtc?.ToLocalTime()).OfType<DateTime>().ToList();
        var byMonth = dates.GroupBy(d => new DateOnly(d.Year, d.Month, 1)).ToDictionary(g => g.Key, g => g.Count());
        var months = new List<HistoryMonth>();
        if (byMonth.Count > 0)
            for (var m = byMonth.Keys.Min(); m <= byMonth.Keys.Max(); m = m.AddMonths(1))
                months.Add(new HistoryMonth(m, byMonth.GetValueOrDefault(m), 0, 0, 0, null, 0, 0));
        _months = months;
        RecentTiles = [];
        JournalNote = Strings.StatsStorageTimeNote;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var first = today.AddDays(-(CalendarLength - 1));
        var byDay = dates.GroupBy(DateOnly.FromDateTime).ToDictionary(g => g.Key, g => g.Count());
        var days = new List<DayCount>();
        for (var day = first; day <= today; day = day.AddDays(1)) days.Add(new DayCount(day, byDay.GetValueOrDefault(day)));
        CalendarDays = days;
        CalendarNote = string.Format(Strings.StatsCalendarNoteFormat, N(days.Sum(d => d.Count)), N(days.Count(d => d.Count > 0)));

        var weekdays = new int[7];
        foreach (var d in dates) weekdays[((int)d.DayOfWeek + 6) % 7]++;
        var culture = AppLocalisationService.Culture;
        var monday = new DateOnly(2024, 1, 1);
        WeekdayValues = weekdays;
        WeekdayTooltips = [.. Enumerable.Range(0, 7).Select(d => $"{monday.AddDays(d).ToString("dddd", culture)}: {N(weekdays[d])}")];
    }

    public string CalendarTooltipFormat => Strings.StatsCalendarTooltipFormat;

    private void BuildTime()
    {
        var now = DateTime.UtcNow;
        if (IsStorage)
        {
            BuildStorageTime();
            BuildCreated();
            ApplyRange();
            return;
        }
        if (_events.Count == 0)
        {
            _months = [.. LiveryStatistics.ByDownloadMonth(_items).Select(m => new HistoryMonth(m.Month, m.Count, 0, 0, 0, null, 0, m.Count))];
            RecentTiles = [];
            JournalNote = Strings.StatsNoJournal;
        }
        else
        {
            var timeline = LiveryTimelineCalculator.Calculate(_events, _timelineOptions);
            _months = timeline.Months;
            var last = LiveryTimelineCalculator.Summarize(_events, now - RecentPeriod, now.AddMinutes(1), _timelineOptions);
            var before = LiveryTimelineCalculator.Summarize(_events, now - 2 * RecentPeriod, now - RecentPeriod, _timelineOptions);
            RecentTiles =
            [
                Recent(last.Arrivals, before.Arrivals, Strings.StatsLegendNew),
                Recent(last.Returns, before.Returns, Strings.StatsLegendReturned),
                Recent(last.Departures, before.Departures, Strings.StatsLegendDeleted),
                Recent(last.Archived, before.Archived, Strings.StatsLegendArchived),
                Recent(last.NewAuthors, before.NewAuthors, Strings.StatsNewAuthorsTitle),
            ];
            string since = timeline.JournalStartUtc is { } start ? start.ToLocalTime().ToString("d", AppLocalisationService.Culture) : "—";
            JournalNote = string.Format(Strings.StatsJournalSinceFormat, since) + (IsShownScope ? " " + Strings.StatsShownHistoryNote : "");
        }

        BuildCreated();

        var today = DateOnly.FromDateTime(DateTime.Now);
        CalendarDays = LiveryStatistics.ByDay(_items, today.AddDays(-(CalendarLength - 1)), today);
        int lastYear = CalendarDays.Sum(d => d.Count);
        int activeDays = CalendarDays.Count(d => d.Count > 0);
        CalendarNote = string.Format(Strings.StatsCalendarNoteFormat, N(lastYear), N(activeDays));

        var culture = AppLocalisationService.Culture;
        var weekdays = LiveryStatistics.ByWeekday(_items);
        var monday = new DateOnly(2024, 1, 1);
        WeekdayValues = weekdays;
        WeekdayTooltips = [.. Enumerable.Range(0, 7).Select(d => $"{monday.AddDays(d).ToString("dddd", culture)}: {N(weekdays[d])}")];

        ApplyRange();
    }

    // When the liveries were made, and their age when downloaded: the same in the game and the storage
    private void BuildCreated()
    {
        _createdMonths = LiveryStatistics.ByCreatedMonth(_items);
        HasCreated = _createdMonths.Count > 0;
        var age = LiveryStatistics.AgeAtDownload(_items);
        AgeText = age.MedianMonths is { } median
            ? string.Format(Strings.StatsAgeFormat, median.ToString("0.#", AppLocalisationService.Culture),
                Percent((double)age.SameMonth / age.Liveries), Percent((double)age.OlderThanYear / age.Liveries))
            : "";
    }

    private static StatTileViewModel Recent(int value, int before, string label)
    {
        int delta = value - before;
        string detail = delta == 0 ? Strings.StatsSameAsBefore
            : string.Format(delta > 0 ? Strings.StatsMoreThanBeforeFormat : Strings.StatsLessThanBeforeFormat, N(Math.Abs(delta)), N(before));
        return new StatTileViewModel(N(value), label, detail, Strings.StatsComparedTooltip, null);
    }

    private void ApplyRange()
    {
        int take = Range switch
        {
            HistoryRange.Year => 12,
            HistoryRange.TwoYears => 24,
            _ => int.MaxValue,
        };
        var months = _months.Count > take ? _months.Skip(_months.Count - take).ToList() : _months;
        var culture = AppLocalisationService.Culture;
        FlowColumns = [.. months.Select(m => new ChartColumn(m.Month, [m.EstimatedArrivals, m.Arrivals - m.EstimatedArrivals, m.Returns],
            [m.Departures, m.Archived], null, false,
            Tooltip(m, culture,
                $"{Strings.StatsLegendNew}: {N(m.Arrivals)}",
                $"{Strings.StatsLegendReturned}: {N(m.Returns)}",
                $"{Strings.StatsLegendDeleted}: {N(m.Departures)}",
                $"{Strings.StatsLegendArchived}: {N(m.Archived)}")))];
        SizeColumns = [.. months.Select(m => new ChartColumn(m.Month, [], [], m.SizeAtEnd, false,
            m.SizeAtEnd is { } size
                ? $"{m.Month.ToString("Y", culture)}\n{string.Format(Strings.StatsSizeTooltipFormat, N(size))}"
                : $"{m.Month.ToString("Y", culture)}\n{Strings.StatsSizeUnknown}"))];
        HasSize = months.Count(m => m.SizeAtEnd.HasValue) >= 2;
        AuthorColumns = [.. months.Select(m => new ChartColumn(m.Month, [m.NewAuthors], [], null, m.Arrivals == m.EstimatedArrivals && m.Estimated,
            Tooltip(m, culture, $"{Strings.StatsNewAuthorsTitle}: {N(m.NewAuthors)}")))];
        var created = _createdMonths.Count > take ? _createdMonths.Skip(_createdMonths.Count - take).ToList() : _createdMonths;
        CreatedColumns = [.. created.Select(m => new ChartColumn(m.Month, [m.Count], [], null, false,
            $"{m.Month.ToString("Y", culture)}\n{string.Format(Strings.StatsCreatedTooltipFormat, N(m.Count))}"))];
    }

    private static string Tooltip(HistoryMonth month, CultureInfo culture, params string[] lines)
    {
        var text = month.Month.ToString("Y", culture) + "\n" + string.Join("\n", lines);
        return month.Estimated
            ? text + "\n\n" + string.Format(Strings.StatsEstimatedTooltipFormat, N(month.EstimatedArrivals))
            : text;
    }
}
