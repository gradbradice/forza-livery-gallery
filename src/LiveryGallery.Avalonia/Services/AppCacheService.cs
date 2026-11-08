using Microsoft.Extensions.Logging;
using LiveryGallery.Configuration;

namespace LiveryGallery.Services;

/// <summary>
/// The scan cache of ForzaToolkit.Liveries with the folders of one game (<see cref="GamePaths"/>): one cache per
/// account in <c>accounts\&lt;key&gt;\cache.json</c>, the game's preview folder, and the cache from before account
/// support (1.6.0 kept it in its root, which the first start of 1.7.0 copies into the game's folder).
/// </summary>
internal sealed class AppCacheService(GamePaths paths)
    : LiveryCacheStore(
        paths.Accounts,
        paths.Thumbs,
        paths.Root,
        logger: AppLog.LoggerFactory.CreateLogger<AppCacheService>())
{
    public GamePaths Paths { get; } = paths;
}
