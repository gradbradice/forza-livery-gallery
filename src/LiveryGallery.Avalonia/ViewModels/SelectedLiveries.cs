using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace LiveryGallery.ViewModels;

/// <summary>
/// The selected liveries. Selecting many at once (Ctrl+A, a Shift range) says so once: one change per livery would run
/// everything that follows the selection (the menu's texts, what may go to the archive) once per livery, each time over
/// the whole selection, 3000 liveries taking seconds
/// </summary>
internal sealed class SelectedLiveries : ObservableCollection<LiveryEntry>
{
    /// <summary>Adds <paramref name="entries"/> at the end with one change (a reset)</summary>
    public void AddMany(IEnumerable<LiveryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        CheckReentrancy();
        int before = Count;
        foreach (var entry in entries) Items.Add(entry);
        if (Count == before) return;
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>Only <paramref name="entries"/>, with one change (a reset); none when it already holds them, in that order</summary>
    public void ReplaceAll(IEnumerable<LiveryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        CheckReentrancy();
        var next = entries.ToList();
        if (next.Count == Count && next.SequenceEqual(Items, ReferenceEqualityComparer.Instance)) return;
        bool countChanges = next.Count != Count;
        Items.Clear();
        foreach (var entry in next) Items.Add(entry);
        if (countChanges) OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
