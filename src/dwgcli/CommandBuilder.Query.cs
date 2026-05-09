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

        var cmd = new Command("query", "Search entities/layers by criteria");
        cmd.Add(fileArg);
        cmd.Add(selectorArg);
        cmd.Add(jsonOption);

        cmd.SetAction(result =>
        {
            var json = result.GetValue(jsonOption);
            return SafeRun(() =>
            {
                var file = result.GetValue(fileArg)!;
                var selector = result.GetValue(selectorArg)!;

                using var handler = DwgHandlerFactory.Open(file.FullName);
                var results = handler.Query(selector);

                var format = json ? OutputFormat.Json : OutputFormat.Text;
                var output = OutputFormatter.FormatNodes(results, format);

                if (json)
                    Console.WriteLine(OutputFormatter.WrapEnvelope(output));
                else
                {
                    Console.WriteLine(output);
                    if (results.Count == 0)
                        Console.Error.WriteLine("No matches found.");
                }

                return 0;
            }, json);
        });

        return cmd;
    }
}
