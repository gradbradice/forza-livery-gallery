using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using ForzaToolkit.UI.Controls;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>The badges of a livery card, in the order they stand (each group's order is its own)</summary>
internal enum CardBadge
{
    // Over the picture: what the livery is
    Installed, Duplicate, PossibleDuplicate, Generated, PaintOnly, Auction, Mine,
    // Under it: where else it is, and how its file was read
    CopyInGame, SimilarInGame, CopyInStorage, SimilarInStorage, AddedByHand, ReadError, ReadPartial,
}

/// <summary>Which badges one <see cref="CardBadges"/> shows</summary>
internal enum CardBadgeGroup
{
    /// <summary>What the livery is: on the car, a duplicate, generated, paint only, auction, mine</summary>
    Top,

    /// <summary>Where else it is (the game, the storage), added by hand, how its file was read</summary>
    Bottom,

    /// <summary>Both, the top group first</summary>
    All,
}

/// <summary>
/// The badges of a livery card as round icons. On the picture (the default) a row of 24: the top group over its top,
/// the bottom group over its bottom as far as the buttons beside it, what does not fit in a last "+N" chip whose tooltip
/// names the rest. At the bottom of the card (<see cref="AtBottomProperty"/>, a setting) both groups of 20 in lines: a
/// group that does not fit the rest of a line starts the next one, a group too long for a line goes on in the next.
/// </summary>
/// <remarks>
/// A fact is filled (white on the badge's colour), an assumption is an outline (the badge's colour on the card's, lighter
/// in the dark theme): a possible duplicate, a similar livery, maybe generated. The chips are made only for the badges a
/// livery has, and only by the instance the setting shows, so a card without badges holds none. Each chip's tooltip is
/// its name, then what it means for this livery
/// </remarks>
internal sealed class CardBadges : Panel
{
    /// <summary>The setting, set once on the window and inherited by every card: the badges at the bottom of the card</summary>
    public static readonly AttachedProperty<bool> AtBottomProperty =
        AvaloniaProperty.RegisterAttached<CardBadges, Control, bool>("AtBottom", inherits: true);

    public static readonly StyledProperty<CardBadgeGroup> GroupProperty =
        AvaloniaProperty.Register<CardBadges, CardBadgeGroup>(nameof(Group));

    /// <summary>This one is the bottom of the card's (lines of 20); else it is the picture's (a row of 24)</summary>
    public static readonly StyledProperty<bool> BelowProperty = AvaloniaProperty.Register<CardBadges, bool>(nameof(Below));

    public static bool GetAtBottom(Control control) => control.GetValue(AtBottomProperty);
    public static void SetAtBottom(Control control, bool value) => control.SetValue(AtBottomProperty, value);

    public CardBadgeGroup Group
    {
        get => GetValue(GroupProperty);
        set => SetValue(GroupProperty, value);
    }

    public bool Below
    {
        get => GetValue(BelowProperty);
        set => SetValue(BelowProperty, value);
    }

    private const int Kinds = LiveryBadges.Count;
    private const double GroupGap = 8, LineGap = 4;

    private readonly Chip?[] _chips = new Chip?[Kinds];
    private readonly Border _more;
    private readonly TextBlock _moreText;
    private readonly TextBlock _moreTip = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
    private LiveryEntry? _entry;
    private int _shownInRow;

    public CardBadges()
    {
        // "+N": the neutral chip of the set, slate with the white ring
        _moreText = new TextBlock { FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center };
        _more = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#66FFFFFF")), BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 1 3 0 #59000000"), Child = _moreText, IsVisible = false,
            [!Border.BackgroundProperty] = new DynamicResourceExtension("BadgeNeutralBrush"),
        };
        ToolTip.SetTip(_more, _moreTip);
        VisualChildren.Add(_more);
        IsVisible = false;
    }

    private double Size => Below ? 20 : 24;
    private double Gap => Below ? 3 : 4;

    private bool InGroup(CardBadge badge) => Group switch
    {
        CardBadgeGroup.Top => LiveryBadges.IsTop(badge),
        CardBadgeGroup.Bottom => !LiveryBadges.IsTop(badge),
        _ => true,
    };

    /// <summary>The setting shows this instance (the picture's or the bottom's)</summary>
    private bool Serves => GetValue(AtBottomProperty) == Below;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AtBottomProperty || change.Property == BelowProperty || change.Property == GroupProperty)
        {
            if (change.Property != AtBottomProperty) DropChips(); // their size or set is another
            Follow();
        }
        else if (change.Property == DataContextProperty) Follow();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AppLocalisationService.LanguageChanged += OnLanguageChanged;
        Follow();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        AppLocalisationService.LanguageChanged -= OnLanguageChanged;
        Watch(null);
    }

    private void OnLanguageChanged() => Refresh();

    // The livery whose badges these are, while the setting shows this instance
    private void Follow()
    {
        Watch(Serves && VisualRoot is not null ? DataContext as LiveryEntry : null);
        Refresh();
    }

    private void Watch(LiveryEntry? entry)
    {
        if (ReferenceEquals(entry, _entry)) return;
        if (_entry is not null) _entry.PropertyChanged -= OnEntryChanged;
        _entry = entry;
        if (_entry is not null) _entry.PropertyChanged += OnEntryChanged;
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        var entry = _entry;
        bool any = false;
        for (int i = 0; i < Kinds; i++)
        {
            var badge = (CardBadge)i;
            bool has = entry is not null && InGroup(badge) && LiveryBadges.Has(entry, badge, onCard: true);
            var chip = _chips[i];
            if (has)
            {
                chip ??= AddChip(badge);
                chip.Show(LiveryBadges.Number(entry!, badge), LiveryBadges.Name(entry!, badge), LiveryBadges.Detail(entry!, badge));
                any = true;
            }
            else if (chip is not null) chip.Border.IsVisible = false;
        }
        IsVisible = any;
        InvalidateMeasure();
    }

    private Chip AddChip(CardBadge badge)
    {
        var chip = new Chip(badge, Size);
        _chips[(int)badge] = chip;
        // In the badges' order among those made so far
        int at = 0;
        for (int i = 0; i < (int)badge; i++) if (_chips[i] is not null) at++;
        Children.Insert(at, chip.Border);
        return chip;
    }

    private void DropChips()
    {
        Children.Clear();
        Array.Clear(_chips);
    }

    // One badge of the card (BadgeChip) and its tooltip: its name, then what it means for this livery
    private sealed class Chip
    {
        public readonly BadgeChip Border;
        private readonly TextBlock _tip = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
        private readonly Run _name = new() { FontWeight = FontWeight.SemiBold };
        private readonly Run _detail = new();

        public Chip(CardBadge badge, double size)
        {
            Border = new BadgeChip { Badge = badge, ChipSize = size };
            _tip.Inlines = [_name, new LineBreak(), _detail];
            ToolTip.SetTip(Border, _tip);
        }

        public string Name => _name.Text ?? "";

        public void Show(string? number, string name, string? detail)
        {
            Border.IsVisible = true;
            Border.Number = number;
            _name.Text = name;
            _detail.Text = detail;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double size = Size;
        foreach (var child in Children) child.Measure(new Size(double.PositiveInfinity, size));
        return Below ? MeasureLines(availableSize.Width) : MeasureRow(availableSize.Width);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Below) ArrangeLines(finalSize.Width);
        else ArrangeRow(finalSize.Height);
        return finalSize;
    }

    private IEnumerable<(CardBadge Badge, Control Chip)> Shown()
    {
        for (int i = 0; i < Kinds; i++)
            if (_chips[i] is { Border.IsVisible: true } chip) yield return ((CardBadge)i, chip.Border);
    }

    // A row: as many as fit, then "+N" (its width depends on N, so it is measured for each count)
    private Size MeasureRow(double width)
    {
        double size = Size, gap = Gap;
        var shown = Shown().ToList();
        double all = shown.Sum(c => c.Chip.DesiredSize.Width) + gap * Math.Max(0, shown.Count - 1);
        if (all <= width)
        {
            _shownInRow = shown.Count;
            _more.IsVisible = false;
            return new Size(all, shown.Count > 0 ? size : 0);
        }
        _more.Height = size;
        _more.MinWidth = size;
        _more.CornerRadius = new CornerRadius(size / 2);
        _more.Padding = new Thickness(size / 4, 0);
        _moreText.FontSize = size >= 24 ? 11.5 : 9.5;
        int count = shown.Count;
        double used = all, moreWidth = 0;
        while (count > 0)
        {
            used -= shown[count - 1].Chip.DesiredSize.Width + (count > 1 ? gap : 0);
            count--;
            _moreText.Text = "+" + (shown.Count - count).ToString(AppLocalisationService.Culture);
            _more.IsVisible = true;
            _more.Measure(new Size(double.PositiveInfinity, size));
            moreWidth = _more.DesiredSize.Width;
            if (used + (count > 0 ? gap : 0) + moreWidth <= width) break;
        }
        _shownInRow = count;
        var rest = shown.Skip(count).ToList();
        _moreTip.Text = MoreHeader(rest.Count) + string.Concat(rest.Select(c => "\n• " + _chips[(int)c.Badge]!.Name));
        return new Size(used + (count > 0 ? gap : 0) + moreWidth, size);
    }

    private static string MoreHeader(int count) => string.Format(AppLocalisationService.Culture,
        StatisticsViewModel.PluralForm(count, AppLocalisationService.Culture) switch
        {
            StatisticsViewModel.Plural.One => Strings.BadgesMoreOne,
            StatisticsViewModel.Plural.Few => Strings.BadgesMoreFew,
            _ => Strings.BadgesMoreMany,
        }, count);

    private void ArrangeRow(double height)
    {
        double size = Size, x = 0, y = (height - size) / 2;
        int index = 0;
        foreach (var (_, chip) in Shown())
        {
            // A badge in "+N" is still laid out (a chip keeps its own height and least width even in an empty box), so it
            // is not drawn and takes no pointer instead; neither changes the layout
            bool fits = index++ < _shownInRow;
            chip.Opacity = fits ? 1 : 0;
            chip.IsHitTestVisible = fits;
            if (fits)
            {
                chip.Arrange(new Rect(x, y, chip.DesiredSize.Width, size));
                x += chip.DesiredSize.Width + Gap;
            }
            else chip.Arrange(new Rect(0, y, 0, size));
        }
        foreach (var child in Children) if (!child.IsVisible) child.Arrange(default);
        if (_more.IsVisible) _more.Arrange(new Rect(x, y, _more.DesiredSize.Width, size));
    }

    // Lines: the chips of a group side by side; the next group after a wider gap on the same line if all of it fits there
    // (else on the next line); a group longer than a line goes on in the next
    private List<(Control Chip, Point At)> Flow(double width)
    {
        double size = Size, gap = Gap, x = 0, y = 0;
        var placed = new List<(Control, Point)>();
        bool? group = null;
        var shown = Shown().ToList();
        for (int i = 0; i < shown.Count; i++)
        {
            var (badge, chip) = shown[i];
            bool top = LiveryBadges.IsTop(badge);
            double w = chip.DesiredSize.Width;
            if (group is { } g && g != top)
            {
                double groupWidth = shown.Skip(i).Sum(c => c.Chip.DesiredSize.Width) + gap * (shown.Count - i - 1);
                if (x + GroupGap + groupWidth <= width) x += GroupGap;
                else { x = 0; y += size + LineGap; }
            }
            else if (group is not null)
            {
                if (x + gap + w <= width) x += gap;
                else { x = 0; y += size + LineGap; }
            }
            group = top;
            placed.Add((chip, new Point(x, y)));
            x += w;
        }
        return placed;
    }

    private Size MeasureLines(double width)
    {
        var placed = Flow(width);
        _more.IsVisible = false;
        if (placed.Count == 0) return default;
        double right = placed.Max(p => p.At.X + p.Chip.DesiredSize.Width), bottom = placed[^1].At.Y + Size;
        return new Size(right, bottom);
    }

    private void ArrangeLines(double width)
    {
        foreach (var (chip, at) in Flow(width))
        {
            chip.Opacity = 1;
            chip.IsHitTestVisible = true;
            chip.Arrange(new Rect(at, new Size(chip.DesiredSize.Width, Size)));
        }
        foreach (var child in Children) if (!child.IsVisible) child.Arrange(default);
    }
}
