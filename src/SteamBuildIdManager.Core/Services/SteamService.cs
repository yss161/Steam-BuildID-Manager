using SteamBuildIdManager.Core.Models;

namespace SteamBuildIdManager.Core.Services;

/// <summary>Steam library layout helpers shared by the UI and tests.</summary>
public sealed class SteamService
{
    public string GenerateAcfContent(AcfParameters parameters) => AcfContentBuilder.Build(parameters);

    /// <summary>Create common\&lt;installDir&gt; and an empty placeholder EXE only when it does not exist.</summary>
    public PlaceholderResult EnsureGameDirAndPlaceholder(string steamLibraryPath, string installDir, string exeFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamLibraryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(exeFileName);

        if (!SteamPathValidator.TryValidate(installDir, out var directoryError))
            throw new ArgumentException(directoryError, nameof(installDir));
        if (!SteamPathValidator.TryValidate(exeFileName, out var executableError, requireExe: true))
            throw new ArgumentException(executableError, nameof(exeFileName));

        var gameDir = Path.Combine(SteamLocator.NormalizeLibraryRoot(steamLibraryPath), "steamapps", "common", installDir);
        var exePath = Path.Combine(gameDir, exeFileName);
        var exeParentDir = Path.GetDirectoryName(exePath)!;

        var dirCreated = !Directory.Exists(gameDir);
        if (!Directory.Exists(exeParentDir))
        {
            Directory.CreateDirectory(exeParentDir);
            dirCreated = true;
        }

        var exeCreated = false;
        // Never overwrite an existing file: it may be a real game executable.
        if (!File.Exists(exePath))
        {
            File.Create(exePath).Dispose();
            exeCreated = true;
        }

        return new PlaceholderResult(gameDir, exePath, dirCreated, exeCreated);
    }

    public string GenerateLaunchCommandFromExe(string exePath, string? launchArguments = null)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return "";
        var arguments = launchArguments?.Trim();
        return string.IsNullOrWhiteSpace(arguments)
            ? $"\"{exePath}\" %command%"
            : $"\"{exePath}\" {arguments} %command%";
    }
}