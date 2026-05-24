using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DwgCli.Core;

internal enum OutputFormat
{
    Text,
    Json,
    Csv
}

/// <summary>
/// Text/JSON output formatter for DWG nodes.
/// </summary>
internal static class OutputFormatter
{
    public static readonly JsonSerializerOptions PublicJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string FormatNode(DwgNode node, OutputFormat format)
    {
        return format switch
        {
            OutputFormat.Json => JsonSerializer.Serialize(node, PublicJsonOptions),
            _ => FormatNodeAsText(node)
        };
    }

    public static string FormatNodes(List<DwgNode> nodes, OutputFormat format)
    {
        if (format == OutputFormat.Json)
            return JsonSerializer.Serialize(new { matches = nodes.Count, results = nodes }, PublicJsonOptions);

        if (format == OutputFormat.Csv)
            return FormatNodesAsCsv(nodes);

        var sb = new StringBuilder();
        foreach (var node in nodes)
            sb.AppendLine(FormatNodeOneline(node));
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Format query results as CSV.
    /// First row = headers (union of all property keys + path, type, text).
    /// Subsequent rows = values per node.
    /// </summary>
    public static string FormatNodesAsCsv(List<DwgNode> nodes)
    {
        if (nodes.Count == 0) return "";

        // Collect all property keys across all nodes
        var headers = new List<string> { "path", "type", "text" };
        var seenKeys = new HashSet<string> { "path", "type", "text" };
        foreach (var node in nodes)
        {
            foreach (var key in node.Properties.Keys)
            {
                if (seenKeys.Add(key))
                    headers.Add(key);
            }
        }

        var sb = new StringBuilder();
        // Header row
        sb.AppendLine(string.Join(",", headers.Select(EscapeCsvField)));
        // Data rows
        foreach (var node in nodes)
        {
            var values = new List<string>();
            values.Add(node.Path ?? "");
            values.Add(node.Type ?? "");
            values.Add(node.Text ?? "");
            foreach (var key in headers.Skip(3))
            {
                if (node.Properties.TryGetValue(key, out var val))
                    values.Add(EscapeCsvField(FormatValue(val)));
                else
                    values.Add("");
            }
            sb.AppendLine(string.Join(",", values));
        }
        return sb.ToString().TrimEnd();
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }

    public static string FormatInfo(Dictionary<string, object?> info, OutputFormat format)
    {
        if (format == OutputFormat.Json)
            return JsonSerializer.Serialize(info, PublicJsonOptions);

        var sb = new StringBuilder();
        foreach (var (key, val) in info)
            sb.AppendLine($"{key}: {FormatValue(val)}");
        return sb.ToString().TrimEnd();
    }

    public static string WrapEnvelope(string dataJson)
    {
        var envelope = new JsonObject { ["success"] = true };
        try { envelope["data"] = JsonNode.Parse(dataJson); }
        catch { envelope["data"] = dataJson; }
        return envelope.ToJsonString(PublicJsonOptions);
    }

    public static string WrapEnvelopeText(string message)
    {
        var envelope = new JsonObject
        {
            ["success"] = true,
            ["data"] = message,
            ["message"] = message
        };
        return envelope.ToJsonString(PublicJsonOptions);
    }

    public static string WrapEnvelopeError(string message)
    {
        var envelope = new JsonObject
        {
            ["success"] = false,
            ["message"] = message
        };
        return envelope.ToJsonString(PublicJsonOptions);
    }

    private static string FormatNodeAsText(DwgNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine(FormatNodeOneline(node));
        foreach (var child in node.Children)
            sb.Append(FormatNodeAsText(child));
        return sb.ToString();
    }

    private static string FormatNodeOneline(DwgNode node)
    {
        var sb = new StringBuilder();
        sb.Append($"{node.Path} ({node.Type})");
        if (node.Text != null)
            sb.Append($" \"{EscapeText(node.Text)}\"");
        if (node.ChildCount > 0 && node.Children.Count == 0)
            sb.Append($" children={node.ChildCount}");
        foreach (var (key, val) in node.Properties)
            sb.Append($" {key}={FormatValue(val)}");
        return sb.ToString();
    }

    private static string EscapeText(string text)
        => text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");

    private static string FormatValue(object? val)
    {
        if (val == null) return "";
        if (val is string s) return s;
        if (val is bool b) return b ? "true" : "false";
        if (val is double d) return d.ToString("G");
        return val.ToString() ?? "";
    }
}
