using System.Text.Json.Serialization;

namespace DwgCli.Core;

/// <summary>
/// A single command in a batch script.
/// </summary>
public class BatchItem
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("parent")]
    public string? Parent { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("selector")]
    public string? Selector { get; set; }

    [JsonPropertyName("depth")]
    public int? Depth { get; set; }

    [JsonPropertyName("props")]
    public Dictionary<string, string>? Props { get; set; }

    [JsonPropertyName("attrs")]
    public Dictionary<string, string>? Attrs { get; set; }

    public static readonly HashSet<string> KnownFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "command", "path", "parent", "type", "selector", "depth", "props", "attrs"
    };
}

public class BatchResult
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("output")]
    public string? Output { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("item")]
    public BatchItem? Item { get; set; }
}
