using Avalonia;
using Avalonia.Controls;
using LiveryGallery.Controller;

namespace LiveryGallery.Views;

/// <summary>
/// A row or card that shows a livery's preview: <c>views:ThumbnailRows.Track="True"</c> hands its attach, detach and
/// data context changes to <see cref="ThumbnailLifecycleController"/>, the same in every window
/// </summary>
internal sealed class ThumbnailRows : AvaloniaObject
{
    public static readonly AttachedProperty<bool> TrackProperty =
        AvaloniaProperty.RegisterAttached<ThumbnailRows, Control, bool>("Track");

    static ThumbnailRows() => TrackProperty.Changed.AddClassHandler<Control>(OnTrackChanged);

    public static bool GetTrack(Control control) => control.GetValue(TrackProperty);

    // The tracked controls hidden away with their livery kept (a pooled row's cards): their previews let go
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> Parked = [];
    private static readonly object Mark = new();

    /// <summary>
    /// The control is hidden away for later (a row put in the pool, a card its row does not need now) with its livery
    /// kept: its preview is let go, so a hidden card holds no picture, and setting the same livery again costs nothing
    /// </summary>
    public static void Park(Control control)
    {
        if (!GetTrack(control) || Parked.TryGetValue(control, out _)) return;
        Parked.Add(control, Mark);
        ThumbnailLifecycleController.OnDetached(control);
    }

    /// <summary>The control is shown again with the livery it kept: its preview comes back</summary>
    public static async void Unpark(Control control)
    {
        try
        {
            if (!Parked.Remove(control)) return;
            await ThumbnailLifecycleController.OnAttachedAsync(control);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Unhandled exception when a preview row was shown again", ex);
        }
    }

    /// <summary>The control is hidden away (<see cref="Park"/>): a preview that finishes loading is not given to it</summary>
    public static bool IsParked(Control control) => Parked.TryGetValue(control, out _);

    public static void SetTrack(Control control, bool value) => control.SetValue(TrackProperty, value);

    private static void OnTrackChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.AttachedToVisualTree -= OnAttached;
        control.DetachedFromVisualTree -= OnDetached;
        control.DataContextChanged -= OnDataContextChanged;
        if (e.NewValue is not true) return;
        control.AttachedToVisualTree += OnAttached;
        control.DetachedFromVisualTree += OnDetached;
        control.DataContextChanged += OnDataContextChanged;
    }

    private static async void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        try
        {
            if (sender is Control control) await ThumbnailLifecycleController.OnAttachedAsync(control);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Unhandled exception when a preview row was attached", ex);
        }
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control) return;
        Parked.Remove(control);
        ThumbnailLifecycleController.OnDetached(control);
    }

    private static async void OnDataContextChanged(object? sender, EventArgs e)
    {
        try
        {
            if (sender is not Control control) return;
            // Another livery for a hidden card: it is about to be shown with it
            Parked.Remove(control);
            await ThumbnailLifecycleController.OnDataContextChangedAsync(control);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Unhandled exception when a preview row's data changed", ex);
        }
    }
}
