using ForzaToolkit.Formats;
using ForzaToolkit.LiveryRender.Formats;
using ForzaToolkit.LiveryRender.Livery;
using ForzaToolkit.LiveryRender.Rendering;
using LiveryGallery.Localisation;
using Games = ForzaToolkit.Games;
using FormatsGame = ForzaToolkit.Formats.ForzaGame;

namespace LiveryGallery.Render;

/// <summary>
/// A livery file read for drawing in its game's format (the profile's, <c>GameProfiles.FormatOf</c>):
/// <see cref="LiveryDrawings.FromContent"/>, as the converter and Craft read one; the drawn preview's size and camera by the game
/// (<see cref="GamePreviews"/>); the words of a livery drawn with another game's files
/// </summary>
internal static class LiveryFileDrawing
{
    /// <summary>The drawing of a livery file of <paramref name="format"/>; an error when it cannot be read</summary>
    /// <param name="format">The game of the file</param>
    /// <param name="carId">The livery's car (from its header)</param>
    /// <param name="livery">The file's bytes</param>
    public static ParseResult<LiveryDrawing> Read(FormatsGame format, uint carId, ReadOnlySpan<byte> livery)
    {
        return LiveryDrawings.FromContent(livery, format, carId);
    }

    /// <summary>
    /// The size and camera of <paramref name="game"/>'s own preview, so a drawn one sits among the game's: FH6 670×376,
    /// FH5 670×376 in FH4's view, FH3 and FH4 816×459
    /// </summary>
    public static RenderOptions PreviewOptions(Games.ForzaGame game) =>
        GamePreviews.For(game.Id) is { Count: > 0 } previews ? previews[0].RenderOptions : new RenderOptions();

    /// <summary>"Drawn with FH4 car models and paints: …", or null when the game's own files draw</summary>
    public static string? BorrowedNote(Games.ForzaGame game, Games.ForzaGame? drawnWith) =>
        drawnWith is { } other && other != game ? string.Format(Strings.RenderBorrowedFormat, game.ShortName, other.ShortName) : null;
}
