using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace LiveryGallery.Views;

// The buttons and frames of a livery card: types of their own, so the app's styles of Button.class (60 of them) and
// Border.class (30) are not tried on them, nor those of TextBlock.class and Path.class on its texts and shapes. Avalonia tries each style whose type matches on every element of that type
// and watches the classes of each one it tried: in a card that was about 1250 styles watched on 22 elements, every
// card built and every livery it showed. A selector names a type exactly (Button is not a CardButton), so none of
// those reaches them; each kind of them has its look in a theme of its own (Themes/CardThemes.axaml), on Theme.
// docs/plans.md, «Облегчение карточки»

/// <summary>A button of a livery card; its look is the theme it is given (CardIconButtonTheme and the others)</summary>
public class CardButton : Button
{
    // Out of the Tab order: a card is reached with the arrows (the keys' cursor), its actions with their keys, Enter and
    // the context menu (Shift+F10, the menu key). Tab through every button of every card would never get past the gallery
    static CardButton() => IsTabStopProperty.OverrideDefaultValue<CardButton>(false);

    protected override Type StyleKeyOverride => typeof(CardButton);
}

/// <summary>A frame of a livery card; plain, or the look of the theme it is given (the card's, the selection mark's)</summary>
public class CardBorder : Border
{
    protected override Type StyleKeyOverride => typeof(CardBorder);
}

/// <summary>
/// A text of a livery card. The app's one style of every TextBlock (the toolkit's: the text's colour) does not reach it
/// either: a text of the card without a colour of its own is given TextPrimaryBrush in the card
/// </summary>
public class CardText : TextBlock
{
    protected override Type StyleKeyOverride => typeof(CardText);
}

/// <summary>A shape of a livery card (its icons)</summary>
public class CardPath : Avalonia.Controls.Shapes.Path
{
    protected override Type StyleKeyOverride => typeof(CardPath);
}

/// <summary>
/// The folder's name under a livery card (selected with the mouse and copied: GalleryClicks.FolderNameClass), made the
/// first time the setting shows it. A SelectableTextBlock is heavier than a text, and every card had one, hidden unless
/// the folders' names are searched too (a setting, off by default). Shown and hidden with the setting after that
/// </summary>
public class CardFolderName : Decorator
{
    public static readonly StyledProperty<bool> ShowProperty = AvaloniaProperty.Register<CardFolderName, bool>(nameof(Show));
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<CardFolderName, string?>(nameof(Text));

    public CardFolderName()
    {
        IsVisible = false;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
    }

    /// <summary>The setting: the folders' names shown</summary>
    public bool Show
    {
        get => GetValue(ShowProperty);
        set => SetValue(ShowProperty, value);
    }

    /// <summary>The folder's name</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowProperty)
        {
            if (Show && Child is null)
            {
                // What the card's template had: small, the third colour, cut with an ellipsis, no menu of its own (a
                // right click gives the card's); the transparent background takes the pointer between the letters
                var name = new SelectableTextBlock
                {
                    Text = Text, FontSize = 10, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, ContextFlyout = null,
                    Background = Avalonia.Media.Brushes.Transparent,
                    [!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("TextTertiaryBrush"),
                };
                name.Classes.Add(GalleryClicks.FolderNameClass);
                Child = name;
            }
            IsVisible = Show;
        }
        else if (change.Property == TextProperty && Child is SelectableTextBlock text) text.Text = Text;
    }
}

/// <summary>
/// The tags of a livery card: a chip for each, a click filters by it. The chips are kept and given the next livery's
/// tags; before, an ItemsControl set its source and made its chips again for every livery the card showed (about a fifth
/// of the time a card took to show another livery), tags or none
/// </summary>
public class CardTags : WrapPanel
{
    public static readonly StyledProperty<IReadOnlyList<string>?> TagsProperty =
        AvaloniaProperty.Register<CardTags, IReadOnlyList<string>?>(nameof(Tags));

    private static ControlTheme? _chipTheme;
    private readonly List<CardButton> _chips = [];
    private CardScope? _scope;

    public CardTags()
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal;
        ItemSpacing = 5;
        LineSpacing = 5;
        IsVisible = false; // no tags yet
    }

    protected override Type StyleKeyOverride => typeof(CardTags);

    /// <summary>The livery's tags</summary>
    public IReadOnlyList<string>? Tags
    {
        get => GetValue(TagsProperty);
        set => SetValue(TagsProperty, value);
    }

    /// <summary>Hidden whatever the tags (a small card shows none); without this, hidden only when there is none</summary>
    public bool Collapsed
    {
        get => GetValue(CollapsedProperty);
        set => SetValue(CollapsedProperty, value);
    }

    public static readonly StyledProperty<bool> CollapsedProperty = AvaloniaProperty.Register<CardTags, bool>(nameof(Collapsed));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TagsProperty) Update();
        else if (change.Property == CollapsedProperty) IsVisible = !Collapsed && Children.Count > 0;
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        // The window's commands (its CardScope), for the chips made so far and those to come
        _scope ??= this.FindResource("CardScope") as CardScope;
        foreach (var chip in _chips) chip.Command = _scope?.Main?.Gallery.ToggleTagCommand;
    }

    private void Update()
    {
        var tags = Tags ?? [];
        for (int i = 0; i < tags.Count; i++)
        {
            if (i == _chips.Count) _chips.Add(NewChip());
            var chip = _chips[i];
            ((CardText)chip.Content!).Text = tags[i];
            chip.CommandParameter = tags[i];
            if (i >= Children.Count) Children.Add(chip);
        }
        while (Children.Count > tags.Count) Children.RemoveAt(Children.Count - 1);
        // Hidden without tags: an empty row would still take the gap above it
        IsVisible = !Collapsed && tags.Count > 0;
    }

    // As the card's template had it: the chip's look (CardTagChipTheme), its text small in the chips' colour
    private CardButton NewChip()
    {
        _chipTheme ??= Application.Current?.FindResource("CardTagChipTheme") as ControlTheme;
        var chip = new CardButton
        {
            Theme = _chipTheme,
            Command = _scope?.Main?.Gallery.ToggleTagCommand,
            Content = new CardText
            {
                FontSize = 10.5,
                [!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("ChipTextBrush"),
            },
        };
        chip.Classes.Add("tagChipCard");
        return chip;
    }
}

/// <summary>
/// The round buttons of a livery card. Under the picture on a normal or large card; on a small one they lie over the
/// picture's bottom on a dark shade and show under the pointer only. A row wider than the card (a small card in a
/// narrow window, with every button shown) is made smaller to fit, not cut
/// </summary>
public class CardActions : Border
{
    public static readonly StyledProperty<bool> SmallProperty = AvaloniaProperty.Register<CardActions, bool>(nameof(Small));

    private static readonly Thickness NormalMargin = new(14, 10, 14, 0);
    private static IBrush? _shade;
    private Control? _card;

    protected override Type StyleKeyOverride => typeof(CardActions);

    /// <summary>The card is small: the buttons over the picture, under the pointer only</summary>
    public bool Small
    {
        get => GetValue(SmallProperty);
        set => SetValue(SmallProperty, value);
    }

    public CardActions() => Update();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SmallProperty)
        {
            Watch(Small);
            Update();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _card = this.FindAncestorOfType<CardBorder>();
        while (_card is not null && !_card.Classes.Contains("liveryCard")) _card = _card.FindAncestorOfType<CardBorder>();
        Watch(Small);
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Watch(false);
        _card = null;
    }

    // The card's pointer is watched on a small card only: a normal one shows its buttons always, and a handler on every
    // property change of every card would be work for nothing
    private bool _watching;

    private void Watch(bool watch)
    {
        if (watch == _watching || _card is null) return;
        if (watch) _card.PropertyChanged += Card_PropertyChanged;
        else _card.PropertyChanged -= Card_PropertyChanged;
        _watching = watch;
    }

    private void Card_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsPointerOverProperty) IsVisible = _card?.IsPointerOver == true;
    }

    private void Update()
    {
        if (!Small)
        {
            Grid.SetRow(this, 1);
            VerticalAlignment = VerticalAlignment.Top;
            Margin = NormalMargin;
            Padding = default;
            Background = null;
            IsVisible = true;
            return;
        }
        Grid.SetRow(this, 0);
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = default;
        Padding = new Thickness(8, 14, 8, 8);
        Background = _shade ??= new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0xB4, 0, 0, 0), 0.45) },
        };
        IsVisible = _card?.IsPointerOver == true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var child = Child;
        if (child is null) return base.MeasureOverride(availableSize);
        var padding = Padding;
        child.Measure(Size.Infinity);
        var wanted = child.DesiredSize;
        double room = availableSize.Width - padding.Left - padding.Right;
        double scale = wanted.Width > room && room > 0 ? room / wanted.Width : 1;
        // A transform of its own, changed only when the scale does: a new one each measure would redraw the row
        if (scale < 1)
        {
            if (_shrink is null)
            {
                _shrink = new ScaleTransform(scale, scale);
                child.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
            }
            else if (_shrink.ScaleX != scale)
            {
                _shrink.ScaleX = scale;
                _shrink.ScaleY = scale;
            }
            child.RenderTransform = _shrink;
        }
        else if (child.RenderTransform is not null)
        {
            child.RenderTransform = null;
        }
        _scale = scale;
        return new Size(Math.Min(availableSize.Width, wanted.Width * scale + padding.Left + padding.Right),
            wanted.Height * scale + padding.Top + padding.Bottom);
    }

    private double _scale = 1;
    private ScaleTransform? _shrink;

    protected override Size ArrangeOverride(Size finalSize)
    {
        var child = Child;
        if (child is null) return base.ArrangeOverride(finalSize);
        var padding = Padding;
        double width = _scale < 1 ? child.DesiredSize.Width : Math.Max(0, finalSize.Width - padding.Left - padding.Right);
        child.Arrange(new Rect(padding.Left, padding.Top, width, child.DesiredSize.Height));
        return finalSize;
    }
}
