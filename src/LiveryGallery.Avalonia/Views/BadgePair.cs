using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using ForzaToolkit.UI.Controls;

namespace LiveryGallery.Views;

/// <summary>
/// Two badges as one (a menu's choice of both, "duplicates and possible ones"): a pill of two halves with a gap between
/// them, each half the look of its badge (<see cref="LiveryBadges.Looks"/>: filled for a fact, an outline for an
/// assumption) and its icon of 12 towards the gap. As high as a <see cref="BadgeChip"/> of 18 in a menu, so a single badge
/// beside it lines up with its second half
/// </summary>
internal sealed class BadgePair : Border
{
    public static readonly StyledProperty<CardBadge> FirstProperty = AvaloniaProperty.Register<BadgePair, CardBadge>(nameof(First));
    public static readonly StyledProperty<CardBadge> SecondProperty = AvaloniaProperty.Register<BadgePair, CardBadge>(nameof(Second));

    /// <summary>The resource an outlined half is filled with: the surface the pill stands on (a menu's)</summary>
    public static readonly StyledProperty<string> SurfaceProperty = AvaloniaProperty.Register<BadgePair, string>(nameof(Surface), "MenuFlyoutPresenterBackground");

    /// <summary>A half's width: an icon of 12, 4 from the rounded edge and 2 from the gap, so the two stand together</summary>
    public const double HalfWidth = 18;

    /// <summary>The line of an outlined half</summary>
    public const double Outline = 1.5;

    /// <summary>The pill's height, as a badge's in a menu</summary>
    public const double Size = 18;

    private bool _built;

    public BadgePair()
    {
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    public CardBadge First
    {
        get => GetValue(FirstProperty);
        set => SetValue(FirstProperty, value);
    }

    public CardBadge Second
    {
        get => GetValue(SecondProperty);
        set => SetValue(SecondProperty, value);
    }

    public string Surface
    {
        get => GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_built && (change.Property == FirstProperty || change.Property == SecondProperty || change.Property == SurfaceProperty)) Build();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Build();
    }

    private void Build()
    {
        _built = true;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{HalfWidth},1,{HalfWidth}"), Height = Size };
        grid.Children.Add(Half(First, left: true));
        var second = Half(Second, left: false);
        Grid.SetColumn(second, 2);
        grid.Children.Add(second);
        Child = grid;
    }

    // One half: rounded on its outer side, square at the gap
    private Border Half(CardBadge badge, bool left)
    {
        var look = LiveryBadges.Looks[(int)badge];
        double radius = Size / 2;
        string ink = look.Outlined ? look.Colour : look.Ink ?? "BadgeOnFillInk";
        // 2 from the gap and 4 from the rounded edge, counted from the half's edge: an outline takes its line from them
        double ring = look.Outlined ? Outline : 0, inner = 2 - ring;
        var half = new Border
        {
            CornerRadius = left ? new CornerRadius(radius, 0, 0, radius) : new CornerRadius(0, radius, radius, 0),
            BorderThickness = new Thickness(ring),
            [!BackgroundProperty] = new DynamicResourceExtension(look.Outlined ? Surface : look.Colour),
            Child = new GridIcon
            {
                Width = 12, Height = 12, StrokeThickness = 2.5,
                HorizontalAlignment = left ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Margin = left ? new Thickness(0, 0, inner, 0) : new Thickness(inner, 0, 0, 0),
                [!GridIcon.DataProperty] = new DynamicResourceExtension(look.Icon),
                [!GridIcon.ForegroundProperty] = new DynamicResourceExtension(ink),
            },
        };
        if (look.Outlined) half[!BorderBrushProperty] = new DynamicResourceExtension(look.Colour);
        return half;
    }
}
