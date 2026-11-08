using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Services.Accounts;
using LiveryGallery.Services.Diagnostics;
using LiveryGallery.ViewModels;
using LiveryGallery.Viewer3D;

namespace LiveryGallery.Views;

internal partial class SettingsDialog : Window
{
    /// <summary>The page the dialog opened on last time, for the rest of this run</summary>
    private static SettingsPage _lastPage = SettingsPage.Appearance;

    private readonly SettingsViewModel _viewModel;
    private readonly GameProfile _profile;
    private readonly Func<Stream, CancellationToken, Task>? _exportDiagnostics;
    private readonly AccountDataActions? _accountData;
    private readonly Services.Screenshots.ScreenshotStore? _screenshots;
    private readonly StorageActions? _storages;
    private readonly BackgroundTaskTracker _tasks = new();
    private Task<bool>? _saveInProgress;
    private readonly UnsavedChangesGuard _closeGuard;
    private bool _closed;
    private SettingsPage _page = _lastPage;
    private bool _updatingPageList;

    public bool SavePathChanged => _viewModel.SavePathChanged;

    /// <param name="profile">The game: its saves and installation, and which pages it has (find in game, server)</param>
    /// <param name="exportDiagnostics">Writes the diagnostics ZIP to the stream; null hides the button</param>
    /// <param name="accountData">Lists and forgets stored account data; null hides the section</param>
    /// <param name="carTextLanguages">The game languages car names can be shown in (read from the game)</param>
    public SettingsDialog(
        AppSettingsData settings, string? resolvedSavePath, IGameFolderCheck gameFolderCheck, GameProfile profile,
        Func<Stream, CancellationToken, Task>? exportDiagnostics = null,
        AccountDataActions? accountData = null,
        IReadOnlyList<ForzaToolkit.StringTables.GameLanguage>? carTextLanguages = null,
        Services.Screenshots.ScreenshotStore? screenshots = null,
        StorageActions? storages = null)
    {
        InitializeComponent();
        _storages = storages;
        _profile = profile;
        _exportDiagnostics = exportDiagnostics;
        _accountData = accountData;
        _screenshots = screenshots;
        DiagnosticsSection.IsVisible = exportDiagnostics is not null;
        _viewModel = new SettingsViewModel(settings, resolvedSavePath, gameFolderCheck, profile, carTextLanguages);
        DataContext = _viewModel;

        ApplyLocalizedTexts();
        if (_accountData is not null) Opened += (_, _) => _ = _tasks.Run(LoadStoredAccountsAsync, "settings: stored accounts");
        if (_accountData?.Accounts is not null) Opened += (_, _) => _ = _tasks.Run(LoadAccountsAsync, "settings: accounts");
        if (_screenshots is not null) Opened += (_, _) => _ = _tasks.Run(LoadScreenshotsUsageAsync, "settings: screenshots");
        if (_storages is not null) Opened += (_, _) => _ = _tasks.Run(LoadStoragesAsync, "settings: storages");
        UpdateHintDisplay();
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SettingsViewModel.HintIsError)) UpdateHintDisplay();
        };

        _closeGuard = new UnsavedChangesGuard(this, () => _viewModel.IsDirty, SaveOnceAsync,
            () => Strings.UnsavedChangesTitle, () => Strings.UnsavedChangesMessage,
            beforeChecking: () => _tasks.WaitAllAsync(Timeout.InfiniteTimeSpan));
        _ = FindGameFolderAsync();
    }

    /// <summary>The game's folder found on this PC: shown, and used while none is given</summary>
    private async Task FindGameFolderAsync()
    {
        try
        {
            string? found = await _profile.FindInstallFolderAsync();
            if (_closed) return;
            _viewModel.FoundGamePath = found;
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to find the game folder", ex);
        }
    }

    private void ApplyLocalizedTexts()
    {
        Title = Strings.SettingsDialogTitle;
        TitleBarText.Text = Strings.SettingsDialogTitle;
        TitleText.Text = Strings.SettingsDialogTitle;
        ThemeSectionLabel.Text = Strings.SettingsSectionTheme;
        LanguageSectionLabel.Text = Strings.SettingsSectionLanguage;
        CarTextLanguageSectionLabel.Text = Strings.SettingsSectionCarTextLanguage;
        CarTextLanguageHintText.Text = _viewModel.HasGameCarTexts ? Strings.CarTextLanguageHint : Strings.CarTextLanguageNoGameTexts;
        AccountDataSectionLabel.Text = Strings.AccountDataSectionLabel;
        AccountDataHint.Text = Strings.AccountDataHint;
        GamePathLabel.Text = Strings.SettingsMenuGamePath;
        GameFolderBox.PickerTitle = Strings.SelectGameFolderDialogTitle;
        GameFolderBox.RefreshTexts();
        SavePathLabel.Text = Strings.SavePathFieldLabel;
        AccountsSectionLabel.Text = Strings.SettingsAccountsSectionLabel;
        AccountsHint.Text = Strings.SettingsAccountsHint;
        AddAccountButtonText.Text = Strings.SettingsAccountAddButton;
        AddAccountLabel.Text = Strings.SettingsAccountNewLabel;
        AddAccountConfirmButton.Content = Strings.SettingsAccountAddConfirm;
        AddAccountCancelButton.Content = Strings.ButtonCancel;
        StorageSectionLabel.Text = Strings.SettingsStorageSectionLabel;
        StorageHint.Text = Strings.SettingsStorageHint;
        OpenStorageFolderButton.Content = Strings.SettingsStorageOpenButton;
        ScanningSectionLabel.Text = Strings.SettingsSectionScanning;
        AutoRefreshLiveriesCheckBox.Content = Strings.AutoRefreshLiveriesLabel;
        RefreshOnButtonClickCheckBox.Content = Strings.RefreshOnButtonClickLabel;
        // The toolkit's text, the converter's setting too
        SearchByFolderNameCheckBox.Content = ForzaToolkit.UI.Localisation.UiStrings.ShowFolderNames;
        ToolTip.SetTip(SearchByFolderNameCheckBox, ForzaToolkit.UI.Localisation.UiStrings.ShowFolderNamesTooltip);
        DiagnosticsSectionLabel.Text = Strings.DiagnosticsSectionLabel;
        DiagnosticsHint.Text = Strings.DiagnosticsHint;
        ExportDiagnosticsButton.Content = Strings.ExportDiagnosticsButton;
        ShowChangesSinceLastLaunchCheckBox.Content = Strings.ShowChangesSinceLastLaunchLabel;
        WarnFewFreeSlotsText.Text = Strings.WarnFewFreeSlotsLabel;
        ScreenshotsSectionLabel.Text = Strings.ScreenshotsLabel;
        ScreenshotsOriginalRadio.Content = Strings.ScreenshotsKeepOriginal;
        ScreenshotsWebPRadio.Content = Strings.ScreenshotsWebP;
        ScreenshotsQualityLabel.Text = Strings.ScreenshotsQualityLabel;
        ScreenshotsWebPHint.Text = Strings.ScreenshotsWebPHint;
        ScreenshotsInBackupsCheckBox.Content = Strings.ScreenshotsInBackups;
        ScreenshotsConvertButton.Content = Strings.ScreenshotsConvertButton;
        ScreenshotsFolderButton.Content = Strings.ScreenshotsOpenFolder;
        UpdatesSectionLabel.Text = Strings.UpdatesSectionLabel;
        BetaUpdatesCheckBox.Content = Strings.BetaUpdatesLabel;
        BetaUpdatesHint.Text = Strings.BetaUpdatesHint;
        ServerConnectionCheckBox.Content = Strings.ServerConnectionLabel;
        ServerConnectionDescription.Text = Strings.ServerConnectionDescription;
        DuplicatesSectionLabel.Text = Strings.SettingsSectionDuplicates;
        AutoBackupSectionLabel.Text = Strings.AutoBackupSectionLabel;
        AutoBackupBeforeArchiveCheckBox.Content = Strings.AutoBackupBeforeArchiveLabel;
        AutoBackupOnChangeCheckBox.Content = Strings.AutoBackupOnChangeLabel;
        AutoBackupPeriodicCheckBox.Content = Strings.AutoBackupPeriodicLabel;
        AutoBackupKeepLabel.Text = Strings.AutoBackupKeepLabel;
        AutoBackupHint.Text = Strings.AutoBackupHint;
        ArchiveWarningsSectionLabel.Text = Strings.ArchiveWarningsSectionLabel;
        WarnArchiveWithoutInstalledDataCheckBox.Content = Strings.WarnArchiveWithoutInstalledDataLabel;
        WarnArchiveWithOutdatedInstalledDataCheckBox.Content = Strings.WarnArchiveWithOutdatedInstalledDataLabel;
        ArchiveWarningsHint.Text = Strings.ArchiveWarningsHint;
        PossibleDuplicateThresholdLabel.Text = Strings.PossibleDuplicateThresholdLabel;
        PossibleDuplicateThresholdHint.Text = Strings.PossibleDuplicateThresholdHint;
        NavigationRowsLabel.Text = Strings.NavigationRowsLabel;
        ServerLiveriesLabel.Text = Strings.ServerLiveriesLabel;
        ServerLiveriesHint.Text = Strings.ServerLiveriesHint;
        NavigationKeyIntervalLabel.Text = Strings.NavigationKeyIntervalLabel;
        NavigationKeyIntervalHint.Text = Strings.NavigationKeyIntervalHint;
        SystemThemeRadio.Content = Strings.ThemeSystemLabel;
        LightThemeRadio.Content = Strings.ThemeLightLabel;
        DarkThemeRadio.Content = Strings.ThemeDarkLabel;
        ExitButton.Content = Strings.ButtonExit;
        SaveButton.Content = Strings.ButtonSave;
        UpdatePageList();
    }

    /// <summary>The pages there is something to show on, with their names in the current language</summary>
    private List<SettingsPageItem> AvailablePages()
    {
        var pages = new List<SettingsPageItem>
        {
            new(SettingsPage.Appearance, Strings.SettingsPageAppearance, this.GetThemeGeometry("IconPalette")),
            new(SettingsPage.Folders, Strings.SettingsMenuPaths, this.GetThemeGeometry("IconFolder")),
            new(SettingsPage.Gallery, Strings.SettingsPageGallery, this.GetThemeGeometry("IconPreview")),
            new(SettingsPage.Backups, Strings.MenuOpenBackups, this.GetThemeGeometry("IconBackup")),
        };
        // Pages of what this game has
        if (_profile.MyDesigns is not null)
            pages.Add(new(SettingsPage.FindInGame, Strings.SettingsSectionNavigation, this.GetThemeGeometry("IconLocate")));
        if (_profile.Features.LiveryServer)
            pages.Add(new(SettingsPage.Server, Strings.SettingsSectionServer, this.GetThemeGeometry("IconGlobe")));
        if (AccountDataSection.IsVisible || DiagnosticsSection.IsVisible)
            pages.Add(new(SettingsPage.Data, Strings.SettingsPageData, this.GetThemeGeometry("IconInfo")));
        return pages;
    }

    /// <summary>Fills the list on the left (again after a language change) and shows the current page</summary>
    private void UpdatePageList()
    {
        var pages = AvailablePages();
        var current = pages.FirstOrDefault(p => p.Page == _page) ?? pages[0];
        _updatingPageList = true;
        try
        {
            PageList.ItemsSource = pages;
            PageList.SelectedItem = current;
        }
        finally
        {
            _updatingPageList = false;
        }
        ShowPage(current);
    }

    /// <summary>Opens <paramref name="page"/>, as if picked in the list</summary>
    private void SelectPage(SettingsPage page)
    {
        if (PageList.ItemsSource is not IEnumerable<SettingsPageItem> pages) return;
        if (pages.FirstOrDefault(p => p.Page == page) is { } item) PageList.SelectedItem = item;
    }

    private void PageList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingPageList || PageList.SelectedItem is not SettingsPageItem item) return;
        ShowPage(item);
    }

    private void ShowPage(SettingsPageItem item)
    {
        bool changed = item.Page != _page;
        _page = _lastPage = item.Page;
        PageTitleText.Text = item.Title;
        // Pages whose every setting is the open game's own; on the others the plate stands beside each such setting
        PageGameChip.IsVisible = item.Page is SettingsPage.Folders or SettingsPage.FindInGame or SettingsPage.Server;
        AppearancePage.IsVisible = item.Page == SettingsPage.Appearance;
        FoldersPage.IsVisible = item.Page == SettingsPage.Folders;
        GalleryPage.IsVisible = item.Page == SettingsPage.Gallery;
        BackupsPage.IsVisible = item.Page == SettingsPage.Backups;
        FindInGamePage.IsVisible = item.Page == SettingsPage.FindInGame;
        ServerPage.IsVisible = item.Page == SettingsPage.Server;
        DataPage.IsVisible = item.Page == SettingsPage.Data;
        if (changed) PageScroll.Offset = default;
    }

    private void UpdateHintDisplay()
    {
        if (_viewModel.HintIsError)
        {
            HintText.Text = Strings.SaveFolderValidationFailed;
            HintText.Foreground = this.GetThemeBrush("DangerBrush");
        }
        else
        {
            // The usual place of the saves, for a game that keeps them in a PC GameSave folder
            HintText.Text = _profile.Game.GetSaveLocation<PcGameSaveLocation>()?.GameId is { } id
                ? Strings.PathFieldAutoHint + ". " + string.Format(Strings.SaveFolderContentHintFormat, id)
                : Strings.PathFieldAutoHint;
            HintText.Foreground = this.GetThemeBrush("TextSecondaryBrush");
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private bool _askingServerConsent;

    private void ServerConnectionCheckBox_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_askingServerConsent || ServerConnectionCheckBox.IsChecked != true || _viewModel.IsServerConnectionSaved) return;
        _ = _tasks.Run(AskServerConsentAsync, "server consent");
    }

    private async Task AskServerConsentAsync()
    {
        _askingServerConsent = true;
        bool agreed = false;
        try
        {
            agreed = await ConfirmDialog.AskAsync(this, Strings.ServerConsentTitle, Strings.ServerConsentMessage,
                Strings.ServerConsentAccept, Strings.ButtonCancel, "IconGlobe");
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to show the livery server consent dialog", ex);
        }
        finally
        {
            if (!agreed)
            {
                ServerConnectionCheckBox.IsChecked = false;
                _viewModel.ServerConnectionEnabled = false;
            }
            _askingServerConsent = false;
        }
    }

    private void SaveBrowseButton_Click(object? sender, RoutedEventArgs e) =>
        _ = _tasks.Run(async () =>
        {
            string? path = await BrowseForFolderAsync(Strings.SelectFolderDialogTitle);
            if (path is not null) _viewModel.SavePath = path;
        }, "browse for save folder");

    private async Task LoadStoredAccountsAsync()
    {
        if (_accountData is null) return;
        var accounts = await _accountData.List();
        if (_closed) return;
        _viewModel.SetStoredAccounts(accounts);
        // Only the open account: nothing to forget, and the section would only take space
        bool show = accounts.Any(a => !a.IsCurrent);
        if (show == AccountDataSection.IsVisible) return;
        AccountDataSection.IsVisible = show;
        UpdatePageList(); // the data page may appear or go
    }

    /// <summary>The accounts the switcher offers, on the folders page; made again after a language change</summary>
    private async Task LoadAccountsAsync()
    {
        if (_accountData?.Accounts is not { } list) return;
        var accounts = await list();
        if (_closed) return;
        AccountsList.ItemsSource = accounts.Select(a => new AccountRowViewModel(a)).ToList();
        AccountsSection.IsVisible = true;
    }

    /// <summary>Every account's storage, on the folders page; the open account's chosen for "Open the storage folder"</summary>
    private async Task LoadStoragesAsync()
    {
        if (_storages is null) return;
        string? selected = (StorageList.ItemsSource as IEnumerable<StorageRowViewModel>)?.FirstOrDefault(r => r.IsSelected)?.Info.AccountKey;
        var rows = (await _storages.List()).Select(r => new StorageRowViewModel(r)).ToList();
        if (_closed) return;
        var chosen = rows.FirstOrDefault(r => string.Equals(r.Info.AccountKey, selected, StringComparison.OrdinalIgnoreCase))
            ?? rows.FirstOrDefault(r => r.IsCurrent) ?? rows.FirstOrDefault();
        if (chosen is not null) chosen.IsSelected = true;
        StorageList.ItemsSource = rows;
        StorageSection.IsVisible = rows.Count > 0;
    }

    private void ChangeStorageFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_storages is null || (sender as Control)?.DataContext is not StorageRowViewModel row) return;
        _ = _tasks.Run(async () =>
        {
            if (await _storages.ChangeFolder(this, row.Info.AccountKey, row.Info.DisplayName)) await LoadStoragesAsync();
        }, "settings: storage folder");
    }

    private void OpenStorageFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        var rows = StorageList.ItemsSource as IEnumerable<StorageRowViewModel>;
        if (rows?.FirstOrDefault(r => r.IsSelected) is not { } row) return;
        // A folder of the user's that is not there is not made again
        if (row.Info.IsCustom && !Directory.Exists(row.Info.Folder)) return;
        Services.Diagnostics.ExplorerLauncher.OpenFolder(row.Info.Folder);
    }

    private void AddAccountButton_Click(object? sender, RoutedEventArgs e)
    {
        AddAccountRow.IsVisible = true;
        AddAccountButton.IsVisible = false;
        AddAccountError.IsVisible = false;
        AddAccountPathBox.Text = "";
        AddAccountPathBox.Focus();
    }

    private void AddAccountCancelButton_Click(object? sender, RoutedEventArgs e) => CloseAddAccountRow();

    private void CloseAddAccountRow()
    {
        AddAccountRow.IsVisible = false;
        AddAccountButton.IsVisible = true;
        AddAccountError.IsVisible = false;
    }

    private void AddAccountBrowseButton_Click(object? sender, RoutedEventArgs e) =>
        _ = _tasks.Run(async () =>
        {
            string? path = await BrowseForFolderAsync(Strings.SelectFolderDialogTitle);
            if (path is null) return;
            AddAccountPathBox.Text = path;
            await AddAccountAsync();
        }, "browse for an account's save folder");

    private void AddAccountPathBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; _ = _tasks.Run(AddAccountAsync, "add account"); }
        else if (e.Key == Key.Escape) { e.Handled = true; CloseAddAccountRow(); }
    }

    private void AddAccountConfirmButton_Click(object? sender, RoutedEventArgs e) => _ = _tasks.Run(AddAccountAsync, "add account");

    private bool _addingAccount;

    private async Task AddAccountAsync()
    {
        if (_addingAccount || _accountData?.Add is not { } add) return;
        string path = AddAccountPathBox.Text?.Trim() ?? "";
        if (path.Length == 0) { AddAccountPathBox.Focus(); return; }
        _addingAccount = true;
        AddAccountConfirmButton.IsEnabled = false;
        try
        {
            var result = await add(path);
            if (_closed) return;
            if (result == AccountAddResult.Added)
            {
                CloseAddAccountRow();
                await LoadAccountsAsync();
                await LoadStoredAccountsAsync();
                return;
            }
            AddAccountError.Text = result == AccountAddResult.AlreadyListed ? Strings.SettingsAccountAlreadyListed : Strings.SettingsAccountNoSave;
            AddAccountError.IsVisible = true;
        }
        finally
        {
            _addingAccount = false;
            AddAccountConfirmButton.IsEnabled = true;
        }
    }

    private void ForgetListedAccountButton_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AccountRowViewModel row)
            _ = _tasks.Run(() => ForgetListedAccountAsync(row), "settings: forget an added account");
    }

    /// <summary>An account added by hand off the list: its data goes, as on the data page</summary>
    private async Task ForgetListedAccountAsync(AccountRowViewModel row)
    {
        if (_accountData is null) return;
        var stored = (await _accountData.List()).FirstOrDefault(a => string.Equals(a.Key, row.Item.Info.Key, StringComparison.OrdinalIgnoreCase));
        string size = stored is null ? "0" : ByteSizeFormatter.Format(stored.Bytes);
        bool confirmed = await ConfirmDialog.AskAsync(
            this, Strings.AccountDataForgetTitle, string.Format(Strings.AccountDataForgetMessage, row.Title, size));
        if (!confirmed) return;

        if (!await _accountData.Forget(row.Item.Info.Key))
            await InfoDialog.ShowAsync(this, Strings.AccountDataForgetTitle, Strings.AccountDataForgetFailedMessage);
        await LoadAccountsAsync();
        await LoadStoredAccountsAsync();
    }

    /// <summary>How much the open game's screenshots take; the compress button only when some are not WebP yet</summary>
    private async Task LoadScreenshotsUsageAsync()
    {
        if (_screenshots is not { } store) return;
        var (usage, notWebP) = await Task.Run(() => (store.Usage(),
            store.ListAll().Count(s => !s.Path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))));
        if (_closed) return;
        ScreenshotsUsageRow.IsVisible = true;
        ScreenshotsUsageText.Text = string.Format(Strings.ScreenshotsUsageFormat, ByteSizeFormatter.Format(usage.Bytes), usage.Count, usage.Liveries);
        ScreenshotsConvertButton.IsVisible = notWebP > 0;
        _screenshotsToConvert = notWebP;
    }

    private int _screenshotsToConvert;

    private void ScreenshotsConvert_Click(object? sender, RoutedEventArgs e) => _ = _tasks.Run(ConvertScreenshotsAsync, "settings: compress screenshots");

    private async Task ConvertScreenshotsAsync()
    {
        if (_screenshots is not { } store || _screenshotsToConvert == 0) return;
        int quality = _viewModel.ScreenshotsWebPQuality;
        if (!await ConfirmDialog.AskAsync(this, Strings.ScreenshotsLabel,
                string.Format(Strings.ScreenshotsConvertConfirmFormat, _screenshotsToConvert, quality))) return;
        ScreenshotsConvertButton.IsEnabled = false;
        try
        {
            int converted = await Task.Run(() => store.ConvertAllToWebP(quality));
            var usage = await Task.Run(store.Usage);
            await InfoDialog.ShowAsync(this, Strings.ScreenshotsLabel,
                string.Format(Strings.ScreenshotsConvertedFormat, converted, ByteSizeFormatter.Format(usage.Bytes)));
        }
        finally
        {
            ScreenshotsConvertButton.IsEnabled = true;
        }
        await LoadScreenshotsUsageAsync();
    }

    private void ScreenshotsFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_screenshots is { } store) LiveryGallery.Services.Diagnostics.ExplorerLauncher.OpenFolder(store.Root);
    }

    private void ForgetAccountButton_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is StoredAccountRowViewModel row)
            _ = _tasks.Run(() => ForgetAccountAsync(row), "settings: forget account");
    }

    private async Task ForgetAccountAsync(StoredAccountRowViewModel row)
    {
        if (_accountData is null) return;
        bool confirmed = await ConfirmDialog.AskAsync(
            this, Strings.AccountDataForgetTitle, string.Format(Strings.AccountDataForgetMessage, row.Title, row.SizeText));
        if (!confirmed) return;

        if (!await _accountData.Forget(row.Account.Key))
            await InfoDialog.ShowAsync(this, Strings.AccountDataForgetTitle, Strings.AccountDataForgetFailedMessage);
        await LoadStoredAccountsAsync();
        await LoadAccountsAsync();
    }

    private void ExportDiagnosticsButton_Click(object? sender, RoutedEventArgs e) =>
        _ = _tasks.Run(ExportDiagnosticsAsync, "export diagnostics");

    private async Task ExportDiagnosticsAsync()
    {
        if (_exportDiagnostics is null || StorageProvider is not { } provider) return;

        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.DiagnosticsSaveDialogTitle,
            SuggestedFileName = DiagnosticsExportService.SuggestedFileName(DateTime.Now),
            DefaultExtension = "zip",
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(Strings.DiagnosticsZipFileType) { Patterns = ["*.zip"] }],
        });
        if (file is null) return;

        string? localPath = file.TryGetLocalPath();
        string shownName = localPath ?? file.Name;
        ExportDiagnosticsButton.IsEnabled = false;
        try
        {
            await using (var stream = await file.OpenWriteAsync())
                await _exportDiagnostics(stream, CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to export diagnostics", ex);
            await InfoDialog.ShowAsync(this, Strings.DiagnosticsSectionLabel, Strings.DiagnosticsExportFailedMessage);
            return;
        }
        finally
        {
            ExportDiagnosticsButton.IsEnabled = true;
            file.Dispose();
        }

        string message = string.Format(Strings.DiagnosticsExportedFormat, shownName);
        if (localPath is null)
        {
            await InfoDialog.ShowAsync(this, Strings.DiagnosticsSectionLabel, message);
            return;
        }
        if (await InfoDialog.ShowWithActionAsync(this, Strings.DiagnosticsSectionLabel, message, Strings.DiagnosticsShowInFolderButton))
            ExplorerLauncher.ShowInFolder(localPath);
    }

    private Task<string?> BrowseForFolderAsync(string title) => FolderPicker.PickAsync(this, title);

    private async Task<bool> PerformSaveAsync()
    {
        var result = await _viewModel.SaveAsync();
        switch (result)
        {
            case SettingsSaveResult.ValidationFailed:
                SelectPage(SettingsPage.Folders); // both checks are about the folders: show what failed
                return false;
            case SettingsSaveResult.PersistFailed:
                await InfoDialog.ShowAsync(this, Strings.SettingsSaveFailedTitle, Strings.SettingsSaveFailedNotice);
                return false;
            case SettingsSaveResult.SavedLanguageChanged:
                // The texts the view model made (the lists of the combo boxes, the status lines), then the dialog's own
                _viewModel.RefreshLocalizedTexts();
                ApplyLocalizedTexts();
                _ = _tasks.Run(LoadAccountsAsync, "settings: accounts in the new language");
                UpdateHintDisplay();
                return true;
            default:
                return true;
        }
    }

    private Task<bool> SaveOnceAsync()
    {
        if (_saveInProgress is { IsCompleted: false } running) return running;
        return _saveInProgress = PerformSaveAsync();
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e) => _ = _tasks.Run(SaveOnceAsync, "save settings");

    private void ExitButton_Click(object? sender, RoutedEventArgs e) => _ = _closeGuard.RequestCloseAsync();

    private void RequestClose(object? sender, RoutedEventArgs e) => _ = _closeGuard.RequestCloseAsync();

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
    }
}
