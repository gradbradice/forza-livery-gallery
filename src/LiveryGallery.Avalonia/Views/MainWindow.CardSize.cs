using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>
/// The cards' size: the switch in the status bar, Ctrl with the wheel over the gallery, Ctrl+"+", Ctrl+"-" and Ctrl+0.
/// The size is kept for the next launch (MainViewModel.SetCardSize); the window lays the cards out again
/// (UpdateGroupWidthsOnly). While the screenshots are shown the same switch and keys size their pictures
/// (MainWindow.ScreenshotLayout.cs)
/// </summary>
internal partial class MainWindow
{
    private void InitializeCardSize()
    {
        UpdateCardSizeSwitch();
        _mainViewModel.Gallery.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GalleryViewModel.CardSize)) UpdateCardSizeSwitch();
        };
        // Before the scroll viewer scrolls: with Ctrl the wheel sizes the cards and the list stays where it is
        GalleryScroll.AddHandler(PointerWheelChangedEvent, GalleryScroll_CtrlWheel, RoutingStrategies.Tunnel);
    }

    private void ApplyCardSizeTexts()
    {
        bool shots = _showingScreenshots;
        string small = shots ? Strings.ScreenshotsSizeSmall : Strings.CardSizeSmall;
        string normal = shots ? Strings.ScreenshotsSizeNormal : Strings.CardSizeNormal;
        string large = shots ? Strings.ScreenshotsSizeLarge : Strings.CardSizeLarge;
        SmallCardsButton.SetValue(ToolTip.TipProperty, new ShortcutTip(small, GalleryShortcut.CardsSmaller));
        NormalCardsButton.SetValue(ToolTip.TipProperty, new ShortcutTip(normal, GalleryShortcut.CardsNormal));
        LargeCardsButton.SetValue(ToolTip.TipProperty, new ShortcutTip(large, GalleryShortcut.CardsLarger));
        AutomationProperties.SetName(SmallCardsButton, small);
        AutomationProperties.SetName(NormalCardsButton, normal);
        AutomationProperties.SetName(LargeCardsButton, large);
    }

    private void UpdateCardSizeSwitch()
    {
        var size = _showingScreenshots ? ScreenshotSize : _mainViewModel.Gallery.CardSize;
        SmallCardsButton.Classes.Set("on", size == CardSize.Small);
        NormalCardsButton.Classes.Set("on", size == CardSize.Normal);
        LargeCardsButton.Classes.Set("on", size == CardSize.Large);
    }

    private void CardSizeButton_Click(object? sender, RoutedEventArgs e)
    {
        var size = sender == SmallCardsButton ? CardSize.Small : sender == LargeCardsButton ? CardSize.Large : CardSize.Normal;
        if (_showingScreenshots) SetScreenshotSize(size);
        else _mainViewModel.SetCardSize(size);
    }

    // One notch, one step: a touchpad gives many small deltas, so they add up to a notch before the size changes
    private double _cardSizeWheel;

    private void GalleryScroll_CtrlWheel(object? sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) == 0 || e.Delta.Y == 0) return;
        e.Handled = true;
        _cardSizeWheel += e.Delta.Y;
        if (Math.Abs(_cardSizeWheel) < 1) return;
        int step = Math.Sign(_cardSizeWheel);
        _cardSizeWheel = 0;
        _mainViewModel.StepCardSize(step);
    }

    /// <summary>The keys of the cards' size; false for another key</summary>
    private bool RunCardSizeShortcut(GalleryShortcut action)
    {
        if (_showingScreenshots)
        {
            switch (action)
            {
                case GalleryShortcut.CardsLarger:
                    StepScreenshotSize(1);
                    return true;
                case GalleryShortcut.CardsSmaller:
                    StepScreenshotSize(-1);
                    return true;
                case GalleryShortcut.CardsNormal:
                    SetScreenshotSize(CardSize.Normal);
                    return true;
                default:
                    return false;
            }
        }
        switch (action)
        {
            case GalleryShortcut.CardsLarger:
                _mainViewModel.StepCardSize(1);
                return true;
            case GalleryShortcut.CardsSmaller:
                _mainViewModel.StepCardSize(-1);
                return true;
            case GalleryShortcut.CardsNormal:
                _mainViewModel.SetCardSize(CardSize.Normal);
                return true;
            case GalleryShortcut.Properties:
                _mainViewModel.IsPropertiesOpen = !_mainViewModel.IsPropertiesOpen;
                return true;
            default:
                return false;
        }
    }

    // "Refresh" of the menu: what F5 does (the button by the search is gone)
    private void RefreshMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        if (_mainViewModel.Status.CanRefresh && _mainViewModel.RefreshCommand.CanExecute(null))
            _mainViewModel.RefreshCommand.Execute(null);
    }
}
