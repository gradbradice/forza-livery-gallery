namespace LiveryGallery.Models;

/// <summary>
/// How big the gallery's cards are: the width a card aims at. The cards fill the row: as many columns as come nearest to
/// that width, the room left shared out (<see cref="CardLayout"/>). One setting for the game and the storage alike
/// </summary>
public enum CardSize
{
    Normal = 0,
    Small = 1,
    Large = 2,
}

/// <summary>
/// The cards' layout in a list of a given width: how many in a row and how wide each is. A card is <see cref="Margin"/>
/// on each side; its picture keeps the game preview's proportions (270 × 150 at the normal size)
/// </summary>
/// <param name="Columns">Cards in a row</param>
/// <param name="CardStep">The room of one card, its margins with it: the list's width over <paramref name="Columns"/></param>
public readonly record struct CardLayout(int Columns, double CardStep)
{
    /// <summary>A card's margin on each side</summary>
    public const double Margin = 8;

    /// <summary>The width of a card at the normal size, and its picture's height there</summary>
    public const double NormalWidth = 270, NormalPictureHeight = 150;

    /// <summary>How far a card may shrink below its size's width to fit one more in a row: else one column fewer</summary>
    public const double MostShrink = 0.85;

    /// <summary>The width a card of <paramref name="size"/> aims at</summary>
    public static double TargetWidth(CardSize size) => size switch
    {
        CardSize.Small => 200,
        CardSize.Large => 360,
        _ => NormalWidth,
    };

    /// <summary>The layout of the cards of <paramref name="size"/> in a list <paramref name="width"/> wide</summary>
    public static CardLayout For(double width, CardSize size)
    {
        double target = TargetWidth(size) + 2 * Margin;
        if (!(width > 0)) return new CardLayout(1, target);
        int columns = Math.Max(1, (int)Math.Round(width / target));
        if (columns > 1 && width / columns < target * MostShrink) columns--;
        return new CardLayout(columns, width / columns);
    }

    /// <summary>The card itself, without its margins</summary>
    public double CardWidth => CardStep - 2 * Margin;

    /// <summary>Its picture's height: the game preview's proportions</summary>
    public double PictureHeight => Math.Round(CardWidth * NormalPictureHeight / NormalWidth);

    /// <summary>The width of a row of cards</summary>
    public double RowWidth => Columns * CardStep;
}

public static class CardSizes
{
    /// <summary>The sizes from the smallest</summary>
    public static IReadOnlyList<CardSize> Ordered { get; } = [CardSize.Small, CardSize.Normal, CardSize.Large];

    /// <summary>The size a step larger (positive) or smaller (negative); the same at either end</summary>
    public static CardSize Step(CardSize size, int step)
    {
        int index = Ordered.ToList().IndexOf(size);
        if (index < 0) index = 1;
        return Ordered[Math.Clamp(index + Math.Sign(step), 0, Ordered.Count - 1)];
    }
}
