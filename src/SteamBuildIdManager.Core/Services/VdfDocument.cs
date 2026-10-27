using System.Text;

namespace SteamBuildIdManager.Core.Services;

/// <summary>
/// Minimal Valve KeyValues parser used for SteamCMD output, loginusers.vdf and appinfo dumps.
/// Duplicate keys keep the last value, matching how Steam itself reads these files.
/// </summary>
public static class VdfDocument
{
    public static IReadOnlyDictionary<string, VdfNode> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = Tokenize(text);
        var index = 0;
        return ParseEntries(tokens, ref index);
    }

    /// <summary>Parse a single KeyValues block and return its children, or null when the text is malformed.</summary>
    public static VdfNode? TryParseBlock(string text)
    {
        try
        {
            var root = Parse(text);
            return root.Count switch
            {
                0 => null,
                1 => root.Values.First(),
                _ => new VdfNode(new Dictionary<string, VdfNode>(root, StringComparer.OrdinalIgnoreCase)),
            };
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, VdfNode> ParseEntries(List<VdfToken> tokens, ref int index)
    {
        var target = new Dictionary<string, VdfNode>(StringComparer.OrdinalIgnoreCase);
        while (index < tokens.Count)
        {
            var token = tokens[index];
            if (token.Kind == VdfTokenKind.CloseBrace)
            {
                index++;
                return target;
            }

            if (token.Kind == VdfTokenKind.OpenBrace)
            {
                index++;
                continue;
            }

            var key = token.Value;
            index++;
            if (index >= tokens.Count) return target;

            var next = tokens[index];
            if (next.Kind == VdfTokenKind.OpenBrace)
            {
                index++;
                target[key] = new VdfNode(ParseEntries(tokens, ref index));
            }
            else if (next.Kind == VdfTokenKind.String)
            {
                index++;
                target[key] = VdfNode.FromValue(next.Value);
            }
            else
            {
                index++;
                return target;
            }
        }

        return target;
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
                        builder.Append(content[index + 1] switch
                        {
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            var other => other,
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
                   && content[index] is not '{' and not '}')
                index++;

            if (index == start) index++;
            else tokens.Add(new VdfToken(VdfTokenKind.String, content[start..index]));
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

/// <summary>One KeyValues entry: either a scalar value or a nested block.</summary>
public sealed class VdfNode
{
    private readonly Dictionary<string, VdfNode> _children;

    private VdfNode(Dictionary<string, VdfNode> children, string? value, bool isBlock)
    {
        _children = children;
        Value = value;
        IsBlock = isBlock;
    }

    public VdfNode(Dictionary<string, VdfNode> children) : this(children, null, true) { }

    public static VdfNode FromValue(string value) =>
        new(new Dictionary<string, VdfNode>(StringComparer.OrdinalIgnoreCase), value, false);

    public string? Value { get; }
    public bool IsBlock { get; }
    public IReadOnlyDictionary<string, VdfNode> Children => _children;

    public VdfNode? this[string key] => _children.TryGetValue(key, out var node) ? node : null;

    public string? Get(string key) => this[key]?.Value;

    public IEnumerable<KeyValuePair<string, VdfNode>> Entries => _children;

    /// <summary>Follow a path such as "config.launch.0" and return null when any segment is missing.</summary>
    public VdfNode? Path(params string[] segments)
    {
        var current = this;
        foreach (var segment in segments)
        {
            current = current[segment];
            if (current == null) return null;
        }
        return current;
    }
}