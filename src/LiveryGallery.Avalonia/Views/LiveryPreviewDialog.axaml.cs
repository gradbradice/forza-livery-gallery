using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using LiveryGallery.Localisation;

namespace LiveryGallery.Views;

internal partial class LiveryPreviewDialog : Window
{
    public LiveryPreviewDialog(string webpPath)
    {
        InitializeComponent();

        Title = Strings.PreviewDialogTitle;
        Bar.Title = Strings.PreviewDialogTitle;

        try
        {
            using var stream = File.OpenRead(webpPath);
            var bitmap = new Bitmap(stream);
            PreviewImage.Source = bitmap;
            // The full-size picture goes with the window, not with the next collection
            Closed += (_, _) =>
            {
                PreviewImage.Source = null;
                bitmap.Dispose();
            };
        }
        catch (Exception ex)
        {
            AppLog.LogError($"Failed to load preview image '{webpPath}'", ex);
        }
    }

}
