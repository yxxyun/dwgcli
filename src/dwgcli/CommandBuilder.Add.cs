using System.CommandLine;
using DwgCli.Core;

namespace DwgCli;

static partial class CommandBuilder
{
    private static Command BuildAddCommand(Option<bool> jsonOption)
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "DWG file path" };
        var parentArg = new Argument<string>("parent")
        {
            Description = "Parent path. /entities (add entity) or /layers (add layer)"
        };
        var typeArg = new Argument<string>("type")
        {
            Description = "Type of element. Entities: line, circle, arc, text, mtext, insert. Tables: layer"
        };
        var propsOpt = new Option<string[]>("--prop")
        {
            Description = "Property (key=value). Entity examples: x1=0 y1=0 x2=100 y2=100. Layer: name=my-layer color=red",
            AllowMultipleArgumentsPerToken = true
        };

        var attrOpt = new Option<string[]>("--attr")
        {
            Description = "Attribute (TAG=VALUE) for insert block references",
            AllowMultipleArgumentsPerToken = true
        };

        var cmd = new Command("add", "Add entities or layers to the drawing");
        cmd.Add(fileArg);
        cmd.Add(parentArg);
        cmd.Add(typeArg);
        cmd.Add(propsOpt);
        cmd.Add(attrOpt);
        cmd.Add(jsonOption);

        cmd.SetAction(result =>
        {
            var json = result.GetValue(jsonOption);
            return SafeRun(() =>
            {
                var file = result.GetValue(fileArg)!;
                var parent = result.GetValue(parentArg)!;
                var type = result.GetValue(typeArg)!;
                var props = result.GetValue(propsOpt);
                var attrs = result.GetValue(attrOpt);

                var properties = ParsePropsArray(props);
                var attributes = attrs != null && attrs.Length > 0 ? ParsePropsArray(attrs) : null;

                using var handler = DwgHandlerFactory.Open(file.FullName, editable: true);
                var resultPath = handler.Add(parent, type, properties, attributes);

                var message = $"Added {type} at {resultPath}";

                if (json)
                    Console.WriteLine(OutputFormatter.WrapEnvelopeText(message));
                else
                    Console.WriteLine(message);

                handler.Save();
                return 0;
            }, json);
        });

        return cmd;
    }
}
