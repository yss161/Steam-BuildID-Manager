using System.Text.Json;
using SteamBuildIdManager.Core.Models;

namespace SteamBuildIdManager.Core.Services;

public static class AppPaths
{
    public static string RoamingDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SteamBuildIDManager");

    public static string LocalDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamBuildIDManager");

    public static string DefaultSettingsPath => Path.Combine(RoamingDirectory, "settings.json");

    public static string DefaultBackupRoot => Path.Combine(LocalDirectory, "backups");

    public static string LogDirectory => Path.Combine(LocalDirectory, "logs");
}

public sealed class SettingsService
{
    private static readonly object SyncRoot = new();
    private readonly string _settingsPath;

    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? AppPaths.DefaultSettingsPath
            : Path.GetFullPath(settingsPath);
    }

    public string SettingsPath => _settingsPath;

    public AppSettings Load()
    {
        lock (SyncRoot)
        {
            return LoadCore();
        }
    }

    public bool Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (SyncRoot)
        {
            return SaveCore(settings);
        }
    }

    /// <summary>Reload and update under one lock so pages cannot overwrite each other with stale snapshots.</summary>
    public bool Update(Action<AppSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (SyncRoot)
        {
            var settings = LoadCore();
            update(settings);
            return SaveCore(settings);
        }
    }

    private AppSettings LoadCore()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new AppSettings();
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppSettings();
            settings.SteamLibraryPaths ??= new List<string>();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    private bool SaveCore(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            var temporary = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _settingsPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}