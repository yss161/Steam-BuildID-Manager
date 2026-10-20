using SteamBuildIdManager.Core.Models;

namespace SteamBuildIdManager.Core.Services;

/// <summary>Validation and write pipeline for creating or rebuilding one placeholder appmanifest.</summary>
public sealed class GameConfigService
{
    private readonly ManifestFileService _files;
    private readonly SteamService _steam;

    public GameConfigService(ManifestFileService? fileService = null, SteamService? steamService = null)
    {
        _files = fileService ?? new ManifestFileService();
        _steam = steamService ?? new SteamService();
    }

    public async Task<GameConfigWriteResult> WriteAsync(GameConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestError = Validate(request, out var normalizedAppId, out var libraryRoot, out var installPath);
        if (requestError != null) throw new ArgumentException(requestError, nameof(request));

        var acfPath = Path.Combine(libraryRoot!, "steamapps", $"appmanifest_{normalizedAppId}.acf");
        var previousBuildId = "";
        if (File.Exists(acfPath))
        {
            try
            {
                previousBuildId = AcfDocument.Parse(acfPath, libraryRoot!, await File.ReadAllTextAsync(acfPath, cancellationToken)
                    .ConfigureAwait(false)).BuildId;
            }
            catch
            {
                previousBuildId = "";
            }
        }

        var content = AcfContentBuilder.Build(new AcfParameters(
            AppId: normalizedAppId,
            Depots: request.Depots,
            LauncherPath: Path.Combine(installPath!, "steam.exe"),
            DisplayName: request.DisplayName.Trim(),
            InstallDir: request.InstallDirectory.Trim(),
            BuildId: request.BuildId.Trim(),
            LastOwner: request.SteamId.Trim(),
            Language: request.Language));

        // Create the placeholder first; a failure here leaves the existing ACF untouched.
        var placeholder = _steam.EnsureGameDirAndPlaceholder(
            libraryRoot!, request.InstallDirectory.Trim(), request.PlaceholderExeName.Trim());

        var write = await _files.WriteWithBackupAsync(
            acfPath, content, normalizedAppId, previousBuildId, request.BuildId.Trim(),
            cancellationToken).ConfigureAwait(false);

        return new GameConfigWriteResult(
            acfPath,
            placeholder.GameDirPath,
            placeholder.ExePath,
            write,
            _steam.GenerateLaunchCommandFromExe(request.RealGameExePath, request.LaunchArguments));
    }

    public static string? Validate(GameConfigRequest request, out string normalizedAppId,
        out string? libraryRoot, out string? steamInstallPath)
    {
        normalizedAppId = "";
        libraryRoot = null;
        steamInstallPath = null;

        if (!SteamAppInfoService.TryNormalizeAppId(request.AppId, out normalizedAppId))
            return "AppID 必须是 1 到 4294967295 之间的整数。";
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Any(c => c < 32 || c is '"' or '\\'))
            return "显示名称不能为空，且不能包含引号、反斜杠或控制字符。";
        if (!SteamPathValidator.TryValidate(request.InstallDirectory, out var installDirError))
            return "安装目录名无效：" + installDirError;
        if (!SteamPathValidator.TryValidate(request.PlaceholderExeName, out var exeError, requireExe: true))
            return "Steam 占位文件名无效：" + exeError;
        if (!BuildIdComparer.TryParse(request.BuildId, out _))
            return "BuildID 必须是正整数。";
        if (string.IsNullOrWhiteSpace(request.SteamId) || !request.SteamId.Trim().All(char.IsAsciiDigit))
            return "请先在设置中填写数字形式的 SteamID64。";
        if (request.Depots == null || request.Depots.Count == 0)
            return "至少需要一个 Depot。";
        if (request.Depots.Any(depot =>
                !ulong.TryParse(depot.DepotId, out var depotId) || depotId == 0
                || !ulong.TryParse(depot.ManifestId, out var manifestId) || manifestId == 0))
            return "每个 Depot 都需要有效的 DepotID 和 ManifestID。";
        if (request.Depots.Select(depot => depot.DepotId).Distinct(StringComparer.Ordinal).Count() != request.Depots.Count)
            return "DepotID 不能重复。";

        if (string.IsNullOrWhiteSpace(request.LibraryPath) || !SteamLocator.IsLibraryRoot(request.LibraryPath))
            return "Steam 库文件夹无效，请先在设置中配置包含 steamapps 的库根目录。";
        libraryRoot = SteamLocator.NormalizeLibraryRoot(request.LibraryPath);

        if (string.IsNullOrWhiteSpace(request.SteamInstallPath))
            return "请先在设置中填写 Steam 安装路径。";
        steamInstallPath = request.SteamInstallPath.Trim();
        if (!File.Exists(Path.Combine(steamInstallPath, "steam.exe")))
            return "Steam 安装路径下找不到 steam.exe，请检查设置。";

        return null;
    }
}