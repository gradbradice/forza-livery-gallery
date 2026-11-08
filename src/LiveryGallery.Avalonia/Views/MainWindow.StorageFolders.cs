using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ForzaToolkit.UI.Dialogs;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;
using LiveryGallery.ViewModels.Collections;
using LiveryGallery.Views.Collections;

namespace LiveryGallery.Views;

/// <summary>
/// The storage's folders (docs/storage.md, stage 4): the panel left of the grid while the storage is shown, a card
/// dragged onto a folder, "Move to the folder" in a stored card's menu, F2 to rename the folder shown
/// </summary>
internal partial class MainWindow
{
    // Stored liveries dragged from the grid onto the panel: their keys
    private static readonly DataFormat<string[]> StoredDragFormat = DataFormat.CreateInProcessFormat<string[]>("LiveryGallery.StoredLiveries");

    // A press on a stored card: where, and whether a click without a drag selects it alone on release (it was selected
    // already, so a drag takes the whole selection)
    private (PointerPressedEventArgs Args, LiveryEntry Entry, Point At, bool SelectOnRelease)? _cardPress;

    private void InitializeStorageFolders()
    {
        _mainViewModel.StorageFoldersChanged += FillStorageSidebar;
        _mainViewModel.ModeChanged += _ =>
        {
            StorageSidebar.IsVisible = _mainViewModel.IsStorageMode && !_mainViewModel.IsStorageUnavailable;
            FillStorageSidebar();
        };
        _mainViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.GameCountText)) StorageSidebar.SetFooter(StorageRoomText());
            if (e.PropertyName is nameof(MainViewModel.StorageFolderView)) StorageSidebar.Select(_mainViewModel.StorageFolderView);
        };

        StorageSidebar.Picked += item => _mainViewModel.StorageFolderView = item.Id;
        StorageSidebar.CreateRequested += () => RunUiTask(() => CreateStorageFolderAsync(), "new storage folder");
        StorageSidebar.RenameRequested += item => RunUiTask(() => RenameStorageFolderAsync(item.Id, item.Name), "rename storage folder");
        StorageSidebar.DeleteRequested += item => RunUiTask(() => DeleteStorageFolderAsync(item.Id, item.Name), "delete storage folder");
        StorageSidebar.ActionRequested += item => RunUiTask(() => _mainViewModel.ReturnStorageFolderToGameAsync(item.Id), "return a folder to the game");
        StorageSidebar.SecondActionRequested += item => RunUiTask(() => _mainViewModel.BackUpStorageAsync(item.Id), "back up a storage folder");
        StorageSidebar.Reordered += _mainViewModel.ReorderStorageFolders;
        StorageSidebar.CanDrop = e => e.DataTransfer.Contains(StoredDragFormat);
        StorageSidebar.Dropped += (item, e) =>
        {
            if (e.DataTransfer.TryGetValue(StoredDragFormat) is not { Length: > 0 } keys) return;
            var set = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var entries = _mainViewModel.Gallery.AllEntries.Where(x => x.IsStored && set.Contains(x.FolderName)).ToList();
            _mainViewModel.MoveToStorageFolder(entries, item.Id == MainViewModel.NoFolderView ? null : item.Id);
        };
    }

    private void ApplyStorageFoldersTexts()
    {
        StorageSidebar.SetTexts(Strings.StorageFoldersHeading, Strings.StorageNewFolder, Strings.StorageFoldersHint);
        StorageSidebar.ActionText = Strings.StorageReturnFolder;
        StorageSidebar.SecondActionText = Strings.StorageBackupFolder;
        if (_mainViewModel.IsStorageMode) FillStorageSidebar();
    }

    /// <summary>"In the game 238 / 1 000 · free 762" under the folders</summary>
    private string StorageRoomText()
    {
        var culture = AppLocalisationService.Culture;
        int counted = Services.SaveRoom.Counted(_mainViewModel.GameEntries);
        return _profile.MaxLiveries is { } limit
            ? string.Format(Strings.StorageSidebarRoomFormat, counted.ToString("N0", culture), limit.ToString("N0", culture),
                Math.Max(0, limit - counted).ToString("N0", culture))
            : string.Format(Strings.StorageSidebarRoomNoLimitFormat, counted.ToString("N0", culture));
    }

    private void FillStorageSidebar()
    {
        if (!_mainViewModel.IsStorageMode) return;
        var folder = (Geometry?)this.FindResource("IconFolder");
        var (all, none, byFolder) = _mainViewModel.StorageFolderCounts();
        StorageSidebar.Items.Clear();
        StorageSidebar.Items.Add(new CollectionSidebarItem(MainViewModel.AllStoredView, Strings.StorageAll, folder, isUsers: false) { Count = all });
        // "No folder" takes liveries too: dropped there, they leave their folder
        StorageSidebar.Items.Add(new CollectionSidebarItem(MainViewModel.NoFolderView, Strings.StorageNoFolder, folder, isUsers: false, acceptsDrops: true)
        {
            Count = none,
        });
        bool first = true;
        foreach (var f in _mainViewModel.StorageFolders())
        {
            StorageSidebar.Items.Add(new CollectionSidebarItem(f.Id, f.Name, folder, isUsers: true)
            {
                Count = byFolder.GetValueOrDefault(f.Id),
                IsFirstOfUsers = first,
            });
            first = false;
        }
        StorageSidebar.Select(_mainViewModel.StorageFolderView);
        StorageSidebar.SetFooter(StorageRoomText());
    }

    private async Task<LiveryArchiveFolder?> CreateStorageFolderAsync(IReadOnlyList<LiveryEntry>? liveries = null)
    {
        string? name = await NameDialog.AskAsync(this, Strings.StorageNewFolder, Strings.StorageFolderNameLabel, "",
            n => _mainViewModel.StorageFolderNameProblem(n), Strings.AlbumCreate);
        if (name is null || _mainViewModel.CreateStorageFolder(name) is not { } folder) return null;
        if (liveries is { Count: > 0 }) _mainViewModel.MoveToStorageFolder(liveries, folder.Id);
        return folder;
    }

    private async Task RenameStorageFolderAsync(string id, string current)
    {
        string? name = await NameDialog.AskAsync(this, Strings.StorageFolderRenameTitle, Strings.StorageFolderNameLabel, current,
            n => _mainViewModel.StorageFolderNameProblem(n, id));
        if (name is not null) _mainViewModel.RenameStorageFolder(id, name);
    }

    private async Task DeleteStorageFolderAsync(string id, string name)
    {
        int count = _mainViewModel.StorageFolderCounts().ByFolder.GetValueOrDefault(id);
        bool toArchive = false;
        if (count == 0)
        {
            if (!await ConfirmDialog.AskAsync(this, Strings.StorageFolderDeleteTitle,
                    string.Format(Strings.StorageFolderDeleteEmptyMessageFormat, name), Strings.ButtonDelete, Strings.ButtonCancel))
                return;
        }
        else
        {
            var (confirmed, option) = await ConfirmDialog.AskWithOptionAsync(this, Strings.StorageFolderDeleteTitle,
                string.Format(Strings.StorageFolderDeleteMessageFormat, name, count.ToString("N0", AppLocalisationService.Culture)),
                Strings.StorageFolderDeleteToArchive, Strings.ButtonDelete, Strings.ButtonCancel);
            if (!confirmed) return;
            toArchive = option;
        }
        await _mainViewModel.DeleteStorageFolderAsync(id, toArchive);
    }

    /// <summary>F2 in the storage: the folder shown, when it is one of the user's</summary>
    private bool RenameShownStorageFolder()
    {
        if (!_mainViewModel.IsStorageMode
            || _mainViewModel.StorageFolders().FirstOrDefault(f => f.Id == _mainViewModel.StorageFolderView) is not { } folder)
            return false;
        RunUiTask(() => RenameStorageFolderAsync(folder.Id, folder.Name), "rename storage folder");
        return true;
    }

    /// <summary>"Move to the folder ▸" of a stored card's menu: no folder, each folder (the one they are all in ticked), a new one</summary>
    private void FillMoveToFolderMenu(Control card)
    {
        if (card.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(m => Equals(m.Tag, "moveToFolder")) is not { } menu) return;
        menu.Items.Clear();
        if (!_mainViewModel.IsStorageMode) return;
        var targets = _mainViewModel.Gallery.SelectedEntries.Where(e => e.IsStored).ToList();
        string? common = targets.Select(e => e.StorageFolderId).Distinct().Count() == 1 ? targets[0].StorageFolderId : "?";

        MenuItem Item(string header, string? folderId)
        {
            var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = targets.Count > 0 && common == folderId };
            item.Click += (_, _) => _mainViewModel.MoveToStorageFolder(targets, folderId);
            return item;
        }
        menu.Items.Add(Item(Strings.StorageNoFolder, null));
        var folders = _mainViewModel.StorageFolders();
        if (folders.Count > 0) menu.Items.Add(new Separator());
        foreach (var f in folders) menu.Items.Add(Item(f.Name, f.Id));
        menu.Items.Add(new Separator());
        var create = new MenuItem { Header = Strings.ContextMenuNewFolder };
        create.Click += (_, _) => RunUiTask(() => CreateStorageFolderAsync(targets), "new storage folder");
        menu.Items.Add(create);
    }

    // A stored card pressed: dragged onto a folder of the panel it takes the selection along (it is selected first)
    private void Card_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_cardPress is not { } press || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var delta = e.GetPosition(this) - press.At;
        if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6) return;
        _cardPress = null;
        RunUiTask(() => DragStoredAsync(press.Args), "drag stored liveries");
    }

    private async Task DragStoredAsync(PointerPressedEventArgs press)
    {
        string[] keys = [.. _mainViewModel.Gallery.SelectedEntries.Where(x => x.IsStored).Select(x => x.FolderName)];
        if (keys.Length == 0) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(StoredDragFormat, keys));
        await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move);
    }

    private void Card_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_cardPress is { SelectOnRelease: true } press) _mainViewModel.Gallery.ClickSelect(press.Entry, ctrl: false, shift: false);
        _cardPress = null;
    }
}
