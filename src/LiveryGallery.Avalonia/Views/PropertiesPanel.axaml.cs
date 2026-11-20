using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>
/// The properties panel on the right of the gallery (<see cref="PropertiesViewModel"/>). Its buttons are the gallery's
/// actions (<see cref="GalleryActions"/>), so they check, explain and undo as the menu and the keys do
/// </summary>
internal sealed partial class PropertiesPanel : UserControl
{
    public PropertiesPanel() => AvaloniaXamlLoader.Load(this);

    /// <summary>The cross in the panel's corner</summary>
    public event Action? CloseRequested;

    private PropertiesViewModel? Model => DataContext as PropertiesViewModel;

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    /// <summary>The cursor into the tag field (T, a card's tag button, the menu's "Tags")</summary>
    public void FocusTags() => this.FindControl<TagEditor>("TagEditor")?.FocusInput();

    /// <summary>The panel is hidden: the tags' list under the field goes with it (it is a popup over the window)</summary>
    public void CloseSuggestions() => this.FindControl<TagEditor>("TagEditor")?.CloseSuggestions();

    private void NoteBox_LostFocus(object? sender, RoutedEventArgs e) => Model?.SaveNote();
}
