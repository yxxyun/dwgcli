using System.CommandLine;
using DwgCli.Core;

namespace DwgCli;

static partial class CommandBuilder
{
    private static Command BuildGetCommand(Option<bool> jsonOption)
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "DWG file path" };
        var pathArg = new Argument<string>("path")
        {
            Description = "Document path. Examples: /, /info, /layers, /layer/0, /layer/Walls, /entities, /entity/{handle}, /blocks, /block/*Model_Space",
            DefaultValueFactory = _ => "/"
        };
        var depthOpt = new Option<int>("--depth")
        {
            Description = "Depth of child nodes to include (0 = no children, 1 = direct children, etc.)",
            DefaultValueFactory = _ => 1
        };

        var cmd = new Command("get", "Get a document node by path");
        cmd.Add(fileArg);
        cmd.Add(pathArg);
        cmd.Add(depthOpt);
        cmd.Add(jsonOption);

        var outOpt = new Option<string?>("--out", "-o")
        {
            Description = "Write output to file instead of stdout"
        };
        cmd.Add(outOpt);

        cmd.SetAction(result =>
        {
            var json = result.GetValue(jsonOption);
            return SafeRun(() =>
            {
                var file = result.GetValue(fileArg)!;
                var path = result.GetValue(pathArg)!;
                var depth = result.GetValue(depthOpt);
                var outPath = result.GetValue(outOpt);

                using var handler = DwgHandlerFactory.Open(file.FullName);
                var node = handler.Get(path, depth);

                var format = json ? OutputFormat.Json : OutputFormat.Text;
                var output = OutputFormatter.FormatNode(node, format);

                if (outPath == "-") outPath = null;
                if (outPath != null)
                {
                    File.WriteAllText(outPath, output);
                    if (json)
                    {
                        var resultStr = $"{{\"outputFile\":\"{outPath.Replace("\\", "\\\\")}\"}}";
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
                        Console.WriteLine(output);
                }

                return 0;
            }, json);
        });

        return cmd;
    }
}
