using System.Diagnostics;

namespace LiveryGallery.Services.Diagnostics;

internal static class ExplorerLauncher
{
    /// <summary>Opens a folder in Explorer (made first when it is not there yet)</summary>
    public static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            AppLog.LogErrorThrottled("ExplorerLauncher", $"Failed to open '{folder}' in Explorer", ex);
        }
    }

    /// <summary>Opens Explorer with the file selected. Windows file names cannot contain quotes, so quoting is safe</summary>
    public static void ShowInFolder(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            AppLog.LogErrorThrottled("ExplorerLauncher", $"Failed to show '{filePath}' in Explorer", ex);
        }
    }
}
