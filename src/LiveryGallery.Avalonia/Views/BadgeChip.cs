using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using ForzaToolkit.UI.Controls;

namespace LiveryGallery.Views;

/// <summary>
/// One badge: a circle (a pill with its number) with the icon of the toolkit's 24 grid in the middle of its first
/// circle. A fact is filled (white on the badge's colour), an assumption is an outline (the badge's colour on the card's,
/// lighter in the dark theme). <see cref="Crossed"/> strikes the icon out in red (a filter's "not": no car uses it).
/// The ring is inside the chip (1 on a fill, 2 on an outline), so the icon's cell is the chip less the ring and the icon a whole number of pixels (16 on 24, 14 on 20): it stands in the very middle with no margin
/// of its own. 24 over a picture (with a shadow), 20 at the bottom of a card and in a list, 18 in a menu (an icon of 12)
/// </summary>
internal sealed class BadgeChip : Border
{
    public static readonly StyledProperty<CardBadge> BadgeProperty = AvaloniaProperty.Register<BadgeChip, CardBadge>(nameof(Badge));
    public static readonly StyledProperty<double> ChipSizeProperty = AvaloniaProperty.Register<BadgeChip, double>(nameof(ChipSize), 24);
    public static readonly StyledProperty<string?> NumberProperty = AvaloniaProperty.Register<BadgeChip, string?>(nameof(Number));

    /// <summary>A shadow under the chip, for a picture of any colour under it</summary>
    public static readonly StyledProperty<bool> ShadowProperty = AvaloniaProperty.Register<BadgeChip, bool>(nameof(Shadow), true);

    /// <summary>The resource an outline is filled with: the surface the chip stands on (a card's by default)</summary>
    public static readonly StyledProperty<string> SurfaceProperty = AvaloniaProperty.Register<BadgeChip, string>(nameof(Surface), "CardBrush");

    /// <summary>
    /// The icon struck out: a red line over it, corner to corner, with an edge of the surface's colour so it stands off
    /// the chip's (a filter's "not")
    /// </summary>
    public static readonly StyledProperty<bool> CrossedProperty = AvaloniaProperty.Register<BadgeChip, bool>(nameof(Crossed));

    private static readonly IBrush RingBrush = new SolidColorBrush(Color.Parse("#66FFFFFF"));
    private static readonly BoxShadows ShadowLook = BoxShadows.Parse("0 1 3 0 #59000000");

    private readonly TextBlock _number = new() { FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private bool _built;

    public BadgeChip()
    {
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    public CardBadge Badge
    {
        get => GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    public double ChipSize
    {
        get => GetValue(ChipSizeProperty);
        set => SetValue(ChipSizeProperty, value);
    }

    public string? Number
    {
        get => GetValue(NumberProperty);
        set => SetValue(NumberProperty, value);
    }

    public bool Shadow
    {
        get => GetValue(ShadowProperty);
        set => SetValue(ShadowProperty, value);
    }

    public string Surface
    {
        get => GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    public bool Crossed
    {
        get => GetValue(CrossedProperty);
        set => SetValue(CrossedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BadgeProperty || change.Property == ChipSizeProperty || change.Property == ShadowProperty || change.Property == SurfaceProperty
            || change.Property == CrossedProperty)
        {
            if (_built) Build();
        }
        else if (change.Property == NumberProperty)
        {
            _number.Text = Number;
            _number.IsVisible = !string.IsNullOrEmpty(Number);
        }
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Build();
    }

    private void Build()
    {
        _built = true;
        var look = LiveryBadges.Looks[(int)Badge];
        bool outlined = look.Outlined;
        double size = ChipSize, ring = outlined ? 2 : 1, cell = size - 2 * ring, icon = size >= 24 ? 16 : size >= 20 ? 14 : 12;
        string ink = outlined ? look.Colour : look.Ink ?? "BadgeOnFillInk";
        var glyph = new GridIcon
        {
            Width = icon, Height = icon, StrokeThickness = 2.5,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            [!GridIcon.DataProperty] = new DynamicResourceExtension(look.Icon),
            [!GridIcon.ForegroundProperty] = new DynamicResourceExtension(ink),
        };
        if (_number.Parent is Panel old) old.Children.Remove(_number);
        _number.FontSize = size >= 24 ? 11.5 : 9.5;
        _number.Margin = new Thickness(0, 0, Math.Round(size / 3), 0);
        _number[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(ink);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{cell},Auto"), Height = cell };
        grid.Children.Add(glyph);
        if (Crossed)
        {
            // Over the icon only, half a pixel in from its corners: its edge first, the red line on it
            double from = (cell - icon) / 2 + 0.5, to = cell - from, line = size >= 20 ? 2 : 1.7;
            grid.Children.Add(Slash(from, to, line + 2, Surface));
            grid.Children.Add(Slash(from, to, line, "BadgeDangerBrush"));
        }
        Grid.SetColumn(_number, 1);
        grid.Children.Add(_number);

        Height = size;
        MinWidth = size;
        CornerRadius = new CornerRadius(size / 2);
        BorderThickness = new Thickness(ring);
        BoxShadow = Shadow ? ShadowLook : default;
        this[!BackgroundProperty] = new DynamicResourceExtension(outlined ? Surface : look.Colour);
        if (outlined) this[!BorderBrushProperty] = new DynamicResourceExtension(look.Colour);
        else BorderBrush = RingBrush;
        Child = grid;
    }

    private static Avalonia.Controls.Shapes.Line Slash(double from, double to, double thickness, string brush) => new()
    {
        StartPoint = new Point(from, from), EndPoint = new Point(to, to), StrokeThickness = thickness,
        StrokeLineCap = PenLineCap.Round, IsHitTestVisible = false,
        [!Avalonia.Controls.Shapes.Shape.StrokeProperty] = new DynamicResourceExtension(brush),
    };
}
