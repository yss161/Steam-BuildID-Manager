using System.Collections.Concurrent;
using SteamBuildIdManager.Core.Models;

namespace SteamBuildIdManager.Core.Services;

/// <summary>Identifiers for the selectable data sources.</summary>
public static class DataSourceModes
{
    public const string SteamCmdFirst = "steamcmd-first";
    public const string ApiFirst = "api-first";
    public const string ApiOnly = "api-only";
    public const string SteamCmdOnly = "steamcmd-only";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        SteamCmdOnly => SteamCmdOnly,
        ApiFirst => ApiFirst,
        ApiOnly => ApiOnly,
        _ => SteamCmdFirst,
    };

    public static string DisplayName(string? value) => Normalize(value) switch
    {
        SteamCmdOnly => "仅 SteamCMD",
        ApiFirst => "优先在线接口",
        ApiOnly => "仅在线接口",
        _ => "优先 SteamCMD",
    };
}

/// <summary>SteamCMD-backed provider: same <see cref="SteamPublicBuild"/> shape as the HTTP one.</summary>
public sealed class SteamCmdBuildIdProvider : IBuildIdProvider
{
    private readonly SteamCmdService _service;

    public SteamCmdBuildIdProvider(SteamCmdService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<string?> GetLatestBuildIdAsync(string appId, bool forceRefresh = false,
        CancellationToken cancellationToken = default) =>
        (await GetLatestBuildAsync(appId, forceRefresh, cancellationToken).ConfigureAwait(false))?.BuildId;

    public async Task<SteamPublicBuild?> GetLatestBuildAsync(string appId, bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var info = await _service.GetAppInfoAsync(appId, cancellationToken).ConfigureAwait(false);
        if (!info.BranchBuildIds.TryGetValue("public", out var buildId) || !BuildIdComparer.TryParse(buildId, out _))
            throw new SteamAppInfoException("SteamCMD 的 public 分支没有可用的 BuildID。");

        var manifests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var depot in info.Depots)
        {
            if (depot.Manifests.TryGetValue("public", out var manifest) && BuildIdComparer.TryParse(manifest, out _))
                manifests[depot.Id] = manifest;
        }

        return new SteamPublicBuild(buildId, manifests);
    }
}

/// <summary>
/// Tries every configured source in order and returns the first usable answer. Individual failures
/// are tolerated so one broken source never blocks a scan; the winning source is recorded for the log.
/// </summary>
public sealed class CompositeBuildIdProvider : IBuildIdProvider
{
    /// <summary>How long a failed source is skipped so one broken source cannot stall a whole scan.</summary>
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(5);

    private readonly IReadOnlyList<Source> _sources;
    private readonly ConcurrentDictionary<string, string> _lastSourceByApp = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cooldownUntil = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _utcNow;

    public CompositeBuildIdProvider(IEnumerable<(string Name, IBuildIdProvider Provider)> sources,
        Func<DateTimeOffset>? utcNow = null)
    {
        _sources = sources.Select(pair => new Source(pair.Name, pair.Provider)).ToList();
        if (_sources.Count == 0) throw new ArgumentException("至少需要一个数据源。", nameof(sources));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<string> SourceNames => _sources.Select(source => source.Name).ToList();

    /// <summary>Which source answered for the given AppID, for UI and log display.</summary>
    public string? SourceFor(string appId) =>
        _lastSourceByApp.TryGetValue(appId, out var name) ? name : null;

    public async Task<string?> GetLatestBuildIdAsync(string appId, bool forceRefresh = false,
        CancellationToken cancellationToken = default) =>
        (await GetLatestBuildAsync(appId, forceRefresh, cancellationToken).ConfigureAwait(false))?.BuildId;

    public async Task<SteamPublicBuild?> GetLatestBuildAsync(string appId, bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        foreach (var source in _sources)
        {
            if (_cooldownUntil.TryGetValue(source.Name, out var until) && _utcNow() < until)
            {
                errors.Add($"{source.Name}：刚失败过，暂时跳过");
                continue;
            }

            try
            {
                var result = await source.Provider.GetLatestBuildAsync(appId, forceRefresh, cancellationToken)
                    .ConfigureAwait(false);
                if (result == null || string.IsNullOrWhiteSpace(result.BuildId))
                {
                    errors.Add($"{source.Name}：没有返回数据");
                    continue;
                }

                _cooldownUntil.TryRemove(source.Name, out _);
                _lastSourceByApp[appId] = source.Name;
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SteamAppInfoNotFoundException ex)
            {
                // The source answered fine, it simply has no data for this AppID; keep using it.
                errors.Add($"{source.Name}：{ex.Message}");
            }
            catch (Exception ex)
            {
                _cooldownUntil[source.Name] = _utcNow() + FailureCooldown;
                errors.Add($"{source.Name}：{ex.Message}");
            }
        }

        throw new SteamAppInfoException("所有数据源都失败了：" + string.Join("；", errors));
    }

    private sealed record Source(string Name, IBuildIdProvider Provider);
}