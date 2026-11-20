using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LiveryGallery.Views;

/// <summary>A livery in the archive's or a backup's list (<see cref="ViewModels.LiveryRowViewModel"/>)</summary>
internal sealed partial class LiveryRow : UserControl
{
    public LiveryRow() => AvaloniaXamlLoader.Load(this);
}
