using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>
/// Selecting cards with a rectangle, as in Explorer (<see cref="GalleryBand"/>): a press on the empty space of the
/// gallery and a drag draw it, every card it touches is selected, Ctrl or Shift keep what was selected before. Dragged
/// to the top or the bottom of the list, it scrolls the list and takes in the rows that come; the wheel scrolls under
/// it too. Esc drops the rectangle and puts the selection back as it was
/// </summary>
internal partial class MainWindow
{
    // A press that may become a rectangle, and the rectangle once the pointer went far enough
    private sealed class Band
    {
        public required IPointer Pointer { get; init; }
        public required Point PressedInList { get; init; }  // in the panel's coordinates: stays with the list as it scrolls
        public required Point PressedInHost { get; init; }
        public required IReadOnlyList<LiveryEntry> Before { get; init; }
        public Point NowInHost;
        public bool Drawn;
    }

    /// <summary>The settings of scrolling with the wheel pressed, given to the lists that have it</summary>
    private void ApplyWheelScrollSettings()
    {
        foreach (var list in new[] { GalleryScroll, ScreenshotsScroll })
        {
            ForzaToolkit.UI.Input.MiddleButtonScroll.SetAllowClick(list, _settings.WheelClickScroll);
            ForzaToolkit.UI.Input.MiddleButtonScroll.SetSpeedFactor(list, ViewModels.SettingsViewModel.ClampWheelSpeed(_settings.WheelScrollSpeedPercent) / 100.0);
        }
    }

    private Band? _band;
    private DispatcherTimer? _bandScroll;
    private long _bandScrollLast;

    private void InitializeBand()
    {
        GalleryHost.AddHandler(PointerMovedEvent, Band_PointerMoved, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        GalleryHost.AddHandler(PointerReleasedEvent, (_, _) => EndBand(keep: true), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        GalleryHost.PointerCaptureLost += (_, _) => EndBand(keep: true);
        GalleryScroll.ScrollChanged += (_, _) => UpdateBand();
        // The list turned with the wheel under a search: the search was looked through (MainWindow.FilterPresets.cs)
        GalleryScroll.AddHandler(PointerWheelChangedEvent, (_, _) => SearchWasUsed(), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>A rectangle is being dragged now (Esc drops it before it does anything else)</summary>
    private bool BandDrawn => _band is { Drawn: true };

    // A left press on the empty space of the gallery (GalleryHost_PointerPressed): nothing is drawn until the pointer moves
    private void PressForBand(PointerPressedEventArgs e)
    {
        if (_showingScreenshots || !GalleryScroll.IsVisible) return;
        EndBand(keep: true);
        bool keep = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0;
        _band = new Band
        {
            Pointer = e.Pointer,
            PressedInList = e.GetPosition(GalleryPanel),
            PressedInHost = e.GetPosition(GalleryHost),
            Before = keep ? [.. _mainViewModel.Gallery.SelectedEntries] : [],
            NowInHost = e.GetPosition(GalleryHost),
        };
    }

    private void Band_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_band is not { } band || !ReferenceEquals(band.Pointer, e.Pointer)) return;
        if (!e.GetCurrentPoint(GalleryHost).Properties.IsLeftButtonPressed)
        {
            EndBand(keep: true); // the button was let go somewhere that did not tell us
            return;
        }
        band.NowInHost = e.GetPosition(GalleryHost);
        if (!band.Drawn)
        {
            if (!GalleryBand.Started(band.PressedInHost, band.NowInHost)) return;
            band.Drawn = true;
            e.Pointer.Capture(GalleryHost); // the drag goes on over the cards and outside the list
            _bandScroll ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => ScrollUnderBand());
            _bandScrollLast = Stopwatch.GetTimestamp();
            _bandScroll.Start();
        }
        UpdateBand();
    }

    // The rectangle from the press (a place in the list) to the pointer (a place on screen), and the cards in it
    private void UpdateBand()
    {
        if (_band is not { Drawn: true } band) return;
        if (GalleryHost.TranslatePoint(band.NowInHost, GalleryPanel) is not { } nowInList) return;
        var rect = GalleryBand.Between(band.PressedInList, nowInList);
        _mainViewModel.Gallery.SelectBand(band.Before, GalleryPanel.EntriesIn(rect));

        if (GalleryPanel.TranslatePoint(rect.TopLeft, BandLayer) is not { } shown) return;
        Canvas.SetLeft(BandRect, shown.X);
        Canvas.SetTop(BandRect, shown.Y);
        BandRect.Width = rect.Width;
        BandRect.Height = rect.Height;
        BandRect.IsVisible = true;
    }

    // The pointer at the top or the bottom of the list, or past it: the list scrolls and the rectangle takes in more
    private void ScrollUnderBand()
    {
        if (_band is not { Drawn: true } band) return;
        long now = Stopwatch.GetTimestamp();
        double seconds = Math.Min(0.1, Stopwatch.GetElapsedTime(_bandScrollLast, now).TotalSeconds);
        _bandScrollLast = now;
        if (GalleryHost.TranslatePoint(band.NowInHost, GalleryScroll) is not { } inScroll) return;
        double speed = GalleryBand.ScrollSpeed(inScroll.Y, GalleryScroll.Bounds.Height);
        if (speed == 0) return;
        double max = Math.Max(0, GalleryScroll.Extent.Height - GalleryScroll.Viewport.Height);
        double y = Math.Clamp(GalleryScroll.Offset.Y + speed * seconds, 0, max);
        if (y != GalleryScroll.Offset.Y) GalleryScroll.Offset = new Vector(GalleryScroll.Offset.X, y); // ScrollChanged updates the rectangle
    }

    /// <summary>The drag is over. <paramref name="keep"/> false (Esc): the selection goes back to what it was before it</summary>
    private void EndBand(bool keep)
    {
        if (_band is not { } band) return;
        _band = null;
        _bandScroll?.Stop();
        BandRect.IsVisible = false;
        if (!band.Drawn) return;
        if (ReferenceEquals(band.Pointer.Captured, GalleryHost)) band.Pointer.Capture(null);
        if (!keep) _mainViewModel.Gallery.SelectBand(band.Before, []);
    }
}
