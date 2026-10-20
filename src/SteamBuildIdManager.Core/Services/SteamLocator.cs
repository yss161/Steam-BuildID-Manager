using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SteamBuildIdManager.Core.Services;

/// <summary>Discovers the Steam client and its library folders without requiring the app to be running.</summary>
public static class SteamLocator
{
    private static readonly Regex PathEntry = new(
        "\"path\"[ \\t]*\"(?<path>(?:[^\"\\\\]|\\\\.)*)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LegacyEntry = new(
        "^[ \\t]*\"[0-9]+\"[ \\t]*\"(?<path>(?:[^\"\\\\]|\\\\.)*)\"",
        RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string? DetectSteamInstallPath()
    {
        foreach (var (hive, keyName, valueName) in new[]
                 {
                     (RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                     (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                     (RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(keyName);
                if (key?.GetValue(valueName) is string path && Directory.Exists(path)
                    && Directory.Exists(Path.Combine(path, "steamapps")))
                    return Path.GetFullPath(path);
            }
            catch
            {
                // Registry access is best-effort; fall through to the well-known locations.
            }
        }

        string[] commonPaths =
        [
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
            @"D:\Steam",
            @"D:\Program Files (x86)\Steam",
            @"D:\Program Files\Steam",
            @"E:\Steam",
            @"E:\Program Files (x86)\Steam",
        ];

        foreach (var path in commonPaths)
        {
            if (Directory.Exists(path) && File.Exists(Path.Combine(path, "steam.exe")))
                return path;
        }

        return null;
    }

    /// <summary>Return the install folder plus every library root recorded in libraryfolders.vdf.</summary>
    public static List<string> DetectLibraryPaths(string? steamInstallPath = null)
    {
        var result = new List<string>();
        var installPath = string.IsNullOrWhiteSpace(steamInstallPath)
            ? DetectSteamInstallPath()
            : steamInstallPath.Trim();

        if (string.IsNullOrWhiteSpace(installPath)) return result;

        AddIfLibrary(result, installPath);

        var foldersFile = Path.Combine(installPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(foldersFile)) return result;

        try
        {
            foreach (var path in ParseLibraryFoldersVdf(File.ReadAllText(foldersFile)))
                AddIfLibrary(result, path);
        }
        catch
        {
            // A malformed VDF must not prevent the default library from being used.
        }

        return result;
    }

    public static IReadOnlyList<string> ParseLibraryFoldersVdf(string content)
    {
        var paths = new List<string>();
        foreach (Match match in PathEntry.Matches(content))
            AddUnique(paths, Unescape(match.Groups["path"].Value));
        foreach (Match match in LegacyEntry.Matches(content))
            AddUnique(paths, Unescape(match.Groups["path"].Value));
        return paths;
    }

    public static string NormalizeLibraryRoot(string path)
    {
        var full = Path.GetFullPath(path.Trim().TrimEnd('\\', '/'));
        return string.Equals(Path.GetFileName(full), "steamapps", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(full) ?? full
            : full;
    }

    public static bool IsLibraryRoot(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path)
                && Directory.Exists(Path.Combine(NormalizeLibraryRoot(path), "steamapps"));
        }
        catch
        {
            return false;
        }
    }

    private static void AddIfLibrary(List<string> result, string path)
    {
        if (!IsLibraryRoot(path)) return;
        AddUnique(result, NormalizeLibraryRoot(path));
    }

    private static void AddUnique(List<string> result, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return;
        var normalized = NormalizeLibraryRoot(path);
        if (!result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            result.Add(normalized);
    }

    private static string Unescape(string value) =>
        value.Replace("\\\\", "\\", StringComparison.Ordinal)
             .Replace("\\\"", "\"", StringComparison.Ordinal);
}