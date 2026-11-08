using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LiveryGallery.Configuration;
using ForzaToolkit.AppHost.Updates;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;
using LiveryGallery.Services.Diagnostics;
using LiveryGallery.Localisation;
using LiveryGallery.Render;
using LiveryGallery.Views;
using Microsoft.Extensions.Logging;

namespace LiveryGallery;

internal partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var settings = AppSettingsService.Load();
        AppThemes.Apply(settings.ThemeMode ?? AppThemeMode.System);
        AppLocalisationService.AppLanguage = settings.Language;
        LiveryParseIssueFormatter.CodeNames = RenderComponents.ParseErrorName;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Signed releases installed by the gallery itself when it was installed by its Setup (on the channel the user
            // chose at each check); otherwise a link to them, its last answer kept beside the other caches
            var identity = AppSettings.Identity;
            var updates = new AppUpdates(
                new AppUpdateOptions { GitHubRepository = identity.GitHubRepository, TrustedPublicKeys = identity.UpdatePublicKeys, Http = AppHttp.Client },
                new GitHubLatestRelease(AppHttp.Client, identity.GitHubRepository, AppSettings.Version, Path.Combine(AppSettings.BaseCachePath, "update-check.meta.json")),
                () => settings.BetaUpdates ? AppUpdateChannel.Beta : AppUpdateChannel.Stable);
            // A window something still holds after closing is told in the log, with its type
            LiveryGallery.Services.Diagnostics.ClosedWindowWatch.Start();
            // Esc closes every window but the gallery itself: previews, the 3D view, settings, statistics, the dialogs
            ForzaToolkit.UI.Dialogs.EscapeClosesWindows.Enable(window => window is not MainWindow);
            var sessions = new GameSessions(settings, updates, GameProfiles.All, window =>
            {
                desktop.MainWindow = window;
                window.Show();
            });
            // The game the gallery was showing last time; the first one it supports when that one is gone
            desktop.MainWindow = sessions.Open(sessions.First);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
