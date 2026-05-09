using System.CommandLine;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Extensions;
using ACadSharp.IO;
using ACadSharp.Tables;
using DwgCli.Core;

namespace DwgCli;

static partial class CommandBuilder
{
    private static Command BuildBlockCommand(Option<bool> jsonOption)
    {
        var cmd = new Command("block", "Block import/export operations");

        cmd.Add(BuildBlockImportCommand(jsonOption));

        return cmd;
    }

    private static Command BuildBlockImportCommand(Option<bool> jsonOption)
    {
        var targetArg = new Argument<FileInfo>("target") { Description = "Target DWG file" };
        var sourceArg = new Argument<FileInfo>("source") { Description = "Source DWG file" };
        var nameOpt = new Option<string>("--name") { Description = "Block name" };

        var cmd = new Command("import", "Import entities from source DWG as a block into target DWG");
        cmd.Add(targetArg);
        cmd.Add(sourceArg);
        cmd.Add(nameOpt);
        cmd.Add(jsonOption);

        cmd.SetAction(result =>
        {
            var json = result.GetValue(jsonOption);
            return SafeRun(() =>
            {
                var target = result.GetValue(targetArg)!;
                var source = result.GetValue(sourceArg)!;
                var blockName = result.GetValue(nameOpt)
                    ?? throw new ArgumentException("--name is required");

                // Read source document
                var sourceDoc = DwgReader.Read(source.FullName);

                // Open target document via handler (for backup + version upgrade)
                using var handler = DwgHandlerFactory.Open(target.FullName, editable: true);

                // Import block from source document
                ImportBlock(handler.Document, sourceDoc, blockName);
                handler.MarkModified();

                handler.Save();

                var msg = $"Imported block '{blockName}' from {source.FullName} into {target.FullName}";
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
    /// Import all model-space entities from sourceDoc as a block named blockName into targetDoc.
    /// Also copies required table entries (layers, linetypes, text styles).
    /// </summary>
    internal static BlockRecord ImportBlock(CadDocument targetDoc, CadDocument sourceDoc, string blockName)
    {
        // Check if block already exists
        if (targetDoc.BlockRecords.Contains(blockName))
            throw new ArgumentException($"Block '{blockName}' already exists in target document");

        // Copy table entries from source to target
        CopyLayers(sourceDoc, targetDoc);
        CopyLineTypes(sourceDoc, targetDoc);
        CopyTextStyles(sourceDoc, targetDoc);

        // Get source model space entities
        var modelSpace = sourceDoc.BlockRecords["*Model_Space"];
        var entities = modelSpace?.Entities ?? sourceDoc.Entities;

        // Create block record
        var block = new BlockRecord(blockName);
        targetDoc.BlockRecords.Add(block);

        // Clone entities into block
        foreach (var entity in entities)
        {
            var cloned = CloneEntityToDoc(entity, targetDoc);
            if (cloned != null)
                block.Entities.Add(cloned);
        }

        return block;
    }

    private static void CopyLayers(CadDocument source, CadDocument target)
    {
        foreach (var layer in source.Layers)
        {
            if (!target.Layers.Contains(layer.Name))
            {
                var clone = layer.CloneTyped<Layer>();
                target.Layers.Add(clone);
            }
        }
    }

    private static void CopyLineTypes(CadDocument source, CadDocument target)
    {
        foreach (var lt in source.LineTypes)
        {
            if (!target.LineTypes.Contains(lt.Name))
            {
                var clone = lt.CloneTyped<LineType>();
                target.LineTypes.Add(clone);
            }
        }
    }

    private static void CopyTextStyles(CadDocument source, CadDocument target)
    {
        foreach (var ts in source.TextStyles)
        {
            if (!target.TextStyles.Contains(ts.Name))
            {
                var clone = ts.CloneTyped<TextStyle>();
                target.TextStyles.Add(clone);
            }
        }
    }

    /// <summary>
    /// Clone an entity and resolve its references to the target document's tables.
    /// </summary>
    private static Entity? CloneEntityToDoc(Entity entity, CadDocument targetDoc)
    {
        // Supported entity types — Clone() is from CadObject (MemberwiseClone)
        // CloneTyped<T> is only available on the specific type, not base Entity.
        Entity? clone;

        if (entity is Line line)
            clone = line.CloneTyped<Line>();
        else if (entity is Circle circle)
            clone = circle.CloneTyped<Circle>();
        else if (entity is Arc arc)
            clone = arc.CloneTyped<Arc>();
        else if (entity is LwPolyline lwp)
            clone = lwp.CloneTyped<LwPolyline>();
        else if (entity is MText mtext)
            clone = mtext.CloneTyped<MText>();
        else if (entity is TextEntity text)
            clone = text.CloneTyped<TextEntity>();
        else if (entity is Insert ins)
            clone = ins.CloneTyped<Insert>();
        else if (entity is Hatch hatch)
            clone = hatch.CloneTyped<Hatch>();
        else if (entity is AttributeDefinition attrDef)
            clone = attrDef.CloneTyped<AttributeDefinition>();
        else
            return null;

        // Resolve Layer reference to target document
        if (entity.Layer != null)
        {
            clone.Layer = targetDoc.Layers[entity.Layer.Name];
        }

        // Resolve LineType reference to target document
        if (entity.LineType != null)
        {
            clone.LineType = targetDoc.LineTypes[entity.LineType.Name];
        }

        // Note: Insert.Block is read-only (set via constructor only).
        // Clone preserves the reference via MemberwiseClone, which is acceptable
        // when the referenced block record exists in the target document.

        return clone;
    }
}
