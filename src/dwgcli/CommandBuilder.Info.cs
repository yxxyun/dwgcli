using System.CommandLine;
using DwgCli.Core;

namespace DwgCli;

static partial class CommandBuilder
{
    private static Command BuildInfoCommand(Option<bool> jsonOption)
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "DWG file path" };

        var cmd = new Command("info", "Show file metadata (version, author, layer count, entity count, etc.)");
        cmd.Add(fileArg);
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
                var outPath = result.GetValue(outOpt);
                using var handler = DwgHandlerFactory.Open(file.FullName);
                var node = handler.GetInfo();

                var format = json ? OutputFormat.Json : OutputFormat.Text;
                var output = OutputFormatter.FormatNode(node, format);

                if (outPath == "-") outPath = null;
                if (outPath != null)
                {
                    File.WriteAllText(outPath, output);
                    if (json)
                    {
                        var result = $"{{\"outputFile\":\"{outPath.Replace("\\", "\\\\")}\"}}";
                        Console.WriteLine(OutputFormatter.WrapEnvelope(result));
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
