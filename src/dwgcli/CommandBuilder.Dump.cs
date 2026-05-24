using System.CommandLine;
using System.Text.Json;
using DwgCli.Core;

namespace DwgCli;

static partial class CommandBuilder
{
    private static Command BuildDumpCommand(Option<bool> jsonOption)
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "DWG file path" };
        var formatOpt = new Option<string>("--format")
        {
            Description = "Output format: 'tree' (default, structure tree), 'batch' (replayable batch script), or 'csv' (entity data as CSV)",
            DefaultValueFactory = _ => "tree"
        };
        var depthOpt = new Option<int>("--depth")
        {
            Description = "Depth of tree output (default: full)",
            DefaultValueFactory = _ => 10
        };
        var outOpt = new Option<string?>("--out", "-o")
        {
            Description = "Write output to file instead of stdout"
        };

        var cmd = new Command("dump", "Dump the DWG document structure as a tree or replayable batch");
        cmd.Add(fileArg);
        cmd.Add(formatOpt);
        cmd.Add(depthOpt);
        cmd.Add(outOpt);
        cmd.Add(jsonOption);

        cmd.SetAction(result =>
        {
            var json = result.GetValue(jsonOption);
            return SafeRun(() =>
            {
                var file = result.GetValue(fileArg)!;
                var format = (result.GetValue(formatOpt) ?? "tree").ToLowerInvariant();
                var depth = result.GetValue(depthOpt);
                var outPath = result.GetValue(outOpt);

                if (format != "tree" && format != "batch" && format != "csv" && format != "excel")
                    throw new ArgumentException($"Unsupported --format: '{format}'. Valid: tree, batch, csv, excel");

                using var handler = DwgHandlerFactory.Open(file.FullName);

                if (format == "batch")
                {
                    // Emit replayable batch JSON
                    var items = EmitDwgBatch(handler);
                    var output = JsonSerializer.Serialize(items, new JsonSerializerOptions
                    {
                        WriteIndented = false,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    });

                    if (outPath == "-") outPath = null;
                    if (outPath != null)
                    {
                        File.WriteAllText(outPath, output);
                        if (json)
                            Console.WriteLine(OutputFormatter.WrapEnvelope(
                                $"{{\"outputFile\":\"{outPath.Replace("\\", "\\\\")}\",\"itemCount\":{items.Count}}}"));
                        else
                            Console.WriteLine(outPath);
                    }
                    else
                    {
                        if (json)
                            Console.WriteLine(OutputFormatter.WrapEnvelope(output));
                        else
                            Console.WriteLine(output);
                    }
                }
                else if (format == "csv")
                {
                    // CSV output — flatten all entities
                    var entities = handler.Query("");
                    var output = OutputFormatter.FormatNodes(entities, OutputFormat.Csv);

                    if (outPath == "-") outPath = null;
                    if (outPath != null)
                    {
                        File.WriteAllText(outPath, output);
                        Console.WriteLine(outPath);
                    }
                    else
                    {
                        if (json)
                            Console.WriteLine(OutputFormatter.WrapEnvelope(output));
                        else
                            Console.WriteLine(output);
                    }
                }
                else if (format == "excel")
                {
                    // Excel output — export all entities to .xlsx
                    var entities = handler.Query("");
                    var xlsxPath = outPath ?? Path.ChangeExtension(file.FullName, ".xlsx");
                    ExcelExporter.ExportToFile(entities, xlsxPath);
                    if (json)
                        Console.WriteLine(OutputFormatter.WrapEnvelope(
                            $"{{\"outputFile\":\"{xlsxPath.Replace("\\", "\\\\")}\",\"itemCount\":{entities.Count}}}"));
                    else
                        Console.WriteLine(xlsxPath);
                }
                else
                {
                    // Tree output
                    var root = handler.Dump(depth);
                    var formatMode = json ? OutputFormat.Json : OutputFormat.Text;
                    var output = OutputFormatter.FormatNode(root, formatMode);

                    if (outPath == "-") outPath = null;
                    if (outPath != null)
                    {
                        File.WriteAllText(outPath, output);
                        Console.WriteLine(outPath);
                    }
                    else
                    {
                        if (json)
                            Console.WriteLine(OutputFormatter.WrapEnvelope(output));
                        else
                            Console.WriteLine(output);
                    }
                }

                return 0;
            }, json);
        });

        return cmd;
    }

    /// <summary>
    /// Emit a replayable batch from the current document state.
    /// </summary>
    private static List<BatchItem> EmitDwgBatch(IDwgHandler handler)
    {
        var items = new List<BatchItem>();

        // Recreate layers
        var layersNode = handler.Get("/layers", 10);
        foreach (var layer in layersNode.Children)
        {
            var props = new Dictionary<string, string>();
            foreach (var (k, v) in layer.Properties)
            {
                if (k == "name") continue;
                if (k == "index") continue;
                if (v != null) props[k] = v.ToString()!;
            }
            items.Add(new BatchItem
            {
                Command = "add",
                Path = "/layers",
                Type = "layer",
                Props = props
            });
        }

        // Recreate entities
        var entitiesNode = handler.Get("/entities", 1);
        foreach (var entity in entitiesNode.Children)
        {
            var type = entity.Type;
            var props = new Dictionary<string, string>();
            foreach (var (k, v) in entity.Properties)
            {
                if (k is "handle" or "layer" or "color" or "linetype" or "colorIndex" or "lineWeight") continue;
                if (v != null) props[k] = v.ToString()!;
            }
            // Add layer/color as props
            if (entity.Properties.TryGetValue("layer", out var layerVal) && layerVal != null)
                props["layer"] = layerVal.ToString()!;
            if (entity.Properties.TryGetValue("color", out var colorVal) && colorVal != null)
                props["color"] = colorVal.ToString()!;

            items.Add(new BatchItem
            {
                Command = "add",
                Path = "/entities",
                Type = type,
                Props = props
            });
        }

        return items;
    }
}
