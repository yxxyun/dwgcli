using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;

namespace DwgCli.Core;

/// <summary>
/// Core handler wrapping ACadSharp's CadDocument.
/// Provides read → manipulate → save lifecycle for DWG files.
/// </summary>
internal sealed class DwgHandler : IDwgHandler
{
    private readonly string _filePath;
    private readonly CadDocument _doc;
    private readonly bool _editable;
    private readonly List<string> _notifications;
    private bool _modified;
    private Dictionary<ulong, Entity>? _entityCache;

    public IReadOnlyList<string> Notifications => _notifications;

    public CadDocument Document => _doc;

    public void MarkModified() => _modified = true;

    public DwgHandler(string filePath, CadDocument doc, bool editable, List<string> notifications)
    {
        _filePath = filePath;
        _doc = doc;
        _editable = editable;
        _notifications = notifications;
    }

    // ======================== IDwgHandler ========================

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

        if (_notifications.Count > 0)
            node.Properties["warnings"] = string.Join("; ", _notifications);

        return node;
    }

    public DwgNode Get(string path, int depth = 1)
    {
        var normalized = NormalizePath(path);
        var segments = normalized.Split('/').Where(s => s.Length > 0).ToArray();

        if (segments.Length == 0)
            return GetInfo();

        return segments[0].ToLowerInvariant() switch
        {
            "info" => GetInfo(),
            "layers" => GetLayersNode(depth),
            "layer" => GetLayerNode(segments.Length > 1 ? segments[1] : null, depth),
            "entities" => GetEntitiesNode(depth),
            "entity" => GetEntityNode(segments.Length > 1 ? segments[1] : null, depth),
            "blocks" => GetBlocksNode(depth),
            "block" => GetBlockNode(segments.Length > 1 ? segments[1] : null, depth),
            _ => throw new ArgumentException($"Unknown path segment: '{segments[0]}'. Valid: info, layers, layer/<id>, entities, entity/<handle>, blocks, block/<name>")
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

    public List<string> Set(string path, Dictionary<string, string> properties)
    {
        if (!_editable)
            throw new InvalidOperationException("File was opened read-only. Cannot modify.");

        var normalized = NormalizePath(path);
        var segments = normalized.Split('/').Where(s => s.Length > 0).ToArray();
        var unsupported = new List<string>();

        if (segments.Length == 0)
        {
            // Set info properties
            var info = _doc.SummaryInfo;
            foreach (var (key, val) in properties)
            {
                switch (key.ToLowerInvariant())
                {
                    case "author": info.Author = val; _modified = true; break;
                    case "title": info.Title = val; _modified = true; break;
                    case "subject": info.Subject = val; _modified = true; break;
                    case "comments": info.Comments = val; _modified = true; break;
                    case "keywords": info.Keywords = val; _modified = true; break;
                    default: unsupported.Add(key); break;
                }
            }
            return unsupported;
        }

        switch (segments[0].ToLowerInvariant())
        {
            case "layer":
                return SetLayer(segments.Length > 1 ? segments[1] : null, properties, unsupported);

            case "entity":
                return SetEntity(segments.Length > 1 ? segments[1] : null, properties, unsupported);

            default:
                throw new ArgumentException($"Cannot set properties on '{segments[0]}'. Valid: layer/<id>, entity/<handle>");
        }
    }

    public string Add(string parentPath, string type, Dictionary<string, string> properties, Dictionary<string, string>? attributes = null)
    {
        if (!_editable)
            throw new InvalidOperationException("File was opened read-only. Cannot modify.");

        var normalized = NormalizePath(parentPath);
        var parentSeg = normalized.Split('/').Where(s => s.Length > 0).FirstOrDefault() ?? "";

        switch (parentSeg.ToLowerInvariant())
        {
            case "layers":
                return AddLayer(type, properties);

            case "entities":
                return AddEntity(type, properties, attributes);

            default:
                throw new ArgumentException($"Cannot add to '{parentSeg}'. Valid: layers, entities");
        }
    }

    public void Save()
    {
        if (!_editable)
            throw new InvalidOperationException("File was opened read-only. Cannot save.");
        if (!_modified)
            return;

        // Auto-upgrade old DWG versions to AC1027 for stable write support
        var currentVersion = _doc.Header.Version;
        if (currentVersion < ACadVersion.AC1021)
        {
            Console.Error.WriteLine(
                $"[Warning] DWG version {currentVersion} has unstable write support. Auto-upgrading to AC1027.");
            _doc.Header.Version = ACadVersion.AC1027;
        }

        // Create backup before writing
        var backupPath = _filePath + ".bak";
        try { File.Copy(_filePath, backupPath, overwrite: true); }
        catch (Exception ex) { Console.Error.WriteLine($"[Warning] Failed to create backup: {ex.Message}"); }

        // Write back
        var errors = new List<string>();
        DwgWriter.Write(_filePath, _doc, notification: (_, e) =>
        {
            if (e.NotificationType == NotificationType.Error)
                errors.Add(e.Message);
        });

        if (errors.Count > 0)
            Console.Error.WriteLine($"DWG write completed with {errors.Count} warning(s): {string.Join("; ", errors)}");

        _entityCache = null;
        _modified = false;
    }

    public void Dispose()
    {
        // Nothing to clean up — CadDocument is in-memory only.
    }

    // ======================== Private: Path resolution ========================

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
            return "/";
        return path.TrimEnd('/');
    }

    // ======================== Private: Get nodes ========================

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
            throw new ArgumentException($"Layer not found: '{identifier}'");

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
            throw new ArgumentException($"Entity not found with handle: '{handleHex}'");

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
            throw new ArgumentException($"Block not found: '{name}'");

        return BuildBlockNode(br, depth);
    }

    // ======================== Private: Build nodes ========================

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

        if (depth > 0)
        {
            // Find entities on this layer
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
        node.Text = GetEntityText(entity);

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

    // ======================== Private: Entity properties ========================

    private static void ExtractEntityProperties(Entity entity, Dictionary<string, object?> props)
    {
        // IMPORTANT: order matters for types with inheritance relationships.
        // Most-specific subclasses must appear before their base types.
        switch (entity)
        {
            // ========== Dimensions (DimensionLinear inherits DimensionAligned) ==========
            case DimensionLinear dimLinear:
                props["type"] = "RotatedDimension";
                props["measurement"] = dimLinear.Measurement;
                props["rotation"] = RadToDeg(dimLinear.Rotation);
                props["firstPoint"] = FormatXYZ(dimLinear.FirstPoint);
                props["secondPoint"] = FormatXYZ(dimLinear.SecondPoint);
                props["dimLinePoint"] = FormatXYZ(dimLinear.DefinitionPoint);
                props["textMidPoint"] = FormatXYZ(dimLinear.TextMiddlePoint);
                props["text"] = dimLinear.Text ?? "";
                break;

            case DimensionAligned dimAlign:
                props["type"] = "AlignedDimension";
                props["measurement"] = dimAlign.Measurement;
                props["firstPoint"] = FormatXYZ(dimAlign.FirstPoint);
                props["secondPoint"] = FormatXYZ(dimAlign.SecondPoint);
                props["offset"] = dimAlign.Offset;
                props["dimLinePoint"] = FormatXYZ(dimAlign.DefinitionPoint);
                props["textMidPoint"] = FormatXYZ(dimAlign.TextMiddlePoint);
                props["text"] = dimAlign.Text ?? "";
                break;

            case DimensionAngular2Line dimAng2:
                props["type"] = "Angular2LineDimension";
                props["measurement"] = RadToDeg(dimAng2.Measurement);
                props["center"] = FormatXYZ(dimAng2.Center);
                props["firstPoint"] = FormatXYZ(dimAng2.FirstPoint);
                props["secondPoint"] = FormatXYZ(dimAng2.SecondPoint);
                props["angleVertex"] = FormatXYZ(dimAng2.AngleVertex);
                props["dimArcPoint"] = FormatXYZ(dimAng2.DimensionArc);
                props["textMidPoint"] = FormatXYZ(dimAng2.TextMiddlePoint);
                props["text"] = dimAng2.Text ?? "";
                break;

            case DimensionAngular3Pt dimAng3:
                props["type"] = "Angular3PointDimension";
                props["measurement"] = RadToDeg(dimAng3.Measurement);
                props["firstPoint"] = FormatXYZ(dimAng3.FirstPoint);
                props["secondPoint"] = FormatXYZ(dimAng3.SecondPoint);
                props["angleVertex"] = FormatXYZ(dimAng3.AngleVertex);
                props["dimLinePoint"] = FormatXYZ(dimAng3.DefinitionPoint);
                props["textMidPoint"] = FormatXYZ(dimAng3.TextMiddlePoint);
                props["text"] = dimAng3.Text ?? "";
                break;

            case DimensionRadius dimRadius:
                props["type"] = "RadialDimension";
                props["measurement"] = dimRadius.Measurement;
                props["center"] = FormatXYZ(dimRadius.AngleVertex);
                props["radiusPoint"] = FormatXYZ(dimRadius.DefinitionPoint);
                props["leaderLength"] = dimRadius.LeaderLength;
                props["textMidPoint"] = FormatXYZ(dimRadius.TextMiddlePoint);
                props["text"] = dimRadius.Text ?? "";
                break;

            case DimensionDiameter dimDiam:
                props["type"] = "DiametricDimension";
                props["measurement"] = dimDiam.Measurement;
                props["center"] = FormatXYZ(dimDiam.Center);
                props["radius"] = dimDiam.Measurement / 2.0;
                props["diameterPoint"] = FormatXYZ(dimDiam.AngleVertex);
                props["dimLinePoint"] = FormatXYZ(dimDiam.DefinitionPoint);
                props["leaderLength"] = dimDiam.LeaderLength;
                props["textMidPoint"] = FormatXYZ(dimDiam.TextMiddlePoint);
                props["text"] = dimDiam.Text ?? "";
                break;

            case DimensionOrdinate dimOrd:
                props["type"] = "OrdinateDimension";
                props["measurement"] = dimOrd.Measurement;
                props["featureLocation"] = FormatXYZ(dimOrd.FeatureLocation);
                props["leaderEndpoint"] = FormatXYZ(dimOrd.LeaderEndpoint);
                props["dimLinePoint"] = FormatXYZ(dimOrd.DefinitionPoint);
                props["isOrdinateTypeX"] = dimOrd.IsOrdinateTypeX;
                props["textMidPoint"] = FormatXYZ(dimOrd.TextMiddlePoint);
                props["text"] = dimOrd.Text ?? "";
                break;

            case Dimension dim:
                // Generic fallback for any unhandled dimension subtypes
                props["type"] = "Dimension";
                props["measurement"] = dim.Measurement;
                props["dimLinePoint"] = FormatXYZ(dim.DefinitionPoint);
                props["textMidPoint"] = FormatXYZ(dim.TextMiddlePoint);
                props["text"] = dim.Text ?? "";
                props["style"] = dim.Style?.Name ?? "";
                break;

            // ========== Block attributes (inherit from TextEntity, must precede TextEntity) ==========
            case AttributeDefinition attrDef:
                props["tag"] = attrDef.Tag;
                props["text"] = attrDef.Value;
                props["height"] = attrDef.Height;
                props["rotation"] = RadToDeg(attrDef.Rotation);
                props["insertPoint"] = FormatXYZ(attrDef.InsertPoint);
                props["flags"] = attrDef.Flags.ToString();
                props["isAttributeDefinition"] = true;
                break;

            case AttributeEntity attr:
                props["tag"] = attr.Tag;
                props["text"] = attr.Value;
                props["height"] = attr.Height;
                props["rotation"] = RadToDeg(attr.Rotation);
                props["insertPoint"] = FormatXYZ(attr.InsertPoint);
                props["flags"] = attr.Flags.ToString();
                props["isAttributeReference"] = true;
                break;

            // ========== Leader ==========
            case Leader leader:
                props["vertexCount"] = leader.Vertices.Count;
                props["arrowHeadEnabled"] = leader.ArrowHeadEnabled;
                props["pathType"] = leader.PathType.ToString();
                props["creationType"] = leader.CreationType.ToString();
                props["hasHookline"] = leader.HasHookline;
                props["horizontalDir"] = FormatXYZ(leader.HorizontalDirection);
                props["normal"] = FormatXYZ(leader.Normal);
                if (leader.AnnotationOffset != XYZ.Zero)
                    props["annotationOffset"] = FormatXYZ(leader.AnnotationOffset);
                if (leader.AssociatedAnnotation != null)
                    props["annotationHandle"] = leader.AssociatedAnnotation.Handle.ToString("X");
                break;

            // ========== RasterImage ==========
            case RasterImage image:
                props["imageFile"] = image.Definition?.FileName ?? "";
                props["pixelWidth"] = image.Definition?.Size.X ?? 0;
                props["pixelHeight"] = image.Definition?.Size.Y ?? 0;
                props["insertPoint"] = FormatXYZ(image.InsertPoint);
                props["sizeU"] = image.Size.X;
                props["sizeV"] = image.Size.Y;
                props["uVector"] = FormatXYZ(image.UVector);
                props["vVector"] = FormatXYZ(image.VVector);
                props["brightness"] = image.Brightness;
                props["contrast"] = image.Contrast;
                props["fade"] = image.Fade;
                props["showImage"] = image.ShowImage;
                props["clipping"] = image.ClippingState;
                break;

            // ========== Primitive geometry ==========
            case Line line:
                props["startPoint"] = FormatXYZ(line.StartPoint);
                props["endPoint"] = FormatXYZ(line.EndPoint);
                props["length"] = (line.EndPoint - line.StartPoint).GetLength();
                props["angle"] = RadToDeg(Math.Atan2(
                    line.EndPoint.Y - line.StartPoint.Y,
                    line.EndPoint.X - line.StartPoint.X));
                break;

            case Arc arc:
                props["center"] = FormatXYZ(arc.Center);
                props["radius"] = arc.Radius;
                props["startAngle"] = RadToDeg(arc.StartAngle);
                props["endAngle"] = RadToDeg(arc.EndAngle);
                props["totalAngle"] = RadToDeg(NormalizeAngle(arc.EndAngle - arc.StartAngle));
                break;

            case Circle circle:
                props["center"] = FormatXYZ(circle.Center);
                props["radius"] = circle.Radius;
                props["diameter"] = circle.Radius * 2;
                props["circumference"] = 2 * Math.PI * circle.Radius;
                break;

            case TextEntity text:
                props["insertPoint"] = FormatXYZ(text.InsertPoint);
                props["height"] = text.Height;
                props["rotation"] = RadToDeg(text.Rotation);
                props["text"] = text.Value;
                break;

            case MText mtext:
                props["insertPoint"] = FormatXYZ(mtext.InsertPoint);
                props["height"] = mtext.Height;
                props["width"] = mtext.RectangleWidth;
                props["rotation"] = RadToDeg(mtext.Rotation);
                props["text"] = mtext.Value;
                props["lineCount"] = mtext.GetTextLines()?.Length ?? 0;
                break;

            case Insert insert:
                props["insertPoint"] = FormatXYZ(insert.InsertPoint);
                props["blockName"] = insert.Block?.Name ?? "(null)";
                props["scale"] = $"{insert.XScale:F3},{insert.YScale:F3},{insert.ZScale:F3}";
                props["rotation"] = RadToDeg(insert.Rotation);
                if (insert.Block != null)
                    props["blockIsDynamic"] = insert.Block.IsDynamic;
                // List attributes attached to this Insert
                var attrs = insert.Attributes.ToList();
                if (attrs.Count > 0)
                {
                    var attrList = attrs.Select(a => $"{a.Tag}={a.Value}").ToList();
                    props["attributes"] = string.Join("; ", attrList);
                    props["attributeCount"] = attrs.Count;
                }
                break;

            case LwPolyline lwp:
                props["vertexCount"] = lwp.Vertices.Count;
                props["isClosed"] = lwp.IsClosed;
                props["elevation"] = lwp.Elevation;
                if (lwp.ConstantWidth != 0)
                    props["width"] = lwp.ConstantWidth;
                else if (lwp.Vertices.Count > 0)
                    props["startWidth"] = lwp.Vertices[0].StartWidth;
                break;

            case Polyline2D p2d:
                props["vertexCount"] = p2d.Vertices.Count;
                props["isClosed"] = p2d.IsClosed;
                props["type"] = "Polyline2D";
                break;

            case Polyline3D p3d:
                props["vertexCount"] = p3d.Vertices.Count;
                props["isClosed"] = p3d.IsClosed;
                props["type"] = "Polyline3D";
                break;

            case Spline spline:
                props["degree"] = spline.Degree;
                props["controlPointCount"] = spline.ControlPoints.Count;
                props["fitPointCount"] = spline.FitPoints.Count;
                props["isClosed"] = spline.IsClosed;
                props["isPeriodic"] = spline.IsPeriodic;
                // Spline is rational if weights are present (not all 1.0)
                props["isRational"] = spline.Weights.Count > 0 && spline.Weights.Any(w => Math.Abs(w - 1.0) > 1e-10);
                break;

            case Ellipse ellipse:
                props["center"] = FormatXYZ(ellipse.Center);
                props["majorAxis"] = ellipse.MajorAxis;
                props["majorAxisEndPoint"] = FormatXYZ(ellipse.MajorAxisEndPoint);
                props["radiusRatio"] = ellipse.RadiusRatio;
                props["startParam"] = ellipse.StartParameter;
                props["endParam"] = ellipse.EndParameter;
                break;

            case Hatch hatch:
                props["patternName"] = hatch.Pattern?.Name ?? "";
                props["isSolid"] = hatch.IsSolid;
                props["isAssociative"] = hatch.IsAssociative;
                props["elevation"] = hatch.Elevation;
                break;

            case Point pt:
                props["location"] = FormatXYZ(pt.Location);
                props["thickness"] = pt.Thickness;
                break;
        }
    }

    private static string? GetEntityText(Entity entity)
    {
        return entity switch
        {
            TextEntity t => t.Value,
            MText mt => mt.Value,
            _ => null
        };
    }

    // ======================== Private: Set ========================

    private List<string> SetLayer(string? identifier, Dictionary<string, string> props, List<string> unsupported)
    {
        var layer = ResolveLayer(identifier);
        if (layer == null)
            throw new ArgumentException($"Layer not found: '{identifier}'");

        foreach (var (key, val) in props)
        {
            switch (key.ToLowerInvariant())
            {
                case "name":
                    if (val != layer.Name)
                    {
                        // Direct Name assignment works because CadObjectCollection<T>
                        // uses linear scan for indexing, not dictionary keys.
                        layer.Name = val;
                        _modified = true;
                    }
                    break;

                case "color":
                    var color = ParseColor(val);
                    if (color.HasValue) { layer.Color = color.Value; _modified = true; }
                    else unsupported.Add($"{key}={val}");
                    break;

                case "linetype":
                    var lt = _doc.LineTypes[val];
                    if (lt != null) { layer.LineType = lt; _modified = true; }
                    else unsupported.Add($"{key}={val}");
                    break;

                case "ison":
                    if (bool.TryParse(val, out var on)) { layer.IsOn = on; _modified = true; }
                    else unsupported.Add($"{key}={val}");
                    break;

                case "isfrozen":
                    if (bool.TryParse(val, out var frozen))
                    {
                        if (frozen) layer.Flags |= LayerFlags.Frozen;
                        else layer.Flags &= ~LayerFlags.Frozen;
                        _modified = true;
                    }
                    else unsupported.Add($"{key}={val}");
                    break;

                case "islocked":
                    if (bool.TryParse(val, out var locked))
                    {
                        if (locked) layer.Flags |= LayerFlags.Locked;
                        else layer.Flags &= ~LayerFlags.Locked;
                        _modified = true;
                    }
                    else unsupported.Add($"{key}={val}");
                    break;

                default:
                    unsupported.Add(key);
                    break;
            }
        }

        return unsupported;
    }

    private List<string> SetEntity(string? handleHex, Dictionary<string, string> props, List<string> unsupported)
    {
        var entity = FindEntityByHandle(handleHex);
        if (entity == null)
            throw new ArgumentException($"Entity not found with handle: '{handleHex}'");

        foreach (var (key, val) in props)
        {
            switch (key.ToLowerInvariant())
            {
                case "layer":
                    var layer = _doc.Layers[val];
                    if (layer != null) { entity.Layer = layer; _modified = true; }
                    else unsupported.Add($"{key}={val} (layer not found)");
                    break;

                case "color":
                    var color = ParseColor(val);
                    if (color.HasValue) { entity.Color = color.Value; _modified = true; }
                    else unsupported.Add($"{key}={val}");
                    break;

                case "linetype":
                    var lt = _doc.LineTypes[val];
                    if (lt != null) { entity.LineType = lt; _modified = true; }
                    else unsupported.Add($"{key}={val}");
                    break;

                case "lineweight":
                    if (Enum.TryParse<LineWeightType>(val, ignoreCase: true, out var lw))
                        { entity.LineWeight = lw; _modified = true; }
                    else unsupported.Add($"{key}={val}");
                    break;

                case "transparency":
                    if (short.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var transpVal)
                        && transpVal >= 0 && transpVal <= 90)
                    {
                        entity.Transparency = new Transparency(transpVal);
                        _modified = true;
                    }
                    else unsupported.Add($"{key}={val} (0-90, 0=opaque, 90=max transparent)");
                    break;

                case "material":
                    var material = _doc.Materials[val];
                    if (material != null) { entity.Material = material; _modified = true; }
                    else unsupported.Add($"{key}={val} (material not found)");
                    break;

                case "invisible":
                    if (bool.TryParse(val, out var invisible)) { entity.IsInvisible = invisible; _modified = true; }
                    else unsupported.Add($"{key}={val}");
                    break;

                case "linetypescale":
                    if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var lts))
                        { entity.LineTypeScale = lts; _modified = true; }
                    else unsupported.Add($"{key}={val}");
                    break;

                // ===== Geometry properties (entity-type-specific) =====
                case "startpoint":
                    if (entity is Line line_sp)
                    {
                        var pt = ParseXYZFromString(val);
                        if (pt.HasValue) { line_sp.StartPoint = pt.Value; _modified = true; }
                        else unsupported.Add($"{key}={val}");
                    }
                    else unsupported.Add($"{key} only valid for Line entities");
                    break;

                case "endpoint":
                    if (entity is Line line_ep)
                    {
                        var pt = ParseXYZFromString(val);
                        if (pt.HasValue) { line_ep.EndPoint = pt.Value; _modified = true; }
                        else unsupported.Add($"{key}={val}");
                    }
                    else unsupported.Add($"{key} only valid for Line entities");
                    break;

                case "center":
                    if (entity is Circle circle_c)
                    {
                        var pt = ParseXYZFromString(val);
                        if (pt.HasValue) { circle_c.Center = pt.Value; _modified = true; }
                        else unsupported.Add($"{key}={val}");
                    }
                    else if (entity is Arc arc_c)
                    {
                        var pt = ParseXYZFromString(val);
                        if (pt.HasValue) { arc_c.Center = pt.Value; _modified = true; }
                        else unsupported.Add($"{key}={val}");
                    }
                    else unsupported.Add($"{key} only valid for Circle/Arc entities");
                    break;

                case "radius":
                    if (entity is Circle circle_r && double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var rad))
                        { circle_r.Radius = rad; _modified = true; }
                    else if (entity is Arc arc_r && double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var rad2))
                        { arc_r.Radius = rad2; _modified = true; }
                    else unsupported.Add($"{key}={val}");
                    break;

                case "text":
                    if (entity is TextEntity textEnt) { textEnt.Value = val; _modified = true; }
                    else unsupported.Add($"{key} only valid for Text/MText entities");
                    break;

                case "height":
                    if (entity is TextEntity textH && double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var h))
                        { textH.Height = h; _modified = true; }
                    else unsupported.Add($"{key}={val}");
                    break;

                default:
                    unsupported.Add(key);
                    break;
            }
        }

        return unsupported;
    }

    // ======================== Private: Add ========================

    private string AddLayer(string type, Dictionary<string, string> props)
    {
        // type is ignored for layers (always Layer)
        var name = props.GetValueOrDefault("name", type);
        if (_doc.Layers.Contains(name))
            throw new ArgumentException($"Layer already exists: '{name}'");

        var layer = new Layer(name);
        if (props.TryGetValue("color", out var colorStr))
        {
            var color = ParseColor(colorStr);
            if (color.HasValue) layer.Color = color.Value;
        }
        if (props.TryGetValue("linetype", out var ltName))
        {
            var lt = _doc.LineTypes[ltName];
            if (lt != null) layer.LineType = lt;
        }

        _doc.Layers.Add(layer);
        _modified = true;
        return $"/layer/{name}";
    }

    private string AddEntity(string type, Dictionary<string, string> props, Dictionary<string, string>? attributes = null)
    {
        var entity = CreateEntity(type, props);
        if (entity == null)
            throw new ArgumentException($"Unsupported entity type: '{type}'. Supported: line, circle, arc, text, mtext, insert");

        // Apply common properties
        ApplyCommonEntityProps(entity, props);

        // Add attribute references for insert block references
        if (entity is Insert insert && attributes != null && attributes.Count > 0 && insert.Block != null)
        {
            foreach (var child in insert.Block.Entities)
            {
                if (child is AttributeDefinition attrDef)
                {
                    var attrValue = attributes.TryGetValue(attrDef.Tag, out var val) ? val : attrDef.Value;
                    var attrRef = new AttributeEntity
                    {
                        Tag = attrDef.Tag,
                        Value = attrValue,
                        Height = attrDef.Height,
                        Rotation = attrDef.Rotation,
                        InsertPoint = attrDef.InsertPoint,
                    };
                    insert.Attributes.Add(attrRef);
                }
            }
        }

        var modelSpace = GetModelSpaceBlock();
        if (modelSpace != null)
            modelSpace.Entities.Add(entity);
        else
            _doc.Entities.Add(entity);

        _modified = true;
        var handle = entity.Handle.ToString("X");
        return $"/entity/{handle}";
    }

    private Entity? CreateEntity(string type, Dictionary<string, string> props)
    {
        switch (type.ToLowerInvariant())
        {
            case "line":
                return new Line
                {
                    StartPoint = ParseXYZ(props, "x1", "y1", "z1"),
                    EndPoint = ParseXYZ(props, "x2", "y2", "z2"),
                };

            case "circle":
                return new Circle
                {
                    Center = ParseXYZ(props, "cx", "cy", "cz"),
                    Radius = ParseDouble(props, "r"),
                };

            case "arc":
                return new Arc
                {
                    Center = ParseXYZ(props, "cx", "cy", "cz"),
                    Radius = ParseDouble(props, "r"),
                    StartAngle = DegToRad(ParseDouble(props, "startAngle")),
                    EndAngle = DegToRad(ParseDouble(props, "endAngle")),
                };

            case "text":
                return new TextEntity
                {
                    InsertPoint = ParseXYZ(props, "x", "y", "z"),
                    Value = props.GetValueOrDefault("text", ""),
                    Height = ParseDoubleOrDefault(props, "height", 2.5),
                    Rotation = DegToRad(ParseDoubleOrDefault(props, "rotation", 0)),
                };

            case "mtext":
                var mtext = new MText
                {
                    InsertPoint = ParseXYZ(props, "x", "y", "z"),
                    Value = props.GetValueOrDefault("text", ""),
                    Height = ParseDoubleOrDefault(props, "height", 2.5),
                };
                // MText.Rotation is read-only; use AlignmentPoint for rotation hint if needed
                mtext.AlignmentPoint = mtext.InsertPoint;
                if (props.TryGetValue("width", out var widthStr)
                    && double.TryParse(widthStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var width))
                {
                    mtext.RectangleWidth = width;
                }
                return mtext;

            case "insert":
            case "blockref":
                var blockName = props.GetValueOrDefault("block", "");
                var block = _doc.BlockRecords[blockName];
                if (block == null)
                    throw new ArgumentException($"Block not found: '{blockName}'");
                return new Insert(block)
                {
                    InsertPoint = ParseXYZ(props, "x", "y", "z"),
                    XScale = ParseDoubleOrDefault(props, "scaleX", 1),
                    YScale = ParseDoubleOrDefault(props, "scaleY", 1),
                    ZScale = ParseDoubleOrDefault(props, "scaleZ", 1),
                    Rotation = DegToRad(ParseDoubleOrDefault(props, "rotation", 0)),
                };

            default:
                return null;
        }
    }

    private void ApplyCommonEntityProps(Entity entity, Dictionary<string, string> props)
    {
        if (props.TryGetValue("layer", out var layerName))
        {
            var layer = _doc.Layers[layerName];
            if (layer != null) entity.Layer = layer;
        }
        if (props.TryGetValue("color", out var colorStr))
        {
            var color = ParseColor(colorStr);
            if (color.HasValue) entity.Color = color.Value;
        }
        if (props.TryGetValue("linetype", out var ltName))
        {
            var lt = _doc.LineTypes[ltName];
            if (lt != null) entity.LineType = lt;
        }
    }

    // ======================== Private: Query matching ========================

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

    // ======================== Private: Helpers ========================

    private Layer? ResolveLayer(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return null;

        // First try by name
        var byName = _doc.Layers[identifier];
        if (byName != null) return byName;

        // Then try by index
        if (int.TryParse(identifier, out var idx) && idx >= 0 && idx < _doc.Layers.Count)
            return _doc.Layers.ElementAt(idx);

        return null;
    }

    private Dictionary<ulong, Entity> GetEntityCache()
    {
        if (_entityCache == null)
        {
            _entityCache = new Dictionary<ulong, Entity>();

            void IndexEntity(Entity e)
            {
                if (!_entityCache.ContainsKey(e.Handle))
                    _entityCache[e.Handle] = e;
            }

            foreach (var e in _doc.Entities)
                IndexEntity(e);

            foreach (var br in _doc.BlockRecords)
            {
                foreach (var e in br.Entities)
                    IndexEntity(e);
            }
        }
        return _entityCache;
    }

    private Entity? FindEntityByHandle(string handleHex)
    {
        if (!ulong.TryParse(handleHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var handle))
            return null;

        var cache = GetEntityCache();
        return cache.TryGetValue(handle, out var entity) ? entity : null;
    }

    // ======================== Public: Remove / Purge ========================

    public string? Remove(string path)
    {
        if (!_editable)
            throw new InvalidOperationException("File was opened read-only. Cannot modify.");

        var normalized = NormalizePath(path);
        var segments = normalized.Split('/').Where(s => s.Length > 0).ToArray();

        if (segments.Length < 2)
            throw new ArgumentException("Path must specify element to remove (e.g. /entity/{handle}, /layer/{name})");

        switch (segments[0].ToLowerInvariant())
        {
            case "entity":
            {
                var entity = FindEntityByHandle(segments[1]);
                if (entity == null)
                    throw new ArgumentException($"Entity not found with handle: '{segments[1]}'");

                RemoveEntityFromDoc(entity);
                _entityCache = null;
                _modified = true;
                return null;
            }

            case "layer":
            {
                var layerName = segments[1];
                if (_doc.Layers.Count <= 1)
                    throw new InvalidOperationException("Cannot remove the last layer");
                if (layerName == "0")
                    throw new InvalidOperationException("Cannot remove layer '0'");

                var layer = _doc.Layers[layerName];
                if (layer == null)
                    throw new ArgumentException($"Layer not found: '{layerName}'");

                _doc.Layers.Remove(layerName);
                _modified = true;
                return null;
            }

            default:
                throw new ArgumentException($"Cannot remove from '{segments[0]}'. Valid: entity/<handle>, layer/<name>");
        }
    }

    public List<string> Purge()
    {
        if (!_editable)
            throw new InvalidOperationException("File was opened read-only. Cannot modify.");

        var purged = new List<string>();

        // Collect used layer names
        var usedLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "0" };
        foreach (var e in GetAllEntities())
        {
            if (e.Layer?.Name != null)
                usedLayers.Add(e.Layer.Name);
        }

        foreach (var layer in _doc.Layers.ToList())
        {
            if (layer.Name == "0") continue;
            if (!usedLayers.Contains(layer.Name))
            {
                _doc.Layers.Remove(layer.Name);
                purged.Add($"layer/{layer.Name}");
            }
        }

        // Collect used block names
        var usedBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "*Model_Space", "*Paper_Space" };
        foreach (var e in GetAllEntities())
        {
            if (e is Insert ins && ins.Block?.Name != null)
                usedBlocks.Add(ins.Block.Name);
        }

        foreach (var br in _doc.BlockRecords.ToList())
        {
            if (br.Name.StartsWith("*")) continue;
            if (!usedBlocks.Contains(br.Name))
            {
                _doc.BlockRecords.Remove(br.Name);
                purged.Add($"block/{br.Name}");
            }
        }

        // Collect used linetype names
        var usedLinetypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "BYLAYER", "BYBLOCK", "Continuous" };
        foreach (var e in GetAllEntities())
        {
            if (e.LineType?.Name != null)
                usedLinetypes.Add(e.LineType.Name);
        }

        foreach (var lt in _doc.LineTypes.ToList())
        {
            if (lt.Name is "BYLAYER" or "BYBLOCK" or "Continuous") continue;
            if (!usedLinetypes.Contains(lt.Name))
            {
                _doc.LineTypes.Remove(lt.Name);
                purged.Add($"linetype/{lt.Name}");
            }
        }

        _modified = purged.Count > 0;
        return purged;
    }

    private void RemoveEntityFromDoc(Entity entity)
    {
        if (_doc.Entities.Contains(entity))
        {
            _doc.Entities.Remove(entity);
            return;
        }

        foreach (var br in _doc.BlockRecords)
        {
            if (br.Entities.Contains(entity))
            {
                br.Entities.Remove(entity);
                return;
            }
        }
    }

    private IEnumerable<Entity> GetAllEntities()
    {
        foreach (var e in _doc.Entities)
            yield return e;

        foreach (var br in _doc.BlockRecords)
        {
            foreach (var e in br.Entities)
                yield return e;
        }
    }

    private BlockRecord? GetModelSpaceBlock()
    {
        return _doc.BlockRecords["*Model_Space"];
    }

    // ======================== Private: Type parsing ========================

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
            throw new ArgumentException($"Required property '{key}' is missing");

        if (!double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            throw new ArgumentException($"Invalid numeric value for '{key}': '{val}'");

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
