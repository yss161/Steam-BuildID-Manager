namespace SteamBuildIdManager.Core.Models;

using System.Text.Json.Serialization;
using SteamBuildIdManager.Core.Services;

/// <summary>Persisted application settings. Unknown JSON fields are ignored for forward compatibility.</summary>
public sealed class AppSettings
{
    public string SteamInstallPath { get; set; } = "";
    public List<string> SteamLibraryPaths { get; set; } = new();
    public string SteamId { get; set; } = "";
    /// <summary>When false only placeholder manifests (SizeOnDisk = 0) are listed.</summary>
    public bool ShowAllSteamGames { get; set; }

    /// <summary>"system", "light" or "dark". Anything else falls back to the system theme.</summary>
    public string ThemeMode { get; set; } = ThemeModes.System;

    /// <summary>Absolute path of a local image used as the window background; empty disables it.</summary>
    public string BackgroundImagePath { get; set; } = "";

    /// <summary>Background image opacity in the 0.1 - 1.0 range.</summary>
    public double BackgroundImageOpacity { get; set; } = 0.35;

    /// <summary>When true the app requests elevation on startup and restarts itself as administrator.</summary>
    public bool RunAsAdministrator { get; set; }

    /// <summary>Which data source chain to use: SteamCMD first, API first, or a single source.</summary>
    public string DataSource { get; set; } = "steamcmd-first";

    /// <summary>Optional explicit path to steamcmd.exe; empty means auto-detect.</summary>
    public string SteamCmdPath { get; set; } = "";

    [JsonIgnore]
    public string NormalizedDataSource => DataSourceModes.Normalize(DataSource);

    [JsonIgnore]
    public string NormalizedThemeMode => ThemeModes.Normalize(ThemeMode);

    [JsonIgnore]
    public double NormalizedBackgroundOpacity =>
        double.IsFinite(BackgroundImageOpacity) ? Math.Clamp(BackgroundImageOpacity, 0.1, 1) : 0.35;
}

/// <summary>Theme identifiers shared by settings, the UI and the tests.</summary>
public static class ThemeModes
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Light => Light,
        Dark => Dark,
        _ => System,
    };

    public static string DisplayName(string? value) => Normalize(value) switch
    {
        Light => "浅色",
        Dark => "深色",
        _ => "跟随系统",
    };
}
