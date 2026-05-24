using System.Globalization;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using DwgCli.Core.Exceptions;

namespace DwgCli.Core;

partial class DwgHandler
{
    public DwgNode GetInfo()
    {
        var info = _doc.SummaryInfo;
        var header = _doc.Header;

        var node = new DwgNode
        {
            Path = "/",
            Type = "dwg",
            Properties = new Dictionary<string, object?>
            {
                ["fileName"] = Path.GetFileName(_filePath),
                ["version"] = header?.Version.ToString() ?? "Unknown",
                ["author"] = info?.Author,
                ["title"] = info?.Title,
                ["subject"] = info?.Subject,
                ["comments"] = info?.Comments,
                ["keywords"] = info?.Keywords,
                ["created"] = info?.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss"),
                ["modified"] = info?.ModifiedDate.ToString("yyyy-MM-dd HH:mm:ss"),
                ["entityCount"] = _doc.Entities.Count,
                ["layerCount"] = _doc.Layers.Count,
                ["blockCount"] = _doc.BlockRecords.Count,
                ["linetypeCount"] = _doc.LineTypes.Count,
                ["textStyleCount"] = _doc.TextStyles.Count,
            }
        };

        // Add layout info
        var layoutNames = _doc.BlockRecords
            .Where(br => br.Layout != null)
            .Select(br => br.Layout!.Name)
            .Distinct()
            .ToList();
        node.Properties["layoutCount"] = layoutNames.Count;
        if (layoutNames.Count > 0)
            node.Properties["layoutNames"] = string.Join(", ", layoutNames);

        if (_notifications.Count > 0)
            node.Properties["warnings"] = string.Join("; ", _notifications);

        return node;
    }

    public DwgNode Get(string path, int depth = 1)
    {
        var normalized = NormalizePath(path);
        var segments = normalized.Split('/').Where(s => s.Length > 0).ToArray();

        if (segments.Length == 0)
        {
            var root = GetInfo();
            // Include layouts section in root output
            var layoutsNode = GetLayoutsNode();
            if (layoutsNode.Children.Count > 0)
            {
                root.Children.Add(layoutsNode);
                root.ChildCount++;
            }
            return root;
        }

        return segments[0].ToLowerInvariant() switch
        {
            "info" => GetInfo(),
            "layers" => GetLayersNode(depth),
            "layer" => GetLayerNode(segments.Length > 1 ? segments[1] : null, depth),
            "entities" => GetEntitiesNode(depth),
            "entity" => GetEntityNode(segments.Length > 1 ? segments[1] : null, depth),
            "blocks" => GetBlocksNode(depth),
            "block" => GetBlockNode(segments.Length > 1 ? segments[1] : null, depth),
            "layouts" => GetLayoutsNode(),
            "layout" => GetLayoutNode(segments.Length > 1 ? segments[1] : null),
            _ => throw new DwgInvalidParameterException("path", $"Unknown path segment: '{segments[0]}'. Valid: info, layers, layer/<id>, entities, entity/<handle>, blocks, block/<name>, layouts, layout/<name>")
        };
    }

    public DwgNode Dump(int depth = 10)
    {
        // Full document tree: info + layers + linetypes + text styles + blocks + entities
        var root = GetInfo();

        // Layers
        var layersSection = GetLayersNode(depth);
        if (layersSection.Children.Count > 0)
            root.Children.Add(layersSection);

        // Line types
        var linetypes = new DwgNode { Path = "/linetypes", Type = "linetypes", ChildCount = _doc.LineTypes.Count };
        foreach (var lt in _doc.LineTypes)
        {
            if (lt.Name is "BYLAYER" or "BYBLOCK") continue;
            linetypes.Children.Add(new DwgNode
            {
                Path = $"/linetype/{lt.Name}",
                Type = "linetype",
                Properties = new() { ["name"] = lt.Name, ["description"] = lt.Description ?? "" }
            });
        }
        if (linetypes.Children.Count > 0)
        {
            linetypes.ChildCount = linetypes.Children.Count;
            root.Children.Add(linetypes);
        }

        // Text styles
        var styles = new DwgNode { Path = "/styles", Type = "textStyles", ChildCount = _doc.TextStyles.Count };
        foreach (var ts in _doc.TextStyles)
        {
            if (string.IsNullOrEmpty(ts.Name) || ts.Name == "Standard") continue;
            styles.Children.Add(new DwgNode
            {
                Path = $"/style/{ts.Name}",
                Type = "textStyle",
                Properties = new() { ["name"] = ts.Name, ["font"] = ts.Filename ?? "", ["height"] = ts.Height }
            });
        }
        if (styles.Children.Count > 0)
        {
            styles.ChildCount = styles.Children.Count;
            root.Children.Add(styles);
        }

        // Blocks
        var blocksSection = GetBlocksNode(depth);
        if (blocksSection.Children.Count > 0)
            root.Children.Add(blocksSection);

        // Model space entities
        var entitiesSection = GetEntitiesNode(depth);
        if (entitiesSection.Children.Count > 0)
            root.Children.Add(entitiesSection);

        return root;
    }

    public DwgNode Stats()
    {
        var stats = new DwgNode
        {
            Path = "/stats",
            Type = "stats"
        };

        // 1. Entity type counts
        var byType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // 2. Layer counts
        var byLayer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // 3. Block reference counts
        var byBlock = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int total = 0;
        void CountEntity(Entity e)
        {
            total++;
            var t = e.ObjectName ?? e.GetType().Name;
            byType[t] = byType.GetValueOrDefault(t, 0) + 1;
            var l = e.Layer?.Name ?? "0";
            byLayer[l] = byLayer.GetValueOrDefault(l, 0) + 1;
            if (e is Insert ins && ins.Block?.Name != null)
            {
                var bn = ins.Block.Name;
                // Use sourceBlock name if available for anonymous dynamic blocks
                if (ins.Block.Source != null)
                    bn = ins.Block.Source.Name;
                byBlock[bn] = byBlock.GetValueOrDefault(bn, 0) + 1;
            }
        }

        foreach (var e in _doc.Entities)
            CountEntity(e);
        foreach (var br in _doc.BlockRecords)
        {
            if (br.Name is "*Model_Space" or "*Paper_Space") continue;
            foreach (var e in br.Entities)
                CountEntity(e);
        }

        // Entity type summary
        var typeNode = new DwgNode { Path = "/stats/type", Type = "stats_entityTypes" };
        foreach (var (k, v) in byType.OrderByDescending(kv => kv.Value))
            typeNode.Properties[k] = v;
        typeNode.Properties["_total"] = total;
        stats.Children.Add(typeNode);

        // Layer summary
        var layerNode = new DwgNode { Path = "/stats/layer", Type = "stats_layers" };
        foreach (var (k, v) in byLayer.OrderByDescending(kv => kv.Value))
            layerNode.Properties[k] = v;
        stats.Children.Add(layerNode);

        // Block reference summary
        var blockNode = new DwgNode { Path = "/stats/block", Type = "stats_blocks" };
        foreach (var (k, v) in byBlock.OrderByDescending(kv => kv.Value))
            blockNode.Properties[k] = v;
        stats.Children.Add(blockNode);

        stats.ChildCount = 3;
        return stats;
    }

    private DwgNode GetLayersNode(int depth)
    {
        var node = new DwgNode { Path = "/layers", Type = "layers", ChildCount = _doc.Layers.Count };
        if (depth <= 0) return node;

        int idx = 0;
        foreach (var layer in _doc.Layers)
        {
            var child = BuildLayerNode(layer, idx, depth - 1);
            if (child != null) node.Children.Add(child);
            idx++;
        }
        return node;
    }

    private DwgNode? GetLayerNode(string? identifier, int depth)
    {
        Layer? layer = ResolveLayer(identifier);
        if (layer == null)
            throw new DwgLayerNotFoundException(identifier);

        if (depth <= 0)
            return new DwgNode { Path = $"/layer/{layer.Name}", Type = "layer" };

        return BuildLayerNode(layer,
            _doc.Layers.ToList().IndexOf(layer),
            depth);
    }

    private DwgNode GetEntitiesNode(int depth)
    {
        var modelSpace = GetModelSpaceBlock();
        var entities = modelSpace?.Entities ?? _doc.Entities;

        var node = new DwgNode
        {
            Path = "/entities",
            Type = "entities",
            ChildCount = entities.Count
        };

        if (depth <= 0) return node;

        foreach (var entity in entities)
        {
            var child = BuildEntityNode(entity, depth - 1);
            if (child != null) node.Children.Add(child);
        }
        return node;
    }

    private DwgNode? GetEntityNode(string? handleHex, int depth)
    {
        if (handleHex == null)
            return GetEntitiesNode(depth);

        var entity = FindEntityByHandle(handleHex);
        if (entity == null)
            throw new DwgEntityNotFoundException(handleHex);

        return BuildEntityNode(entity, depth);
    }

    private DwgNode GetBlocksNode(int depth)
    {
        var node = new DwgNode { Path = "/blocks", Type = "blocks" };
        var count = 0;
        foreach (var br in _doc.BlockRecords)
        {
            if (br.Name is "*Model_Space" or "*Paper_Space") continue;
            count++;
            if (depth > 0)
                node.Children.Add(BuildBlockNode(br, depth - 1));
        }
        node.ChildCount = count;
        return node;
    }

    private DwgNode? GetBlockNode(string? name, int depth)
    {
        if (name == null)
            return GetBlocksNode(depth);

        var br = _doc.BlockRecords[name];
        if (br == null)
            throw new DwgBlockNotFoundException(name);

        return BuildBlockNode(br, depth);
    }

    private DwgNode BuildLayerNode(Layer layer, int index, int depth)
    {
        var node = new DwgNode
        {
            Path = $"/layer/{layer.Name}",
            Type = "layer",
            Properties = new()
            {
                ["name"] = layer.Name,
                ["color"] = layer.Color.IsTrueColor
                    ? $"#{layer.Color.R:X2}{layer.Color.G:X2}{layer.Color.B:X2}"
                    : layer.Color.Index.ToString(),
                ["colorIndex"] = layer.Color.Index,
                ["isOn"] = layer.IsOn,
                ["isFrozen"] = layer.Flags.HasFlag(LayerFlags.Frozen),
                ["isLocked"] = layer.Flags.HasFlag(LayerFlags.Locked),
                ["lineWeight"] = layer.LineWeight.ToString(),
                ["plot"] = layer.PlotFlag,
                ["index"] = index >= 0 ? index.ToString() : "",
            }
        };

        if (layer.LineType != null)
            node.Properties["linetype"] = layer.LineType.Name;

        // Count all entities on this layer (model space + block records)
        var entityCount = _doc.Entities.Count(e => e.Layer?.Name == layer.Name);
        foreach (var br in _doc.BlockRecords)
        {
            if (br.Name is "*Model_Space" or "*Paper_Space") continue;
            entityCount += br.Entities.Count(e => e.Layer?.Name == layer.Name);
        }
        node.Properties["entityCount"] = entityCount;

        if (depth > 0)
        {
            // Find entities on this layer (model space only for children)
            foreach (var entity in _doc.Entities)
            {
                if (entity.Layer?.Name == layer.Name && entity.Handle != layer.Handle)
                {
                    var child = BuildEntityNode(entity, depth - 1);
                    if (child != null)
                    {
                        node.Children.Add(child);
                        node.ChildCount++;
                    }
                }
            }
        }

        return node;
    }

    private DwgNode BuildEntityNode(Entity entity, int depth)
    {
        var handle = entity.Handle.ToString("X");
        var node = new DwgNode
        {
            Path = $"/entity/{handle}",
            Type = entity.ObjectName,
            Properties = new()
            {
                ["handle"] = handle,
                ["layer"] = entity.Layer?.Name ?? "0",
                ["color"] = entity.Color.IsTrueColor
                    ? $"#{entity.Color.R:X2}{entity.Color.G:X2}{entity.Color.B:X2}"
                    : entity.Color.Index.ToString(),
                ["colorIndex"] = entity.Color.Index,
                ["lineWeight"] = entity.LineWeight.ToString(),
            }
        };

        if (entity.LineType != null)
            node.Properties["linetype"] = entity.LineType.Name;

        // Entity-specific geometry
        ExtractEntityProperties(entity, node.Properties);

        // For text entities, surface the text content
        node.Text = SanitizeForJson(GetEntityText(entity) ?? "");

        if (depth > 0 && entity is Insert insert && insert.Block != null)
        {
            foreach (var childEntity in insert.Block.Entities)
            {
                var child = BuildEntityNode(childEntity, depth - 1);
                if (child != null)
                    node.Children.Add(child);
            }
        }

        return node;
    }

    private DwgNode BuildBlockNode(BlockRecord block, int depth)
    {
        var node = new DwgNode
        {
            Path = $"/block/{block.Name}",
            Type = "block",
            Properties = new()
            {
                ["name"] = block.Name,
                ["isDynamic"] = block.IsDynamic,
                ["isAnonymous"] = block.IsAnonymous,
                ["entityCount"] = block.Entities.Count,
                ["units"] = block.Units.ToString(),
            }
        };

        if (block.BlockEntity != null)
        {
            var bp = block.BlockEntity.BasePoint;
            node.Properties["basePoint"] = $"{bp.X:F3},{bp.Y:F3},{bp.Z:F3}";
        }

        if (depth > 0)
        {
            foreach (var entity in block.Entities)
            {
                var child = BuildEntityNode(entity, depth - 1);
                if (child != null)
                    node.Children.Add(child);
            }
        }

        return node;
    }

    private BlockRecord? GetModelSpaceBlock()
    {
        return _doc.BlockRecords["*Model_Space"];
    }

    private static (double X, double Y)? GetEntityPosition(Entity entity)
    {
        return entity switch
        {
            Insert ins => (ins.InsertPoint.X, ins.InsertPoint.Y),
            Line line => ((line.StartPoint.X + line.EndPoint.X) / 2,
                          (line.StartPoint.Y + line.EndPoint.Y) / 2),
            TextEntity t => (t.InsertPoint.X, t.InsertPoint.Y),
            MText mt => (mt.InsertPoint.X, mt.InsertPoint.Y),
            Arc a => (a.Center.X, a.Center.Y),
            Circle c => (c.Center.X, c.Center.Y),
            Ellipse e => (e.Center.X, e.Center.Y),
            Point pt => (pt.Location.X, pt.Location.Y),
            // Polyline2D inherits LwPolyline, so handled by LwPolyline arm below
            // Polyline3D handles differently
            LwPolyline lwp when lwp.Vertices.Count > 0 =>
                (lwp.Vertices.Average(v => v.Location.X),
                 lwp.Vertices.Average(v => v.Location.Y)),
            _ => null
        };
    }

    private static (double XMin, double YMin, double XMax, double YMax)? GetEntityBBox(Entity entity)
    {
        try
        {
            var box = entity.GetBoundingBox();
            if (box.Extent == BoundingBoxExtent.Infinite)
                return null;
            return (box.Min.X, box.Min.Y, box.Max.X, box.Max.Y);
        }
        catch
        {
            return null;
        }
    }
}
