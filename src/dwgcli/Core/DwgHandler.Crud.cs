using System.Globalization;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using DwgCli.Core.Exceptions;

namespace DwgCli.Core;

partial class DwgHandler
{
    public List<string> Set(string path, Dictionary<string, string> properties)
    {
        if (!_editable)
            throw new DwgReadOnlyException();

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
                throw new DwgInvalidParameterException("path", $"Cannot set properties on '{segments[0]}'. Valid: layer/<id>, entity/<handle>");
        }
    }

    public string Add(string parentPath, string type, Dictionary<string, string> properties, Dictionary<string, string>? attributes = null)
    {
        if (!_editable)
            throw new DwgReadOnlyException();

        var normalized = NormalizePath(parentPath);
        var parentSeg = normalized.Split('/').Where(s => s.Length > 0).FirstOrDefault() ?? "";

        switch (parentSeg.ToLowerInvariant())
        {
            case "layers":
                return AddLayer(type, properties);

            case "entities":
                return AddEntity(type, properties, attributes);

            default:
                throw new DwgInvalidParameterException("parent", $"Cannot add to '{parentSeg}'. Valid: layers, entities");
        }
    }

    public string? Remove(string path)
    {
        if (!_editable)
            throw new DwgReadOnlyException();

        var normalized = NormalizePath(path);
        var segments = normalized.Split('/').Where(s => s.Length > 0).ToArray();

        if (segments.Length < 2)
            throw new DwgInvalidParameterException("path", "Path must specify element to remove (e.g. /entity/{handle}, /layer/{name})");

        switch (segments[0].ToLowerInvariant())
        {
            case "entity":
            {
                var entity = FindEntityByHandle(segments[1]);
                if (entity == null)
                    throw new DwgEntityNotFoundException(segments[1]);

                RemoveEntityFromDoc(entity);
                _entityCache = null;
                _modified = true;
                return null;
            }

            case "layer":
            {
                var layerName = segments[1];
                if (_doc.Layers.Count <= 1)
                    throw new DwgOperationException("remove", "Cannot remove the last layer");
                if (layerName == "0")
                    throw new DwgOperationException("remove", "Cannot remove layer '0'");

                var layer = _doc.Layers[layerName];
                if (layer == null)
                    throw new DwgLayerNotFoundException(layerName);

                _doc.Layers.Remove(layerName);
                _modified = true;
                return null;
            }

            default:
                throw new DwgInvalidParameterException("path", $"Cannot remove from '{segments[0]}'. Valid: entity/<handle>, layer/<name>");
        }
    }

    public List<string> Purge()
    {
        if (!_editable)
            throw new DwgReadOnlyException();

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

    private List<string> SetLayer(string? identifier, Dictionary<string, string> props, List<string> unsupported)
    {
        var layer = ResolveLayer(identifier);
        if (layer == null)
            throw new DwgLayerNotFoundException(identifier);

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

                case "ison" or "toggleon" or "show":
                    if (bool.TryParse(val, out var on)) { layer.IsOn = on; _modified = true; }
                    else unsupported.Add($"{key}={val}");
                    break;

                case "freeze":
                    layer.Flags |= LayerFlags.Frozen; _modified = true;
                    break;

                case "thaw":
                    layer.Flags &= ~LayerFlags.Frozen; _modified = true;
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

                case "lock":
                    layer.Flags |= LayerFlags.Locked; _modified = true;
                    break;

                case "unlock":
                    layer.Flags &= ~LayerFlags.Locked; _modified = true;
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
            throw new DwgEntityNotFoundException(handleHex);

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

                case "insertpoint":
                case "insertPoint":
                case "InsertPoint":
                    if (entity is TextEntity text_ip)
                    {
                        var pt = ParseXYZFromString(val);
                        if (pt.HasValue) { text_ip.InsertPoint = pt.Value; _modified = true; }
                        else unsupported.Add($"{key}={val}");
                    }
                    else unsupported.Add($"{key} only valid for Text/MText/Insert entities");
                    break;

                default:
                    unsupported.Add(key);
                    break;
            }
        }

        return unsupported;
    }

    private string AddLayer(string type, Dictionary<string, string> props)
    {
        // type is ignored for layers (always Layer)
        var name = props.GetValueOrDefault("name", type);
        if (_doc.Layers.Contains(name))
            throw new DwgInvalidParameterException("name", name, $"Layer already exists: '{name}'");

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
            throw new DwgNotSupportedException("entity type", $"Unsupported entity type: '{type}'. Supported: line, circle, arc, text, mtext, insert");

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
                    throw new DwgBlockNotFoundException(blockName);
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
}
