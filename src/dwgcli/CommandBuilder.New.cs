using System.CommandLine;
using ACadSharp;
using ACadSharp.IO;
using DwgCli.Core;

namespace DwgCli;

static partial class CommandBuilder
{
    private static Command BuildNewCommand(Option<bool> jsonOption)
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "Output DWG file path" };

        var cmd = new Command("new", "Create a new empty DWG file (AC1027)");
        cmd.Add(fileArg);
        cmd.Add(jsonOption);

        cmd.SetAction(result =>
        {
            var json = result.GetValue(jsonOption);
            return SafeRun(() =>
            {
                var file = result.GetValue(fileArg)!;

                var doc = CreateEmptyDocument();

                DwgWriter.Write(file.FullName, doc);

                var msg = $"Created empty DWG: {file.FullName}";
                if (json)
                    Console.WriteLine(OutputFormatter.WrapEnvelopeText(msg));
                else
                    Console.WriteLine(msg);

                return 0;
            }, json);
        });

        return cmd;
    }

    /// <summary>
    /// Creates a new empty CadDocument with AC1027 version and default tables.
    /// </summary>
    internal static CadDocument CreateEmptyDocument()
    {
        var doc = new CadDocument();
        doc.Header.Version = ACadVersion.AC1027;
        return doc;
    }
}
