using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DwgCli.Core;

namespace DwgCli.Mcp;

/// <summary>
/// Helper for MCP server tools: opens DWG/DXF files, executes operations,
/// and formats JSON output.
/// </summary>
internal static class DwgHelper
{
    // ==================== File Open (delegates to DwgHandlerFactory) ====================

    /// <summary>
    /// Opens a DWG or DXF file and returns an IDwgHandler.
    /// Delegates to DwgHandlerFactory in the core library.
    /// </summary>
    public static IDwgHandler Open(string filePath, bool editable = false)
        => DwgHandlerFactory.Open(filePath, editable);

    // ==================== Execution helpers ====================

    /// <summary>
    /// Execute a read-only operation with automatic error wrapping.
    /// </summary>
    public static string ExecuteRead(string filePath, Func<IDwgHandler, string> action)
    {
        try
        {
            using var handler = Open(filePath, false);
            return action(handler);
        }
        catch (Exception ex)
        {
            return WrapEnvelopeError(ex.Message);
        }
    }

    /// <summary>
    /// Execute a write operation with automatic error wrapping and save.
    /// </summary>
    public static string ExecuteWrite(string filePath, Func<IDwgHandler, string> action)
    {
        try
        {
            using var handler = Open(filePath, true);
            var result = action(handler);
            handler.Save();
            return result;
        }
        catch (Exception ex)
        {
            return WrapEnvelopeError(ex.Message);
        }
    }

    /// <summary>
    /// Execute an operation with automatic error wrapping (backward compatible).
    /// </summary>
    public static string SafeRun(Func<IDwgHandler, string> action, string filePath, bool editable = false)
    {
        if (editable)
            return ExecuteWrite(filePath, action);
        return ExecuteRead(filePath, action);
    }

    // ==================== JSON Output Helpers ====================

    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string FormatNode(DwgNode node) =>
        JsonSerializer.Serialize(node, JsonOpts);

    public static string FormatNodes(List<DwgNode> nodes) =>
        JsonSerializer.Serialize(new { matches = nodes.Count, results = nodes }, JsonOpts);

    /// <summary>UI resource type hints for MCP client rendering.</summary>
    public static class UiType
    {
        public const string Table = "table";
        public const string List = "list";
        public const string Tree = "tree";
        public const string Text = "text";
        public const string Json = "json";
    }

    /// <summary>Wrap data with success envelope and optional UI metadata.</summary>
    public static string WrapEnvelope(string dataJson, string? uiType = null)
    {
        var envelope = new JsonObject { ["success"] = true };
        try { envelope["data"] = JsonNode.Parse(dataJson); }
        catch { envelope["data"] = dataJson; }
        AddMeta(envelope, uiType);
        return envelope.ToJsonString(JsonOpts);
    }

    /// <summary>Wrap a text message with success envelope and optional UI metadata.</summary>
    public static string WrapEnvelopeText(string message, string? uiType = null)
    {
        var envelope = new JsonObject
        {
            ["success"] = true,
            ["data"] = message,
            ["message"] = message
        };
        AddMeta(envelope, uiType);
        return envelope.ToJsonString(JsonOpts);
    }

    public static string WrapEnvelopeError(string message)
    {
        var envelope = new JsonObject
        {
            ["success"] = false,
            ["message"] = message
        };
        return envelope.ToJsonString(JsonOpts);
    }

    /// <summary>Add _meta.ui.resourceUri to the envelope for MCP Apps compatible clients.</summary>
    private static void AddMeta(JsonObject envelope, string? uiType)
    {
        if (string.IsNullOrEmpty(uiType)) return;

        envelope["_meta"] = new JsonObject
        {
            ["ui"] = new JsonObject
            {
                ["resourceUri"] = $"ui://dwgcli/{uiType}",
                ["type"] = uiType
            }
        };
    }

    // ==================== Property parsing ====================

    public static Dictionary<string, string> ParseProps(string[]? props)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in props ?? Array.Empty<string>())
        {
            var eqIdx = prop.IndexOf('=');
            if (eqIdx <= 0)
                throw new ArgumentException($"Invalid prop '{prop}'. Use key=value (e.g. layer=0)");
            dict[prop[..eqIdx]] = prop[(eqIdx + 1)..];
        }
        return dict;
    }
}
