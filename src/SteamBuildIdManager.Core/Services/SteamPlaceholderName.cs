using SteamBuildIdManager.Core.Models;

namespace SteamBuildIdManager.Core.Services;

/// <summary>
/// Derives the placeholder EXE name Steam expects for an AppID. Steam records the launcher
/// executable in appinfo, so the placeholder must use the same file name or Steam refuses to launch.
/// </summary>
public static class SteamPlaceholderName
{
    /// <summary>Prefer a launch option whose executable sits in the app root and looks like a game EXE.</summary>
    public static string FromExecutables(IReadOnlyList<SteamExecutableCandidate> executables)
    {
        if (executables.Count == 0) return "";

        var candidates = executables
            .Select(candidate => (Candidate: candidate, Name: Path.GetFileName(candidate.Path)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && item.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0) return "";

        // A single unambiguous candidate wins outright.
        var distinct = candidates.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count == 1) return distinct[0];

        // Otherwise prefer the shallowest path: the real game launcher usually lives at the root.
        return candidates
            .OrderBy(item => item.Candidate.Path.Count(c => c is '\\' or '/'))
            .ThenBy(item => item.Name.Length)
            .Select(item => item.Name)
            .First();
    }

    /// <summary>Last-resort fallback when no launch option is available: sanitize the install directory.</summary>
    public static string FromInstallDirectory(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory)) return "";
        var trimmed = installDirectory.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "";
        // The placeholder must sit in the game root, so a nested install directory is not usable.
        if (trimmed.Contains('\\', StringComparison.Ordinal) || trimmed.Contains('/', StringComparison.Ordinal)) return "";
        return SteamPathValidator.TryValidate(trimmed, out _, requireExe: false) ? trimmed + ".exe" : "";
    }
}
