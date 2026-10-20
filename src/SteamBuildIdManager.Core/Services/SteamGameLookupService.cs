using SteamBuildIdManager.Core.Models;

namespace SteamBuildIdManager.Core.Services;

/// <summary>
/// Combines the SteamCMD metadata request with the platform/language selection policy so the UI
/// only has to name an AppID and read the resulting summary.
/// </summary>
public sealed class SteamGameLookupService
{
    private readonly ISteamAppInfoService _appInfo;

    public SteamGameLookupService(ISteamAppInfoService? appInfo = null)
    {
        _appInfo = appInfo ?? new SteamAppInfoService();
    }

    public async Task<SteamGameSummary> LookupAsync(string appId, SteamAppSelectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var selection = await SelectAsync(appId, options, cancellationToken).ConfigureAwait(false);
        return Summarize(appId, selection);
    }

    public async Task<SteamAppSelection> SelectAsync(string appId, SteamAppSelectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!SteamAppInfoService.TryNormalizeAppId(appId, out var normalizedId))
            throw new SteamAppInfoException("AppID 必须是 1 到 4294967295 之间的整数。");

        var app = await _appInfo.GetAppInfoAsync(normalizedId, cancellationToken).ConfigureAwait(false);
        return SteamAppSelectionService.Select(app, options ?? DefaultOptions());
    }

    public static SteamAppSelectionOptions DefaultOptions() => new(
        OperatingSystem: "windows",
        Architecture: Environment.Is64BitOperatingSystem ? "64" : "32",
        Language: "schinese",
        Branch: "public");

    /// <summary>The first compatible depot with a public Manifest becomes the primary depot.</summary>
    public static SteamGameSummary Summarize(string appId, SteamAppSelection selection)
    {
        var depots = SteamAppSelectionService.ChooseDepotsForAutomaticUpdate(selection);
        var candidates = depots.Candidates;
        var primary = candidates.Count > 0 ? candidates[0] : null;

        var warnings = selection.Warnings.ToList();
        if (string.IsNullOrWhiteSpace(selection.DisplayName)) warnings.Add("没有获取到显示名称。");
        if (string.IsNullOrWhiteSpace(selection.InstallDirectory)) warnings.Add("没有获取到安装目录名。");
        if (string.IsNullOrWhiteSpace(selection.BuildId)) warnings.Add("public 分支没有可用 BuildID。");
        if (!depots.IsSuccess) warnings.Add(depots.Error);

        return new SteamGameSummary(
            AppId: appId,
            DisplayName: selection.DisplayName,
            InstallDirectory: selection.InstallDirectory,
            BuildId: selection.BuildId,
            PrimaryDepotId: primary?.Depot.Id ?? "",
            PrimaryManifest: primary?.Manifest ?? "",
            Depots: candidates,
            Executables: selection.Executables,
            Warnings: warnings);
    }
}
