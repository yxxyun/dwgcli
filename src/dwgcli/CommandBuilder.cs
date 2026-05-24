using System.CommandLine;
using System.Text.Json;
using DwgCli.Core;

namespace DwgCli;

static partial class CommandBuilder
{
    public static RootCommand BuildRootCommand()
    {
        var jsonOption = new Option<bool>("--json") { Description = "Output as JSON (AI-friendly)" };

        var rootCommand = new RootCommand("""
            dwgcli: CLI for AutoCAD DWG/DXF files
            Read, query, and modify .dwg files using ACadSharp.
            """);
        rootCommand.Add(jsonOption);

        rootCommand.Add(BuildInfoCommand(jsonOption));
        rootCommand.Add(BuildGetCommand(jsonOption));
        rootCommand.Add(BuildDumpCommand(jsonOption));
        rootCommand.Add(BuildQueryCommand(jsonOption));
        rootCommand.Add(BuildSetCommand(jsonOption));
        rootCommand.Add(BuildAddCommand(jsonOption));
        rootCommand.Add(BuildRemoveCommand(jsonOption));
        rootCommand.Add(BuildPurgeCommand(jsonOption));
        rootCommand.Add(BuildBatchCommand(jsonOption));
        rootCommand.Add(BuildNewCommand(jsonOption));
        rootCommand.Add(BuildBlockCommand(jsonOption));
        rootCommand.Add(BuildStatsCommand(jsonOption));

        return rootCommand;
    }

    // ==================== Helper: SafeRun ====================

    internal static int SafeRun(Func<int> action, bool json = false)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            if (json)
                Console.WriteLine(OutputFormatter.WrapEnvelopeError(ex.Message));
            else
                Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    internal static int SafeRunWithHandler(
        string filePath, bool editable, bool json,
        Action<IDwgHandler> action)
    {
        return SafeRun(() =>
        {
            using var handler = DwgHandlerFactory.Open(filePath, editable);
            action(handler);
            return 0;
        }, json);
    }

    // ==================== Batch execution (shared by batch command and partials) ====================

    internal static int ExecuteInBatch(
        string filePath, bool json,
        Action<IDwgHandler> action)
    {
        using var handler = DwgHandlerFactory.Open(filePath, editable: true);
        action(handler);
        handler.Save();
        return 0;
    }

    private static readonly Dictionary<string, Func<IDwgHandler, BatchItem, bool, string>> BatchDispatch = new(StringComparer.OrdinalIgnoreCase)
    {
        ["info"] = (h, _, json) =>
        {
            var format = json ? OutputFormat.Json : OutputFormat.Text;
            return OutputFormatter.FormatNode(h.GetInfo(), format);
        },

        ["get"] = (h, item, json) =>
        {
            var format = json ? OutputFormat.Json : OutputFormat.Text;
            var path = item.Path ?? "/";
            var depth = item.Depth ?? 1;
            return OutputFormatter.FormatNode(h.Get(path, depth), format);
        },

        ["dump"] = (h, item, json) =>
        {
            var format = json ? OutputFormat.Json : OutputFormat.Text;
            var depth = item.Depth ?? 10;
            return OutputFormatter.FormatNode(h.Dump(depth), format);
        },

        ["query"] = (h, item, json) =>
        {
            var format = json ? OutputFormat.Json : OutputFormat.Text;
            var selector = item.Selector ?? "";
            var results = h.Query(selector);
            return OutputFormatter.FormatNodes(results, format);
        },

        ["set"] = (h, item, _) =>
        {
            var props = item.Props ?? new Dictionary<string, string>();
            if (string.IsNullOrEmpty(item.Path))
                throw new ArgumentException("'set' command requires 'path' field");
            var unsupported = h.Set(item.Path, props);
            var applied = props.Where(kv => !unsupported.Contains(kv.Key)).ToList();
            if (applied.Count == 0)
                return $"No properties applied to {item.Path}";
            return $"Updated {item.Path}: {string.Join(", ", applied.Select(kv => $"{kv.Key}={kv.Value}"))}";
        },

        ["add"] = (h, item, _) =>
        {
            var props = item.Props ?? new Dictionary<string, string>();
            if (string.IsNullOrEmpty(item.Path) && string.IsNullOrEmpty(item.Parent))
                throw new ArgumentException("'add' command requires 'path' or 'parent' field");
            var parentPath = item.Parent ?? item.Path ?? "/";
            var type = item.Type ?? throw new ArgumentException("'add' command requires 'type' field");
            var resultPath = h.Add(parentPath, type, props, item.Attrs);
            return $"Added {type} at {resultPath}";
        },

        ["remove"] = (h, item, _) =>
        {
            if (string.IsNullOrEmpty(item.Path))
                throw new ArgumentException("'remove' command requires 'path' field");
            var warning = h.Remove(item.Path);
            var msg = $"Removed {item.Path}";
            if (warning != null) msg += $"\n{warning}";
            return msg;
        },

        ["purge"] = (h, _, _) =>
        {
            var purged = h.Purge();
            return $"Purged {purged.Count} item(s): {string.Join(", ", purged)}";
        },
    };

    internal static string ExecuteBatchItem(IDwgHandler handler, BatchItem item, bool json)
    {
        var command = item.Command.ToLowerInvariant();
        if (BatchDispatch.TryGetValue(command, out var handlerFn))
            return handlerFn(handler, item, json);
        throw new InvalidOperationException($"Unknown command: '{item.Command}'. Valid: {string.Join(", ", BatchDispatch.Keys)}");
    }

    // ==================== Helper: parse --prop key=value pairs ====================

    internal static Dictionary<string, string> ParsePropsArray(string[]? props)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in props ?? Array.Empty<string>())
        {
            var eqIdx = prop.IndexOf('=');
            if (eqIdx <= 0)
                throw new ArgumentException($"Invalid --prop '{prop}'. Use key=value (e.g. --prop layer=0)");
            dict[prop[..eqIdx]] = prop[(eqIdx + 1)..];
        }
        return dict;
    }
}
