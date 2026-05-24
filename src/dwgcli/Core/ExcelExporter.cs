using ClosedXML.Excel;

namespace DwgCli.Core;

/// <summary>
/// Exports DWG query results to Excel (.xlsx) format using ClosedXML.
/// </summary>
internal static class ExcelExporter
{
    /// <summary>
    /// Export a list of DwgNodes (e.g. from Query or dump) to an .xlsx file.
    /// First sheet "Data" has property names as headers and one row per node.
    /// </summary>
    public static void ExportToFile(List<DwgNode> nodes, string filePath)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Data");

        if (nodes.Count == 0)
        {
            ws.Cell(1, 1).Value = "No data";
            workbook.SaveAs(filePath);
            return;
        }

        // Collect all property keys in order (first node's keys first, then union)
        var headers = new List<string> { "path", "type", "text" };
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "path", "type", "text" };
        foreach (var node in nodes)
        {
            foreach (var key in node.Properties.Keys)
            {
                if (seenKeys.Add(key))
                    headers.Add(key);
            }
        }

        // Header row with bold style
        for (int c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        // Data rows
        for (int r = 0; r < nodes.Count; r++)
        {
            var node = nodes[r];
            var row = r + 2;

            ws.Cell(row, 1).Value = node.Path ?? "";
            ws.Cell(row, 2).Value = node.Type ?? "";
            ws.Cell(row, 3).Value = node.Text ?? "";

            for (int c = 3; c < headers.Count; c++)
            {
                var key = headers[c];
                if (node.Properties.TryGetValue(key, out var val) && val != null)
                {
                    var cell = ws.Cell(row, c + 1);

                    // Determine value type for better Excel display
                    if (val is double d)
                        cell.Value = d;
                    else if (val is bool b)
                        cell.Value = b;
                    else if (val is long l)
                        cell.Value = l;
                    else if (val is int i)
                        cell.Value = i;
                    else
                        cell.Value = val.ToString() ?? "";
                }
            }
        }

        // Auto-fit columns (with max width)
        ws.Columns().AdjustToContents(1, 50);

        workbook.SaveAs(filePath);
    }
}
