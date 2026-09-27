namespace Accession.UI.Records;

/// <summary>A file category with its totals.</summary>
public sealed record CategoryRowVm(int CategoryId, string Name, string Description, string Files, string Size, long FileCount);

/// <summary>An extension of a category with its totals.</summary>
public sealed record ExtensionCountVm(string RawExtension, string Extension, string Files, string Size, bool HasFiles);

/// <summary>An audit log entry with display-ready values.</summary>
public sealed record AuditRowVm(long AuditId, string Time, string User, string Machine, string Action, string MediaId, string Details);
