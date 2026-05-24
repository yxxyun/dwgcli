using System.CommandLine;
using DwgCli.Core;

namespace DwgCli;

static partial class CommandBuilder
{
    private static Command BuildQueryCommand(Option<bool> jsonOption)
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "DWG file path" };
        var selectorArg = new Argument<string>("selector")
        {
            Description = "Query selector. Examples: type=Line, layer=Walls, type=Circle layer=0, target=layers name=Walls"
        };

        var formatOpt = new Option<string>("--format")
        {
            Description = "Output format: 'text' (default), 'csv', or 'json'",
        };
        var outOpt = new Option<string?>("--out", "-o")
        {
            Description = "Write output to file instead of stdout"
        };

        var cmd = new Command("query", "Search entities/layers by criteria");
        cmd.Add(fileArg);
        cmd.Add(selectorArg);
        cmd.Add(formatOpt);
        cmd.Add(outOpt);
        cmd.Add(jsonOption);

        cmd.SetAction(result =>
        {
            var json = result.GetValue(jsonOption);
            return SafeRun(() =>
            {
                var file = result.GetValue(fileArg)!;
                var selector = result.GetValue(selectorArg)!;
                var formatStr = (result.GetValue(formatOpt) ?? "").ToLowerInvariant();
                var outPath = result.GetValue(outOpt);

                using var handler = DwgHandlerFactory.Open(file.FullName);
                var results = handler.Query(selector);

                OutputFormat format;
                if (formatStr == "csv")
                    format = OutputFormat.Csv;
                else if (formatStr == "json" || json)
                    format = OutputFormat.Json;
                else
                    format = OutputFormat.Text;

                var output = OutputFormatter.FormatNodes(results, format);

                if (outPath == "-") outPath = null;
                if (outPath != null)
                {
                    File.WriteAllText(outPath, output);
                    if (json)
                    {
                        var resultStr = $"{{\"outputFile\":\"{outPath.Replace("\\", "\\\\")}\",\"matchCount\":{results.Count}}}";
                        Console.WriteLine(OutputFormatter.WrapEnvelope(resultStr));
                    }
                    else
                        Console.WriteLine(outPath);
                }
                else
                {
                    if (json)
                        Console.WriteLine(OutputFormatter.WrapEnvelope(output));
                    else
                    {
                        Console.WriteLine(output);
                        if (results.Count == 0)
                            Console.Error.WriteLine("No matches found.");
                    }
                }

                return 0;
            }, json);
        });

        return cmd;
    }
}
