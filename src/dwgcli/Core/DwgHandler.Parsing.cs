using System.Globalization;
using System.Text.RegularExpressions;
using ACadSharp.Entities;
using CSMath;
using DwgCli.Core.Exceptions;

namespace DwgCli.Core;

partial class DwgHandler
{
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
            return "/";
        return path.TrimEnd('/');
    }

    private static XYZ ParseXYZ(Dictionary<string, string> props, string xKey, string yKey, string zKey)
    {
        var x = ParseDoubleOrDefault(props, xKey, 0);
        var y = ParseDoubleOrDefault(props, yKey, 0);
        var z = ParseDoubleOrDefault(props, zKey, 0);
        return new XYZ(x, y, z);
    }

    private static double ParseDouble(Dictionary<string, string> props, string key)
    {
        if (!props.TryGetValue(key, out var val))
            throw new DwgInvalidParameterException(key, $"Required property '{key}' is missing");

        if (!double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            throw new DwgInvalidParameterException(key, val, $"Invalid numeric value for '{key}': '{val}'");

        return result;
    }

    private static double ParseDoubleOrDefault(Dictionary<string, string> props, string key, double defaultValue)
    {
        if (!props.TryGetValue(key, out var val))
            return defaultValue;

        return double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    private static ACadSharp.Color? ParseColor(string value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        // Apply input validation (alias normalization, fuzzy matching)
        value = InputValidator.NormalizeColor(value);

        // ByLayer / ByBlock
        if (value.Equals("byLayer", StringComparison.OrdinalIgnoreCase))
            return ACadSharp.Color.ByLayer;
        if (value.Equals("byBlock", StringComparison.OrdinalIgnoreCase))
            return ACadSharp.Color.ByBlock;

        // #RRGGBB true color
        var match = Regex.Match(value, @"^#?([0-9a-fA-F]{2})([0-9a-fA-F]{2})([0-9a-fA-F]{2})$");
        if (match.Success)
        {
            var r = byte.Parse(match.Groups[1].Value, NumberStyles.HexNumber);
            var g = byte.Parse(match.Groups[2].Value, NumberStyles.HexNumber);
            var b = byte.Parse(match.Groups[3].Value, NumberStyles.HexNumber);
            return new ACadSharp.Color(r, g, b);
        }

        // ACI color index (1-255)
        if (short.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var idx)
            && idx >= 1 && idx <= 255)
        {
            return new ACadSharp.Color(idx);
        }

        // Named color (common names)
        var named = value.ToLowerInvariant() switch
        {
            "red" => new ACadSharp.Color(1),
            "yellow" => new ACadSharp.Color(2),
            "green" => new ACadSharp.Color(3),
            "cyan" => new ACadSharp.Color(4),
            "blue" => new ACadSharp.Color(5),
            "magenta" => new ACadSharp.Color(6),
            "white" => new ACadSharp.Color(7),
            "black" => new ACadSharp.Color((short)250),
            _ => (ACadSharp.Color?)null
        };

        return named;
    }

    private static string FormatXYZ(XYZ v)
        => $"{v.X:F3},{v.Y:F3},{v.Z:F3}";

    private static XYZ? ParseXYZFromString(string value)
    {
        var parts = value.Split(',');
        if (parts.Length >= 2
            && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var x)
            && double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var y))
        {
            var z = parts.Length >= 3
                && double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var zv) ? zv : 0;
            return new XYZ(x, y, z);
        }
        return null;
    }

    private static double RadToDeg(double rad)
        => rad * 180.0 / Math.PI;

    private static double DegToRad(double deg)
        => deg * Math.PI / 180.0;

    private static double NormalizeAngle(double rad)
    {
        while (rad < 0) rad += 2 * Math.PI;
        while (rad >= 2 * Math.PI) rad -= 2 * Math.PI;
        return rad;
    }
}
