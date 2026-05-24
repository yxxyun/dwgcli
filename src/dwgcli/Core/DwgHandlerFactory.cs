using ACadSharp;
using ACadSharp.IO;
using DwgCli.Core.Exceptions;

namespace DwgCli.Core;

/// <summary>
/// Opens DWG (or DXF) files and returns an IDwgHandler.
/// </summary>
internal static class DwgHandlerFactory
{
    public static IDwgHandler Open(string filePath, bool editable = false)
    {
        if (!File.Exists(filePath))
            throw new DwgFileNotFoundException(filePath);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        switch (ext)
        {
            case ".dwg":
                return OpenDwg(filePath, editable);
            case ".dxf":
                return OpenDxf(filePath, editable);
            default:
                throw new DwgNotSupportedException("file type", $"Unsupported file type: {ext}. Supported: .dwg, .dxf");
        }
    }

    private static IDwgHandler OpenDwg(string filePath, bool editable)
    {
        var notifications = new List<string>();
        CadDocument doc;
        try
        {
            doc = DwgReader.Read(filePath, (_, e) =>
            {
                if (e.NotificationType is NotificationType.Warning or NotificationType.Error)
                    notifications.Add($"[{e.NotificationType}] {e.Message}");
            });
        }
        catch (Exception ex)
        {
            throw new DwgOperationException("read", $"Failed to read DWG file: {ex.Message}", ex);
        }

        return new DwgHandler(filePath, doc, editable, notifications);
    }

    private static IDwgHandler OpenDxf(string filePath, bool editable)
    {
        var notifications = new List<string>();
        CadDocument doc;
        try
        {
            doc = DxfReader.Read(filePath, (_, e) =>
            {
                if (e.NotificationType is NotificationType.Warning or NotificationType.Error)
                    notifications.Add($"[{e.NotificationType}] {e.Message}");
            });
        }
        catch (Exception ex)
        {
            throw new DwgOperationException("read", $"Failed to read DXF file: {ex.Message}", ex);
        }

        return new DwgHandler(filePath, doc, editable, notifications);
    }
}
