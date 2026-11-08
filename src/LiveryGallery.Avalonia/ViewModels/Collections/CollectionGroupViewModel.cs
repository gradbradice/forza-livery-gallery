using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LiveryGallery.ViewModels.Collections;

/// <summary>
/// The heading of a group of tiles (<see cref="Views.Collections.CollectionGroupHeader"/>): a fold arrow, a small
/// picture, the title, a grey line, the count, and a link on the right
/// </summary>
internal partial class CollectionGroupViewModel(string key, string title, string? subtitle, string countText) : ObservableObject
{
    /// <summary>The same group after a regroup: what was folded stays folded</summary>
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string? Subtitle { get; } = subtitle;
    public string CountText { get; } = countText;
    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private Bitmap? _thumbnail;

    /// <summary>The link on the right ("Open the livery"); none when null</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLink))]
    private string? _linkText;

    public bool HasLink => !string.IsNullOrEmpty(LinkText);
}
