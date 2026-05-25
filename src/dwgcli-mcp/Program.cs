using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    // ==================== Unified Tools (dispatch-based) ====================

    private static readonly Dictionary<string, Func<JsonObject, IDwgHandler, JsonObject>> _readDispatch = new()
    {
        ["info"] = (spec, h) => new JsonObject
        {
            ["success"] = true,
            ["data"] = JsonNode.Parse(DwgHelper.FormatNode(h.GetInfo()))
        },
        ["get"] = (spec, h) =>
        {
            var path = spec.TryGetPropertyValue("path", out var pn) ? pn?.GetValue<string>() ?? "/" : "/";
            var depth = spec.TryGetPropertyValue("depth", out var dn) ? dn?.GetValue<int>() ?? 1 : 1;
            return new JsonObject
            {
                ["success"] = true,
                ["data"] = JsonNode.Parse(DwgHelper.FormatNode(h.Get(path, depth)))
            };
        },
        ["query"] = (spec, h) =>
        {
            var selector = spec["selector"]?.GetValue<string>() ?? "";
            return new JsonObject
            {
                ["success"] = true,
                ["data"] = JsonNode.Parse(DwgHelper.FormatNodes(h.Query(selector)))
            };
        },
        ["stats"] = (spec, h) => new JsonObject
        {
            ["success"] = true,
            ["data"] = JsonNode.Parse(DwgHelper.FormatNode(h.Stats()))
        },
        ["dump"] = (spec, h) =>
        {
            var depth = spec.TryGetPropertyValue("depth", out var dn) ? dn?.GetValue<int>() ?? 10 : 10;
            return new JsonObject
            {
                ["success"] = true,
                ["data"] = JsonNode.Parse(DwgHelper.FormatNode(h.Dump(depth)))
            };
        },
    };

    private static readonly Dictionary<string, Func<JsonObject, IDwgHandler, JsonObject>> _writeDispatch = new()
    {
        ["set"] = (spec, h) =>
        {
            var path = spec["path"]?.GetValue<string>() ?? "";
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (spec.TryGetPropertyValue("prop", out var propNode) && propNode is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    var kv = item?.GetValue<string>() ?? "";
                    var eqIdx = kv.IndexOf('=');
                    if (eqIdx > 0)
                        props[kv[..eqIdx]] = kv[(eqIdx + 1)..];
                }
            }
            var unsupported = h.Set(path, props);
            var applied = props.Where(kv => !unsupported.Contains(kv.Key)).ToList();
            var message = applied.Count > 0
                ? $"Updated {path}: {string.Join(", ", applied.Select(kv => $"{kv.Key}={kv.Value}"))}"
                : $"No properties applied to {path}";
            var success = applied.Count > 0;
            return new JsonObject
            {
                ["success"] = success,
                ["detail"] = message
            };
        },
        ["add"] = (spec, h) =>
        {
            var parent = spec["parent"]?.GetValue<string>() ?? "";
            var type = spec["type"]?.GetValue<string>() ?? "";
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (spec.TryGetPropertyValue("prop", out var propNode) && propNode is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    var kv = item?.GetValue<string>() ?? "";
                    var eqIdx = kv.IndexOf('=');
                    if (eqIdx > 0)
                        props[kv[..eqIdx]] = kv[(eqIdx + 1)..];
                }
            }
            Dictionary<string, string>? attrs = null;
            if (spec.TryGetPropertyValue("attr", out var attrNode) && attrNode is JsonArray attrArr)
            {
                attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in attrArr)
                {
                    var kv = item?.GetValue<string>() ?? "";
                    var eqIdx = kv.IndexOf('=');
                    if (eqIdx > 0)
                        attrs[kv[..eqIdx]] = kv[(eqIdx + 1)..];
                }
            }
            var resultPath = h.Add(parent, type, props, attrs);
            return new JsonObject
            {
                ["success"] = true,
                ["detail"] = $"Added {type} at {resultPath}"
            };
        },
        ["remove"] = (spec, h) =>
        {
            var path = spec["path"]?.GetValue<string>() ?? "";
            var warning = h.Remove(path);
            return new JsonObject
            {
                ["success"] = true,
                ["detail"] = warning != null ? $"Removed {path}. Warning: {warning}" : $"Removed {path}"
            };
        },
        ["purge"] = (spec, h) =>
        {
            var purged = h.Purge();
            return new JsonObject
            {
                ["success"] = true,
                ["detail"] = $"Purged {purged.Count} item(s): {string.Join(", ", purged)}"
            };
        },
    };

    [McpServerTool, Description("Query a DWG/DXF file. Accepts JSON operations array. Actions: info, get, query, stats, dump")]
    public static string dwg_query(
        [Description("Path to the .dwg or .dxf file")] string filePath,
        [Description("JSON operations array. Example: [{\"action\":\"info\"},{\"action\":\"stats\"}]")] string operations) =>
        ExecuteUnifiedRead(filePath, operations, "dwg_query", _readDispatch,
            ["info", "get", "query", "stats", "dump"]);

    [McpServerTool, Description("Edit a DWG/DXF file. Accepts JSON operations array. Actions: set, add, remove, purge")]
    public static string dwg_edit(
        [Description("Path to the .dwg or .dxf file")] string filePath,
        [Description("JSON operations array. Example: [{\"action\":\"set\",\"path\":\"/layer/0\",\"prop\":[\"color=red\"]}]")] string operations) =>
        ExecuteUnifiedWrite(filePath, operations, "dwg_edit", _writeDispatch,
            ["set", "add", "remove", "purge"]);

    // ==================== Unified Execution Engine ====================

    private static string ExecuteUnifiedRead(
        string filePath, string operations, string toolName,
        Dictionary<string, Func<JsonObject, IDwgHandler, JsonObject>> dispatch,
        string[] validActions)
    {
        try
        {
            var ops = JsonSerializer.Deserialize<List<JsonObject>>(operations);
            if (ops == null || ops.Count == 0)
                return DwgHelper.WrapEnvelopeError($"'operations' must be a non-empty JSON array for {toolName}");

            return DwgHelper.ExecuteRead(filePath, h =>
            {
                var results = new List<JsonObject>();
                foreach (var (specRaw, i) in ops.Select((s, i) => (s, i)))
                {
                    var spec = (JsonObject)specRaw;
                    var action = spec["action"]?.GetValue<string>() ?? "";
                    var result = new JsonObject { ["index"] = i, ["action"] = action };

                    if (string.IsNullOrEmpty(action))
                    {
                        result["success"] = false;
                        result["error"] = "Missing 'action' field";
                    }
                    else if (!dispatch.TryGetValue(action.ToLowerInvariant(), out var handler))
                    {
                        result["success"] = false;
                        result["error"] = $"Unknown action '{action}'. Supported: {string.Join(", ", validActions)}";
                    }
                    else
                    {
                        try
                        {
                            var r = handler(spec, h);
                            foreach (var (k, v) in r)
                                result[k] = v?.DeepClone();
                        }
                        catch (Exception ex)
                        {
                            result["success"] = false;
                            result["error"] = ex.Message;
                        }
                    }
                    results.Add(result);
                }

                var envelope = new JsonObject
                {
                    ["success"] = results.All(r => r["success"]?.GetValue<bool>() == true),
                    ["total"] = results.Count,
                    ["succeeded"] = results.Count(r => r["success"]?.GetValue<bool>() == true),
                    ["results"] = new JsonArray(results.Select(r => JsonNode.Parse(r.ToJsonString())).Select(n => n).ToArray()),
                    ["_meta"] = new JsonObject
                    {
                        ["ui"] = new JsonObject
                        {
                            ["resourceUri"] = "ui://dwgcli/operations",
                            ["type"] = "list"
                        }
                    }
                };
                return envelope.ToJsonString(DwgHelper.JsonOpts);
            });
        }
        catch (JsonException ex)
        {
            return DwgHelper.WrapEnvelopeError($"Invalid JSON in operations: {ex.Message}");
        }
    }

    private static string ExecuteUnifiedWrite(
        string filePath, string operations, string toolName,
        Dictionary<string, Func<JsonObject, IDwgHandler, JsonObject>> dispatch,
        string[] validActions)
    {
        try
        {
            var ops = JsonSerializer.Deserialize<List<JsonObject>>(operations);
            if (ops == null || ops.Count == 0)
                return DwgHelper.WrapEnvelopeError($"'operations' must be a non-empty JSON array for {toolName}");

            return DwgHelper.ExecuteWrite(filePath, h =>
            {
                var results = new List<JsonObject>();
                foreach (var (specRaw, i) in ops.Select((s, i) => (s, i)))
                {
                    var spec = (JsonObject)specRaw;
                    var action = spec["action"]?.GetValue<string>() ?? "";
                    var result = new JsonObject { ["index"] = i, ["action"] = action };

                    if (string.IsNullOrEmpty(action))
                    {
                        result["success"] = false;
                        result["error"] = "Missing 'action' field";
                    }
                    else if (!dispatch.TryGetValue(action.ToLowerInvariant(), out var handler))
                    {
                        result["success"] = false;
                        result["error"] = $"Unknown action '{action}'. Supported: {string.Join(", ", validActions)}";
                    }
                    else
                    {
                        try
                        {
                            var r = handler(spec, h);
                            foreach (var (k, v) in r)
                                result[k] = v?.DeepClone();
                        }
                        catch (Exception ex)
                        {
                            result["success"] = false;
                            result["error"] = ex.Message;
                        }
                    }
                    results.Add(result);
                }

                var envelope = new JsonObject
                {
                    ["success"] = results.All(r => r["success"]?.GetValue<bool>() == true),
                    ["total"] = results.Count,
                    ["succeeded"] = results.Count(r => r["success"]?.GetValue<bool>() == true),
                    ["results"] = new JsonArray(results.Select(r => JsonNode.Parse(r.ToJsonString())).Select(n => n).ToArray()),
                    ["_meta"] = new JsonObject
                    {
                        ["ui"] = new JsonObject
                        {
                            ["resourceUri"] = "ui://dwgcli/operations",
                            ["type"] = "list"
                        }
                    }
                };
                return envelope.ToJsonString(DwgHelper.JsonOpts);
            });
        }
        catch (JsonException ex)
        {
            return DwgHelper.WrapEnvelopeError($"Invalid JSON in operations: {ex.Message}");
        }
    }

    // ==================== Legacy Tools (backward compatible wrappers) ====================

    [Obsolete("Use dwg_query with {'action':'info'} instead")]
    [McpServerTool, Description("Get DWG/DXF file metadata (version, author, layer count, entity count, etc.)")]
    public static string dwg_info(
        [Description("Path to the .dwg or .dxf file")] string filePath) =>
        dwg_query(filePath, """[{"action":"info"}]""");

    [Obsolete("Use dwg_query with {'action':'query'} instead")]
    [McpServerTool, Description("Query entities/layers by selector. Supports: type=, layer=, text= (fuzzy text match), limit=, count=true, xMin/xMax/yMin/yMax. Examples: 'type=Line', 'layer=Walls', 'type=MText text=PAGE1', 'type=Insert limit=5 count=true'")]
    public static string dwg_query_legacy(
        [Description("Path to the .dwg or .dxf file")] string filePath,
        [Description("Query selector.")] string selector) =>
        dwg_query(filePath, $$"""[{"action":"query","selector":"{{selector}}"}]""");

    [Obsolete("Use dwg_query with {'action':'get'} instead")]
    [McpServerTool, Description("Get document structure by path. Paths: /, /info, /layers, /layer/<name>, /entities, /entity/<handle>, /blocks, /block/<name>, /layouts")]
    public static string dwg_get(
        [Description("Path to the .dwg or .dxf file")] string filePath,
        [Description("Document path (default: /)")] string path = "/",
        [Description("Depth of child nodes (0=no children)")] int depth = 1) =>
        dwg_query(filePath, $$"""[{"action":"get","path":"{{path}}","depth":{{depth}}}]""");

    [Obsolete("Use dwg_query with {'action':'stats'} instead")]
    [McpServerTool, Description("Get entity counts by type, layer, and block")]
    public static string dwg_stats(
        [Description("Path to the .dwg or .dxf file")] string filePath) =>
        dwg_query(filePath, """[{"action":"stats"}]""");

    [Obsolete("Use dwg_query with {'action':'dump'} instead")]
    [McpServerTool, Description("Dump the full DWG document structure as a tree")]
    public static string dwg_dump(
        [Description("Path to the .dwg or .dxf file")] string filePath,
        [Description("Depth of tree output (default: 10)")] int depth = 10) =>
        dwg_query(filePath, $$"""[{"action":"dump","depth":{{depth}}}]""");

    [Obsolete("Use dwg_edit with {'action':'set'} instead")]
    [McpServerTool, Description("Modify properties on a layer, entity, or document info.")]
    public static string dwg_set(
        [Description("Path to the .dwg or .dxf file")] string filePath,
        [Description("Path to the element.") ] string path,
        [Description("Properties as key=value pairs.")] string[] prop) =>
        dwg_edit(filePath, JsonSerializer.Serialize(new[]
        {
            new JsonObject
            {
                ["action"] = "set",
                ["path"] = path,
                ["prop"] = new JsonArray(prop.Select(p => JsonValue.Create(p)).ToArray())
            }
        }, DwgHelper.JsonOpts));

    [Obsolete("Use dwg_edit with {'action':'add'} instead")]
    [McpServerTool, Description("Add an entity or layer.")]
    public static string dwg_add(
        [Description("Path to the .dwg or .dxf file")] string filePath,
        [Description("Parent path.")] string parent,
        [Description("Type: line, circle, arc, text, mtext, insert, layer")] string type,
        [Description("Properties as key=value.")] string[]? prop = null,
        [Description("Attributes as TAG=VALUE for insert.")] string[]? attr = null) =>
        dwg_edit(filePath, JsonSerializer.Serialize(new[]
        {
            new JsonObject
            {
                ["action"] = "add",
                ["parent"] = parent,
                ["type"] = type,
                ["prop"] = prop != null ? new JsonArray(prop.Select(p => JsonValue.Create(p)).ToArray()) : null,
                ["attr"] = attr != null ? new JsonArray(attr.Select(a => JsonValue.Create(a)).ToArray()) : null
            }
        }, DwgHelper.JsonOpts));

    [Obsolete("Use dwg_edit with {'action':'remove'} instead")]
    [McpServerTool, Description("Remove an entity or layer by path.")]
    public static string dwg_remove(
        [Description("Path to the .dwg or .dxf file")] string filePath,
        [Description("Path to remove.")] string path) =>
        dwg_edit(filePath, $$"""[{"action":"remove","path":"{{path}}"}]""");

    [McpServerTool, Description("Execute shorthand format commands. Pipe-separated lines: command|arg1|arg2|key=value|...")]
    public static string dwg_shorthand(
        [Description("Path to the .dwg or .dxf file")] string filePath,
        [Description("Shorthand commands, one per line. Example:\ninfo|\nadd|/entities|line|x1=0|y1=0|x2=100|y2=100|color=red\nset|/author|value=John")] string commands)
    {
        try
        {
            var items = ShorthandParser.Parse(commands);
            var results = new List<JsonObject>();

            foreach (var (specRaw, i) in items.Select((s, i) => (s, i)))
            {
                var spec = new JsonObject();
                var action = specRaw.Command.ToLowerInvariant();

                // Convert BatchItem to the same format dwg_edit dispatch expects
                spec["action"] = action;
                switch (action)
                {
                    case "set":
                        spec["path"] = specRaw.Path ?? "";
                        if (specRaw.Props != null)
                            spec["prop"] = new JsonArray(specRaw.Props.Select(kv => JsonValue.Create($"{kv.Key}={kv.Value}")).ToArray());
                        break;
                    case "add":
                        spec["parent"] = specRaw.Parent ?? specRaw.Path ?? "/entities";
                        spec["type"] = specRaw.Type ?? "";
                        if (specRaw.Props != null)
                            spec["prop"] = new JsonArray(specRaw.Props.Select(kv => JsonValue.Create($"{kv.Key}={kv.Value}")).ToArray());
                        if (specRaw.Attrs != null)
                            spec["attr"] = new JsonArray(specRaw.Attrs.Select(kv => JsonValue.Create($"{kv.Key}={kv.Value}")).ToArray());
                        break;
                    case "remove":
                        spec["path"] = specRaw.Path ?? "";
                        break;
                    case "purge":
                        break;
                }

                results.Add(spec);
            }

            var operationsJson = new JsonArray(results.Select(r => JsonNode.Parse(r.ToJsonString())).Select(n => n).ToArray());
            return dwg_edit(filePath, operationsJson.ToJsonString(DwgHelper.JsonOpts));
        }
        catch (Exception ex)
        {
            return DwgHelper.WrapEnvelopeError($"Shorthand parse error: {ex.Message}");
        }
    }

    [Obsolete("Use dwg_edit with {'action':'purge'} instead")]
    [McpServerTool, Description("Remove unused layers, blocks, and linetypes from the drawing")]
    public static string dwg_purge(
        [Description("Path to the .dwg or .dxf file")] string filePath) =>
        dwg_edit(filePath, """[{"action":"purge"}]""");

    // ==================== CAD Automation Tool (optional, requires AutoCAD) ====================

    /// <summary>
    /// Dispatch map for CAD automation operations.
    /// Uses shared DwgComAutomation instance (MCP 会话内复用)。
    /// </summary>
    private static readonly Dictionary<string, Func<JsonObject, string>> _cadDispatch = new()
    {
        ["is_available"] = static _ =>
        {
            var available = DwgComAutomation.IsAutoCADAvailable();
            return new JsonObject
            {
                ["success"] = true,
                ["available"] = available,
                ["detail"] = available ? "AutoCAD detected" : "AutoCAD not found"
            }.ToJsonString(DwgHelper.JsonOpts);
        },

        ["screenshot"] = static spec =>
        {
            var cad = DwgComAutomation.GetShared(visible: false);
            if (!cad.IsConnected)
                return DwgHelper.WrapEnvelopeError("Cannot connect to AutoCAD. Is it installed and running?");

            var pngBase64 = cad.Screenshot();
            if (pngBase64 == null)
                return DwgHelper.WrapEnvelopeError("Screenshot failed. Make sure AutoCAD window is visible.");

            return new JsonObject
            {
                ["success"] = true,
                ["data"] = pngBase64,
                ["mimeType"] = "image/png",
                ["_meta"] = new JsonObject
                {
                    ["ui"] = new JsonObject
                    {
                        ["type"] = "image",
                        ["resourceUri"] = "data:image/png;base64," + pngBase64
                    }
                }
            }.ToJsonString(DwgHelper.JsonOpts);
        },

        ["export_png"] = static spec =>
        {
            var dwgPath = GetString(spec, "filePath");
            var output = GetString(spec, "output") ?? Path.ChangeExtension(dwgPath, ".png");

            if (string.IsNullOrEmpty(dwgPath))
                return DwgHelper.WrapEnvelopeError("'filePath' is required");
            if (!File.Exists(dwgPath))
                return DwgHelper.WrapEnvelopeError($"File not found: {dwgPath}");

            var cad = DwgComAutomation.GetShared(visible: false);
            if (!cad.IsConnected)
                return DwgHelper.WrapEnvelopeError("Cannot connect to AutoCAD");

            var ok = cad.ExportPng(dwgPath, output);
            if (!ok)
                return DwgHelper.WrapEnvelopeError("PNG export failed");

            return DwgHelper.WrapEnvelopeText($"Exported PNG: {output}");
        },

        ["plot_pdf"] = static spec =>
        {
            var dwgPath = GetString(spec, "filePath");
            var output = GetString(spec, "output") ?? Path.ChangeExtension(dwgPath, ".pdf");
            var paperSize = GetString(spec, "paperSize") ?? "A1";
            var plotter = GetString(spec, "plotter") ?? "DWG To PDF.pc3";
            var xMin = GetDouble(spec, "xMin") ?? 0;
            var yMin = GetDouble(spec, "yMin") ?? 0;
            var xMax = GetDouble(spec, "xMax") ?? 1000;
            var yMax = GetDouble(spec, "yMax") ?? 1000;

            if (string.IsNullOrEmpty(dwgPath))
                return DwgHelper.WrapEnvelopeError("'filePath' is required");
            if (!File.Exists(dwgPath))
                return DwgHelper.WrapEnvelopeError($"File not found: {dwgPath}");

            var cad = DwgComAutomation.GetShared(visible: false);
            if (!cad.IsConnected)
                return DwgHelper.WrapEnvelopeError("Cannot connect to AutoCAD");

            var ok = cad.PlotWindowToPdf(dwgPath, xMin, yMin, xMax, yMax, output, paperSize, plotter);
            if (!ok)
                return DwgHelper.WrapEnvelopeError("PDF plot failed");

            return DwgHelper.WrapEnvelopeText($"Plotted PDF: {output}");
        },

        ["open"] = static spec =>
        {
            var dwgPath = GetString(spec, "filePath");
            if (string.IsNullOrEmpty(dwgPath))
                return DwgHelper.WrapEnvelopeError("'filePath' is required");
            if (!File.Exists(dwgPath))
                return DwgHelper.WrapEnvelopeError($"File not found: {dwgPath}");

            var cad = DwgComAutomation.GetShared(visible: true);
            if (!cad.IsConnected)
                return DwgHelper.WrapEnvelopeError("Cannot connect to AutoCAD");

            var ok = cad.OpenInCad(dwgPath);
            return ok
                ? DwgHelper.WrapEnvelopeText($"Opened {dwgPath} in AutoCAD")
                : DwgHelper.WrapEnvelopeError("Failed to open file in AutoCAD");
        },

        ["zoom_extents"] = static spec =>
        {
            var cad = DwgComAutomation.GetShared(visible: false);
            if (!cad.IsConnected)
                return DwgHelper.WrapEnvelopeError("Cannot connect to AutoCAD");

            return cad.ZoomExtents()
                ? DwgHelper.WrapEnvelopeText("Zoomed to extents")
                : DwgHelper.WrapEnvelopeError("Zoom extents failed");
        },
    };

    private static string GetString(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var node) && node != null
            ? node.GetValue<string>() ?? ""
            : "";
    }

    private static double? GetDouble(JsonObject obj, string key)
    {
        if (obj.TryGetPropertyValue(key, out var node) && node != null)
        {
            if (node.GetValueKind() == System.Text.Json.JsonValueKind.Number)
                return node.GetValue<double>();
            if (double.TryParse(node.GetValue<string>(), out var val))
                return val;
        }
        return null;
    }

    [McpServerTool, Description("CAD Automation — requires AutoCAD installed. Actions: is_available, screenshot, export_png, plot_pdf, open, zoom_extents")]
    public static string dwg_cad(
        [Description("Action to perform")] string action,
        [Description("JSON parameters for the action")] string parameters = "{}")
    {
        if (string.IsNullOrEmpty(action))
            return DwgHelper.WrapEnvelopeError("'action' is required. Supported: is_available, screenshot, export_png, plot_pdf, open, zoom_extents");

        try
        {
            var spec = JsonSerializer.Deserialize<JsonObject>(parameters) ?? new JsonObject();

            if (!_cadDispatch.TryGetValue(action.ToLowerInvariant(), out var handler))
                return DwgHelper.WrapEnvelopeError(
                    $"Unknown action '{action}'. Supported: {string.Join(", ", _cadDispatch.Keys)}");

            return handler(spec);
        }
        catch (JsonException ex)
        {
            return DwgHelper.WrapEnvelopeError($"Invalid JSON in parameters: {ex.Message}");
        }
        catch (Exception ex)
        {
            return DwgHelper.WrapEnvelopeError($"CAD automation error: {ex.Message}");
        }
    }
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
