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

    private static readonly Regex ManifestLine = new(
        "^[ \\t]*\"manifest\"[ \\t]+\"(?<value>[^\"]*)\"",
        RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
        _ = ulong.TryParse(ReadString(content, "BytesToDownload"), NumberStyles.None,
            CultureInfo.InvariantCulture, out var pendingBytes);
        _ = ulong.TryParse(ReadString(content, "StagingSize"), NumberStyles.None,
            CultureInfo.InvariantCulture, out var stagingSize);

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
                : DateTimeOffset.UtcNow,
            DepotManifests: ReadDepotManifests(content),
            DownloadType: ReadString(content, "DownloadType") ?? "",
            PendingBytes: pendingBytes,
            StagingSize: stagingSize);
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

    /// <summary>DepotID → manifest GID currently recorded in InstalledDepots.</summary>
    public static IReadOnlyDictionary<string, string> ReadDepotManifests(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var block in EnumerateDepotBlocks(content))
        {
            var match = ManifestLine.Match(block.Body);
            if (match.Success) result[block.DepotId] = match.Groups["value"].Value;
        }
        return result;
    }

    /// <summary>
    /// Update buildid, refresh the manifest GID of every depot Steam knows about, and clear the
    /// "update required" state bit. Steam compares depot manifests as well as buildid, so patching
    /// buildid alone leaves the game showing "需要更新" in the client.
    /// </summary>
    public static string WithBuildIdAndDepots(string content, string newBuildId,
        IReadOnlyDictionary<string, string>? depotManifests, bool clearUpdateState = true)
    {
        var result = WithBuildId(content, newBuildId);
        if (depotManifests is { Count: > 0 })
            result = WithDepotManifests(result, depotManifests);
        if (clearUpdateState)
            result = ResetUpdateState(result);
        return result;
    }

    /// <summary>Rewrite existing depot manifests and append depots the ACF is missing.</summary>
    public static string WithDepotManifests(string content, IReadOnlyDictionary<string, string> manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        if (manifests.Count == 0) return content;

        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var edits = new List<(int Start, int Length, string Text)>();
        var present = new HashSet<string>(StringComparer.Ordinal);

        foreach (var block in EnumerateDepotBlocks(content))
        {
            present.Add(block.DepotId);
            if (!manifests.TryGetValue(block.DepotId, out var manifest) || string.IsNullOrWhiteSpace(manifest))
                continue;

            var match = ManifestLine.Match(block.Body);
            if (!match.Success) continue;
            var value = match.Groups["value"];
            edits.Add((block.BodyStart + value.Index, value.Length, manifest));
        }

        // Append depots the manifest file does not list yet; Steam would otherwise keep asking for them.
        var missing = manifests
            .Where(pair => !present.Contains(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToList();
        if (missing.Count > 0 && TryGetInstalledDepotsSpan(content, out _, out var closeBrace))
        {
            // Insert just before the InstalledDepots closing brace, matching its indentation.
            var lineStart = content.LastIndexOf('\n', Math.Max(closeBrace - 1, 0));
            var insertAt = lineStart >= 0 ? lineStart + 1 : closeBrace;
            var builder = new System.Text.StringBuilder();
            foreach (var (depotId, manifest) in missing)
            {
                builder.Append('\t').Append('"').Append(depotId).Append('"').Append(newline);
                builder.Append('\t').Append('{').Append(newline);
                builder.Append("\t\t\"manifest\"\t\t\"").Append(manifest).Append('"').Append(newline);
                builder.Append("\t\t\"size\"\t\t\"0\"").Append(newline);
                builder.Append('\t').Append('}').Append(newline);
            }
            edits.Add((insertAt, 0, builder.ToString()));
        }

        // Apply from the end so earlier offsets stay valid.
        foreach (var edit in edits.OrderByDescending(item => item.Start))
            content = content.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
        return content;
    }

    /// <summary>
    /// Rewrite the update state the way a freshly generated manifest looks: fully installed,
    /// no queued target build and no pending download. Steam resumes any paused or queued
    /// transfer it finds here, which is what used to overwrite our edit on the next client start.
    /// </summary>
    public static string ResetUpdateState(string content)
    {
        // 4 = "Fully Installed". Clearing only bit 2 leaves 512 (UpdatePaused) behind, so Steam
        // still treated the app as updating; a healthy install has exactly 4.
        content = SetValue(content, "StateFlags", "4");
        content = SetValue(content, "DownloadType", "1");
        content = SetValue(content, "UpdateResult", "0");
        content = SetValue(content, "BytesToDownload", "0");
        content = SetValue(content, "BytesDownloaded", "0");
        content = SetValue(content, "BytesToStage", "0");
        content = SetValue(content, "BytesStaged", "0");
        content = SetValue(content, "StagingSize", "0");
        content = SetValue(content, "ScheduledAutoUpdate", "0");
        content = SetValue(content, "TargetBuildID", "0");
        return content;
    }

    /// <summary>Replace the quoted value of an existing key, keeping indentation and quoting style.</summary>
    public static string SetValue(string content, string key, string value)
    {
        var pattern = "^(?<prefix>[ \\t]*\"" + Regex.Escape(key) + "\"[ \\t]+\")(?<value>[^\"]*)(?<quote>\")";
        return Regex.Replace(content, pattern,
            match => match.Groups["prefix"].Value + value + match.Groups["quote"].Value,
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
    }

    private static IEnumerable<DepotBlock> EnumerateDepotBlocks(string content)
    {
        if (!TryGetInstalledDepotsSpan(content, out var openBrace, out var closeBrace)) yield break;

        var blockStart = openBrace + 1;
        var blockLength = closeBrace - blockStart;
        if (blockLength <= 0) yield break;
        var block = content.Substring(blockStart, blockLength);

        foreach (Match match in DepotKey.Matches(block))
        {
            var braceOffset = match.Index + match.Length - 1;
            var end = FindMatchingBrace(block, braceOffset);
            if (end < 0) continue;
            var bodyStart = braceOffset + 1;
            yield return new DepotBlock(
                match.Groups["id"].Value,
                blockStart + bodyStart,
                block.Substring(bodyStart, end - bodyStart));
        }
    }

    private static bool TryGetInstalledDepotsSpan(string content, out int openBrace, out int closeBrace)
    {
        openBrace = -1;
        closeBrace = -1;
        var header = InstalledDepotsHeader.Match(content);
        if (!header.Success) return false;
        openBrace = content.IndexOf('{', header.Index);
        if (openBrace < 0) return false;
        closeBrace = FindMatchingBrace(content, openBrace);
        return closeBrace > openBrace;
    }

    private readonly record struct DepotBlock(string DepotId, int BodyStart, string Body);

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