using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ACadSharp.Entities;
using CSMath;

namespace DwgCli.Core;

partial class DwgHandler
{
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
                props["plainText"] = StripMTextFormatCodes(mtext.Value);
                props["lineCount"] = mtext.GetTextLines()?.Length ?? 0;
                break;

            case Insert insert:
                props["insertPoint"] = FormatXYZ(insert.InsertPoint);
                props["blockName"] = insert.Block?.Name ?? "(null)";
                props["scale"] = $"{insert.XScale:F3},{insert.YScale:F3},{insert.ZScale:F3}";
                props["rotation"] = RadToDeg(insert.Rotation);
                if (insert.Block != null)
                {
                    props["blockIsDynamic"] = insert.Block.IsDynamic;
                    // Map anonymous dynamic blocks (*U*) to their source block definition
                    var source = insert.Block.Source;
                    if (source != null)
                        props["sourceBlock"] = source.Name;
                }
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

    /// <summary>
    /// Strip AutoCAD MText format control codes, returning clean plain text.
    /// Handles: \P (new paragraph), \pxqc; (paragraph alignment), \L, \l, \O, \o, \K, \k,
    /// \H, \W, \S, \A, \C, \T, \F, \{fontname}, \~ (non-breaking space), and more.
    /// </summary>
    private static string StripMTextFormatCodes(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Remove all {...} groups with format codes inside
        // e.g. {\FCalibri;Hello} → Hello,  {\LHello} → Hello
        text = Regex.Replace(text, @"\{[^}]*\}", m =>
        {
            var inner = m.Value;
            // Find the last semicolon or start of content after all codes
            var semicolon = inner.LastIndexOf(';');
            if (semicolon > 0 && semicolon < inner.Length - 1)
                return inner[(semicolon + 1)..^1];
            // No semicolon, strip braces and codes at start
            var stripped = inner.TrimStart('{');
            var codeEnd = FindMTextCodeEnd(stripped);
            return codeEnd > 0 ? stripped[codeEnd..^1] : inner[1..^1];
        });

        // Replace \P with newline
        text = text.Replace("\\P", "\n");

        // Replace other common single-char codes
        text = Regex.Replace(text, @"\\[pP]", "\n");     // paragraph
        text = Regex.Replace(text, @"\\l", "");           // underline off
        text = Regex.Replace(text, @"\\L", "");           // underline on
        text = Regex.Replace(text, @"\\o", "");           // overline off
        text = Regex.Replace(text, @"\\O", "");           // overline on
        text = Regex.Replace(text, @"\\k", "");           // strike-through off
        text = Regex.Replace(text, @"\\K", "");           // strike-through on

        // Remove all remaining \x; format sequences
        text = Regex.Replace(text, @"\\[a-zA-Z]+[^;]*;", "");

        // Remove standalone codes (e.g. \S, \P that weren't caught)
        text = Regex.Replace(text, @"\\(?:[pPlLoOKkHWCcFfTtAaSQ]|~)", m =>
            m.Value switch
            {
                "\\P" or "\\p" => "\n",
                "\\~" => " ",
                _ => ""
            });

        // Collapse multiple consecutive whitespace characters into one
        text = Regex.Replace(text, @"[ \t]+", " ");

        // Trim leading/trailing whitespace per line
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            lines[i] = lines[i].Trim();
        text = string.Join("\n", lines);

        return SanitizeForJson(text.Trim());
    }

    private static int FindMTextCodeEnd(string s)
    {
        // Find where the format code ends - after a letter sequence optionally followed by params
        if (s.Length > 0 && char.IsLetter(s[0]))
        {
            int i = 1;
            while (i < s.Length && char.IsLetter(s[i])) i++;
            // Skip optional parameters (digits, semicolons, etc.)
            return i;
        }
        return 0;
    }

    /// <summary>
    /// Strip control characters (U+0000-U+001F) from text to ensure valid JSON output.
    /// Allows \t, \n, \r which are valid in JSON strings.
    /// </summary>
    private static string SanitizeForJson(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var cleaned = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c >= 0x20 || c is '\t' or '\n' or '\r')
                cleaned.Append(c);
        }
        return cleaned.ToString();
    }
}
