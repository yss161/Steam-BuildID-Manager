namespace SteamBuildIdManager.Core.Models;

/// <summary>Persisted application settings. Unknown JSON fields are ignored for forward compatibility.</summary>
public sealed class AppSettings
{
    public string SteamInstallPath { get; set; } = "";
    public List<string> SteamLibraryPaths { get; set; } = new();
    public string SteamId { get; set; } = "";
    /// <summary>When false only placeholder manifests (SizeOnDisk = 0) are listed.</summary>
    public bool ShowAllSteamGames { get; set; }
}