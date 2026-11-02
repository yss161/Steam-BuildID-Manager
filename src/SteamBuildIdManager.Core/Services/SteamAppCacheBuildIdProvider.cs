using SteamBuildIdManager.Core.Models;

namespace SteamBuildIdManager.Core.Services;

/// <summary>
/// Reads the Steam client's own appinfo cache. This is the fastest and most authoritative source
/// when the app has already been cached, and it works with no network at all.
/// </summary>
public sealed class SteamAppCacheBuildIdProvider : IBuildIdProvider
{
    private readonly string? _cachePath;

    public SteamAppCacheBuildIdProvider(string? cachePath) => _cachePath = cachePath;

    public string? CachePath => _cachePath;

    public async Task<string?> GetLatestBuildIdAsync(string appId, bool forceRefresh = false,
        CancellationToken cancellationToken = default) =>
        (await GetLatestBuildAsync(appId, forceRefresh, cancellationToken).ConfigureAwait(false))?.BuildId;

    public async Task<SteamPublicBuild?> GetLatestBuildAsync(string appId, bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_cachePath) || !File.Exists(_cachePath))
            throw new SteamAppInfoException("本机 Steam 缓存不存在。");

        var info = await SteamAppCacheReader.ReadAsync(_cachePath, appId, cancellationToken).ConfigureAwait(false);
        return SteamPublicBuildFactory.FromAppInfo(info);
    }
}

/// <summary>SteamKit2 anonymous PICS provider: public product info straight from Steam.</summary>
public sealed class SteamLiveBuildIdProvider : IBuildIdProvider
{
    private readonly ISteamAppInfoService _service;

    public SteamLiveBuildIdProvider(ISteamAppInfoService? service = null) =>
        _service = service ?? new SteamLiveAppInfoService();

    public async Task<string?> GetLatestBuildIdAsync(string appId, bool forceRefresh = false,
        CancellationToken cancellationToken = default) =>
        (await GetLatestBuildAsync(appId, forceRefresh, cancellationToken).ConfigureAwait(false))?.BuildId;

    public async Task<SteamPublicBuild?> GetLatestBuildAsync(string appId, bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var info = await _service.GetAppInfoAsync(appId, cancellationToken).ConfigureAwait(false);
        return SteamPublicBuildFactory.FromAppInfo(info);
    }
}

/// <summary>Shared projection from a full appinfo snapshot to the public-branch build summary.</summary>
public static class SteamPublicBuildFactory
{
    public static SteamPublicBuild FromAppInfo(SteamAppInfo info)
    {
        if (!info.BranchBuildIds.TryGetValue("public", out var buildId) || !BuildIdComparer.TryParse(buildId, out _))
            throw new SteamAppInfoException($"{Describe(info)}没有可用的 public 分支 BuildID。");

        var manifests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var depot in info.Depots)
        {
            if (depot.Manifests.TryGetValue("public", out var manifest) && BuildIdComparer.TryParse(manifest, out _))
                manifests[depot.Id] = manifest;
        }

        return new SteamPublicBuild(buildId, manifests)
        {
            Source = string.IsNullOrWhiteSpace(info.Source) ? "" : info.Source,
            RetrievedAt = info.RetrievedAt,
        };
    }

    private static string Describe(SteamAppInfo info) =>
        string.IsNullOrWhiteSpace(info.Source) ? "该来源" : info.Source + " ";
}