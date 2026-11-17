using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ForzaToolkit.AppHost;
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
        var timeline = StartupTimeline.Current;
        timeline.Mark("Avalonia");
        var settings = AppSettingsService.Load();
        AppThemes.Apply(settings.ThemeMode ?? AppThemeMode.System);
        AppLocalisationService.AppLanguage = settings.Language;
        LiveryParseIssueFormatter.CodeNames = RenderComponents.ParseErrorName;
        timeline.Mark("settings");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // A window something still holds after closing is told in the log, with its type
            LiveryGallery.Services.Diagnostics.ClosedWindowWatch.Start();
            // Esc closes every window but the gallery itself (and the one shown while it opens): previews, the 3D view,
            // settings, statistics, the dialogs
            ForzaToolkit.UI.Dialogs.EscapeClosesWindows.Enable(window => window is not MainWindow and not StartupWindow);
            // No window bigger than its screen: on 1920×1080 at 125 % the gallery opened with its title bar above it
            ForzaToolkit.UI.WindowsFitScreen.Enable();
            // A second start shows the window there is (the gallery's, or this one while it opens) and ends; a gallery
            // that is closing makes it wait and open in its place
            Program.Instance?.OnSecondStart(() =>
            {
                if (desktop.MainWindow is MainWindow { IsClosing: true }) return false;
                Dispatcher.UIThread.Post(() =>
                {
                    if (desktop.MainWindow is not { } window) return;
                    if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                    window.Activate();
                });
                return true;
            });
            // A window at once: the game's services and the gallery's window take a while to make, and a double-click
            // that showed nothing for that long was clicked again (a user's report, 1.7.1). They are made once it is
            // drawn, and it closes when the gallery's window opens
            var startup = new StartupWindow(AppSettings.Identity.Name);
            desktop.MainWindow = startup;
            startup.Opened += (_, _) =>
            {
                timeline.Mark("startup window");
                DispatcherTimer.RunOnce(() => OpenGallery(desktop, settings, startup), TimeSpan.FromMilliseconds(50));
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    // The game shown last time with its services and window; the startup window closes once it is up
    private static void OpenGallery(IClassicDesktopStyleApplicationLifetime desktop, AppSettingsData settings, StartupWindow startup)
    {
        var timeline = StartupTimeline.Current;
        // Signed releases installed by the gallery itself when it was installed by its Setup (on the channel the user
        // chose at each check); otherwise a link to them, its last answer kept beside the other caches
        var identity = AppSettings.Identity;
        var updates = new AppUpdates(
            new AppUpdateOptions { GitHubRepository = identity.GitHubRepository, TrustedPublicKeys = identity.UpdatePublicKeys, Http = AppHttp.Client },
            new GitHubLatestRelease(AppHttp.Client, identity.GitHubRepository, AppSettings.Version, Path.Combine(AppSettings.BaseCachePath, "update-check.meta.json")),
            () => settings.BetaUpdates ? AppUpdateChannel.Beta : AppUpdateChannel.Stable);
        var sessions = new GameSessions(settings, updates, GameProfiles.All, window =>
        {
            desktop.MainWindow = window;
            window.Show();
        });
        // The game the gallery was showing last time; the first one it supports when that one is gone
        var gallery = sessions.Open(sessions.First);
        timeline.Mark("game");
        gallery.Opened += Opened;
        desktop.MainWindow = gallery;
        gallery.Show();
        startup.Close();

        void Opened(object? sender, EventArgs e)
        {
            gallery.Opened -= Opened;
            timeline.Mark("gallery window");
            if (timeline.SlowStartReport() is { } report) AppLog.LogWarning(report);
        }
    }
}
