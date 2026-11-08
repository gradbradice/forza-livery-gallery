using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Services.Accounts;

namespace LiveryGallery.ViewModels;

/// <summary>
/// An account's storage on the settings' folders page: where it is (its folder, or the program's), how many liveries
/// and how much, the free room of its disk, or that its folder is not there
/// </summary>
internal sealed partial class StorageRowViewModel(StorageRowInfo info) : ObservableObject
{
    public StorageRowInfo Info { get; } = info;

    public string Name => AccountLabels.Name(Info.DisplayName);

    public bool IsCurrent => Info.IsCurrent;

    public string CurrentText => Strings.AccountDataCurrent;

    /// <summary>Its own folder as it is; the program's shortened ("Program folder: …\games\fh6\storage\0009…")</summary>
    public string PathText => Info.IsCustom ? $"{Info.Folder} ({AccountLabels.Marker(Info.AccountKey)})"
        : string.Format(Strings.SettingsStorageProgramFolderFormat, Shorten(Info.Folder));

    public bool IsUnavailable => !Info.IsAvailable;

    public string DetailText
    {
        get
        {
            if (!Info.IsAvailable) return Strings.SettingsStorageUnavailable;
            var parts = new List<string>(3) { StatisticsViewModel.LiveryCount(Info.Liveries), ByteSizeFormatter.Format(Info.Bytes) };
            if (Info.FreeSpace is { } free)
                parts.Add(string.Format(Strings.SettingsStorageFreeFormat, free.Disk, ByteSizeFormatter.Format(free.Free)));
            return string.Join(" · ", parts);
        }
    }

    public string ChangeText => Strings.SettingsStorageChangeButton;

    /// <summary>The row "Open the storage folder" opens</summary>
    [ObservableProperty]
    private bool _isSelected;

    private static string Shorten(string path)
    {
        const int Most = 56;
        if (path.Length <= Most) return path;
        string tail = path[^Most..];
        int cut = tail.IndexOf(Path.DirectorySeparatorChar);
        return "…" + (cut > 0 ? tail[cut..] : tail);
    }
}

/// <summary>What the settings may do with the storages, without knowing the services behind them</summary>
/// <param name="List">Every account's storage</param>
/// <param name="ChangeFolder">Shows "Storage folder" for an account (its key, its nickname) over a window; true when it moved</param>
internal sealed record StorageActions(
    Func<Task<List<StorageRowInfo>>> List, Func<Avalonia.Controls.Window, string, string?, Task<bool>> ChangeFolder);
