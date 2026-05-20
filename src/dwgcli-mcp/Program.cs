using System.ComponentModel;
using DwgCli.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DwgCli.Mcp;

[McpServerToolType]
public sealed class DwgTools
{
    [McpServerTool, Description("Get DWG/DXF file metadata (version, author, layer count, entity count, etc.)")]
    public static string dwg_info(
        [Description("Path to the .dwg or .dxf file")] string filePath) =>
        DwgHelper.SafeRun(h => DwgHelper.WrapEnvelope(DwgHelper.FormatNode(h.GetInfo())), filePath);

    [McpServerTool, Description("Query entities/layers by selector. Examples: 'type=Line', 'layer=0', 'type=Circle layer=0', 'type=Insert xMin=13000 xMax=14000'")]
    public static string dwg_query(
        [Description("Path to the .dwg or .dxf file")] string filePath,
        [Description("Query selector. Supported: type=<EntityType>, layer=<name>, xMin/xMax/yMin/yMax for coordinate filtering")] string selector) =>
        DwgHelper.SafeRun(h => DwgHelper.WrapEnvelope(DwgHelper.FormatNodes(h.Query(selector))), filePath);

    [McpServerTool, Description("Get document structure by path. Paths: /, /info, /layers, /layer/<name>, /entities, /entity/<handle>, /blocks, /block/<name>, /layouts")]
    public static string dwg_get(
        [Description("Path to the .dwg or .dxf file")] string filePath,
        [Description("Document path (default: /)")] string path = "/",
        [Description("Depth of child nodes to include (0=no children, 1=direct children, etc.)")] int depth = 1) =>
        DwgHelper.SafeRun(h => DwgHelper.WrapEnvelope(DwgHelper.FormatNode(h.Get(path, depth))), filePath);

    [McpServerTool, Description("Get entity counts by type, layer, and block")]
    public static string dwg_stats(
        [Description("Path to the .dwg or .dxf file")] string filePath) =>
        DwgHelper.SafeRun(h => DwgHelper.WrapEnvelope(DwgHelper.FormatNode(h.Stats())), filePath);

    [McpServerTool, Description("Dump the full DWG document structure as a tree")]
    public static string dwg_dump(
        [Description("Path to the .dwg or .dxf file")] string filePath,
        [Description("Depth of tree output (default: 10)")] int depth = 10) =>
        DwgHelper.SafeRun(h => DwgHelper.WrapEnvelope(DwgHelper.FormatNode(h.Dump(depth))), filePath);

    [McpServerTool, Description("Modify properties on a layer, entity, or document info. Path examples: /layer/Walls, /entity/<handle>, / (for summary info)")]
    public static string dwg_set(
        [Description("Path to the .dwg or .dxf file")] string filePath,
        [Description("Path to the element. Examples: /layer/Walls, /entity/<handle>, /")] string path,
        [Description("Properties to set as key=value pairs. Examples: color=red, lineWeight=0.5")] string[] prop) =>
        DwgHelper.SafeRun(h =>
        {
            var properties = DwgHelper.ParseProps(prop);
            if (properties.Count == 0)
                return DwgHelper.WrapEnvelopeError("No properties specified. Use prop=key=value");
            var unsupported = h.Set(path, properties);
            var applied = properties.Where(kv => !unsupported.Contains(kv.Key)).ToList();
            var message = applied.Count > 0
                ? $"Updated {path}: {string.Join(", ", applied.Select(kv => $"{kv.Key}={kv.Value}"))}"
                : $"No properties applied to {path}";
            if (applied.Count == 0 && unsupported.Count > 0)
                return DwgHelper.WrapEnvelopeError($"{message}. Unsupported: {string.Join(", ", unsupported)}");
            return DwgHelper.WrapEnvelopeText(message);
        }, filePath, editable: true);

    [McpServerTool, Description("Add an entity or layer. Parent: /entities (add entity) or /layers (add layer). Types: line, circle, arc, text, mtext, insert, layer")]
    public static string dwg_add(
        [Description("Path to the .dwg or .dxf file")] string filePath,
        [Description("Parent path. /entities (add entity) or /layers (add layer)")] string parent,
        [Description("Type: line, circle, arc, text, mtext, insert, layer")] string type,
        [Description("Properties as key=value. Entity: x1=0 y1=0 x2=100 y2=100. Layer: name=my-layer color=red")] string[]? prop = null,
        [Description("Attributes as TAG=VALUE for insert block references")] string[]? attr = null) =>
        DwgHelper.SafeRun(h =>
        {
            var properties = DwgHelper.ParseProps(prop);
            var attributes = attr != null && attr.Length > 0 ? DwgHelper.ParseProps(attr) : null;
            var resultPath = h.Add(parent, type, properties, attributes);
            return DwgHelper.WrapEnvelopeText($"Added {type} at {resultPath}");
        }, filePath, editable: true);

    [McpServerTool, Description("Remove an entity or layer by path. Examples: /entity/<handle>, /layer/<name>")]
    public static string dwg_remove(
        [Description("Path to the .dwg or .dxf file")] string filePath,
        [Description("Path to remove. Examples: /entity/<handle>, /layer/<name>")] string path) =>
        DwgHelper.SafeRun(h =>
        {
            var warning = h.Remove(path);
            var msg = warning != null ? $"Removed {path}. Warning: {warning}" : $"Removed {path}";
            return DwgHelper.WrapEnvelopeText(msg);
        }, filePath, editable: true);

    [McpServerTool, Description("Remove unused layers, blocks, and linetypes from the drawing")]
    public static string dwg_purge(
        [Description("Path to the .dwg or .dxf file")] string filePath) =>
        DwgHelper.SafeRun(h =>
        {
            var purged = h.Purge();
            var msg = $"Purged {purged.Count} item(s): {string.Join(", ", purged)}";
            return DwgHelper.WrapEnvelopeText(msg);
        }, filePath, editable: true);
}

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Suppress all console logging so it doesn't interfere with MCP JSON-RPC on stdout
        builder.Logging.SetMinimumLevel(LogLevel.None);

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<DwgTools>();

        await builder.Build().RunAsync();
    }
}
