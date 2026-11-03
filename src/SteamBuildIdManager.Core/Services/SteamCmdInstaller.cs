using System.IO.Compression;
using System.Net.Http;

namespace SteamBuildIdManager.Core.Services;

/// <summary>
/// Installs and maintains a private SteamCMD copy under the app's local data folder so users do not
/// have to find and unpack it manually. Nothing here touches the system PATH or the registry.
/// </summary>
public sealed class SteamCmdInstaller
{
    /// <summary>Valve's official bootstrap; the second host is the fallback when the CDN is blocked.</summary>
    public static readonly IReadOnlyList<Uri> DefaultDownloadUrls = new[]
    {
        new Uri("https://media.steampowered.com/client/installer/steamcmd.zip"),
        new Uri("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip"),
    };

    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(90) };

    private readonly HttpClient _client;
    private readonly IReadOnlyList<Uri> _downloadUrls;
    private readonly string _directory;
    private readonly TimeSpan _perUrlTimeout = TimeSpan.FromSeconds(90);

    public SteamCmdInstaller(HttpClient? client = null, IEnumerable<Uri>? downloadUrls = null,
        string? directory = null)
    {
        _client = client ?? SharedClient;
        _downloadUrls = (downloadUrls ?? DefaultDownloadUrls).ToList();
        if (_downloadUrls.Count == 0) throw new ArgumentException("至少需要一个下载地址。", nameof(downloadUrls));
        _directory = string.IsNullOrWhiteSpace(directory)
            ? DefaultDirectory
            : Path.GetFullPath(directory);
    }

    /// <summary>%LocalAppData%\SteamBuildIDManager\steamcmd — independent of the system installation.</summary>
    public static string DefaultDirectory => Path.Combine(AppPaths.LocalDirectory, "steamcmd");

    public static string DefaultExecutablePath => Path.Combine(DefaultDirectory, "steamcmd.exe");

    /// <summary>
    /// Folder next to the application that the installer fills with the bundled bootstrap.
    /// The bundled copy is read-only under Program Files, so it is only a template.
    /// </summary>
    public static string BundledDirectory => Path.Combine(AppContext.BaseDirectory, "steamcmd");

    public static string BundledExecutablePath => Path.Combine(BundledDirectory, "steamcmd.exe");

    /// <summary>True when the installer shipped a SteamCMD payload with this build.</summary>
    public static bool HasBundledCopy => File.Exists(BundledExecutablePath);

    public string InstallDirectory => _directory;

    public string ExecutablePath => Path.Combine(_directory, "steamcmd.exe");

    public bool IsInstalled => File.Exists(ExecutablePath);

    /// <summary>
    /// Copy the bundled bootstrap into the writable data folder. SteamCMD rewrites itself on its
    /// first run, so it cannot live in Program Files; this keeps the install working with no
    /// network access while still allowing self-update.
    /// </summary>
    public string? DeployBundledCopy(IProgress<string>? progress = null)
    {
        if (!HasBundledCopy) return null;
        return DeployBundledCopyFrom(BundledDirectory, progress);
    }

    /// <summary>Deploy a specific template folder; returns null when it holds no steamcmd.exe.</summary>
    public string? DeployBundledCopyFrom(string templateDirectory, IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(templateDirectory)
            || !File.Exists(Path.Combine(templateDirectory, "steamcmd.exe")))
            return null;

        Directory.CreateDirectory(_directory);
        progress?.Report("正在解压内置 SteamCMD…");

        foreach (var source in Directory.EnumerateFiles(templateDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(templateDirectory, source);
            var destination = Path.Combine(_directory, relative);
            var destinationFolder = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destinationFolder)) Directory.CreateDirectory(destinationFolder);

            // SteamCMD updates its own files, so always refresh from the bundled template.
            try
            {
                File.Copy(source, destination, overwrite: true);
            }
            catch (IOException ex)
            {
                throw new SteamAppInfoException($"无法写入 SteamCMD（{destination}）：{ex.Message}", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new SteamAppInfoException($"没有权限写入 SteamCMD（{destination}）：{ex.Message}", ex);
            }
        }

        if (!File.Exists(ExecutablePath))
            throw new SteamAppInfoException("内置 SteamCMD 部署失败：找不到 steamcmd.exe。");

        AppLog.Success($"已从安装包内置副本部署 SteamCMD 到 {ExecutablePath}");
        progress?.Report("SteamCMD 已就绪。");
        return ExecutablePath;
    }

    /// <summary>Deploy the bundled copy when present, otherwise download it.</summary>
    public async Task<string> EnsureAvailableAsync(IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsInstalled) return ExecutablePath;
        if (HasBundledCopy) return DeployBundledCopy(progress)!;
        return await InstallOrUpdateAsync(progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Download and extract the official bootstrap, trying each mirror in turn. SteamCMD still runs
    /// its own first-run self-update, so callers usually follow this with a test invocation.
    /// </summary>
    public async Task<string> InstallOrUpdateAsync(IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);

        var errors = new List<string>();
        foreach (var url in _downloadUrls)
        {
            var archivePath = Path.Combine(_directory, "steamcmd.zip");
            try
            {
                progress?.Report($"正在连接 {url.Host} …");
                AppLog.Info($"开始下载 SteamCMD：{url}");

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_perUrlTimeout);

                using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength;
                await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
                await using (var target = File.Create(archivePath))
                {
                    var buffer = new byte[81920];
                    long copied = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                        copied += read;
                        if (total is > 0)
                            progress?.Report($"正在下载 SteamCMD… {copied * 100 / total.Value}%");
                    }
                }

                progress?.Report("正在解压…");
                ZipFile.ExtractToDirectory(archivePath, _directory, overwriteFiles: true);

                if (!File.Exists(ExecutablePath))
                    throw new SteamAppInfoException("压缩包里没有 steamcmd.exe。");

                AppLog.Success($"SteamCMD 已安装到 {ExecutablePath}");
                progress?.Report("SteamCMD 已就绪。");
                return ExecutablePath;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{url.Host}：{ex.Message}");
                AppLog.Warning($"从 {url.Host} 下载 SteamCMD 失败：{ex.Message}");
            }
            finally
            {
                try { if (File.Exists(archivePath)) File.Delete(archivePath); } catch { }
            }
        }

        throw new SteamAppInfoException("下载 SteamCMD 失败，请检查网络后重试。" + string.Join("；", errors));
    }

    /// <summary>Delete the private copy. The app keeps working through the HTTP source.</summary>
    public void Delete()
    {
        if (!Directory.Exists(_directory)) return;
        try
        {
            Directory.Delete(_directory, recursive: true);
            AppLog.Info("已删除本机的 SteamCMD。");
        }
        catch (Exception ex)
        {
            throw new SteamAppInfoException($"删除 SteamCMD 失败：{ex.Message}", ex);
        }
    }
}