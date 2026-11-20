using System.ComponentModel;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>
/// What the livery cards of a window bind their commands and the window's settings to: one object in the window's
/// resources (<c>CardScope</c>), given the window's view model once. A card's binding takes it as its source, found when
/// the card is built; before, each of its 11 bindings looked for the window among the card's ancestors
/// (<c>$parent[Window]</c>) and watched its DataContext, every card. docs/plans.md, «Облегчение карточки»
/// </summary>
internal sealed class CardScope : INotifyPropertyChanged
{
    private MainViewModel? _main;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The window's view model</summary>
    public MainViewModel? Main
    {
        get => _main;
        set
        {
            if (ReferenceEquals(_main, value)) return;
            _main = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Main)));
        }
    }
}
