using System.CommandLine;
using System.Text.Json;
using DwgCli.Core;

namespace DwgCli;

static partial class CommandBuilder
{
    private static Command BuildBatchCommand(Option<bool> jsonOption)
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "DWG file path" };
        var inputOpt = new Option<FileInfo?>("--input")
        {
            Description = "Input file (JSON or shorthand text). If omitted, reads from stdin"
        };
        var commandsOpt = new Option<string?>("--commands")
        {
            Description = "Inline commands (JSON array or shorthand text, alternative to --input or stdin)"
        };
        var stopOnErrorOpt = new Option<bool>("--stop-on-error")
        {
            Description = "Abort on first failure (default: continue and report per-item errors)"
        };
        var shorthandOpt = new Option<bool>("--shorthand")
        {
            Description = "Parse input as shorthand format (pipe-separated) instead of JSON"
        };

        var cmd = new Command("batch", "Execute multiple commands in a single open/save cycle");

        cmd.Add(fileArg);
        cmd.Add(inputOpt);
        cmd.Add(commandsOpt);
        cmd.Add(stopOnErrorOpt);
        cmd.Add(shorthandOpt);
        cmd.Add(jsonOption);

        cmd.SetAction(result =>
        {
            var json = result.GetValue(jsonOption);
            return SafeRun(() =>
            {
                var file = result.GetValue(fileArg)!;
                var inputFile = result.GetValue(inputOpt);
                var inlineCommands = result.GetValue(commandsOpt);
                var stopOnError = result.GetValue(stopOnErrorOpt);
                var shorthand = result.GetValue(shorthandOpt);

                // Read input text from one of the three sources
                string inputText;
                if (inlineCommands != null && inputFile != null)
                    throw new ArgumentException("--commands and --input are mutually exclusive.");
                if (inlineCommands != null)
                    inputText = inlineCommands;
                else if (inputFile != null)
                {
                    if (!inputFile.Exists)
                        throw new FileNotFoundException($"Input file not found: {inputFile.FullName}");
                    inputText = File.ReadAllText(inputFile.FullName);
                }
                else
                {
                    if (Console.IsInputRedirected)
                        inputText = Console.In.ReadToEnd();
                    else
                        throw new ArgumentException("No input. Use --commands, --input, or pipe to stdin.");
                }

                // Parse items based on mode
                List<BatchItem> items;
                if (shorthand)
                {
                    items = ShorthandParser.Parse(inputText);
                }
                else
                {
                    // Validate JSON
                    using var jsonDoc = JsonDocument.Parse(inputText);
                    if (jsonDoc.RootElement.ValueKind != JsonValueKind.Array)
                        throw new ArgumentException("Batch input must be a JSON array (or use --shorthand).");

                    int i = 0;
                    foreach (var elem in jsonDoc.RootElement.EnumerateArray())
                    {
                        if (elem.ValueKind == JsonValueKind.Object)
                        {
                            var unknown = new List<string>();
                            foreach (var prop in elem.EnumerateObject())
                            {
                                if (!BatchItem.KnownFields.Contains(prop.Name))
                                    unknown.Add(prop.Name);
                            }
                            if (unknown.Count > 0)
                                throw new ArgumentException($"batch item[{i}]: unknown field(s): {string.Join(", ", unknown)}");
                        }
                        i++;
                    }

                    items = JsonSerializer.Deserialize<List<BatchItem>>(inputText) ?? new();
                }

                if (items.Count == 0)
                {
                    if (!file.Exists)
                        throw new FileNotFoundException($"File not found: {file.FullName}");
                    Console.Error.WriteLine("Batch contains 0 commands.");
                    return 0;
                }

                // Execute all in one open/save cycle
                using var handler = DwgHandlerFactory.Open(file.FullName, editable: true);
                var results = new List<BatchResult>();

                for (int bi = 0; bi < items.Count; bi++)
                {
                    var item = items[bi];
                    try
                    {
                        var output = ExecuteBatchItem(handler, item, json);
                        results.Add(new BatchResult { Index = bi, Success = true, Output = output });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new BatchResult { Index = bi, Success = false, Item = item, Error = ex.Message });
                        if (stopOnError) break;
                    }
                }

                // Save if any mutations succeeded
                if (results.Any(r => r.Success))
                    handler.Save();

                // Output results
                if (json)
                {
                    var outputJson = JsonSerializer.Serialize(
                        new { results, summary = new { total = items.Count, executed = results.Count, succeeded = results.Count(r => r.Success), failed = results.Count(r => !r.Success) } },
                        new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
                    Console.WriteLine(OutputFormatter.WrapEnvelope(outputJson));
                }
                else
                {
                    foreach (var r in results)
                    {
                        var prefix = $"[{r.Index}] ";
                        if (r.Success)
                            Console.WriteLine(r.Output != null ? $"{prefix}{r.Output}" : $"{prefix}OK");
                        else
                            Console.Error.WriteLine($"{prefix}ERROR: {r.Error}");
                    }
                    var succeeded = results.Count(r => r.Success);
                    var failed = results.Count - succeeded;
                    Console.Error.WriteLine($"\nBatch: {succeeded} succeeded, {failed} failed, {items.Count} total");
                }

                return results.Any(r => !r.Success) ? 1 : 0;
            }, json);
        });

        return cmd;
    }
}
