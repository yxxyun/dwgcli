using ACadSharp;
using ACadSharp.IO;
using DwgCli.Core;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DwgCli.Mcp;

/// <summary>
/// Opens DWG/DXF files and returns an IDwgHandler.
/// Duplicated from dwgcli/Core/DwgHandlerFactory.cs (internal) to avoid modifying existing code.
/// </summary>
internal static class DwgHelper
{
    public static IDwgHandler Open(string filePath, bool editable = false)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".dwg" => OpenDwg(filePath, editable),
            ".dxf" => OpenDxf(filePath, editable),
            _ => throw new NotSupportedException($"Unsupported file type: {ext}. Supported: .dwg, .dxf"),
        };
    }

    private static IDwgHandler OpenDwg(string filePath, bool editable)
    {
        var notifications = new List<string>();
        CadDocument doc;
        try
        {
            doc = DwgReader.Read(filePath, (_, e) =>
            {
                if (e.NotificationType is NotificationType.Warning or NotificationType.Error)
                    notifications.Add($"[{e.NotificationType}] {e.Message}");
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read DWG file: {ex.Message}", ex);
        }
        return new DwgHandler(filePath, doc, editable, notifications);
    }

    private static IDwgHandler OpenDxf(string filePath, bool editable)
    {
        var notifications = new List<string>();
        CadDocument doc;
        try
        {
            doc = DxfReader.Read(filePath, (_, e) =>
            {
                if (e.NotificationType is NotificationType.Warning or NotificationType.Error)
                    notifications.Add($"[{e.NotificationType}] {e.Message}");
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read DXF file: {ex.Message}", ex);
        }
        return new DwgHandler(filePath, doc, editable, notifications);
    }

    // ==================== JSON Output Helpers ====================

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string FormatNode(DwgNode node) =>
        JsonSerializer.Serialize(node, JsonOpts);

    public static string FormatNodes(List<DwgNode> nodes) =>
        JsonSerializer.Serialize(new { matches = nodes.Count, results = nodes }, JsonOpts);

    public static string WrapEnvelope(string dataJson)
    {
        var envelope = new JsonObject { ["success"] = true };
        try { envelope["data"] = JsonNode.Parse(dataJson); }
        catch { envelope["data"] = dataJson; }
        return envelope.ToJsonString(JsonOpts);
    }

    public static string WrapEnvelopeText(string message)
    {
        var envelope = new JsonObject
        {
            ["success"] = true,
            ["data"] = message,
            ["message"] = message
        };
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

    public static string SafeRun(Func<IDwgHandler, string> action, string filePath, bool editable = false)
    {
        try
        {
            using var handler = Open(filePath, editable);
            return action(handler);
        }
        catch (Exception ex)
        {
            return WrapEnvelopeError(ex.Message);
        }
    }

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
