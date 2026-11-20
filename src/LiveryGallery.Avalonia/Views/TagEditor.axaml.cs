using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>
/// The tags of one livery or of several (<see cref="TagEditorViewModel"/>), in the properties panel and the livery
/// window: chips with a cross, dashed ones that a click gives to all, a field with the gallery's tags offered
/// </summary>
internal sealed partial class TagEditor : UserControl
{
    /// <summary>"Take the tag X off", the name a screen reader gives a chip's cross</summary>
    public static readonly IValueConverter RemoveTagName =
        new FuncValueConverter<string?, string>(tag => string.Format(CultureInfo.CurrentCulture, Strings.PropertiesRemoveTagFormat, tag));

    public TagEditor() => AvaloniaXamlLoader.Load(this);

    private TagEditorViewModel? Model => DataContext as TagEditorViewModel;

    /// <summary>The list under the field goes: its panel was closed or hidden (the list is a popup over the window)</summary>
    public void CloseSuggestions() => Model?.SetInputFocused(false);

    // The window this editor listens to for presses elsewhere, while it is on screen
    private TopLevel? _pressSource;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _pressSource = TopLevel.GetTopLevel(this);
        _pressSource?.AddHandler(PointerPressedEvent, AnyPress, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _pressSource?.RemoveHandler(PointerPressedEvent, AnyPress);
        _pressSource = null;
        CloseSuggestions();
    }

    // A press anywhere but the field and its list closes the list, as a click away from a combo box does: on the
    // panel's text or background too, which take no focus (the field would keep it, and the list with it). Typing in
    // the field brings the list back (TagInput_KeyDown)
    private void AnyPress(object? sender, PointerPressedEventArgs e)
    {
        if (Model is not { ShowSuggestions: true } model || e.Source is not Avalonia.Visual pressed) return;
        var box = this.FindControl<TextBox>("TagInputBox");
        var list = this.FindControl<Avalonia.Controls.Primitives.Popup>("SuggestionPopup")?.Child;
        foreach (var visual in pressed.GetSelfAndVisualAncestors())
            if (ReferenceEquals(visual, box) || ReferenceEquals(visual, list)) return;
        model.SetInputFocused(false);
    }

    /// <summary>The cursor into the field (T, the card's tag button, the menu's "Tags")</summary>
    public void FocusInput()
    {
        if (this.FindControl<TextBox>("TagInputBox") is { } box)
        {
            box.Focus(NavigationMethod.Tab);
            box.CaretIndex = box.Text?.Length ?? 0;
        }
    }

    // A dashed chip: the tag onto those that do not have it yet
    private void PartTag_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PropertyTag { OnAll: false } tag }) Model?.AddTagCommand.Execute(tag.Tag);
    }

    private void RemoveTag_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PropertyTag tag }) Model?.RemoveTagCommand.Execute(tag.Tag);
    }

    private void Suggestion_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TagSuggestion suggestion })
        {
            Model?.ChooseSuggestionCommand.Execute(suggestion);
            FocusInput();
        }
    }

    private void TagInput_GotFocus(object? sender, FocusChangedEventArgs e) => Model?.SetInputFocused(true);

    // A suggestion clicked takes the focus first: the list stays while the focus is in the editor or its popup
    private void TagInput_LostFocus(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsKeyboardFocusWithin && this.FindControl<Avalonia.Controls.Primitives.Popup>("SuggestionPopup")?.Child?.IsKeyboardFocusWithin != true) Model?.SetInputFocused(false);
        });

    // Enter adds what is typed; Esc empties the field; the down arrow goes to the suggestions
    private void TagInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not { } model) return;
        // Typing again after a click elsewhere closed the list: the list comes back
        if (sender is TextBox { IsFocused: true }) model.SetInputFocused(true);
        switch (e.Key)
        {
            case Key.Enter:
                model.AddTypedTagCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape when model.TagInput.Length > 0:
                model.TagInput = "";
                e.Handled = true;
                break;
            case Key.Down when model.ShowSuggestions:
                if (this.FindControl<ItemsControl>("SuggestionList")?.ContainerFromIndex(0) is { } first
                    && first.GetVisualDescendants().OfType<Button>().FirstOrDefault() is { } button)
                    button.Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
        }
    }
}
