using CommunityToolkit.Mvvm.Input;

namespace LiveryGallery.ViewModels;

/// <summary>
/// A chip of an "only …" filter that hides liveries now (<see cref="FilterBarViewModel.FilterChips"/>), beside the
/// quick filters' chips: what the filter is and its value, and a cross that takes it off
/// </summary>
internal sealed partial class ModeFilterChipViewModel(string text, Action remove)
{
    public string Text { get; } = text;

    [RelayCommand]
    private void Remove() => remove();
}
