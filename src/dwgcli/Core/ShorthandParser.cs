using System.Globalization;

namespace DwgCli.Core;

/// <summary>
/// Parses compact pipe-separated shorthand format into BatchItem objects.
/// 
/// Format: command|arg1|arg2|key=value|...
/// - First token is always the command name
/// - Positional args depend on command type
/// - key=value tokens are parsed as props/attributes
/// - Comments (// or #) and blank lines are ignored
/// 
/// Example line: set|/layer/Walls|color=red
///              add|/entities|line|x1=0|y1=0|x2=100|y2=100|color=red|layer=Walls
/// </summary>
public static class ShorthandParser
{
    /// <summary>
    /// Parse a multi-line shorthand string into a list of BatchItems.
    /// </summary>
    public static List<BatchItem> Parse(string shorthandText)
    {
        var items = new List<BatchItem>();
        var lines = shorthandText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Skip empty lines, comments
            if (line.Length == 0) continue;
            if (line.StartsWith("//") || line.StartsWith("#")) continue;

            items.Add(ParseLine(line));
        }

        return items;
    }

    /// <summary>
    /// Parse a single shorthand line into a BatchItem.
    /// Format: command|param1|param2|key=value|...
    /// </summary>
    public static BatchItem ParseLine(string line)
    {
        var tokens = SplitLine(line);
        if (tokens.Count == 0)
            throw new ArgumentException("Empty shorthand line");

        var command = tokens[0].ToLowerInvariant();
        var args = tokens.Skip(1).ToList();

        // Separate positional args from key=value props
        var positionalArgs = new List<string>();
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var arg in args)
        {
            var eqIdx = arg.IndexOf('=');
            if (eqIdx > 0)
            {
                var key = arg[..eqIdx];
                var val = arg[(eqIdx + 1)..];
                // Treat "attr:*" keys as attributes for insert
                if (key.StartsWith("attr:", StringComparison.OrdinalIgnoreCase) || key.StartsWith("attrib:", StringComparison.OrdinalIgnoreCase))
                    attrs[key[(key.IndexOf(':') + 1)..]] = val;
                else
                    props[key] = val;
            }
            else
            {
                positionalArgs.Add(arg);
            }
        }

        // Build BatchItem based on command type
        switch (command)
        {
            case "info":
                return new BatchItem { Command = "info" };

            case "get":
                return new BatchItem
                {
                    Command = "get",
                    Path = positionalArgs.Count > 0 ? positionalArgs[0] : "/",
                    Depth = positionalArgs.Count > 1 && int.TryParse(positionalArgs[1], out var d) ? d : 1
                };

            case "query":
            case "search":
                var selector = string.Join(" ", positionalArgs);
                foreach (var (k, v) in props)
                    selector += $" {k}={v}";
                return new BatchItem
                {
                    Command = "query",
                    Selector = selector.Trim()
                };

            case "stats":
                return new BatchItem { Command = "stats" };

            case "dump":
                return new BatchItem
                {
                    Command = "dump",
                    Depth = positionalArgs.Count > 0 && int.TryParse(positionalArgs[0], out var dd) ? dd : 10
                };

            case "set":
                var path = positionalArgs.Count > 0 ? positionalArgs[0] : "";
                if (string.IsNullOrEmpty(path))
                    throw new ArgumentException("'set' shorthand requires a path (e.g. set|/layer/Walls|color=red)");
                return new BatchItem
                {
                    Command = "set",
                    Path = path,
                    Props = props.Count > 0 ? props : null
                };

            case "add":
            case "create":
                var parent = positionalArgs.Count > 0 ? positionalArgs[0] : "/entities";
                var type = positionalArgs.Count > 1 ? positionalArgs[1] : "";
                if (string.IsNullOrEmpty(type))
                    throw new ArgumentException("'add' shorthand requires a type (e.g. add|/entities|line|...)");
                return new BatchItem
                {
                    Command = "add",
                    Path = parent,
                    Parent = parent,
                    Type = type,
                    Props = props.Count > 0 ? props : null,
                    Attrs = attrs.Count > 0 ? attrs : null
                };

            case "remove":
            case "delete":
                var removePath = positionalArgs.Count > 0 ? positionalArgs[0] : "";
                if (string.IsNullOrEmpty(removePath))
                    throw new ArgumentException("'remove' shorthand requires a path (e.g. remove|/entity/ABC)");
                return new BatchItem
                {
                    Command = "remove",
                    Path = removePath
                };

            case "purge":
                return new BatchItem { Command = "purge" };

            default:
                throw new ArgumentException($"Unknown command in shorthand: '{command}'. Known: info, get, query, stats, dump, set, add, remove, purge");
        }
    }

    /// <summary>
    /// Split a shorthand line by pipe (|) while respecting quoted strings.
    /// </summary>
    private static List<string> SplitLine(string line)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuote = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"' || ch == '\'')
            {
                inQuote = !inQuote;
            }
            else if (ch == '|' && !inQuote)
            {
                tokens.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        tokens.Add(current.ToString().Trim());
        return tokens;
    }
}
