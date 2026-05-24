using System.Globalization;
using System.Text.RegularExpressions;

namespace DwgCli.Core;

/// <summary>
/// Input validation and auto-correction for AI-generated parameters.
/// Provides fuzzy color name matching, type coercion, and coordinate normalization
/// to make the CLI more resilient to typos and format variations.
/// </summary>
public static class InputValidator
{
    // ========== Color Aliases ==========

    private static readonly Dictionary<string, string> ColorAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // British English
        ["grey"] = "gray",
        ["light_grey"] = "light_gray",
        ["dark_grey"] = "dark_gray",
        ["lightgrey"] = "light_gray",
        ["darkgrey"] = "dark_gray",
        ["lightgray"] = "light_gray",
        ["darkgray"] = "dark_gray",
        ["light-gray"] = "light_gray",
        ["dark-gray"] = "dark_gray",
        // Common misspellings
        ["maganta"] = "magenta",
        ["mageta"] = "magenta",
        ["majenta"] = "magenta",
        ["viol"] = "magenta",
        ["violet"] = "magenta",
        ["purple"] = "magenta",
        ["purp"] = "magenta",
        ["aqua"] = "cyan",
        ["teal"] = "cyan",
        ["turquoise"] = "cyan",
        ["orng"] = "orange",
        ["orenge"] = "orange",
        ["yello"] = "yellow",
        ["yllw"] = "yellow",
        ["gren"] = "green",
        ["grn"] = "green",
        ["blu"] = "blue",
        ["ble"] = "blue",
        ["rd"] = "red",
        ["wht"] = "white",
        ["blck"] = "black",
        ["blk"] = "black",
        ["bylayer"] = "byLayer",
        ["by-layer"] = "byLayer",
        ["by_layer"] = "byLayer",
        ["byblock"] = "byBlock",
        ["by-block"] = "byBlock",
        ["by_block"] = "byBlock",
    };

    /// <summary>
    /// Normalize a color string by checking aliases.
    /// Returns the corrected color name, or the original if no alias matches.
    /// </summary>
    public static string NormalizeColor(string? color)
    {
        if (string.IsNullOrEmpty(color)) return color ?? "";
        if (ColorAliases.TryGetValue(color, out var corrected))
            return corrected;
        return color;
    }

    // ========== Numeric Coercion ==========

    /// <summary>
    /// Attempt to parse a string as a double, returning null on failure.
    /// </summary>
    public static double? TryParseDouble(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            return result;
        // Try stripping non-numeric suffixes (e.g., "2.5mm" -> "2.5")
        var cleaned = Regex.Match(value, @"^([+-]?\d+\.?\d*)").Value;
        if (cleaned.Length > 0 && double.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out result))
            return result;
        return null;
    }

    /// <summary>
    /// Attempt to parse a string as a boolean from various representations.
    /// </summary>
    public static bool? TryParseBool(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value.ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" or "enabled" => true,
            "false" or "0" or "no" or "off" or "disabled" => false,
            _ => null
        };
    }

    // ========== Coordinate Normalization ==========

    /// <summary>
    /// Normalize a coordinate string to "x,y,z" format.
    /// Handles: "x,y", "x, y", "x;y", "(x,y)", "[x,y]", "x y" (space-separated).
    /// </summary>
    public static string NormalizeCoordinate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        var cleaned = value.Trim().TrimStart('(').TrimStart('[').TrimEnd(')').TrimEnd(']');

        // Replace semicolons and spaces with commas (but be careful with "x y z" format)
        if (cleaned.Contains(';'))
            cleaned = cleaned.Replace(';', ',');

        // Split and rejoin to normalize spacing
        var parts = cleaned.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            // If first split didn't work, try splitting by whitespace
            if (parts.Length == 1)
            {
                parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            return string.Join(",", parts);
        }

        return value; // Return original if we can't parse
    }

    // ========== Property Value Correction ==========

    /// <summary>
    /// Apply all validations to a property dictionary (in-place).
    /// Used by CommandBuilder.ParsePropsArray.
    /// </summary>
    public static void CorrectProperties(Dictionary<string, string> props)
    {
        foreach (var key in props.Keys.ToList())
        {
            var val = props[key];

            // Normalize color values
            if (key.Equals("color", StringComparison.OrdinalIgnoreCase))
            {
                props[key] = NormalizeColor(val);
            }
        }
    }
}
