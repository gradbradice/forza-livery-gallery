using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using System.Text;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

internal sealed partial class ServerStatusViewModel : ObservableObject, IServerStatusSink
{
    private ServerIndicatorState _state = ServerIndicatorState.Hidden;
    private InstalledSyncState _sync = InstalledSyncState.None;
    private string? _serverVersion;
    private int? _apiVersion;
    private TimeSpan? _latency;
    private string? _serverMessage;
    private TimeSpan? _retryAfter;
    private int _installedCount;
    private DateTime? _syncedAt;

    public ServerIndicatorState State => _state;
    public bool IsVisible => _state != ServerIndicatorState.Hidden;
    public bool IsGreen => _state == ServerIndicatorState.Online && !HasSyncProblem;
    public bool IsYellow => _state is ServerIndicatorState.Degraded or ServerIndicatorState.RateLimited or ServerIndicatorState.UpdateRequired
        || (_state == ServerIndicatorState.Online && HasSyncProblem);
    public bool IsBlue => _state == ServerIndicatorState.Maintenance;
    public bool IsRed => _state is ServerIndicatorState.AppRejected or ServerIndicatorState.CertificateError or ServerIndicatorState.ServerError;
    public bool IsGray => !IsGreen && !IsYellow && !IsBlue && !IsRed;

    private bool HasSyncProblem => _sync is InstalledSyncState.SaveNotSupported or InstalledSyncState.SaveRejected;

    public string Text => _state switch
    {
        ServerIndicatorState.Checking => Strings.ServerStatusChecking,
        ServerIndicatorState.Online when _sync == InstalledSyncState.SaveNotSupported => Strings.ServerStatusSaveNotSupported,
        ServerIndicatorState.Online when _sync == InstalledSyncState.Syncing => Strings.ServerStatusSyncing,
        ServerIndicatorState.Online => Strings.ServerStatusOnline,
        ServerIndicatorState.Degraded => Strings.ServerStatusDegraded,
        ServerIndicatorState.Maintenance => Strings.ServerStatusMaintenance,
        ServerIndicatorState.Unreachable => Strings.ServerStatusUnreachable,
        ServerIndicatorState.NoInternet => Strings.ServerStatusNoInternet,
        ServerIndicatorState.RateLimited => Strings.ServerStatusRateLimited,
        ServerIndicatorState.UpdateRequired => Strings.ServerStatusUpdateRequired,
        ServerIndicatorState.AppRejected => Strings.ServerStatusAppRejected,
        ServerIndicatorState.CertificateError => Strings.ServerStatusCertificateError,
        ServerIndicatorState.ServerError => Strings.ServerStatusServerError,
        ServerIndicatorState.NotConfigured => Strings.ServerStatusNotConfigured,
        _ => string.Empty
    };

    public string Tooltip
    {
        get
        {
            var text = new StringBuilder();
            void Line(string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                if (text.Length > 0) text.AppendLine();
                text.Append(value);
            }

            if (_serverVersion is not null)
            {
                string details = string.Format(Strings.ServerStatusVersionFormat, _serverVersion);
                if (_apiVersion is int api) details += $" · API v{api}";
                if (_latency is { } latency) details += $" · {latency.TotalMilliseconds:0} ms";
                Line(details);
            }
            // A refusal of the app check: what its code means, else the general hint
            Line(_state == ServerIndicatorState.AppRejected ? AppRejectedReason(_serverMessage) ?? Strings.ServerStatusAppRejectedHint : _serverMessage);
            Line(_state switch
            {
                ServerIndicatorState.Unreachable or ServerIndicatorState.NoInternet or ServerIndicatorState.ServerError => Strings.ServerStatusRetryHint,
                ServerIndicatorState.RateLimited when _retryAfter is { } wait =>
                    string.Format(Strings.ServerStatusRetryInFormat, Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))),
                ServerIndicatorState.UpdateRequired => Strings.ServerStatusUpdateHint,
                ServerIndicatorState.CertificateError => Strings.ServerStatusCertificateHint,
                _ => null
            });
            Line(_sync switch
            {
                InstalledSyncState.Synced when _syncedAt is { } at => SyncedText(_installedCount, at, DateTime.Now, AppLocalisationService.Culture),
                InstalledSyncState.Syncing => Strings.ServerStatusSyncing,
                InstalledSyncState.ProfileNotFound => Strings.ServerSyncProfileNotFound,
                InstalledSyncState.SaveNotSupported => Strings.ServerSyncSaveNotSupported,
                InstalledSyncState.SaveRejected => Strings.ServerSyncSaveRejected,
                InstalledSyncState.Failed => Strings.ServerSyncFailed,
                _ => null
            });
            if (_state is not (ServerIndicatorState.Hidden or ServerIndicatorState.Checking or ServerIndicatorState.NotConfigured))
                Line(Strings.ServerStatusClickToRecheck);
            return text.ToString();
        }
    }

    /// <summary>
    /// The line of the last update: the time when it was today, the day and the time when the day has changed since
    /// (the gallery left open overnight showed "updated at 15:03" of the day before)
    /// </summary>
    internal static string SyncedText(int count, DateTime syncedAt, DateTime now, System.Globalization.CultureInfo culture)
    {
        string time = syncedAt.ToString("t", culture);
        if (syncedAt.Date == now.Date) return string.Format(culture, Strings.ServerSyncDoneFormat, count, time);
        // The day and month; with the year when that is another one too
        string day = syncedAt.ToString(syncedAt.Year == now.Year ? "M" : "d", culture);
        return string.Format(culture, Strings.ServerSyncDoneOnDayFormat, count, day, time);
    }

    /// <summary>
    /// What the server's refusal of the app check means: the gallery's name or version not recognised (the server's
    /// User-Agent pattern), no build fingerprint, a build not on the server's list (or another app name). The code is
    /// added for the server's admin; null for a code the gallery does not know
    /// </summary>
    internal static string? AppRejectedReason(string? code) => code switch
    {
        "app_header_missing" or "app_header_invalid" => string.Format(AppLocalisationService.Culture, Strings.ServerRejectedHeaderFormat, code),
        "app_fingerprint_missing" or "app_fingerprint_invalid" => string.Format(AppLocalisationService.Culture, Strings.ServerRejectedFingerprintFormat, code),
        "app_not_allowed" => string.Format(AppLocalisationService.Culture, Strings.ServerRejectedBuildFormat, code),
        _ => null,
    };

    public void SetHidden()
    {
        _state = ServerIndicatorState.Hidden;
        _sync = InstalledSyncState.None;
        _serverVersion = null;
        _apiVersion = null;
        _latency = null;
        _serverMessage = null;
        _retryAfter = null;
        _syncedAt = null;
        _installedCount = 0;
        RaiseAll();
    }

    public void SetConnection(ServerIndicatorState state, string? serverVersion = null, int? apiVersion = null,
        TimeSpan? latency = null, string? serverMessage = null, TimeSpan? retryAfter = null)
    {
        _state = state;
        if (state != ServerIndicatorState.Checking)
        {
            _serverVersion = serverVersion;
            _apiVersion = apiVersion;
            _latency = latency;
            _serverMessage = serverMessage;
            _retryAfter = retryAfter;
        }
        RaiseAll();
    }

    public void SetSync(InstalledSyncState sync, int installedCount = 0, DateTime? syncedAt = null)
    {
        _sync = sync;
        if (sync == InstalledSyncState.Synced)
        {
            _installedCount = installedCount;
            _syncedAt = syncedAt ?? DateTime.Now;
        }
        RaiseAll();
    }

    public void RefreshLocalizedText() => RaiseAll();

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsGreen));
        OnPropertyChanged(nameof(IsYellow));
        OnPropertyChanged(nameof(IsBlue));
        OnPropertyChanged(nameof(IsRed));
        OnPropertyChanged(nameof(IsGray));
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Tooltip));
    }
}
