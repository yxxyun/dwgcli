using System.CommandLine;
using DwgCli.Core;

namespace DwgCli;

static partial class CommandBuilder
{
    private static Command BuildSetCommand(Option<bool> jsonOption)
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "DWG file path" };
        var pathArg = new Argument<string>("path")
        {
            Description = "Path to the element. Examples: /layer/Walls, /entity/{handle}, / (for summary info)"
        };
        var propsOpt = new Option<string[]>("--prop")
        {
            Description = "Property to set (key=value)",
            AllowMultipleArgumentsPerToken = true
        };
        var dryRunOpt = new Option<bool>("--dry-run")
        {
            Description = "Preview changes without writing"
        };

        var cmd = new Command("set", "Modify properties on layers, entities, or document info");
        cmd.Add(fileArg);
        cmd.Add(pathArg);
        cmd.Add(propsOpt);
        cmd.Add(dryRunOpt);
        cmd.Add(jsonOption);

        cmd.SetAction(result =>
        {
            var json = result.GetValue(jsonOption);
            return SafeRun(() =>
            {
                var file = result.GetValue(fileArg)!;
                var path = result.GetValue(pathArg)!;
                var props = result.GetValue(propsOpt);
                var dryRun = result.GetValue(dryRunOpt);

                var properties = ParsePropsArray(props);
                if (properties.Count == 0)
                    throw new ArgumentException("No properties specified. Use --prop key=value");

                var format = json ? OutputFormat.Json : OutputFormat.Text;

                if (dryRun)
                {
                    var msg = $"Dry-run: would set {properties.Count} property(ies) on {path}";
                    if (json)
                        Console.WriteLine(OutputFormatter.WrapEnvelopeText(msg));
                    else
                        Console.WriteLine(msg);
                    return 0;
                }

                using var handler = DwgHandlerFactory.Open(file.FullName, editable: true);
                var unsupported = handler.Set(path, properties);

                var applied = properties.Where(kv => !unsupported.Contains(kv.Key)).ToList();
                var message = applied.Count > 0
                    ? $"Updated {path}: {string.Join(", ", applied.Select(kv => $"{kv.Key}={kv.Value}"))}"
                    : $"No properties applied to {path}";

                if (json)
                {
                    if (applied.Count == 0 && unsupported.Count > 0)
                        Console.WriteLine(OutputFormatter.WrapEnvelopeError(
                            $"{message}. Unsupported: {string.Join(", ", unsupported)}"));
                    else
                        Console.WriteLine(OutputFormatter.WrapEnvelopeText(message));
                }
                else
                {
                    Console.WriteLine(message);
                    if (unsupported.Count > 0)
                        Console.Error.WriteLine($"Unsupported props: {string.Join(", ", unsupported)}");
                }

                handler.Save();

                return applied.Count > 0 ? 0 : 2;
            }, json);
        });

        return cmd;
    }
}
