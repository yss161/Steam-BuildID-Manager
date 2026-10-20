namespace SteamBuildIdManager.Core.Models;

public sealed record ManifestBackupOptions(int MaximumCount, TimeSpan MaximumAge)
{
    public static ManifestBackupOptions Default { get; } = new(5, TimeSpan.FromDays(30));
}

public sealed record ManifestWriteResult(
    bool Changed,
    string? BackupPath,
    int RemovedBackupCount,
    string OldBuildId,
    string NewBuildId)
{
    public bool CreatedNewFile => !Changed && BackupPath == null && string.IsNullOrEmpty(OldBuildId);
}

public sealed record PlaceholderResult(
    string GameDirPath,
    string ExePath,
    bool DirCreated,
    bool ExeCreated);

public sealed record BuildIdUpdateResult(
    AcfManifest UpdatedManifest,
    ManifestWriteResult Write);

public sealed record BatchUpdateResult(
    IReadOnlyList<BuildIdUpdateResult> Updated,
    IReadOnlyList<string> Failures)
{
    public int UpdatedCount => Updated.Count;
    public bool HasFailures => Failures.Count > 0;
}

/// <summary>Everything required to create or rebuild one placeholder appmanifest.</summary>
public sealed record GameConfigRequest(
    string AppId,
    string DisplayName,
    string InstallDirectory,
    string PlaceholderExeName,
    string BuildId,
    string SteamId,
    IReadOnlyList<SteamDepotConfig> Depots,
    string LibraryPath,
    string SteamInstallPath,
    string RealGameExePath = "",
    string LaunchArguments = "",
    string Language = "schinese");

public sealed record GameConfigWriteResult(
    string AcfPath,
    string GameDirectory,
    string PlaceholderExePath,
    ManifestWriteResult Write,
    string LaunchCommand);