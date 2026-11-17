using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LiveryGallery.Localisation;

namespace LiveryGallery.Views;

/// <summary>
/// The small window shown at once while the game's services and the gallery's window are being made: before it, a
/// double-click showed nothing for a long while, and a user clicked again (a user's report, 1.7.1). It closes when the
/// gallery's window opens
/// </summary>
internal sealed class StartupWindow : Window
{
    private static readonly Uri IconUri = new("avares://LiveryGallery.Avalonia/Assets/app-icon.png");

    public StartupWindow(string programName)
    {
        Title = programName;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // A native border and shadow, no title bar, as the gallery's dialogs; still in the taskbar so it is seen to be starting
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;
        ShowInTaskbar = true;
        Bind(BackgroundProperty, this.GetResourceObservable("BgBrush"));

        Bitmap? icon = null;
        try
        {
            using var stream = AssetLoader.Open(IconUri);
            icon = new Bitmap(stream);
            Icon = new WindowIcon(icon);
        }
        catch (Exception e) when (e is FileNotFoundException or InvalidOperationException or ArgumentException)
        {
            // No icon: the window still says what is starting
        }

        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = programName, FontSize = 18, FontWeight = FontWeight.SemiBold });
        var starting = new TextBlock { Name = "StartingText", Text = Strings.StartupStarting };
        starting.Bind(TextBlock.ForegroundProperty, starting.GetResourceObservable("TextSecondaryBrush"));
        text.Children.Add(starting);
        var row = new DockPanel();
        if (icon is not null)
        {
            var image = new Image { Source = icon, Width = 48, Height = 48, Margin = new Thickness(0, 0, 16, 0) };
            DockPanel.SetDock(image, Dock.Left);
            row.Children.Add(image);
        }
        row.Children.Add(text);
        var panel = new StackPanel { Spacing = 20, Margin = new Thickness(24, 22) };
        panel.Children.Add(row);
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 4, MinHeight = 4 });
        Content = panel;
    }
}
