using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SteamBuildIdManager.Core.Services;

/// <summary>
/// Detects conflicting version evidence before a write. Passing this check does not prove the
/// candidate is fresh; it only proves nothing known contradicts it.
/// </summary>
public static class SteamManifestUpdateGuard
{
    /// <summary>Re-read the ACF and confirm Steam is closed. Call immediately before writing.</summary>
    public static async Task EnsureWritableAsync(string path, string appId, string candidateBuildId,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(path))
            Validate(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), appId, candidateBuildId);
        cancellationToken.ThrowIfCancellationRequested();

        var processes = Process.GetProcessesByName("steam");
        try
        {
            if (processes.Length > 0)
                throw new SteamAppInfoException(
                    "请先通过 Steam 菜单完全退出 Steam，再执行一键更新，避免配置被 Steam 写回覆盖。原配置未修改。");
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    /// <summary>
    /// Reject candidates that contradict what Steam itself recorded. Returns null when consistent,
    /// or a human-readable reason when the write must not happen.
    /// </summary>
    public static string? DescribeConflict(string acf, string appId, string candidateBuildId)
    {
        try
        {
            Validate(acf, appId, candidateBuildId);
            return null;
        }
        catch (SteamAppInfoException ex)
        {
            return ex.Message;
        }
    }

    public static void Validate(string acf, string appId, string candidateBuildId)
    {
        var fields = ReadAppState(acf);

        if (!fields.TryGetValue("appid", out var existingAppId) || existingAppId != appId)
            throw new SteamAppInfoException("现有 ACF 的 AppID 不匹配，已停止更新。原配置未修改。");

        if (!ulong.TryParse(candidateBuildId, NumberStyles.None, CultureInfo.InvariantCulture, out var candidate)
            || candidate == 0)
            throw new SteamAppInfoException("来源没有有效的 BuildID，已停止更新。");

        var installed = ReadBuild(fields, "buildid");
        var target = ReadBuild(fields, "TargetBuildID");

        // Build IDs are identifiers, not semantic versions: any target disagreement requires
        // reconciliation even when the candidate looks numerically newer (branch or rollback cases).
        if (target != 0 && target != candidate)
            throw new SteamAppInfoException(
                $"版本信息冲突：Steam 记录的目标 BuildID 为 {target}，当前来源返回 {candidate}。" +
                "来源可能尚未同步，或分支信息不同。请稍后重试；原配置未修改。不能沿用旧 Manifest 只改 BuildID。");

        if (installed > candidate)
            throw new SteamAppInfoException(
                $"无法确认最新版本：现有 BuildID 为 {installed}，来源返回 {candidate}。已阻止自动回退，原配置未修改。");
    }

    /// <summary>True when Steam already knows a newer build than the source reports.</summary>
    public static bool HasNewerTarget(string acf, string candidateBuildId)
    {
        try
        {
            var fields = ReadAppState(acf);
            var target = ReadBuild(fields, "TargetBuildID");
            if (target == 0) return false;
            return !ulong.TryParse(candidateBuildId, NumberStyles.None, CultureInfo.InvariantCulture, out var candidate)
                   || target != candidate;
        }
        catch (SteamAppInfoException)
        {
            return false;
        }
    }

    /// <summary>
    /// Field-level variant used while scanning: returns a reason when Steam's recorded target build
    /// disagrees with the source, or when the source would roll the install backwards.
    /// </summary>
    public static string? DescribeStaleSource(string? targetBuildId, string? installedBuildId, string candidateBuildId)
    {
        if (!ulong.TryParse(candidateBuildId?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var candidate)
            || candidate == 0)
            return "来源没有有效的 BuildID。";

        if (ulong.TryParse(targetBuildId?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var target)
            && target != 0 && target != candidate)
            return $"Steam 已记录目标 BuildID {target}，当前来源只提供 {candidate}；" +
                   "来源可能尚未同步，直接改写会丢掉 Steam 的更新意图，请稍后重新检测。";

        if (ulong.TryParse(installedBuildId?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var installed)
            && installed > candidate)
            return $"已安装 BuildID {installed} 高于来源返回的 {candidate}，已阻止自动回退。";

        return null;
    }

    private static ulong ReadBuild(Dictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out var value)) return 0;
        if (ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return id;
        throw new SteamAppInfoException($"现有 ACF 的 {key} 无效，无法安全比较版本。原配置未修改。");
    }

    /// <summary>
    /// Parse the complete text structure and take only direct AppState scalars. Nested depot
    /// fields, comments, escaped quotes and braces inside strings must not become version evidence.
    /// </summary>
    private static Dictionary<string, string> ReadAppState(string text)
    {
        var tokens = new List<string>();
        var pattern = new Regex("\\G(?:\\s+|//[^\\r\\n]*|\"(?:\\\\.|[^\"\\\\])*\"|[{}])",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        for (var position = 0; position < text.Length;)
        {
            var match = pattern.Match(text, position);
            if (!match.Success) throw Invalid();
            position += match.Length;
            if (!string.IsNullOrWhiteSpace(match.Value) && !match.Value.StartsWith("//", StringComparison.Ordinal))
                tokens.Add(match.Value);
        }

        var index = 0;
        string Next() => index < tokens.Count ? tokens[index++] : throw Invalid();
        static string Scalar(string token) =>
            token.Length >= 2 && token.StartsWith('"') && token.EndsWith('"') ? token[1..^1] : throw Invalid();

        Dictionary<string, string> ReadObject(int depth)
        {
            if (depth > 64 || Next() != "{") throw Invalid();
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (index < tokens.Count && tokens[index] != "}")
            {
                var key = Scalar(Next());
                if (!keys.Add(key) || index >= tokens.Count) throw Invalid();
                if (tokens[index] == "{") ReadObject(depth + 1);
                else result[key] = Scalar(Next());
            }
            if (Next() != "}") throw Invalid();
            return result;
        }

        if (!string.Equals(Scalar(Next()), "AppState", StringComparison.OrdinalIgnoreCase)) throw Invalid();
        var fields = ReadObject(0);
        if (index != tokens.Count) throw Invalid();
        return fields;
    }

    private static SteamAppInfoException Invalid() =>
        new("现有 ACF 格式无法识别，无法安全比较版本。原配置未修改。");
}