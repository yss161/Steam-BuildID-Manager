using System.Globalization;

namespace SteamBuildIdManager.Core.Models;

/// <summary>Read-only view of one <c>appmanifest_&lt;AppID&gt;.acf</c> file.</summary>
public sealed record AcfManifest(
    string FilePath,
    string LibraryPath,
    string AppId,
    string Name,
    string InstallDirectory,
    string BuildId,
    string TargetBuildId,
    string StateFlags,
    string LauncherPath,
    ulong SizeOnDisk,
    IReadOnlyList<string> DepotIds,
    DateTimeOffset LastWriteTime)
{
    /// <summary>
    /// A configuration created by this tool keeps SizeOnDisk at 0 instead of the real
    /// download size. Real Steam installs normally report a non-zero value.
    /// </summary>
    public bool LooksLikePlaceholder => SizeOnDisk == 0;

    public string BuildIdDisplay => string.IsNullOrWhiteSpace(BuildId) ? "—" : BuildId;

    public string SizeOnDiskDisplay => SizeOnDisk == 0
        ? "占位配置"
        : $"{SizeOnDisk / (1024d * 1024 * 1024):0.##} GB";

    public bool IsBuildIdValid => ulong.TryParse(BuildId,
        NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0;
}

public sealed record AcfScanResult(IReadOnlyList<AcfManifest> Manifests, IReadOnlyList<string> Errors)
{
    public static AcfScanResult Empty { get; } = new(Array.Empty<AcfManifest>(), Array.Empty<string>());
}

public enum BuildIdState
{
    Unknown,
    UpToDate,
    UpdateAvailable,
    Error,
}

/// <summary>Result of comparing one manifest with the public branch BuildID.</summary>
public sealed record BuildIdCheckResult(
    AcfManifest Manifest,
    string? LatestBuildId,
    BuildIdState State,
    string Message)
{
    public bool CanUpdate => State == BuildIdState.UpdateAvailable
        && !string.IsNullOrWhiteSpace(LatestBuildId);
}