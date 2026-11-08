using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>"Return to the game" with too little room: closes with the user's choice, or null</summary>
internal partial class ReturnRoomDialog : Window
{
    private readonly ReturnRoomViewModel _viewModel;

    public ReturnRoomDialog() : this(new ReturnRoomViewModel(1, 0, 1, [], [], DateTime.Today)) { } // the designer

    public ReturnRoomDialog(ReturnRoomViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Title = Strings.ReturnBeyondLimitTitle;
        TitleBarText.Text = Strings.ReturnBeyondLimitTitle;
        FolderLabel.Text = Strings.ReturnRoomFolderLabel;
        CancelButton.Content = Strings.ButtonCancel;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void ApplyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.Choice() is { } choice) Close(choice);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape) return;
        e.Handled = true;
        Close(null);
    }

    /// <summary>Shows the window over <paramref name="owner"/>; the choice, or null when cancelled</summary>
    public static Task<ReturnRoomChoice?> AskAsync(Window owner, ReturnRoomViewModel viewModel) =>
        new ReturnRoomDialog(viewModel).ShowDialog<ReturnRoomChoice?>(owner);
}
