using System.Text.Json;

namespace DwgCli.Core.Config;

/// <summary>
/// Loads dwgcli configuration via cascade search:
/// 1. Current working directory: ./dwgcli.json
/// 2. User config directory: ~/.dwgcli/config.json
/// 3. Executable directory: (app)/dwgcli.json
/// 4. Built-in defaults (always)
/// </summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static DwgCliConfig? _instance;
    private static string? _loadedFrom;

    /// <summary>
    /// Get the current effective configuration.
    /// Lazily loaded on first access, then cached.
    /// </summary>
    public static DwgCliConfig Current
    {
        get
        {
            if (_instance == null)
                _instance = Load();
            return _instance;
        }
    }

    /// <summary>
    /// Path the config was loaded from, or null if using defaults only.
    /// </summary>
    public static string? LoadedFrom => _loadedFrom;

    /// <summary>
    /// Load configuration by searching through cascade locations.
    /// First match wins.
    /// </summary>
    public static DwgCliConfig Load()
    {
        var locations = GetSearchLocations();
        var config = new DwgCliConfig();

        foreach (var loc in locations)
        {
            if (File.Exists(loc))
            {
                try
                {
                    var text = File.ReadAllText(loc);
                    var loaded = JsonSerializer.Deserialize<DwgCliConfig>(text, JsonOpts);
                    if (loaded != null)
                    {
                        // Merge: loaded values override defaults
                        MergeConfig(config, loaded);
                        _loadedFrom = loc;
                    }
                }
                catch (Exception ex)
                {
                    // Silently skip malformed config files — just log to stderr
                    Console.Error.WriteLine($"dwgcli: warning: skipping malformed config '{loc}': {ex.Message}");
                }
            }
        }

        return config;
    }

    /// <summary>
    /// Reset the cached config (useful for testing).
    /// </summary>
    public static void Reset()
    {
        _instance = null;
        _loadedFrom = null;
    }

    /// <summary>
    /// Get search locations in priority order.
    /// </summary>
    public static List<string> GetSearchLocations()
    {
        var locations = new List<string>();

        // 1. Current working directory
        locations.Add(Path.Combine(Environment.CurrentDirectory, "dwgcli.json"));

        // 2. User config directory
        var userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userDir))
        {
            var userConfigDir = Path.Combine(userDir, ".dwgcli");
            locations.Add(Path.Combine(userConfigDir, "config.json"));
        }

        // 3. Executable directory
        var exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        if (exeDir != null)
        {
            locations.Add(Path.Combine(exeDir, "dwgcli.json"));
        }

        return locations;
    }

    /// <summary>
    /// Merge loaded config values into the default config.
    /// Only non-default values override.
    /// </summary>
    private static void MergeConfig(DwgCliConfig target, DwgCliConfig source)
    {
        if (source.DefaultTextHeight != 2.5)
            target.DefaultTextHeight = source.DefaultTextHeight;
        if (source.BackupEnabled != true)
            target.BackupEnabled = source.BackupEnabled;
        if (!string.IsNullOrEmpty(source.DefaultOutputFormat) && source.DefaultOutputFormat != "text")
            target.DefaultOutputFormat = source.DefaultOutputFormat;
        if (source.DefaultQueryLimit != 100)
            target.DefaultQueryLimit = source.DefaultQueryLimit;
        if (source.VerboseWarnings)
            target.VerboseWarnings = source.VerboseWarnings;
    }
}
