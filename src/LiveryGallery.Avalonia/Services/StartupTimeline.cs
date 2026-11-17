using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace LiveryGallery.Services;

/// <summary>
/// How long each step of the start took, from the process's start to the gallery's window: told in the log when the
/// whole start is slow (<see cref="SlowStart"/>), so a user's "it takes ages to open" shows where the time went. The
/// log keeps only warnings and errors; a quick start leaves nothing in it.
/// <para>
/// A step is named by what ended it (<see cref="Mark"/>): "Main" is the time before the program's own code ran (the
/// runtime loading, an antivirus reading the files), "updates" the updater's step, and so on
/// </para>
/// </summary>
internal sealed class StartupTimeline
{
    private readonly Func<DateTime> _now;
    private readonly DateTime _start;
    private readonly List<(string Step, DateTime At)> _marks = [];
    private bool _reported;

    /// <param name="start">The process's start (UTC)</param>
    /// <param name="now">The clock (UTC)</param>
    public StartupTimeline(DateTime start, Func<DateTime> now)
    {
        _start = start;
        _now = now;
    }

    /// <summary>A start this long or longer is told in the log</summary>
    public static TimeSpan SlowStart { get; } = TimeSpan.FromSeconds(5);

    /// <summary>The program's own start, counted from its process's</summary>
    public static StartupTimeline Current { get; } = new(ProcessStart(), () => DateTime.UtcNow);

    /// <summary>The step that ends now</summary>
    public void Mark(string step)
    {
        lock (_marks) _marks.Add((step, _now()));
    }

    /// <summary>From the process's start to the last step</summary>
    public TimeSpan Total
    {
        get { lock (_marks) return _marks.Count == 0 ? TimeSpan.Zero : _marks[^1].At - _start; }
    }

    /// <summary>
    /// The report of a slow start, once: "Slow start (not an error): 12.4 s to the gallery's window — Main 3.1 s,
    /// updates 0.1 s, …"; null when the start was quick or has been told already
    /// </summary>
    public string? SlowStartReport()
    {
        lock (_marks)
        {
            if (_reported || _marks.Count == 0 || Total < SlowStart) return null;
            _reported = true;
            var text = new StringBuilder("Slow start (not an error): ").Append(Seconds(Total)).Append(" to the gallery's window — ");
            var previous = _start;
            for (int i = 0; i < _marks.Count; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(_marks[i].Step).Append(' ').Append(Seconds(_marks[i].At - previous));
                previous = _marks[i].At;
            }
            return text.ToString();
        }
    }

    private static string Seconds(TimeSpan time) => Math.Max(0, time.TotalSeconds).ToString("0.0", CultureInfo.InvariantCulture) + " s";

    private static DateTime ProcessStart()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return DateTime.UtcNow;
        }
    }
}
