using System.Globalization;
using System.Text.RegularExpressions;
using SteamBuildIdManager.Core.Models;

namespace SteamBuildIdManager.Core.Services;

/// <summary>
/// Minimal reader/writer for Valve KeyValues appmanifest files. The writer only replaces the
/// BuildID fields and never re-serialises the whole document, so user and Steam specific fields
/// (LauncherPath, UserConfig, MountedConfig, extra keys) survive a BuildID refresh untouched.
/// </summary>
public static class AcfDocument
{
    private static readonly Regex BuildIdLine = new(
        "^(?<prefix>[ \\t]*\"buildid\"[ \\t]+\")(?<value>[^\"]*)(?<quote>\")",
        RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TargetBuildIdLine = new(
        "^(?<prefix>[ \\t]*\"TargetBuildID\"[ \\t]+\")(?<value>[^\"]*)(?<quote>\")",
        RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex InstalledDepotsHeader = new(
        "^(?<indent>[ \\t]*)\"InstalledDepots\"[ \\t]*(?:\r?\n)?[ \\t]*\\{",
        RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DepotKey = new(
        "\"(?<id>[0-9]+)\"[ \\t]*(?:\r?\n)?[ \\t]*\\{",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static AcfManifest Parse(string filePath, string libraryPath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(content);

        var appId = ReadString(content, "appid");
        if (!ulong.TryParse(appId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedAppId) || parsedAppId == 0)
        {
            var match = Regex.Match(Path.GetFileName(filePath), @"^appmanifest_(?<id>[0-9]+)\.acf$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            appId = match.Success ? match.Groups["id"].Value : "";
        }

        if (string.IsNullOrWhiteSpace(appId))
            throw new FormatException("ACF 缺少有效的 appid。");

        _ = ulong.TryParse(ReadString(content, "SizeOnDisk"), NumberStyles.None,
            CultureInfo.InvariantCulture, out var sizeOnDisk);

        return new AcfManifest(
            FilePath: filePath,
            LibraryPath: libraryPath,
            AppId: appId,
            Name: ReadString(content, "name") ?? "",
            InstallDirectory: ReadString(content, "installdir") ?? "",
            BuildId: ReadString(content, "buildid") ?? "",
            TargetBuildId: ReadString(content, "TargetBuildID") ?? "",
            StateFlags: ReadString(content, "StateFlags") ?? "",
            LauncherPath: ReadString(content, "LauncherPath") ?? "",
            SizeOnDisk: sizeOnDisk,
            DepotIds: ReadDepotIds(content),
            LastWriteTime: File.Exists(filePath)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero)
                : DateTimeOffset.UtcNow);
    }

    public static bool TryParse(string filePath, string libraryPath, string content,
        out AcfManifest? manifest, out string error)
    {
        try
        {
            manifest = Parse(filePath, libraryPath, content);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            manifest = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Replace buildid and clear TargetBuildID, preserving every other byte and line ending.</summary>
    public static string WithBuildId(string content, string newBuildId)
    {
        if (!ulong.TryParse(newBuildId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed == 0)
            throw new ArgumentException("BuildID 必须是正整数。", nameof(newBuildId));

        var replaced = false;
        var result = BuildIdLine.Replace(content, match =>
        {
            replaced = true;
            return match.Groups["prefix"].Value + newBuildId + match.Groups["quote"].Value;
        }, 1);
        result = TargetBuildIdLine.Replace(result, match =>
            match.Groups["prefix"].Value + "0" + match.Groups["quote"].Value, 1);

        if (replaced) return result;

        // Some hand-edited manifests omit buildid entirely; insert it before the root closing brace.
        var close = result.LastIndexOf('}');
        if (close < 0) throw new FormatException("ACF 结构不完整，找不到根节点结束符。");
        var newline = result.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return result.Insert(close, $"\t\"buildid\"\t\t\"{newBuildId}\"{newline}");
    }

    public static string? ReadString(string content, string key)
    {
        var pattern = "^[ \\t]*\"" + Regex.Escape(key) + "\"[ \\t]+(?:\"(?<value>[^\"]*)\"|(?<value>[^\\s\"]+))";
        var match = Regex.Match(content, pattern,
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value : null;
    }

    public static IReadOnlyList<string> ReadDepotIds(string content)
    {
        var header = InstalledDepotsHeader.Match(content);
        if (!header.Success) return Array.Empty<string>();

        var openBrace = content.IndexOf('{', header.Index);
        if (openBrace < 0) return Array.Empty<string>();
        var closeBrace = FindMatchingBrace(content, openBrace);
        if (closeBrace < 0) return Array.Empty<string>();

        var block = content.Substring(openBrace + 1, closeBrace - openBrace - 1);
        return DepotKey.Matches(block)
            .Select(match => match.Groups["id"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static int FindMatchingBrace(string text, int openBrace)
    {
        var depth = 0;
        var inQuotes = false;
        for (var i = openBrace; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"') inQuotes = !inQuotes;
            else if (!inQuotes && c == '{') depth++;
            else if (!inQuotes && c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }
}

/// <summary>Numeric BuildID comparison. Missing values are lower than any real BuildID.</summary>
public static class BuildIdComparer
{
    public static bool TryParse(string? value, out ulong buildId) =>
        ulong.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out buildId) && buildId > 0;

    /// <summary>Returns -1, 0, 1 for current &lt; latest, equal, current &gt; latest; null when either side is invalid.</summary>
    public static int? Compare(string? current, string? latest)
    {
        if (!TryParse(latest, out var latestId)) return null;
        if (!TryParse(current, out var currentId)) return -1;
        return currentId.CompareTo(latestId);
    }
}