using Avalonia.Controls;
using Avalonia.Interactivity;
using LiveryGallery.ViewModels.Collections;

namespace LiveryGallery.Views.Collections;

/// <summary>The heading of a group of tiles; the owner hears the fold and the link</summary>
internal partial class CollectionGroupHeader : UserControl
{
    public CollectionGroupHeader() => InitializeComponent();

    /// <summary>The group was folded or unfolded (its <see cref="CollectionGroupViewModel.IsExpanded"/> is already changed)</summary>
    public event Action<CollectionGroupViewModel>? Folded;

    /// <summary>The link on the right was clicked</summary>
    public event Action<CollectionGroupViewModel>? LinkClicked;

    private void Fold_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not CollectionGroupViewModel group) return;
        group.IsExpanded = !group.IsExpanded;
        Folded?.Invoke(group);
    }

    private void Link_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CollectionGroupViewModel group) LinkClicked?.Invoke(group);
    }
}
