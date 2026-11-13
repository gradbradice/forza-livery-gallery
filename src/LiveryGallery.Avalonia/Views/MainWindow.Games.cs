using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

/// <summary>
/// The game switcher in the title bar, the account switcher next to the search, and the screen of a game whose save
/// was not found. Accounts are added and forgotten in the settings, not here
/// </summary>
internal partial class MainWindow
{
    /// <summary>The games to switch to, from the host; without it the button only shows the game</summary>
    public GameSwitcher? Games { get; init; }

    private bool _switchingGame;

    private void GameButton_Click(object? sender, RoutedEventArgs e) => RunUiTask(ShowGameMenuAsync, "game menu");

    private void AccountSwitcherButton_Click(object? sender, RoutedEventArgs e) => ShowAccountMenu();

    /// <summary>Every supported game, the one shown ticked; a game without a save says so under its name</summary>
    private async Task ShowGameMenuAsync()
    {
        if (_isClosing) return;
        var choices = Games is { } games ? await games.ListAsync() : [];
        if (_isClosing) return;

        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        TextBlock? firstName = null;
        foreach (var choice in choices)
        {
            var header = new StackPanel { Spacing = 1 };
            var name = new TextBlock { Text = choice.Name, FontSize = 13, FontWeight = FontWeight.SemiBold };
            firstName ??= name;
            header.Children.Add(name);
            if (choice.State == GameSaveState.NotFound)
                header.Children.Add(new TextBlock { Text = Strings.GameStateNotFound, FontSize = 11, Opacity = 0.7 });
            var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.Radio, IsChecked = Games!.IsCurrent(choice.Id) };
            string id = choice.Id;
            item.Click += (_, _) => RunUiTask(() => SwitchGameAsync(id), "switch game");
            menu.Items.Add(item);
        }
        ShowUnderText(menu, GameButton, GameButtonText, firstName, _gameMenuOffset, shift => _gameMenuOffset = shift);
    }

    // How far left the game menu opens so that the games' names stand under the button's: measured on its first
    // opening, kept for the next ones (it depends on the menu's look only)
    private double? _gameMenuOffset;

    /// <summary>
    /// Opens <paramref name="menu"/> under <paramref name="button"/> with the items' text right under the button's
    /// (<paramref name="buttonText"/>): the menu opens at the button's left edge and its items' text begins further right,
    /// after the menu's padding and the column of the radio mark. The first opening measures that distance and moves the
    /// open menu by it; the next ones open where it was moved to
    /// </summary>
    private static void ShowUnderText(MenuFlyout menu, Control button, Visual buttonText, TextBlock? itemText, double? offset,
        Action<double> remember)
    {
        if (offset is not null || itemText is null)
        {
            menu.HorizontalOffset = offset ?? 0;
            menu.ShowAt(button);
            return;
        }
        void Align(object? sender, EventArgs e)
        {
            menu.Opened -= Align;
            // The items are laid out first: the menu is a window of its own, laid out after it opens. Its left edge is
            // the button's (BottomEdgeAlignedLeft), so the distance is taken inside each, not on the screen, where the
            // menu may not stand yet
            itemText.UpdateLayout();
            if (TopLevel.GetTopLevel(itemText) is not { } menuTop || !itemText.IsEffectivelyVisible
                || itemText.TranslatePoint(default, menuTop) is not { } item || buttonText.TranslatePoint(default, button) is not { } under)
                return;
            double shift = menu.HorizontalOffset + under.X - item.X;
            menu.HorizontalOffset = shift;
            menu.Popup.HorizontalOffset = shift;
            remember(shift);
        }
        menu.Opened += Align;
        menu.ShowAt(button);
    }

    /// <summary>This game's accounts: the name, then the end of the XUID in the quiet style ("ZDaurtsev ***6B60")</summary>
    private void ShowAccountMenu()
    {
        if (_isClosing) return;
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var option in _mainViewModel.Accounts)
        {
            var item = new MenuItem
            {
                Header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        // Both on the bottom: the smaller marker stands on the same line as the name
                        new TextBlock
                        {
                            Text = option.Name, FontSize = 13, MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis,
                            VerticalAlignment = VerticalAlignment.Bottom,
                        },
                        new TextBlock
                        {
                            Text = option.Marker, FontSize = 12, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Bottom,
                            Margin = new Avalonia.Thickness(0, 0, 0, 1),
                        },
                    },
                },
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = ReferenceEquals(option, _mainViewModel.SelectedAccount),
            };
            ToolTip.SetTip(item, option.Tooltip);
            var chosen = option;
            item.Click += (_, _) => _mainViewModel.SelectedAccount = chosen; // the view model switches
            menu.Items.Add(item);
        }
        menu.ShowAt(AccountSwitcherButton);
    }

    /// <summary>
    /// Another game: this window shuts its game's services down (they let go of the save, the renderer, the server),
    /// the host opens the other game's window, and this one closes. Nothing happens while the save is being written:
    /// the shutdown waits for it
    /// </summary>
    /// <summary>The window closes for another game's: the app goes on</summary>
    internal bool IsSwitchingGame => _switchingGame;

    internal async Task SwitchGameAsync(string gameId)
    {
        if (_isClosing || _switchingGame || Games is not { } games || games.IsCurrent(gameId)) return;
        _switchingGame = true;
        try
        {
            await ShutdownAsync();
            await games.SwitchToAsync(gameId);
        }
        catch (Exception ex)
        {
            AppLog.LogError($"Failed to switch to the game '{gameId}'", ex);
        }
        finally
        {
            _shutdownCompleted = true;
            Close();
        }
    }

    private void ChooseSaveFolderButton_Click(object? sender, RoutedEventArgs e) =>
        RunUiTask(() => _mainViewModel.PromptForSavePathAsync(initial: false), "choose save folder");

    private void SearchSaveAgainButton_Click(object? sender, RoutedEventArgs e) =>
        RunUiTask(_mainViewModel.SearchSaveAgainAsync, "search save again");
}
