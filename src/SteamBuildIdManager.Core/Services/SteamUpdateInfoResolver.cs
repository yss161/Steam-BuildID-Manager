using SteamBuildIdManager.Core.Models;

namespace SteamBuildIdManager.Core.Services;

public sealed record SteamResolvedUpdate(SteamAppInfo App, string Source, IReadOnlyList<string> Diagnostics);

/// <summary>
/// Collects candidate snapshots (local Steam cache, direct Steam query, third-party mirror), drops
/// incomplete or conflicting ones, then picks the highest BuildID so a stale source can never
/// silently roll a configuration backwards. Fields are never mixed across sources.
/// </summary>
public sealed class SteamUpdateInfoResolver
{
    private readonly IReadOnlyList<ISteamAppInfoService> _remoteSources;

    public SteamUpdateInfoResolver(IEnumerable<ISteamAppInfoService> remoteSources)
    {
        _remoteSources = remoteSources.ToList();
    }

    public async Task<SteamResolvedUpdate> ResolveAsync(string appId, string? cachePath, string? manifestPath,
        SteamAppSelectionOptions options, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<string>();
        var candidates = new List<(SteamAppInfo App, string Source)>();

        if (!string.IsNullOrWhiteSpace(cachePath))
        {
            try
            {
                candidates.Add((await SteamAppCacheReader.ReadAsync(cachePath, appId, cancellationToken)
                    .ConfigureAwait(false), "本机 Steam 缓存"));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                                           or ArgumentException or SteamAppInfoException)
            {
                diagnostics.Add("本机 Steam 缓存不可用：" + ex.Message);
            }
        }
        else
        {
            diagnostics.Add("本机 Steam 缓存不存在（Steam 尚未缓存该 App）。");
        }

        foreach (var remote in _remoteSources)
        {
            try
            {
                var app = await remote.GetAppInfoAsync(appId, cancellationToken).ConfigureAwait(false);
                candidates.Add((app, string.IsNullOrWhiteSpace(app.Source) ? "在线接口" : app.Source));
            }
            catch (SteamAppInfoException ex)
            {
                diagnostics.Add("在线接口不可用：" + ex.Message);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var acf = manifestPath != null && File.Exists(manifestPath)
            ? await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false)
            : null;

        return Choose(candidates, acf, appId, options, diagnostics);
    }

    public static SteamResolvedUpdate Choose(IEnumerable<(SteamAppInfo App, string Source)> candidates,
        string? acf, string appId, SteamAppSelectionOptions options, List<string>? diagnostics = null)
    {
        diagnostics ??= new List<string>();
        var valid = new List<(SteamAppInfo App, string Source, ulong Build)>();

        foreach (var candidate in candidates)
        {
            try
            {
                var selection = SteamAppSelectionService.Select(candidate.App, options);
                if (candidate.App.AppId != appId
                    || !ulong.TryParse(selection.BuildId, out var build) || build == 0
                    || string.IsNullOrWhiteSpace(selection.DisplayName)
                    || string.IsNullOrWhiteSpace(selection.InstallDirectory)
                    || selection.Executables.Count == 0
                    || selection.Depots.Count == 0
                    || selection.Depots.Any(depot => string.IsNullOrEmpty(depot.Manifest)))
                    throw new SteamAppInfoException("缺少完整的版本、Depot 或启动信息");

                // A candidate that contradicts what Steam recorded is excluded, not repaired.
                if (acf != null) SteamManifestUpdateGuard.Validate(acf, appId, selection.BuildId);

                valid.Add((candidate.App, candidate.Source, build));
            }
            catch (SteamAppInfoException ex)
            {
                diagnostics.Add(candidate.Source + "：" + ex.Message);
            }
        }

        // Prefer the highest build to avoid replacing newer metadata with an older snapshot.
        // Equal builds keep caller order, which puts the freshest source first.
        var selected = valid.OrderByDescending(item => item.Build).FirstOrDefault();
        if (selected.App == null)
        {
            throw new SteamAppInfoException(
                "没有找到与 Steam 状态一致的完整版本信息，原配置未修改。\n" + string.Join("\n", diagnostics));
        }

        return new SteamResolvedUpdate(selected.App, selected.Source, diagnostics);
    }
}