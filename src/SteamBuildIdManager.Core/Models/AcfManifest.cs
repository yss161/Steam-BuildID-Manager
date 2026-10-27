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
    DateTimeOffset LastWriteTime,
    IReadOnlyDictionary<string, string>? DepotManifests = null,
    string DownloadType = "",
    ulong PendingBytes = 0,
    ulong StagingSize = 0)
{
    /// <summary>DepotID → manifest GID recorded in InstalledDepots.</summary>
    public IReadOnlyDictionary<string, string> Manifests =>
        DepotManifests ?? EmptyManifests;

    private static readonly IReadOnlyDictionary<string, string> EmptyManifests =
        new Dictionary<string, string>();

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

    /// <summary>StateFlags bit 2 means "update required".</summary>
    public bool HasUpdateRequiredFlag =>
        int.TryParse(StateFlags, NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags)
        && (flags & 2) != 0;

    /// <summary>
    /// True when Steam's persisted update state would make the client resume or re-offer a
    /// download: a non-4 StateFlags (518 = paused, 6 = update required), a queued target build,
    /// or non-zero download counters. Missing keys are treated as "nothing queued".
    /// </summary>
    public bool NeedsStateRepair
    {
        get
        {
            var flags = StateFlags.Trim();
            var target = TargetBuildId.Trim();
            return (flags.Length > 0 && !string.Equals(flags, "4", StringComparison.Ordinal))
                || (target.Length > 0 && !string.Equals(target, "0", StringComparison.Ordinal))
                || PendingBytes > 0
                || StagingSize > 0
                || string.Equals(DownloadType.Trim(), "4", StringComparison.Ordinal);
        }
    }
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

/// <summary>Result of comparing one manifest with the public branch state.</summary>
public sealed record BuildIdCheckResult(
    AcfManifest Manifest,
    string? LatestBuildId,
    BuildIdState State,
    string Message)
{
    /// <summary>Public branch depot manifests; null when only the BuildID was compared.</summary>
    public IReadOnlyDictionary<string, string>? LatestDepotManifests { get; init; }

    public bool CanUpdate => State == BuildIdState.UpdateAvailable
        && !string.IsNullOrWhiteSpace(LatestBuildId);

    /// <summary>True when Steam would still offer an update because a depot manifest differs.</summary>
    public bool HasDepotDrift => LatestDepotManifests is { Count: > 0 }
        && LatestDepotManifests.Any(pair =>
            Manifest.Manifests.TryGetValue(pair.Key, out var local)
            && !string.Equals(local, pair.Value, StringComparison.Ordinal));
}