using System.CommandLine;
using DwgCli.Core;

namespace DwgCli;

static partial class CommandBuilder
{
    private static Command BuildPurgeCommand(Option<bool> jsonOption)
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "DWG file path" };
        var dryRunOpt = new Option<bool>("--dry-run")
        {
            Description = "Preview what would be purged without removing"
        };

        var cmd = new Command("purge", "Remove unused layers, blocks, and linetypes from the drawing");
        cmd.Add(fileArg);
        cmd.Add(dryRunOpt);
        cmd.Add(jsonOption);

        cmd.SetAction(result =>
        {
            var json = result.GetValue(jsonOption);
            return SafeRun(() =>
            {
                var file = result.GetValue(fileArg)!;
                var dryRun = result.GetValue(dryRunOpt);

                using var handler = DwgHandlerFactory.Open(file.FullName, editable: !dryRun);
                var purged = handler.Purge();

                if (dryRun)
                {
                    var msg = $"Dry-run: would purge {purged.Count} item(s): {string.Join(", ", purged)}";
                    if (json)
                        Console.WriteLine(OutputFormatter.WrapEnvelopeText(msg));
                    else
                        Console.WriteLine(msg);
                }
                else
                {
                    var msg = $"Purged {purged.Count} item(s): {string.Join(", ", purged)}";
                    if (json)
                        Console.WriteLine(OutputFormatter.WrapEnvelopeText(msg));
                    else
                        Console.WriteLine(msg);
                    handler.Save();
                }
                return 0;
            }, json);
        });

        return cmd;
    }
}
