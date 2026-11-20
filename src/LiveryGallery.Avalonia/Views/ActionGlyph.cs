using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>
/// The icon of a gallery action (<see cref="GalleryAction"/>) in the bar and the panel: the star filled when every
/// selected livery is a favourite, the lock closed or open, the action's 24-grid shape otherwise; it follows the action
/// </summary>
internal sealed class ActionGlyph : Decorator
{
    public static readonly StyledProperty<GalleryAction?> ActionProperty =
        AvaloniaProperty.Register<ActionGlyph, GalleryAction?>(nameof(Action));

    /// <summary>The icon's colour; the text's when not set (white on a filled button)</summary>
    public static readonly StyledProperty<Avalonia.Media.IBrush?> IconBrushProperty =
        AvaloniaProperty.Register<ActionGlyph, Avalonia.Media.IBrush?>(nameof(IconBrush));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<ActionGlyph, double>(nameof(Size), 15);

    static ActionGlyph()
    {
        ActionProperty.Changed.AddClassHandler<ActionGlyph>((glyph, e) => glyph.Watch(e.OldValue as GalleryAction, e.NewValue as GalleryAction));
        SizeProperty.Changed.AddClassHandler<ActionGlyph>((glyph, _) => glyph.Build());
        IconBrushProperty.Changed.AddClassHandler<ActionGlyph>((glyph, _) => glyph.Build());
    }

    public GalleryAction? Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    public Avalonia.Media.IBrush? IconBrush
    {
        get => GetValue(IconBrushProperty);
        set => SetValue(IconBrushProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private void Watch(GalleryAction? before, GalleryAction? now)
    {
        if (before is not null) before.PropertyChanged -= Action_PropertyChanged;
        if (now is not null && VisualRoot is not null) now.PropertyChanged += Action_PropertyChanged;
        Build();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Action is { } action) action.PropertyChanged += Action_PropertyChanged;
        Build();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Action is { } action) action.PropertyChanged -= Action_PropertyChanged;
    }

    private void Action_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryAction.IsOn) or nameof(GalleryAction.IconKey)) Build();
    }

    private void Build()
    {
        Child = Action is { } action ? MainWindow.ActionIcon(action, Size) : null;
        if (IconBrush is { } brush && Child is ForzaToolkit.UI.Controls.GridIcon icon) icon.Foreground = brush;
    }
}
