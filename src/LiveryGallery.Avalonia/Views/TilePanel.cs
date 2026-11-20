using Avalonia;
using Avalonia.Controls;

namespace LiveryGallery.Views;

/// <summary>
/// The screenshots' tiles in rows that fill the width: as many columns as come nearest to <see cref="TileWidthProperty"/>
/// (the size chosen), the room left shared out, <see cref="GapProperty"/> between them both ways. Both are inherited, so
/// the window sets them once for every group's panel (MainWindow.ScreenshotLayout.cs)
/// </summary>
internal sealed class TilePanel : Panel
{
    /// <summary>The width a tile aims at</summary>
    public static readonly AttachedProperty<double> TileWidthProperty =
        AvaloniaProperty.RegisterAttached<TilePanel, Control, double>("TileWidth", 300, inherits: true);

    /// <summary>The room between two tiles, across and down</summary>
    public static readonly AttachedProperty<double> GapProperty =
        AvaloniaProperty.RegisterAttached<TilePanel, Control, double>("Gap", 14, inherits: true);

    static TilePanel() => AffectsMeasure<TilePanel>(TileWidthProperty, GapProperty);

    public static double GetTileWidth(Control control) => control.GetValue(TileWidthProperty);

    public static void SetTileWidth(Control control, double value) => control.SetValue(TileWidthProperty, value);

    public static double GetGap(Control control) => control.GetValue(GapProperty);

    public static void SetGap(Control control, double value) => control.SetValue(GapProperty, value);

    /// <summary>How many tiles a row of <paramref name="width"/> holds, and how wide each is</summary>
    public static (int Columns, double Width) Layout(double width, double tileWidth, double gap)
    {
        if (double.IsInfinity(width) || width <= 0) return (1, tileWidth);
        // The nearest count to the size asked: the tiles grow or shrink by less than half a tile
        int columns = Math.Max(1, (int)Math.Round((width + gap) / (tileWidth + gap)));
        return (columns, Math.Max(1, (width - gap * (columns - 1)) / columns));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double gap = GetValue(GapProperty);
        var (columns, width) = Layout(availableSize.Width, GetValue(TileWidthProperty), gap);
        double height = 0, row = 0;
        int shown = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            if (!child.IsVisible) continue;
            row = Math.Max(row, child.DesiredSize.Height);
            if (++shown % columns == 0)
            {
                height += row + gap;
                row = 0;
            }
        }
        if (shown % columns != 0) height += row + gap;
        if (shown > 0) height -= gap;
        double used = double.IsInfinity(availableSize.Width) ? columns * width + gap * (columns - 1) : availableSize.Width;
        return new Size(used, Math.Max(0, height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double gap = GetValue(GapProperty);
        var (columns, width) = Layout(finalSize.Width, GetValue(TileWidthProperty), gap);
        double y = 0;
        var row = new List<Control>(columns);
        void Place()
        {
            double height = row.Max(c => c.DesiredSize.Height);
            for (int i = 0; i < row.Count; i++) row[i].Arrange(new Rect(i * (width + gap), y, width, height));
            y += height + gap;
            row.Clear();
        }
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            row.Add(child);
            if (row.Count == columns) Place();
        }
        if (row.Count > 0) Place();
        return finalSize;
    }
}

/// <summary>A picture's box that keeps its proportions: as high as its width times <see cref="Ratio"/></summary>
internal sealed class AspectBox : Decorator
{
    public static readonly StyledProperty<double> RatioProperty = AvaloniaProperty.Register<AspectBox, double>(nameof(Ratio), 9.0 / 16);

    static AspectBox() => AffectsMeasure<AspectBox>(RatioProperty);

    /// <summary>Height over width (9/16 for a screenshot)</summary>
    public double Ratio
    {
        get => GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width;
        var size = new Size(width, Math.Round(width * Ratio));
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
