namespace DwgCli.Core.Exceptions;

/// <summary>Base exception for all dwgcli domain errors.</summary>
public abstract class DwgException : Exception
{
    protected DwgException(string message) : base(message) { }
    protected DwgException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Thrown when the specified file does not exist.</summary>
public class DwgFileNotFoundException : DwgException
{
    public string FilePath { get; }
    public DwgFileNotFoundException(string filePath)
        : base($"File not found: {filePath}")
        => FilePath = filePath;
}

/// <summary>Thrown when attempting to modify a file opened as read-only.</summary>
public class DwgReadOnlyException : DwgException
{
    public DwgReadOnlyException()
        : base("File was opened read-only. Cannot modify.") { }
}

/// <summary>Thrown when an operation receives invalid parameters.</summary>
public class DwgInvalidParameterException : DwgException
{
    public string ParameterName { get; }
    public string? ParameterValue { get; }

    public DwgInvalidParameterException(string parameterName, string message)
        : base(message)
        => ParameterName = parameterName;

    public DwgInvalidParameterException(string parameterName, string? parameterValue, string message)
        : base(message)
    {
        ParameterName = parameterName;
        ParameterValue = parameterValue;
    }
}

/// <summary>Thrown when an entity is not found by handle.</summary>
public class DwgEntityNotFoundException : DwgException
{
    public string Handle { get; }
    public DwgEntityNotFoundException(string handle)
        : base($"Entity not found with handle: '{handle}'")
        => Handle = handle;
}

/// <summary>Thrown when a layer is not found by name.</summary>
public class DwgLayerNotFoundException : DwgException
{
    public string LayerName { get; }
    public DwgLayerNotFoundException(string layerName)
        : base($"Layer not found: '{layerName}'")
        => LayerName = layerName;
}

/// <summary>Thrown when a block is not found by name.</summary>
public class DwgBlockNotFoundException : DwgException
{
    public string BlockName { get; }
    public DwgBlockNotFoundException(string blockName)
        : base($"Block not found: '{blockName}'")
        => BlockName = blockName;
}

/// <summary>Thrown when a generic operation fails (file read/write, structural issues).</summary>
public class DwgOperationException : DwgException
{
    public string Operation { get; }
    public DwgOperationException(string operation, string message)
        : base(message)
        => Operation = operation;
    public DwgOperationException(string operation, string message, Exception inner)
        : base(message, inner)
        => Operation = operation;
}

/// <summary>Thrown when an unsupported file type or entity type is used.</summary>
public class DwgNotSupportedException : DwgException
{
    public string Feature { get; }
    public DwgNotSupportedException(string feature, string message)
        : base(message)
        => Feature = feature;
}
