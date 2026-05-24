using DwgCli.Core.Exceptions;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;

namespace DwgCli.Core;

/// <summary>
/// Core handler wrapping ACadSharp's CadDocument.
/// Provides read → manipulate → save lifecycle for DWG files.
/// </summary>
internal sealed partial class DwgHandler : IDwgHandler
{
    private readonly string _filePath;
    private readonly CadDocument _doc;
    private readonly bool _editable;
    private readonly List<string> _notifications;
    private bool _modified;
    private Dictionary<ulong, Entity>? _entityCache;

    public IReadOnlyList<string> Notifications => _notifications;

    public CadDocument Document => _doc;

    public void MarkModified() => _modified = true;

    public DwgHandler(string filePath, CadDocument doc, bool editable, List<string> notifications)
    {
        _filePath = filePath;
        _doc = doc;
        _editable = editable;
        _notifications = notifications;
    }

    public void Save()
    {
        if (!_editable)
            throw new DwgReadOnlyException();
        if (!_modified)
            return;

        // Auto-upgrade old DWG versions to AC1027 for stable write support
        var currentVersion = _doc.Header.Version;
        if (currentVersion < ACadVersion.AC1021)
        {
            Console.Error.WriteLine(
                $"[Warning] DWG version {currentVersion} has unstable write support. Auto-upgrading to AC1027.");
            _doc.Header.Version = ACadVersion.AC1027;
        }

        // Create backup before writing
        var backupPath = _filePath + ".bak";
        try { File.Copy(_filePath, backupPath, overwrite: true); }
        catch (Exception ex) { Console.Error.WriteLine($"[Warning] Failed to create backup: {ex.Message}"); }

        // Write back
        var errors = new List<string>();
        DwgWriter.Write(_filePath, _doc, notification: (_, e) =>
        {
            if (e.NotificationType == NotificationType.Error)
                errors.Add(e.Message);
        });

        if (errors.Count > 0)
            Console.Error.WriteLine($"DWG write completed with {errors.Count} warning(s): {string.Join("; ", errors)}");

        _entityCache = null;
        _modified = false;
    }

    public void Dispose()
    {
        // Nothing to clean up — CadDocument is in-memory only.
    }
}

