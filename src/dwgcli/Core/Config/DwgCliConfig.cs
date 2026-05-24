using System.Text.Json;

namespace DwgCli.Core.Config;

/// <summary>
/// dwgcli configuration model with default values.
/// </summary>
public class DwgCliConfig
{
    /// <summary>Default text height for new text/mtext entities.</summary>
    public double DefaultTextHeight { get; set; } = 2.5;

    /// <summary>Whether to create .bak backup before overwriting.</summary>
    public bool BackupEnabled { get; set; } = true;

    /// <summary>Default output format when --json is not specified.</summary>
    public string DefaultOutputFormat { get; set; } = "text";

    /// <summary>Maximum query results returned by default.</summary>
    public int DefaultQueryLimit { get; set; } = 100;

    /// <summary>Whether to show verbose warnings.</summary>
    public bool VerboseWarnings { get; set; } = false;
}
