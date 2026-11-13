using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace LiveryGallery.Services.Diagnostics;

/// <summary>
/// Tells in the log about a window that something still holds after it closed: everything the window references (its
/// pictures, its view model, the liveries it showed) stays in memory with it. Every window of the app is watched, held
/// weakly; one still alive a minute after closing and after two full collections since is written once, with its type
/// (not an error: the memory it keeps is what to look at). Started once, on the UI thread
/// </summary>
internal static class ClosedWindowWatch
{
    // A full collection has to have run (twice: a finalizer may hold the window for one) and this long has to have
    // passed: an awaited dialog is read by its caller just after closing
    internal static readonly TimeSpan HeldAfter = TimeSpan.FromMinutes(1);
    internal const int CollectionsBeforeTelling = 2;

    // Without full collections there is nothing to tell: one not collected by then is forgotten
    internal static readonly TimeSpan ForgetAfter = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    private sealed record Closed(string Name, WeakReference Window, DateTime AtUtc, int FullCollections);

    private static readonly List<Closed> _closed = [];
    private static DispatcherTimer? _timer;
    private static bool _started;

    /// <summary>Starts watching every window that closes from now on</summary>
    public static void Start()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_started) return;
        _started = true;
        Window.WindowClosedEvent.AddClassHandler<Window>((window, _) => Watch(window), RoutingStrategies.Direct);
    }

    /// <summary>Watches <paramref name="window"/>, which has just closed</summary>
    internal static void Watch(Window window)
    {
        // The main window closes the app: nothing is left to tell. Unless another game's window takes its place: then
        // the app goes on, and a main window still held keeps the whole gallery of the game it showed
        if (window is Views.MainWindow { IsSwitchingGame: false }) return;
        Track(window.GetType().Name, window);
    }

    /// <summary>Watches <paramref name="closed"/>, told as <paramref name="name"/></summary>
    internal static void Track(string name, object closed)
    {
        _closed.Add(new Closed(name, new WeakReference(closed), DateTime.UtcNow, GC.CollectionCount(2)));
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = CheckInterval };
            _timer.Tick += (_, _) => Check(DateTime.UtcNow, GC.CollectionCount(2), AppLog.LogWarning);
        }
        _timer.Start();
    }

    /// <summary>The windows watched now, by type (tests)</summary>
    internal static IReadOnlyList<string> Watched => [.. _closed.Select(c => c.Name)];

    /// <summary>Goes over the windows closed: tells the ones still held, forgets the ones gone</summary>
    /// <param name="nowUtc">Now</param>
    /// <param name="fullCollections">Full collections run since the start (<see cref="GC.CollectionCount"/> of 2)</param>
    /// <param name="tell">Where a window still held is told</param>
    internal static void Check(DateTime nowUtc, int fullCollections, Action<string> tell)
    {
        for (int i = _closed.Count - 1; i >= 0; i--)
        {
            var c = _closed[i];
            if (!c.Window.IsAlive) { _closed.RemoveAt(i); continue; }
            var since = nowUtc - c.AtUtc;
            int collections = fullCollections - c.FullCollections;
            if (since >= HeldAfter && collections >= CollectionsBeforeTelling)
            {
                tell($"Closed window {c.Name} is still held {since.TotalSeconds:0} s after closing, after {collections} full "
                    + "collections (not an error: the memory it references is not given back)");
                _closed.RemoveAt(i);
            }
            else if (since >= ForgetAfter)
            {
                _closed.RemoveAt(i);
            }
        }
        if (_closed.Count == 0) _timer?.Stop();
    }

    /// <summary>Forgets every window watched (tests)</summary>
    internal static void Clear()
    {
        _closed.Clear();
        _timer?.Stop();
    }
}
