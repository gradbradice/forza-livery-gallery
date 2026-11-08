using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Localisation;

namespace LiveryGallery.Views.Collections;

/// <summary>
/// Asks for a name (an album of screenshots, a folder of the storage). The caller says what is wrong with a name
/// (empty, taken); the dialog closes only with a good one. Enter confirms, Esc cancels
/// </summary>
internal partial class NameDialog : Window
{
    private readonly Func<string, string?> _problem;
    private string? _result;

    public NameDialog() : this("", "", "", _ => null) { }

    /// <param name="problem">What is wrong with a name, for the line under the box; null when it is fine</param>
    public NameDialog(string title, string label, string initial, Func<string, string?> problem, string? okText = null)
    {
        InitializeComponent();
        _problem = problem;
        Title = title;
        Bar.Title = title;
        Bar.IconKey = "IconPalette";
        LabelText.Text = label;
        NameBox.Text = initial;
        OkButton.Content = okText ?? Strings.ButtonSave;
        CancelButton.Content = Strings.ButtonCancel;
        Opened += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    /// <summary>The name the user gave; null when cancelled</summary>
    public static async Task<string?> AskAsync(Window owner, string title, string label, string initial,
        Func<string, string?> problem, string? okText = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var dialog = new NameDialog(title, label, initial, problem, okText);
        await dialog.ShowDialog(owner).ConfigureAwait(true);
        return dialog._result;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => TryClose();

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void NameBox_TextChanged(object? sender, TextChangedEventArgs e) => ErrorText.IsVisible = false;

    private void NameBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        TryClose();
    }

    private void TryClose()
    {
        string name = NameBox.Text ?? "";
        if (_problem(name) is { } problem)
        {
            ErrorText.Text = problem;
            ErrorText.IsVisible = true;
            return;
        }
        _result = name.Trim();
        Close();
    }
}
