using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using LiveryGallery.Services.Screenshots;

namespace LiveryGallery.Views;

/// <summary>
/// How the "Screenshots" view lays its pictures out: with the line under each (a gap of 14) or as tiles (the pictures
/// alone, a gap of 4, the line in the tooltip); and how big they are — three sizes, as the cards have, by the same switch
/// in the status bar, Ctrl with the wheel and Ctrl+"+", Ctrl+"-", Ctrl+0 while the screenshots are shown. The tiles fill
/// each row (TilePanel). Both are kept for the next launch
/// </summary>
internal partial class MainWindow
{
    private const double CaptionedGap = 14, TilesGap = 4;

    // The pictures are decoded a little wider than the size: a row's tiles grow to fill it (less than half a tile)
    private const double DecodeMargin = 1.3;

    // The width the pictures shown now were decoded at; a larger size decodes them again, a much smaller one too
    private int _screenshotDecodeWidth;

    private double _screenshotSizeWheel;

    /// <summary>The width a screenshot's tile aims at in each size</summary>
    internal static double ScreenshotWidth(CardSize size) => size switch
    {
        CardSize.Small => 200,
        CardSize.Large => 440,
        _ => 300,
    };

    private CardSize ScreenshotSize => Enum.IsDefined(_settings.ScreenshotsSize) ? _settings.ScreenshotsSize : CardSize.Normal;

    // The pixels a tile's picture is decoded at, the screen's and the interface's scale with it
    private int ScreenshotDecodeWidth() =>
        ScreenshotStore.PixelWidth(ScreenshotWidth(ScreenshotSize) * DecodeMargin, RenderScaling * ForzaToolkit.UI.UiScale.Current);

    private void InitializeScreenshotLayout()
    {
        ApplyScreenshotLayout();
        // Before the scroll viewer scrolls: with Ctrl the wheel sizes the pictures
        ScreenshotsScroll.AddHandler(PointerWheelChangedEvent, ScreenshotsScroll_CtrlWheel, RoutingStrategies.Tunnel);
    }

    private void ApplyScreenshotLayoutTexts()
    {
        ShotsCaptionedButton.Content = Strings.ScreenshotsLayoutCaptioned;
        ShotsTilesButton.Content = Strings.ScreenshotsLayoutTiles;
        ToolTip.SetTip(ShotsCaptionedButton, Strings.ScreenshotsLayoutCaptionedTooltip);
        ToolTip.SetTip(ShotsTilesButton, Strings.ScreenshotsLayoutTilesTooltip);
    }

    // The layout and the size on the view: the class "tiles" (Themes/Styles.axaml), the panels' width and gap
    private void ApplyScreenshotLayout()
    {
        bool tiles = _settings.ScreenshotsTiles;
        ScreenshotsView.Classes.Set("tiles", tiles);
        TilePanel.SetTileWidth(ScreenshotGroups, ScreenshotWidth(ScreenshotSize));
        TilePanel.SetGap(ScreenshotGroups, tiles ? TilesGap : CaptionedGap);
        ShotsCaptionedButton.Classes.Set("on", !tiles);
        ShotsTilesButton.Classes.Set("on", tiles);
    }

    private double ScreenshotGap => _settings.ScreenshotsTiles ? TilesGap : CaptionedGap;

    private void ScreenshotsLayout_Click(object? sender, RoutedEventArgs e)
    {
        bool tiles = sender == ShotsTilesButton;
        if (tiles == _settings.ScreenshotsTiles) return;
        _settings.ScreenshotsTiles = tiles;
        AppSettingsService.Save(_settings);
        ApplyScreenshotLayout();
    }

    /// <summary>The pictures' size: kept, laid out again, decoded again when they would be blurred (or far too big)</summary>
    private void SetScreenshotSize(CardSize size)
    {
        if (size == ScreenshotSize) return;
        _settings.ScreenshotsSize = size;
        AppSettingsService.Save(_settings);
        ApplyScreenshotLayout();
        UpdateCardSizeSwitch();
        int width = ScreenshotDecodeWidth();
        if (_showingScreenshots && _screenshotGroups.Count > 0 && (width > _screenshotDecodeWidth || width < _screenshotDecodeWidth * 0.6))
            ReloadScreenshotPictures();
    }

    private void StepScreenshotSize(int step)
    {
        // Small < Normal < Large
        CardSize[] order = [CardSize.Small, CardSize.Normal, CardSize.Large];
        int at = Array.IndexOf(order, ScreenshotSize);
        SetScreenshotSize(order[Math.Clamp(at + Math.Sign(step), 0, order.Length - 1)]);
    }

    private void ScreenshotsScroll_CtrlWheel(object? sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) == 0 || e.Delta.Y == 0) return;
        e.Handled = true;
        _screenshotSizeWheel += e.Delta.Y;
        if (Math.Abs(_screenshotSizeWheel) < 1) return;
        int step = Math.Sign(_screenshotSizeWheel);
        _screenshotSizeWheel = 0;
        StepScreenshotSize(step);
    }

    // The tiles kept (their selection too), their pictures decoded again at the new width
    private void ReloadScreenshotPictures()
    {
        _screenshotTilesCts.Cancel();
        _screenshotTilesCts.Dispose();
        _screenshotTilesCts = new CancellationTokenSource();
        foreach (var group in _screenshotGroups)
            foreach (var tile in group.Tiles) tile.Thumbnail = null;
        foreach (var (key, bitmap) in _screenshotPictures.Where(p => !p.Key.StartsWith("group|", StringComparison.Ordinal)).ToList())
        {
            bitmap?.Dispose();
            _screenshotPictures.Remove(key);
        }
        int width = ScreenshotDecodeWidth();
        _screenshotDecodeWidth = width;
        _ = LoadScreenshotPicturesAsync([.. _screenshotGroups], width,
            ScreenshotStore.PixelWidth(GroupPictureWidth, RenderScaling * ForzaToolkit.UI.UiScale.Current), _screenshotTilesCts.Token);
    }
}
