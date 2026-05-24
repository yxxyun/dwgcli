using System.Globalization;
using System.Linq;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace DwgCli.Core;

partial class DwgHandler
{
    public List<DwgNode> Query(string selector)
    {
        var filters = ParseSelector(selector);
        var results = new List<DwgNode>();

        // Determine what to search
        var searchTarget = filters.GetValueOrDefault("target", "entities").ToLowerInvariant();

        switch (searchTarget)
        {
            case "layers":
            case "layer":
                foreach (var layer in _doc.Layers)
                {
                    if (MatchLayer(layer, filters))
                        results.Add(BuildLayerNode(layer, -1, 0));
                }
                break;

            case "blocks":
            case "block":
                foreach (var br in _doc.BlockRecords)
                {
                    if (MatchBlock(br, filters))
                        results.Add(BuildBlockNode(br, 0));
                }
                break;

            default: // entities
                foreach (var entity in _doc.Entities)
                {
                    if (MatchEntity(entity, filters))
                        results.Add(BuildEntityNode(entity, 0));
                }
                // Also search in blocks
                foreach (var br in _doc.BlockRecords)
                {
                    if (br.Name is "*Model_Space" or "*Paper_Space") continue;
                    foreach (var entity in br.Entities)
                    {
                        if (MatchEntity(entity, filters))
                            results.Add(BuildEntityNode(entity, 0));
                    }
                }
                break;
        }

        return results;
    }

    private static Dictionary<string, string> ParseSelector(string selector)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parts = selector.Split(' ', ',', ';');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx > 0)
                result[trimmed[..eqIdx].Trim()] = trimmed[(eqIdx + 1)..].Trim();
        }
        return result;
    }

    private static bool MatchEntity(Entity entity, Dictionary<string, string> filters)
    {
        foreach (var (key, val) in filters)
        {
            if (key == "target") continue;

            switch (key.ToLowerInvariant())
            {
                case "type":
                    if (!entity.ObjectName.Equals(val, StringComparison.OrdinalIgnoreCase)
                        && !entity.GetType().Name.Equals(val, StringComparison.OrdinalIgnoreCase))
                        return false;
                    break;

                case "layer":
                    if (!string.Equals(entity.Layer?.Name, val, StringComparison.OrdinalIgnoreCase))
                        return false;
                    break;

                case "handle":
                    if (!entity.Handle.ToString("X").Equals(val, StringComparison.OrdinalIgnoreCase))
                        return false;
                    break;

                case "color":
                    var filterColor = ParseColor(val);
                    if (filterColor.HasValue && filterColor.Value.Index != entity.Color.Index)
                        return false;
                    break;

                case "linetype":
                    if (!string.Equals(entity.LineType?.Name, val, StringComparison.OrdinalIgnoreCase))
                        return false;
                    break;

                case "hastext":
                    if (bool.TryParse(val, out var wantText))
                    {
                        var hasText = entity is TextEntity or MText;
                        if (hasText != wantText) return false;
                    }
                    break;

                case "text":
                {
                    // Case-insensitive contains (substring) search in text content
                    var entityText = GetEntityText(entity);
                    if (entityText == null) return false;
                    if (entityText.IndexOf(val, StringComparison.OrdinalIgnoreCase) < 0)
                        return false;
                    break;
                }

                case "xmin":
                    if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var xMinVal))
                    {
                        var pos = GetEntityPosition(entity);
                        if (pos == null || pos.Value.X < xMinVal)
                            return false;
                    }
                    break;

                case "xmax":
                    if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var xMaxVal))
                    {
                        var pos = GetEntityPosition(entity);
                        if (pos == null || pos.Value.X > xMaxVal)
                            return false;
                    }
                    break;

                case "ymin":
                    if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var yMinVal))
                    {
                        var pos = GetEntityPosition(entity);
                        if (pos == null || pos.Value.Y < yMinVal)
                            return false;
                    }
                    break;

                case "ymax":
                    if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var yMaxVal))
                    {
                        var pos = GetEntityPosition(entity);
                        if (pos == null || pos.Value.Y > yMaxVal)
                            return false;
                    }
                    break;
            }
        }
        return true;
    }

    private static bool MatchLayer(Layer layer, Dictionary<string, string> filters)
    {
        foreach (var (key, val) in filters)
        {
            if (key == "target") continue;
            switch (key.ToLowerInvariant())
            {
                case "name":
                    if (!layer.Name.Equals(val, StringComparison.OrdinalIgnoreCase)) return false;
                    break;
                case "ison":
                    if (bool.TryParse(val, out var on) && layer.IsOn != on) return false;
                    break;
                case "isfrozen":
                    if (bool.TryParse(val, out var frozen) && layer.Flags.HasFlag(LayerFlags.Frozen) != frozen) return false;
                    break;
                case "color":
                    var c = ParseColor(val);
                    if (c.HasValue && c.Value.Index != layer.Color.Index) return false;
                    break;
            }
        }
        return true;
    }

    private static bool MatchBlock(BlockRecord block, Dictionary<string, string> filters)
    {
        foreach (var (key, val) in filters)
        {
            if (key == "target") continue;
            switch (key.ToLowerInvariant())
            {
                case "name":
                    if (!block.Name.Equals(val, StringComparison.OrdinalIgnoreCase)) return false;
                    break;
                case "isdynamic":
                    if (bool.TryParse(val, out var dyn) && block.IsDynamic != dyn) return false;
                    break;
                case "isanonymous":
                    if (bool.TryParse(val, out var anon) && block.IsAnonymous != anon) return false;
                    break;
            }
        }
        return true;
    }
}
