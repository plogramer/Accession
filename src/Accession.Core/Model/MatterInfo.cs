namespace Accession.Core.Model;

/// <summary>The user-editable matter fields of an inventory.</summary>
public sealed record MatterInfo(
    string ClientName,
    string ClientCode,
    string MatterName,
    string MatterCode,
    string? Description,
    string? MatterUrl);
