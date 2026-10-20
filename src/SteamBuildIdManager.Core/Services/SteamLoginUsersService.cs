using System.Globalization;
using System.Text;
using SteamBuildIdManager.Core.Models;

namespace SteamBuildIdManager.Core.Services;

/// <summary>
/// Reads the local SteamID64 of remembered accounts from config/loginusers.vdf.
/// Falls back to userdata folder names (SteamID32) when loginusers.vdf is missing or unreadable.
/// </summary>
public sealed class SteamLoginUsersService
{
    private const ulong SteamId64Base = 76561197960265728UL;

    public IReadOnlyList<SteamLoginUser> Read(string? steamInstallPath = null)
    {
        var root = ResolveInstallPath(steamInstallPath);
        if (root == null) return Array.Empty<SteamLoginUser>();

        var loginUsersFile = Path.Combine(root, "config", "loginusers.vdf");
        if (File.Exists(loginUsersFile))
        {
            try
            {
                var parsed = Parse(File.ReadAllText(loginUsersFile));
                if (parsed.Count > 0) return parsed;
            }
            catch
            {
                // A damaged or locked file must not prevent the userdata fallback.
            }
        }

        return ReadUserData(root);
    }

    /// <summary>Parse loginusers.vdf content; invalid entries are ignored instead of failing the whole file.</summary>
    public static IReadOnlyList<SteamLoginUser> Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return Array.Empty<SteamLoginUser>();

        try
        {
            var root = ParseDocument(Tokenize(content));
            if (!root.TryGetValue("users", out var node)
                || node is not Dictionary<string, object> users)
                return Array.Empty<SteamLoginUser>();

            var result = new List<SteamLoginUser>();
            foreach (var (key, value) in users)
            {
                if (!IsValidSteamId64(key) || value is not Dictionary<string, object> block)
                    continue;

                result.Add(new SteamLoginUser(
                    SteamId64: key,
                    AccountName: ReadString(block, "AccountName"),
                    PersonaName: ReadString(block, "PersonaName"),
                    MostRecent: ReadBoolean(block, "MostRecent"),
                    AutoLogin: ReadBoolean(block, "AutoLogin"),
                    Timestamp: ReadTimestamp(block, "Timestamp")));
            }

            return result
                .OrderByDescending(user => user.MostRecent)
                .ThenByDescending(user => user.AutoLogin)
                .ThenByDescending(user => user.Timestamp)
                .ThenBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<SteamLoginUser>();
        }
    }

    public static bool IsValidSteamId64(string? value) =>
        value is { Length: 17 }
        && value.StartsWith("7656119", StringComparison.Ordinal)
        && value.All(char.IsAsciiDigit);

    private static string? ResolveInstallPath(string? steamInstallPath)
    {
        if (!string.IsNullOrWhiteSpace(steamInstallPath))
        {
            try
            {
                var full = Path.GetFullPath(steamInstallPath.Trim());
                if (Directory.Exists(full)) return full;
            }
            catch
            {
                // Fall through to SteamLocator.
            }
        }

        return SteamLocator.DetectSteamInstallPath();
    }

    private static IReadOnlyList<SteamLoginUser> ReadUserData(string root)
    {
        var userData = Path.Combine(root, "userdata");
        if (!Directory.Exists(userData)) return Array.Empty<SteamLoginUser>();

        var result = new List<SteamLoginUser>();
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(userData))
            {
                var name = Path.GetFileName(directory);
                if (!uint.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var accountId)
                    || accountId == 0)
                    continue;

                var steamId64 = (SteamId64Base + accountId).ToString(CultureInfo.InvariantCulture);
                if (!IsValidSteamId64(steamId64)) continue;

                long timestamp;
                try
                {
                    timestamp = new DateTimeOffset(Directory.GetLastWriteTimeUtc(directory)).ToUnixTimeSeconds();
                }
                catch
                {
                    timestamp = 0;
                }

                result.Add(new SteamLoginUser(steamId64, "", "", false, false, timestamp));
            }
        }
        catch
        {
            return Array.Empty<SteamLoginUser>();
        }

        return result
            .OrderByDescending(user => user.Timestamp)
            .ThenBy(user => user.SteamId64, StringComparer.Ordinal)
            .ToList();
    }

    private static string ReadString(Dictionary<string, object> block, string key) =>
        block.TryGetValue(key, out var value) && value is string text ? text : "";

    private static bool ReadBoolean(Dictionary<string, object> block, string key)
    {
        var text = ReadString(block, key);
        return text == "1" || bool.TryParse(text, out var value) && value;
    }

    private static long ReadTimestamp(Dictionary<string, object> block, string key) =>
        long.TryParse(ReadString(block, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private static Dictionary<string, object> ParseDocument(List<VdfToken> tokens)
    {
        var root = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        ParseEntries(tokens, ref index, root);
        return root;
    }

    private static void ParseEntries(List<VdfToken> tokens, ref int index, Dictionary<string, object> target)
    {
        while (index < tokens.Count)
        {
            var token = tokens[index];
            if (token.Kind == VdfTokenKind.CloseBrace)
            {
                index++;
                return;
            }

            if (token.Kind == VdfTokenKind.OpenBrace)
            {
                index++;
                continue;
            }

            var key = token.Value;
            index++;
            if (index >= tokens.Count) return;

            var next = tokens[index];
            if (next.Kind == VdfTokenKind.OpenBrace)
            {
                index++;
                var child = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                ParseEntries(tokens, ref index, child);
                target[key] = child;
            }
            else if (next.Kind == VdfTokenKind.String)
            {
                target[key] = next.Value;
                index++;
            }
            else
            {
                index++;
                return;
            }
        }
    }

    private static List<VdfToken> Tokenize(string content)
    {
        var tokens = new List<VdfToken>();
        var index = content.Length > 0 && content[0] == '\uFEFF' ? 1 : 0;

        while (index < content.Length)
        {
            var current = content[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '/' && index + 1 < content.Length && content[index + 1] == '/')
            {
                index += 2;
                while (index < content.Length && content[index] != '\n') index++;
                continue;
            }

            if (current == '{')
            {
                tokens.Add(new VdfToken(VdfTokenKind.OpenBrace, "{"));
                index++;
                continue;
            }

            if (current == '}')
            {
                tokens.Add(new VdfToken(VdfTokenKind.CloseBrace, "}"));
                index++;
                continue;
            }

            if (current == '"')
            {
                index++;
                var builder = new StringBuilder();
                while (index < content.Length)
                {
                    current = content[index];
                    if (current == '\\' && index + 1 < content.Length)
                    {
                        var escaped = content[index + 1];
                        builder.Append(escaped switch
                        {
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            _ => escaped,
                        });
                        index += 2;
                    }
                    else if (current == '"')
                    {
                        index++;
                        break;
                    }
                    else
                    {
                        builder.Append(current);
                        index++;
                    }
                }

                tokens.Add(new VdfToken(VdfTokenKind.String, builder.ToString()));
                continue;
            }

            var start = index;
            while (index < content.Length
                   && !char.IsWhiteSpace(content[index])
                   && content[index] is not '{' and not '}'
                   && !(content[index] == '/' && index + 1 < content.Length && content[index + 1] == '/'))
                index++;

            if (index == start)
            {
                index++;
                continue;
            }

            tokens.Add(new VdfToken(VdfTokenKind.String, content[start..index]));
        }

        return tokens;
    }

    private enum VdfTokenKind
    {
        String,
        OpenBrace,
        CloseBrace,
    }

    private readonly record struct VdfToken(VdfTokenKind Kind, string Value);
}