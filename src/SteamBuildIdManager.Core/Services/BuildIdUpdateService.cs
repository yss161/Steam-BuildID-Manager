using System.Collections.Concurrent;
using System.Diagnostics;
using SteamBuildIdManager.Core.Models;

namespace SteamBuildIdManager.Core.Services;

public interface IBuildIdProvider
{
    Task<string?> GetLatestBuildIdAsync(string appId, bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Public branch buildid plus the manifest GID of every depot. Steam validates both, so the
    /// default implementation only knows the BuildID and reports no depot manifests.
    /// </summary>
    async Task<SteamPublicBuild?> GetLatestBuildAsync(string appId, bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var buildId = await GetLatestBuildIdAsync(appId, forceRefresh, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(buildId) ? null : new SteamPublicBuild(buildId, EmptyManifests);
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyManifests =
        new Dictionary<string, string>();
}

/// <summary>Public branch state used to make a local ACF match Steam again.</summary>
public sealed record SteamPublicBuild(string BuildId, IReadOnlyDictionary<string, string> DepotManifests);

/// <summary>Reads the public branch BuildID from the SteamCMD info API and caches short-lived results.</summary>
public sealed class SteamBuildIdProvider : IBuildIdProvider
{
    private readonly ISteamAppInfoService _appInfo;
    private readonly TimeSpan _cacheLifetime;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public SteamBuildIdProvider(ISteamAppInfoService? appInfo = null, TimeSpan? cacheLifetime = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _appInfo = appInfo ?? new SteamAppInfoService();
        _cacheLifetime = cacheLifetime ?? TimeSpan.FromMinutes(10);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<string?> GetLatestBuildIdAsync(string appId, bool forceRefresh = false,
        CancellationToken cancellationToken = default) =>
        (await GetLatestBuildAsync(appId, forceRefresh, cancellationToken).ConfigureAwait(false))?.BuildId;

    public async Task<SteamPublicBuild?> GetLatestBuildAsync(string appId, bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!SteamAppInfoService.TryNormalizeAppId(appId, out var normalized))
            throw new SteamAppInfoException("AppID 无效。");

        if (!forceRefresh && _cache.TryGetValue(normalized, out var cached)
            && _utcNow() - cached.FetchedAt < _cacheLifetime)
            return cached.Build;

        var info = await _appInfo.GetAppInfoAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (!info.BranchBuildIds.TryGetValue("public", out var buildId)
            || !BuildIdComparer.TryParse(buildId, out _))
            throw new SteamAppInfoException("public 分支没有可用的 BuildID。");

        var manifests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var depot in info.Depots)
        {
            if (depot.Manifests.TryGetValue("public", out var manifest) && BuildIdComparer.TryParse(manifest, out _))
                manifests[depot.Id] = manifest;
        }

        var build = new SteamPublicBuild(buildId, manifests);
        _cache[normalized] = new CacheEntry(build, _utcNow());
        return build;
    }

    private sealed record CacheEntry(SteamPublicBuild Build, DateTimeOffset FetchedAt);
}

/// <summary>Scans libraries, compares BuildIDs and performs backed-up one-click updates.</summary>
public sealed class BuildIdUpdateService
{
    public const int DefaultConcurrency = 4;

    private readonly IBuildIdProvider _provider;
    private readonly ManifestFileService _files;

    public BuildIdUpdateService(IBuildIdProvider provider, ManifestFileService? fileService = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _files = fileService ?? new ManifestFileService();
    }

    public ManifestFileService FileService => _files;

    public AcfScanResult Scan(IEnumerable<string> libraryRoots)
    {
        ArgumentNullException.ThrowIfNull(libraryRoots);
        var manifests = new List<AcfManifest>();
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawRoot in libraryRoots.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string root;
            string steamApps;
            try
            {
                root = SteamLocator.NormalizeLibraryRoot(rawRoot);
                steamApps = Path.Combine(root, "steamapps");
            }
            catch (Exception ex)
            {
                errors.Add($"{rawRoot}：路径无效（{ex.Message}）");
                continue;
            }

            if (!Directory.Exists(steamApps)) continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                errors.Add($"{steamApps}：无法读取目录（{ex.Message}）");
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    var content = File.ReadAllText(file);
                    var manifest = AcfDocument.Parse(file, root, content);
                    if (!seen.Add(manifest.AppId)) continue;
                    manifests.Add(manifest);
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(file)}：{ex.Message}");
                }
            }
        }

        return new AcfScanResult(
            manifests.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(m => m.AppId, StringComparer.Ordinal).ToList(),
            errors);
    }

    public async Task<IReadOnlyList<BuildIdCheckResult>> CheckAsync(
        IReadOnlyList<AcfManifest> manifests,
        bool forceRefresh = false,
        IProgress<BuildIdCheckResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        using var gate = new SemaphoreSlim(DefaultConcurrency);
        var results = new BuildIdCheckResult[manifests.Count];

        var tasks = manifests.Select(async (manifest, index) =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = await CheckOneAsync(manifest, forceRefresh, cancellationToken).ConfigureAwait(false);
                results[index] = result;
                progress?.Report(result);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private async Task<BuildIdCheckResult> CheckOneAsync(AcfManifest manifest, bool forceRefresh,
        CancellationToken cancellationToken)
    {
        try
        {
            var latest = await _provider.GetLatestBuildAsync(manifest.AppId, forceRefresh, cancellationToken)
                .ConfigureAwait(false);
            if (latest == null || string.IsNullOrWhiteSpace(latest.BuildId)
                || !BuildIdComparer.TryParse(latest.BuildId, out _))
                return new(manifest, null, BuildIdState.Error, "接口未返回有效 BuildID");

            var comparison = BuildIdComparer.Compare(manifest.BuildId, latest.BuildId);
            if (comparison is null)
                return new(manifest, latest.BuildId, BuildIdState.Error, "当前 ACF 的 BuildID 格式异常")
                {
                    LatestDepotManifests = latest.DepotManifests,
                };

            // Steam shows "需要更新" when any of these disagree: buildid, depot manifests,
            // or the StateFlags "update required" bit. Fixing only buildid is not enough.
            var drift = FindDepotDrift(manifest, latest.DepotManifests);
            var reasons = new List<string>();
            if (comparison < 0)
                reasons.Add(string.IsNullOrWhiteSpace(manifest.BuildId)
                    ? "ACF 缺少 BuildID"
                    : $"BuildID {manifest.BuildId} → {latest.BuildId}");
            else if (comparison > 0 && drift.Count == 0 && !manifest.HasUpdateRequiredFlag)
                return new(manifest, latest.BuildId, BuildIdState.UpToDate, "当前值高于公开分支")
                {
                    LatestDepotManifests = latest.DepotManifests,
                };

            if (manifest.HasUpdateRequiredFlag) reasons.Add("StateFlags 仍标记为需要更新");
            if (drift.Count > 0) reasons.Add($"{drift.Count} 个 Depot Manifest 落后");

            if (reasons.Count == 0)
                return new(manifest, latest.BuildId, BuildIdState.UpToDate, "已是最新")
                {
                    LatestDepotManifests = latest.DepotManifests,
                };

            return new(manifest, latest.BuildId, BuildIdState.UpdateAvailable,
                string.Join("；", reasons) + "，可一键修复")
            {
                LatestDepotManifests = latest.DepotManifests,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new(manifest, null, BuildIdState.Error, Shorten(ex.Message));
        }
    }

    /// <summary>Depots whose local manifest GID differs from the public branch.</summary>
    private static IReadOnlyList<string> FindDepotDrift(AcfManifest manifest,
        IReadOnlyDictionary<string, string> publicManifests)
    {
        if (publicManifests.Count == 0) return Array.Empty<string>();

        var drift = new List<string>();
        foreach (var (depotId, publicManifest) in publicManifests)
        {
            if (!manifest.Manifests.TryGetValue(depotId, out var local)) continue;
            if (!string.Equals(local, publicManifest, StringComparison.Ordinal)) drift.Add(depotId);
        }
        return drift;
    }

    public async Task<BuildIdUpdateResult> UpdateAsync(AcfManifest manifest, string latestBuildId,
        IReadOnlyDictionary<string, string>? latestDepotManifests = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!BuildIdComparer.TryParse(latestBuildId, out _))
            throw new ArgumentException("最新 BuildID 无效。", nameof(latestBuildId));

        var content = await File.ReadAllTextAsync(manifest.FilePath, cancellationToken).ConfigureAwait(false);
        var current = AcfDocument.Parse(manifest.FilePath, manifest.LibraryPath, content);

        // Refuse to overwrite changes made by Steam or another tool after the scan.
        if (!string.Equals(current.BuildId, manifest.BuildId, StringComparison.Ordinal))
            throw new InvalidOperationException("ACF 已被其他程序修改，请刷新列表后重试。");
        if (!string.Equals(current.AppId, manifest.AppId, StringComparison.Ordinal))
            throw new InvalidOperationException("ACF 的 AppID 与扫描结果不一致，请刷新列表。");

        var updatedContent = AcfDocument.WithBuildIdAndDepots(content, latestBuildId, latestDepotManifests);
        var write = await _files.WriteWithBackupAsync(
            manifest.FilePath, updatedContent, manifest.AppId, manifest.BuildId, latestBuildId,
            cancellationToken).ConfigureAwait(false);

        var updated = AcfDocument.Parse(manifest.FilePath, manifest.LibraryPath, updatedContent);
        return new BuildIdUpdateResult(updated, write);
    }

    public async Task<BatchUpdateResult> UpdateAllAsync(
        IReadOnlyList<BuildIdCheckResult> checks,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checks);
        var updated = new List<BuildIdUpdateResult>();
        var failures = new List<string>();

        foreach (var check in checks.Where(check => check.CanUpdate))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await UpdateAsync(check.Manifest, check.LatestBuildId!, check.LatestDepotManifests,
                        cancellationToken)
                    .ConfigureAwait(false);
                updated.Add(result);
                progress?.Report($"已更新 {check.Manifest.Name} → BuildID {check.LatestBuildId}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var failure = $"{check.Manifest.Name}（{check.Manifest.AppId}）：{Shorten(ex.Message)}";
                failures.Add(failure);
                progress?.Report("失败 " + failure);
            }
        }

        return new BatchUpdateResult(updated, failures);
    }

    private static string Shorten(string message) => message.Length <= 160 ? message : message[..160] + "…";
}

/// <summary>Detects a running Steam client; edits made while Steam is running can be overwritten.</summary>
public static class SteamProcess
{
    public static bool IsRunning()
    {
        try
        {
            return Process.GetProcessesByName("steam").Length > 0;
        }
        catch
        {
            return false;
        }
    }
}