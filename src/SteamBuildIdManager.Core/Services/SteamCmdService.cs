using System.Diagnostics;
using System.Globalization;
using System.Text;
using SteamBuildIdManager.Core.Models;

namespace SteamBuildIdManager.Core.Services;

/// <summary>
/// Optional second data source: runs the official Valve SteamCMD client with
/// <c>+app_info_print &lt;appid&gt;</c> and parses the KeyValues it prints. This reflects the same
/// PICS data the Steam client uses, so it is fresher than third-party mirrors for newer apps.
/// </summary>
public sealed class SteamCmdService
{
    /// <summary>
    /// SteamCMD keeps a single update lock; running several instances at once makes every one of
    /// them wait. All invocations therefore go through this gate.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(5);

    private readonly string _executablePath;
    private readonly TimeSpan _timeout;

    /// <summary>
    /// Set after a failed invocation. Queued callers re-check it *inside* the gate so a broken
    /// SteamCMD costs one timeout in total instead of one per app.
    /// </summary>
    private static DateTimeOffset _disabledUntil = DateTimeOffset.MinValue;

    public SteamCmdService(string executablePath, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _executablePath = executablePath;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public string ExecutablePath => _executablePath;

    public async Task<SteamAppInfo> GetAppInfoAsync(string appId, CancellationToken cancellationToken = default)
    {
        if (!SteamAppInfoService.TryNormalizeAppId(appId, out var normalized))
            throw new SteamAppInfoException("AppID 必须为 1 到 4294967295 之间的整数。");

        var (output, exitCode) = await RunAsync($"+login anonymous +app_info_print {normalized} +quit",
            _timeout, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(output) || !output.Contains('{'))
        {
            if (exitCode == 0)
                throw new SteamAppInfoNotFoundException("SteamCMD 没有返回该 AppID 的 appinfo，请确认 AppID 正确且该 App 公开。");
            throw new SteamAppInfoException(
                $"SteamCMD 退出码 {exitCode}，没有返回可用数据（首次运行需要联网自更新）。");
        }

        return SteamCmdAppInfoParser.Parse(output, normalized);
    }

    /// <summary>Quick availability probe used by the settings page.</summary>
    public async Task<string> TestAsync(CancellationToken cancellationToken = default)
    {
        var (output, exitCode) = await RunAsync("+login anonymous +quit", TimeSpan.FromSeconds(45),
            cancellationToken, ignoreCooldown: true).ConfigureAwait(false);
        if (exitCode == 0)
        {
            _disabledUntil = DateTimeOffset.MinValue;
            return "SteamCMD 可以正常启动与登录。";
        }
        var hint = output.Split('\n').FirstOrDefault(line => line.Contains("Error", StringComparison.OrdinalIgnoreCase))
                   ?? output.Split('\n').LastOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? "";
        return $"SteamCMD 退出码 {exitCode}。{hint.Trim()}";
    }

    private async Task<(string Output, int ExitCode)> RunAsync(string arguments, TimeSpan timeout,
        CancellationToken cancellationToken, bool ignoreCooldown = false)
    {
        if (!File.Exists(_executablePath))
            throw new SteamAppInfoException($"找不到 SteamCMD：{_executablePath}");

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ignoreCooldown && DateTimeOffset.UtcNow < _disabledUntil)
                throw new SteamAppInfoException("SteamCMD 最近一次调用失败，5 分钟内不再重试。");

            try
            {
                return await RunCoreAsync(arguments, timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (SteamAppInfoNotFoundException)
            {
                throw;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                _disabledUntil = DateTimeOffset.UtcNow + FailureCooldown;
                throw;
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<(string Output, int ExitCode)> RunCoreAsync(string arguments, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(_executablePath) ?? AppContext.BaseDirectory,
        };

        using var process = new Process { StartInfo = startInfo };
        var builder = new StringBuilder();
        var error = new StringBuilder();
        using var earlyAbort = new CancellationTokenSource();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            builder.AppendLine(e.Data);
            // SteamCMD retries its self-update for a long time; fail fast on a fatal bootstrap error.
            if (IsFatalBootstrapLine(e.Data)) earlyAbort.Cancel();
        };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };

        try
        {
            if (!process.Start())
                throw new SteamAppInfoException("SteamCMD 启动失败。");
        }
        catch (Exception ex) when (ex is not SteamAppInfoException)
        {
            throw new SteamAppInfoException($"无法启动 SteamCMD：{ex.Message}", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutSource.Token, earlyAbort.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (earlyAbort.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            var detail = builder.ToString().Split('\n')
                .LastOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? "";
            throw new SteamAppInfoException(
                "SteamCMD 无法完成自更新（通常是网络或证书被拦截）。" + detail.Trim());
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new SteamAppInfoException($"SteamCMD 超过 {timeout.TotalSeconds:0} 秒仍未返回，已终止。");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        // Give the async readers a moment to flush after the process exits.
        await Task.Delay(120, CancellationToken.None).ConfigureAwait(false);

        var text = builder.ToString();
        if (string.IsNullOrWhiteSpace(text)) text = error.ToString();
        return (text, process.ExitCode);
    }

    private static bool IsFatalBootstrapLine(string line) =>
        line.Contains("Fatal Error", StringComparison.OrdinalIgnoreCase)
        || line.Contains("needs to be online to update", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Can't use HTTPS", StringComparison.OrdinalIgnoreCase);

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort only.
        }
    }
}

/// <summary>Maps the KeyValues printed by <c>app_info_print</c> onto the same model the HTTP parser returns.</summary>
public static class SteamCmdAppInfoParser
{
    public static SteamAppInfo Parse(string output, string appId)
    {
        var body = ExtractAppBlock(output, appId);
        var root = VdfDocument.TryParseBlock(body)
                   ?? throw new SteamAppInfoException("SteamCMD 返回的内容不是有效的 KeyValues 结构。");

        var common = root["common"];
        var config = root["config"];
        var depots = root["depots"];

        var builds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (depots?["branches"] is { } branches)
        {
            foreach (var (name, node) in branches.Entries)
            {
                var buildId = node.Get("buildid");
                if (!string.IsNullOrWhiteSpace(buildId)) builds[name] = buildId;
            }
        }

        var depotList = new List<SteamDepotInfo>();
        if (depots != null)
        {
            foreach (var (id, node) in depots.Entries)
            {
                if (!SteamAppInfoService.TryNormalizeAppId(id, out _) || !node.IsBlock) continue;
                depotList.Add(new SteamDepotInfo(
                    Id: id,
                    Name: node.Get("name") ?? "",
                    Filter: ReadFilter(node),
                    DlcAppId: node.Get("dlcappid") ?? "",
                    Manifests: ReadManifests(node["manifests"])));
            }
        }

        var launches = new List<SteamLaunchInfo>();
        if (config?["launch"] is { } launch)
        {
            foreach (var (id, node) in launch.Entries)
            {
                if (!node.IsBlock) continue;
                launches.Add(new SteamLaunchInfo(
                    Id: id,
                    Description: node.Get("description") ?? "",
                    Executable: node.Get("executable") ?? "",
                    Arguments: node.Get("arguments") ?? "",
                    Type: node.Get("type") ?? "",
                    Filter: ReadFilter(node)));
            }
        }

        return new SteamAppInfo(
            AppId: appId,
            Name: common?.Get("name") ?? "",
            LocalizedNames: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            InstallDirectory: config?.Get("installdir") ?? "",
            BranchBuildIds: builds,
            Depots: depotList,
            LaunchOptions: launches);
    }

    /// <summary>SteamCMD prefixes the dump with a header line; the payload starts at the first brace after it.</summary>
    private static string ExtractAppBlock(string output, string appId)
    {
        var marker = output.IndexOf("\"" + appId + "\"", StringComparison.Ordinal);
        if (marker >= 0)
        {
            var brace = output.IndexOf('{', marker);
            if (brace >= 0) return output[brace..];
        }

        var anyBrace = output.IndexOf('{');
        if (anyBrace < 0)
            throw new SteamAppInfoException("SteamCMD 输出中没有 appinfo 数据，请确认 AppID 正确且该 App 公开。");
        return output[anyBrace..];
    }

    private static SteamPlatformFilter ReadFilter(VdfNode node)
    {
        var config = node["config"];
        return new SteamPlatformFilter(
            OperatingSystems: config?.Get("oslist") ?? "",
            Architecture: config?.Get("osarch") ?? "",
            Language: config?.Get("language") ?? "");
    }

    private static IReadOnlyDictionary<string, string> ReadManifests(VdfNode? manifests)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (manifests == null) return result;

        foreach (var (branch, node) in manifests.Entries)
        {
            // Newer dumps use a block with gid/size; older ones use a bare id string.
            var gid = node.IsBlock ? node.Get("gid") : node.Value;
            if (string.IsNullOrWhiteSpace(gid)) continue;
            if (ulong.TryParse(gid, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                result[branch] = gid;
        }

        return result;
    }
}

/// <summary>Finds a usable steamcmd.exe from settings, well-known folders or PATH.</summary>
public static class SteamCmdLocator
{
    public static string? Resolve(string? configuredPath, string? steamInstallPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var configured = configuredPath.Trim();
            if (File.Exists(configured)) return configured;
        }

        foreach (var candidate in Candidates(steamInstallPath))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) return candidate;
        }

        return FromPath();
    }

    public static IEnumerable<string> Candidates(string? steamInstallPath = null)
    {
        if (!string.IsNullOrWhiteSpace(steamInstallPath))
            yield return Path.Combine(steamInstallPath, "steamcmd", "steamcmd.exe");

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     @"C:\",
                 })
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            yield return Path.Combine(root, "steamcmd", "steamcmd.exe");
        }
    }

    private static string? FromPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;

        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(folder.Trim(), "steamcmd.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // Ignore malformed PATH segments.
            }
        }

        return null;
    }
}