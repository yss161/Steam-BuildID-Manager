using System.Text;
using SteamBuildIdManager.Core.Models;

namespace SteamBuildIdManager.Core.Services;

/// <summary>Compares, backs up and atomically replaces appmanifest files.</summary>
public sealed class ManifestFileService
{
    private readonly string _backupRoot;
    private readonly ManifestBackupOptions _options;
    private readonly Func<DateTimeOffset> _utcNow;

    public ManifestFileService(string? backupRoot = null, ManifestBackupOptions? options = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _backupRoot = string.IsNullOrWhiteSpace(backupRoot) ? AppPaths.DefaultBackupRoot : backupRoot;
        _options = options ?? ManifestBackupOptions.Default;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        if (_options.MaximumCount < 0) throw new ArgumentOutOfRangeException(nameof(options));
        if (_options.MaximumAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
    }

    public string BackupRoot => _backupRoot;

    public async Task<ManifestWriteResult> WriteWithBackupAsync(
        string targetPath,
        string content,
        string appId,
        string previousBuildId,
        string newBuildId = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (!SteamAppInfoService.TryNormalizeAppId(appId, out var normalizedAppId))
            throw new ArgumentException("AppID 必须为有效的正整数。", nameof(appId));

        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new ArgumentException("配置文件路径缺少目录。", nameof(targetPath));
        Directory.CreateDirectory(targetDirectory);

        if (File.Exists(targetPath))
        {
            var current = await File.ReadAllTextAsync(targetPath, cancellationToken).ConfigureAwait(false);
            if (string.Equals(current, content, StringComparison.Ordinal))
            {
                // Identical content creates no backup, but still applies the retention policy.
                var removed = CleanupBackups(normalizedAppId);
                return new(false, null, removed, previousBuildId, newBuildId);
            }
        }

        string? backupPath = null;
        var now = _utcNow();
        if (File.Exists(targetPath))
        {
            var backupDirectory = Path.Combine(_backupRoot, normalizedAppId);
            Directory.CreateDirectory(backupDirectory);
            var build = ulong.TryParse(previousBuildId, out _) ? previousBuildId : "unknown";
            var baseName = $"appmanifest_{normalizedAppId}_build{build}_{now:yyyyMMdd-HHmmssfff}";
            backupPath = Path.Combine(backupDirectory, baseName + ".acf");
            for (var suffix = 1; File.Exists(backupPath); suffix++)
                backupPath = Path.Combine(backupDirectory, $"{baseName}-{suffix}.acf");
            File.Copy(targetPath, backupPath, overwrite: false);
            File.SetCreationTimeUtc(backupPath, now.UtcDateTime);
            File.SetLastWriteTimeUtc(backupPath, now.UtcDateTime);
        }

        var temporaryPath = Path.Combine(targetDirectory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            // The temporary file lives in the target directory so the final move is atomic.
            await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
        }

        return new(true, backupPath, CleanupBackups(normalizedAppId), previousBuildId, newBuildId);
    }

    public int CleanupBackups(string appId)
    {
        var directory = Path.Combine(_backupRoot, appId);
        if (!Directory.Exists(directory)) return 0;

        var removed = 0;
        var cutoff = _utcNow().UtcDateTime - _options.MaximumAge;
        var files = Directory.EnumerateFiles(directory, $"appmanifest_{appId}_*.acf")
            .Select(path => new FileInfo(path)).ToList();
        foreach (var file in files.Where(file => file.LastWriteTimeUtc < cutoff))
            if (TryDelete(file)) removed++;

        files = Directory.EnumerateFiles(directory, $"appmanifest_{appId}_*.acf")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .ToList();
        foreach (var file in files.Skip(_options.MaximumCount))
            if (TryDelete(file)) removed++;
        return removed;
    }

    private static bool TryDelete(FileInfo file)
    {
        try { file.Delete(); return true; }
        catch { return false; }
    }
}