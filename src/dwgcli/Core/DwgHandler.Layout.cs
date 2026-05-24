using System.Globalization;
using System.Linq;
using ACadSharp.Entities;
using DwgCli.Core.Exceptions;

namespace DwgCli.Core;

partial class DwgHandler
{
    private DwgNode GetLayoutsNode()
    {
        var node = new DwgNode { Path = "/layouts", Type = "layouts" };
        foreach (var br in _doc.BlockRecords)
        {
            if (br.Layout == null) continue;
            var layoutNode = new DwgNode
            {
                Path = $"/layout/{br.Layout.Name}",
                Type = "layout",
                Properties = new()
                {
                    ["name"] = br.Layout.Name,
                    ["blockName"] = br.Name,
                    ["tabOrder"] = br.Layout.TabOrder,
                    ["minLimits"] = $"{br.Layout.MinLimits.X:F3},{br.Layout.MinLimits.Y:F3}",
                    ["maxLimits"] = $"{br.Layout.MaxLimits.X:F3},{br.Layout.MaxLimits.Y:F3}",
                    ["paperWidth"] = br.Layout.PaperWidth,
                    ["paperHeight"] = br.Layout.PaperHeight,
                }
            };

            // Add viewport info
            var viewports = br.Viewports.ToList();
            if (viewports.Count > 0)
            {
                var vpInfos = viewports.Select(vp =>
                {
                    var parts = new List<string>
                    {
                        $"center={FormatXYZ(vp.Center)}",
                        $"width={vp.Width:F1}",
                        $"height={vp.Height:F1}"
                    };
                    if (vp.RepresentsPaper)
                        parts.Add("paper");
                    return string.Join(" ", parts);
                }).ToList();
                layoutNode.Properties["viewports"] = string.Join("; ", vpInfos);
                layoutNode.Properties["viewportCount"] = viewports.Count;
            }

            node.Children.Add(layoutNode);
            node.ChildCount++;
        }
        return node;
    }

    private DwgNode? GetLayoutNode(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return GetLayoutsNode();

        // Find the block record with matching layout name
        var br = _doc.BlockRecords.FirstOrDefault(b => b.Layout?.Name.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
        if (br == null)
            throw new DwgInvalidParameterException("name", $"Layout not found: '{name}'");

        var layoutNode = new DwgNode
        {
            Path = $"/layout/{br.Layout!.Name}",
            Type = "layout",
            Properties = new()
            {
                ["name"] = br.Layout.Name,
                ["blockName"] = br.Name,
                ["tabOrder"] = br.Layout.TabOrder,
                ["minLimits"] = $"{br.Layout.MinLimits.X:F3},{br.Layout.MinLimits.Y:F3}",
                ["maxLimits"] = $"{br.Layout.MaxLimits.X:F3},{br.Layout.MaxLimits.Y:F3}",
                ["paperWidth"] = br.Layout.PaperWidth,
                ["paperHeight"] = br.Layout.PaperHeight,
            }
        };

        var viewports = br.Viewports.ToList();
        if (viewports.Count > 0)
        {
            var vpInfos = viewports.Select(vp =>
            {
                var parts = new List<string>
                {
                    $"center={FormatXYZ(vp.Center)}",
                    $"width={vp.Width:F1}",
                    $"height={vp.Height:F1}"
                };
                if (vp.RepresentsPaper)
                    parts.Add("paper");
                return string.Join(" ", parts);
            }).ToList();
            layoutNode.Properties["viewports"] = string.Join("; ", vpInfos);
            layoutNode.Properties["viewportCount"] = viewports.Count;
        }

        return layoutNode;
    }
}
