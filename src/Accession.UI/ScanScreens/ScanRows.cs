namespace Accession.UI.ScanScreens;

/// <summary>A media waiting in the scan queue.</summary>
public sealed record QueueRow(int Position, long MediaKey, string MediaId, string Type);

/// <summary>A scan error with display-ready values.</summary>
public sealed record ErrorRow(
    long ErrorId,
    long MediaKey,
    string Time,
    string MediaId,
    string RelativePath,
    string ItemType,
    string ErrorType,
    string Severity,
    string Code,
    string Message);
