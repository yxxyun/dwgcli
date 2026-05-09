using System.Text.Json.Serialization;

namespace DwgCli.Core;

/// <summary>
/// Represents a node in the DWG document tree.
/// </summary>
public class DwgNode
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("childCount")]
    public int ChildCount { get; set; }

    [JsonPropertyName("properties")]
    public Dictionary<string, object?> Properties { get; set; } = new();

    [JsonPropertyName("children")]
    public List<DwgNode> Children { get; set; } = new();
}
