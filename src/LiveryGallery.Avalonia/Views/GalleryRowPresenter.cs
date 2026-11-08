using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

namespace LiveryGallery.Views;

public sealed class GalleryRowPresenter : Panel
{
    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<GalleryRowPresenter, IDataTemplate?>(nameof(ItemTemplate));

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    private int _visibleCount;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UpdateCards();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemTemplateProperty) UpdateCards();
    }

    private void UpdateCards()
    {
        var items = (DataContext as GalleryRow)?.Items;
        int count = items?.Count ?? 0;

        for (int i = 0; i < count; i++)
        {
            var item = items![i];
            if (i < Children.Count)
            {
                var card = Children[i];
                if (ReferenceEquals(card.DataContext, item))
                {
                    // Hidden away with this livery (its row was in the pool): its preview comes back
                    ThumbnailRows.Unpark(card);
                }
                else
                {
                    card.DataContext = item;
                    // A card taken from the pool for another livery is measured again whole. Avalonia measures a
                    // control from its parent only while the controls between them are invalid too; a text whose own
                    // measure was lost while the card sat hidden in the pool kept the size of its empty text inside
                    // a valid line, and the make, country, author and date were drawn as nothing until the row was
                    // scrolled out and back (seen in the main window, a few cards at a time)
                    LayoutHelper.InvalidateSelfAndChildrenMeasure(card);
                }
                card.IsVisible = true;
            }
            else
            {
                var card = ItemTemplate?.Build(item);
                if (card is null) break;
                card.DataContext = item;
                Children.Add(card);
            }
        }

        // The cards this row does not need now are hidden with their livery kept: no binding of them goes to nothing and
        // back, and their previews are let go (ThumbnailRows.Park)
        for (int i = count; i < Children.Count; i++) Park(Children[i]);

        _visibleCount = Math.Min(count, Children.Count);
        InvalidateMeasure();
    }

    /// <summary>The row is put away (the gallery's pool, hidden whole): every card keeps its livery, its preview let go</summary>
    internal void ParkAll()
    {
        foreach (var card in Children) ThumbnailRows.Park(card);
    }

    /// <summary>The row is shown again with the same row of liveries: the previews of the cards it shows come back</summary>
    internal void UnparkShown()
    {
        for (int i = 0; i < _visibleCount; i++) ThumbnailRows.Unpark(Children[i]);
    }

    private static void Park(Control card)
    {
        card.IsVisible = false;
        ThumbnailRows.Park(card);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var childConstraint = new Size(double.PositiveInfinity, availableSize.Height);
        double width = 0, height = 0;
        for (int i = 0; i < _visibleCount; i++)
        {
            var card = Children[i];
            card.Measure(childConstraint);
            width += card.DesiredSize.Width;
            height = Math.Max(height, card.DesiredSize.Height);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        for (int i = 0; i < _visibleCount; i++)
        {
            var card = Children[i];
            double width = card.DesiredSize.Width;
            card.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }
        return finalSize;
    }
}
