using ForzaToolkit.Crypto.Client;
using LiveryGallery.Services;

namespace LiveryGallery.Render;

/// <summary>
/// The livery server through the closed ForzaToolkit.Crypto.Client, as the open <see cref="ILiveryServerClient"/>:
/// the client's status and errors become the toolkit's types. The profile save it sends is found by the open
/// <see cref="ProfileDataLocator"/>.
/// </summary>
/// <param name="gameId">The game whose saves are sent (ForzaToolkit.Games id): the server decrypts them with its keys</param>
internal sealed class LiveryServerService(string gameId) : ILiveryServerClient
{
    // Short requests (status, challenge). The save is sent with the client's own upload timeout (5 minutes,
    // ForzaLiveryClientOptions.UploadTimeout): with a ping of seconds a megabyte takes more than a minute
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);

    private readonly Lock _lock = new();
    private ForzaLiveryClient? _client;
    private bool _notConfigured;

    public bool IsAvailable
    {
        get
        {
            try { return ForzaLiveryServer.IsConfigured; }
            catch (Exception ex)
            {
                AppLog.LogErrorThrottled("LiveryServer.IsConfigured", "Failed to read the built-in livery server address", ex);
                return false;
            }
        }
    }

    public static string UserAgent => Configuration.AppSettings.UserAgent;

    private ForzaLiveryClient? GetClient()
    {
        lock (_lock)
        {
            if (_client is not null || _notConfigured) return _client;
            try
            {
                var options = new ForzaLiveryClientOptions
                {
                    ProofOfWorkThreads = Math.Max(1, Environment.ProcessorCount / 2),
                    GameId = gameId,
                };
                _client = ForzaLiveryClient.CreateForBuiltInServer(UserAgent, options, RequestTimeout);
            }
            catch (InvalidOperationException ex)
            {
                _notConfigured = true;
                AppLog.LogError("The livery server client has no server address built in", ex);
            }
            return _client;
        }
    }

    public long MaxSaveBytes => ForzaLiveryClient.MaxFileBytes;

    public async Task<ServerCheckResult?> CheckConnectionAsync(CancellationToken ct)
    {
        if (GetClient() is not { } client) return null;
        var status = await client.CheckConnectionAsync(gameId, ct).ConfigureAwait(false);
        return ToCheckResult(status);
    }

    public async Task<IReadOnlyDictionary<string, int>> GetInstalledLiveriesAsync(ReadOnlyMemory<byte> profileData, CancellationToken ct) =>
        (await ReadSaveAsync(profileData, ct).ConfigureAwait(false)).Installed;

    /// <summary>The liveries on the player's cars and the player's name as the game wrote it in the save (server 1.4.0; before, null)</summary>
    public async Task<ServerSaveRead> ReadSaveAsync(ReadOnlyMemory<byte> profileData, CancellationToken ct)
    {
        var client = GetClient() ?? throw new InvalidOperationException("The livery server is not configured in this build");
        try
        {
            var read = await client.ReadSaveAsync(profileData, cancellationToken: ct).ConfigureAwait(false);
            return new ServerSaveRead(CountByContainer(read.Liveries), read.PlayerName);
        }
        catch (LiveryServiceException ex)
        {
            throw ToToolkitException(ex);
        }
    }

    // A refusal of the app check passes its code (app_header_invalid, app_not_allowed…) as the message: the status
    // indicator says what it means in the interface language (ServerStatusViewModel.AppRejectedReason)
    internal static ServerCheckResult ToCheckResult(ServerConnectionStatus status) => new(
        ToState(status.State), status.Server?.Version, status.Server?.ApiVersion, status.Latency,
        status.State == ConnectionState.AppRejected && status.Error?.ServerCode is { } code ? code : status.ServerMessage,
        status.RetryAfter, status.RecheckAfter);

    internal static LiveryServerException ToToolkitException(LiveryServiceException ex) =>
        new(ToFailure(ex.Error), ex.Message, ex.StatusCode, ex.RetryAfter, ex);

    internal static ServerConnectionState ToState(ConnectionState state) => state switch
    {
        ConnectionState.Online => ServerConnectionState.Online,
        ConnectionState.Degraded => ServerConnectionState.Degraded,
        ConnectionState.Maintenance => ServerConnectionState.Maintenance,
        ConnectionState.Unreachable => ServerConnectionState.Unreachable,
        ConnectionState.RateLimited => ServerConnectionState.RateLimited,
        ConnectionState.UpdateRequired => ServerConnectionState.UpdateRequired,
        ConnectionState.AppRejected => ServerConnectionState.AppRejected,
        ConnectionState.CertificateError => ServerConnectionState.CertificateError,
        _ => ServerConnectionState.ServerError,
    };

    internal static ServerFailure ToFailure(LiveryServiceError error) => error switch
    {
        LiveryServiceError.InvalidSave => ServerFailure.InvalidSave,
        LiveryServiceError.FileTooLarge => ServerFailure.FileTooLarge,
        LiveryServiceError.SaveNotSupported => ServerFailure.SaveNotSupported,
        LiveryServiceError.RateLimited => ServerFailure.RateLimited,
        LiveryServiceError.AppRejected => ServerFailure.AppRejected,
        LiveryServiceError.AppOutdated => ServerFailure.AppOutdated,
        LiveryServiceError.ProofOfWorkFailed => ServerFailure.ProofOfWorkFailed,
        LiveryServiceError.ServerCertificateRejected => ServerFailure.ServerCertificateRejected,
        LiveryServiceError.ConnectionFailed => ServerFailure.ConnectionFailed,
        LiveryServiceError.Timeout => ServerFailure.Timeout,
        LiveryServiceError.InvalidResponse => ServerFailure.InvalidResponse,
        LiveryServiceError.Maintenance => ServerFailure.Maintenance,
        _ => ServerFailure.ServerError,
    };

    internal static Dictionary<string, int> CountByContainer(IEnumerable<InstalledLivery> liveries)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var livery in liveries)
        {
            if (!livery.HasLivery) continue;
            string name = Path.GetFileName(livery.LiveryFileName.Replace('\\', '/').TrimEnd('/'));
            if (name.Length == 0) continue;
            counts[name] = counts.TryGetValue(name, out int n) ? n + 1 : 1;
        }
        return counts;
    }
}
