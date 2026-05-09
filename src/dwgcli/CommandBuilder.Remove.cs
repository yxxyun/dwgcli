using System.CommandLine;
using DwgCli.Core;

namespace DwgCli;

static partial class CommandBuilder
{
    private static Command BuildRemoveCommand(Option<bool> jsonOption)
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "DWG file path" };
        var pathArg = new Argument<string>("path")
        {
            Description = "Path to remove. Examples: /entity/{handle}, /layer/{name}"
        };

        var cmd = new Command("remove", "Remove an entity or layer from the drawing");
        cmd.Add(fileArg);
        cmd.Add(pathArg);
        cmd.Add(jsonOption);

        cmd.SetAction(result =>
        {
            var json = result.GetValue(jsonOption);
            return SafeRun(() =>
            {
                var file = result.GetValue(fileArg)!;
                var path = result.GetValue(pathArg)!;

                using var handler = DwgHandlerFactory.Open(file.FullName, editable: true);
                var warning = handler.Remove(path);

                var msg = warning != null
                    ? $"Removed {path}. Warning: {warning}"
                    : $"Removed {path}";

                if (json)
                    Console.WriteLine(OutputFormatter.WrapEnvelopeText(msg));
                else
                    Console.WriteLine(msg);

                handler.Save();
                return 0;
            }, json);
        });

        return cmd;
    }
}
