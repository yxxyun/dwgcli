using ACadSharp;

namespace DwgCli.Core;

/// <summary>
/// Common interface for DWG document operations.
/// </summary>
public interface IDwgHandler : IDisposable
{
    /// <summary>
    /// File-level metadata (version, author, layer count, entity count, etc.).
    /// </summary>
    DwgNode GetInfo();

    /// <summary>
    /// Get a node by path (e.g. "/", "/layers", "/layer/0", "/entities", "/entity/{handle}").
    /// </summary>
    DwgNode Get(string path, int depth = 1);

    /// <summary>
    /// Dump the full document structure as a tree.
    /// </summary>
    DwgNode Dump(int depth = 10);

    /// <summary>
    /// Query entities/layers by selector (e.g. "type=Line", "layer=0").
    /// Returns list of matching nodes.
    /// </summary>
    List<DwgNode> Query(string selector);

    /// <summary>
    /// Set properties on a document node.
    /// Returns list of property names that were not applied (unsupported).
    /// </summary>
    List<string> Set(string path, Dictionary<string, string> properties);

    /// <summary>
    /// Add an entity or table entry.
    /// Returns the path of the newly added element.
    /// </summary>
    string Add(string parentPath, string type, Dictionary<string, string> properties, Dictionary<string, string>? attributes = null);

    /// <summary>
    /// Remove an entity or layer by path (e.g. /entity/{handle}, /layer/{name}).
    /// Returns an optional warning message.
    /// </summary>
    string? Remove(string path);

    /// <summary>
    /// Purge unused layers, blocks, and linetypes from the drawing.
    /// Returns a list of paths of purged items.
    /// </summary>
    List<string> Purge();

    /// <summary>
    /// Save changes back to the DWG file.
    /// </summary>
    void Save();

    /// <summary>
    /// The underlying CadDocument for direct ACadSharp access.
    /// </summary>
    CadDocument Document { get; }

    /// <summary>
    /// Mark the document as modified so Save() will write changes.
    /// Required after direct Document manipulations (e.g. block import).
    /// </summary>
    void MarkModified();
}
