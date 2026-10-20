using SteamBuildIdManager.Core.Models;

namespace SteamBuildIdManager.Core.Models;

/// <summary>Compact summary shown by the "获取游戏信息" dialog.</summary>
public sealed record SteamGameSummary(
    string AppId,
    string DisplayName,
    string InstallDirectory,
    string BuildId,
    string PrimaryDepotId,
    string PrimaryManifest,
    IReadOnlyList<SteamDepotCandidate> Depots,
    IReadOnlyList<SteamExecutableCandidate> Executables,
    IReadOnlyList<string> Warnings)
{
    public bool HasPrimaryDepot => !string.IsNullOrWhiteSpace(PrimaryDepotId)
        && !string.IsNullOrWhiteSpace(PrimaryManifest);

    public string PrimaryDepotDisplay => HasPrimaryDepot
        ? PrimaryDepotId
        : "未找到";

    public string PrimaryManifestDisplay => HasPrimaryDepot
        ? PrimaryManifest
        : "未找到";

    /// <summary>Additional depots written together with the primary one.</summary>
    public IReadOnlyList<SteamDepotCandidate> AdditionalDepots =>
        Depots.Count <= 1 ? Array.Empty<SteamDepotCandidate>() : Depots.Skip(1).ToList();

    public string DepotSummary => Depots.Count == 0
        ? "没有可用 Depot"
        : string.Join("\n", Depots.Select((candidate, index) =>
            $"{(index == 0 ? "主" : "附")} · {candidate.Depot.Id} · Manifest {candidate.Manifest}"));
}
